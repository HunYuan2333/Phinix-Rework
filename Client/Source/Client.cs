using Authentication;
using Connections;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;
using Verse.Sound;
using Thing = Verse.Thing;
using PhinixClient.Framework;

namespace PhinixClient
{
    public class Client : Mod
    {
        public static Client Instance;
        public static readonly Version Version = typeof(Client).Assembly.GetName().Version;
        public const string PackageId = "Thomotron.Phinix";

        public void Log(LogEventArgs e) => ILoggableHandler(null, e);

        public override string SettingsCategory() => "Phinix";

        #region Modules
        private NetClient netClient;
        public bool Connected => netClient.Connected;
        public void Send(string module, byte[] serialisedMessage) => netClient.Send(module, serialisedMessage);
        public event EventHandler OnConnecting;
        public event EventHandler OnDisconnect;

        private ClientAuthenticator authenticator;
        public bool Authenticated => authenticator.Authenticated;
        public string SessionId => authenticator.SessionId;
        public event EventHandler<AuthenticationEventArgs> OnAuthenticationSuccess;
        public event EventHandler<AuthenticationEventArgs> OnAuthenticationFailure;

        private ClientUserManager userManager;
        public bool LoggedIn => userManager.LoggedIn;
        public string Uuid => userManager.Uuid;
        public bool TryGetDisplayName(string uuid, out string displayName) => userManager.TryGetDisplayName(uuid, out displayName);
        public bool TryGetUser(string uuid, out ImmutableUser user) => userManager.TryGetUser(uuid, out user);
        public string[] GetUserUuids(bool loggedIn = false) => userManager.GetUuids(loggedIn);
        public ImmutableUser[] GetUsers(bool loggedIn = false) => userManager.GetUsers(loggedIn);
        public event EventHandler<LoginEventArgs> OnLoginSuccess;
        public event EventHandler<LoginEventArgs> OnLoginFailure;
        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;
        public event EventHandler<UserLoginStateChangedEventArgs> OnUserLoggedIn;
        public event EventHandler<UserLoginStateChangedEventArgs> OnUserLoggedOut;
        public event EventHandler<UserCreatedEventArgs> OnUserCreated;
        public event EventHandler OnUserSync;

        public bool Online => Connected && Authenticated && LoggedIn;

        public bool CanUseFrameworkChat => frameworkClient != null && frameworkClient.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2;
        public void SendMessage(string message)
        {
            frameworkClient.TryHandleOutgoingMessage(message);
        }
        public IReadOnlyList<IMainTabProvider> MainTabProviders => frameworkClient?.ResolveExtensionApis<IMainTabProvider>() ?? Array.Empty<IMainTabProvider>();
        public IReadOnlyList<IServerSidebarProvider> SidebarProviders => frameworkClient?.ResolveExtensionApis<IServerSidebarProvider>() ?? Array.Empty<IServerSidebarProvider>();
        public LookTargets DropPods(IEnumerable<Thing> verseThings) => dropPods(verseThings);
        #endregion

        private PhinixFrameworkClient frameworkClient;
        private ClientUserEventStream userEventStream;
        private ClientMainThreadDispatcher mainThreadDispatcher;
        public Settings Settings { get; }

        /// <summary>
        /// Queue of sounds to play on the next frame.
        /// Necessary because sounds are only played on the main Unity thread.
        /// </summary>
        private List<SoundDef> soundQueue = new List<SoundDef>();
        /// <summary>
        /// Lock object to prevent race conditions when accessing soundQueue.
        /// </summary>
        private object soundQueueLock = new object();

        public Client(ModContentPack content) : base(content)
        {
            Instance = this;

            // Apply Harmony patches
            new HarmonyLib.Harmony(PackageId).PatchAll();

            // Load in Settings
            Settings = GetSettings<Settings>();
            if (!Settings.Migrated) Settings.MigrateFromHugsLib();
            // Set up our module instances
            netClient = new NetClient();
            authenticator = new ClientAuthenticator(netClient, getCredentials);
            userManager = new ClientUserManager(netClient, authenticator);
            IClientUserDirectory frameworkUserDirectory = new ClientFrameworkUserDirectoryAdapter(userManager);
            IClientSessionContext sessionContext = new ClientSessionContextAdapter(authenticator, userManager);
            IClientSettingsContext settingsContext = new ClientSettingsContextAdapter(this);
            userEventStream = new ClientUserEventStream();
            mainThreadDispatcher = new ClientMainThreadDispatcher();
            IClientWindowService windowService = new ClientWindowService();
            IClientSoundService soundService = new ClientSoundService(this);
            ExtensionHostContext extensionHostContext = new ExtensionHostContext
            {
                HostKind = "client",
                Log = (message, level) => Log(new LogEventArgs(message, level)),
                StorageProvider = new FileSystemExtensionStorageProvider(System.IO.Path.Combine("framework-extensions", "client"))
            };
            extensionHostContext.AddService(userManager);
            extensionHostContext.AddService(frameworkUserDirectory);
            extensionHostContext.AddService(sessionContext);
            extensionHostContext.AddService(settingsContext);
            extensionHostContext.AddService<IClientUserEventStream>(userEventStream);
            extensionHostContext.AddService<IClientMainThreadDispatcher>(mainThreadDispatcher);
            extensionHostContext.AddService<IClientWindowService>(windowService);
            extensionHostContext.AddService<IClientSoundService>(soundService);
            extensionHostContext.AddService<Action>(windowService.OpenSettingsWindow);
            extensionHostContext.AddService<Func<IEnumerable<Thing>, LookTargets>>(verseThings => dropPods(verseThings));
            Verse.Log.Message($"[Phinix] Loading extensions, probe dirs: {string.Join("; ", GetExtensionProbeDirectories())}");
            ExtensionAssemblyLoader.LoadAssemblies(
                GetExtensionProbeDirectories(),
                (message, level) =>
                {
                    // Always pass through to the log handler so warnings/errors are visible
                    // even when DevMode is off
                    Log(new LogEventArgs(message, level));
                });
            Verse.Log.Message("[Phinix] Constructing framework client and discovering extensions...");
            frameworkClient = new PhinixFrameworkClient(netClient, authenticator, userManager, extensionHostContext);
            Verse.Log.Message($"[Phinix] Framework client ready. MainTabProviders={MainTabProviders.Count}, SidebarProviders={SidebarProviders.Count}");
            // Subscribe to log events (after construction so constructor diagnostics
            // already went through the hostContext.Log callback above)
            authenticator.OnLogEntry += ILoggableHandler;
            userManager.OnLogEntry += ILoggableHandler;
            frameworkClient.OnLogEntry += ILoggableHandler;
            #region Module Event Handlers
            // Subscribe to connection events
            netClient.OnDisconnect += (sender, args) =>
            {
                userEventStream.RaiseDisconnected();
            };

            // Subscribe to authentication events
            authenticator.OnAuthenticationSuccess += (sender, args) =>
            {
                Verse.Log.Message("Successfully authenticated with server.");
                userManager.SendLogin(
                    displayName: Settings.DisplayName,
                    acceptingTrades: Settings.AcceptingTrades
                );
            };
            authenticator.OnAuthenticationFailure += (sender, args) =>
            {
                Verse.Log.Message(string.Format("Failed to authenticate with server: {0} ({1})", args.FailureMessage, args.FailureReason.ToString()));

                Find.WindowStack.Add(new Dialog_MessageBox(title: "Phinix_error_authFailedTitle".Translate(), text: "Phinix_error_authFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString())));

                Disconnect();
            };

            // Subscribe to user management events
            userManager.OnLoginSuccess += (sender, args) =>
            {
                Verse.Log.Message(string.Format("Successfully logged in with UUID {0}", userManager.Uuid));
                frameworkClient.BeginNegotiation();
            };
            userManager.OnLoginFailure += (sender, args) =>
            {
                Verse.Log.Message(string.Format("Failed to log in to server: {0} ({1})", args.FailureMessage, args.FailureReason.ToString()));

                Find.WindowStack.Add(new Dialog_MessageBox(title: "Phinix_error_loginFailedTitle".Translate(), text: "Phinix_error_loginFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString())));

                Disconnect();
            };
            userManager.OnUserDisplayNameChanged += (sender, args) =>
            {
                userEventStream.RaiseUsersChanged();
                userEventStream.RaiseUserDisplayNameChanged(args);
                if (Prefs.DevMode) Verse.Log.Message(string.Format("User with UUID {0} changed their display name from \"{1}\" to \"{2}\"", args.Uuid, args.OldDisplayName, args.NewDisplayName));
            };
            userManager.OnUserLoggedIn += (sender, args) =>
            {
                userEventStream.RaiseUsersChanged();
                if (Prefs.DevMode) Verse.Log.Message(string.Format("User {0} logged in", args.Uuid));
            };
            userManager.OnUserLoggedOut += (sender, args) =>
            {
                userEventStream.RaiseUsersChanged();
                if (Prefs.DevMode) Verse.Log.Message(string.Format("User {0} logged out", args.Uuid));
            };
            userManager.OnUserCreated += (sender, args) =>
            {
                userEventStream.RaiseUsersChanged();
                if (Prefs.DevMode) Verse.Log.Message(string.Format("New user created: {0} ({1}) - {2}ogged in", args.DisplayName, args.Uuid, args.LoggedIn ? "L" : "Not l"));
            };
            userManager.OnUserSync += (sender, args) => userEventStream.RaiseUsersChanged();

            #endregion

            // Forward events so the UI can handle them
            netClient.OnConnecting += (sender, e) => { OnConnecting?.Invoke(sender, e); };
            netClient.OnDisconnect += (sender, e) => { OnDisconnect?.Invoke(sender, e); };
            authenticator.OnAuthenticationSuccess += (sender, e) => { OnAuthenticationSuccess?.Invoke(sender, e); };
            authenticator.OnAuthenticationFailure += (sender, e) => { OnAuthenticationFailure?.Invoke(sender, e); };
            userManager.OnLoginSuccess += (sender, e) => { OnLoginSuccess?.Invoke(sender, e); };
            userManager.OnLoginFailure += (sender, e) => { OnLoginFailure?.Invoke(sender, e); };
            userManager.OnUserDisplayNameChanged += (sender, e) => { OnUserDisplayNameChanged?.Invoke(sender, e); };
            userManager.OnUserLoggedIn += (sender, e) => { OnUserLoggedIn?.Invoke(sender, e); };
            userManager.OnUserLoggedOut += (sender, e) => { OnUserLoggedOut?.Invoke(sender, e); };
            userManager.OnUserCreated += (sender, e) => { OnUserCreated?.Invoke(sender, e); };
            userManager.OnUserSync += (sender, e) => { OnUserSync?.Invoke(sender, e); };
            // Connect to the server set in the config
            Connect(Settings.ServerAddress, Settings.ServerPort);
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard()
            {
                ColumnWidth = Math.Min(600f, inRect.width / 2)
            };
            listing.Begin(inRect);

            listing.Label("Phinix_modSettings_serverAddressTitle".Translate());
            Settings.ServerAddress = listing.TextEntry(Settings.ServerAddress);

            listing.Label("Phinix_modSettings_serverPortTitle".Translate());
            string portStr = Settings.ServerPort.ToString();
            portStr = listing.TextEntry(portStr);
            int.TryParse(portStr, out int serverPort);
            Settings.ServerPort = serverPort;

            listing.Label("Phinix_modSettings_displayNameTitle".Translate());
            Settings.DisplayName = listing.TextEntry(Settings.DisplayName);

            bool acceptingTrades = Settings.AcceptingTrades;
            listing.CheckboxLabeled("Phinix_modSettings_acceptingTradesTitle".Translate(), ref acceptingTrades);
            Settings.AcceptingTrades = acceptingTrades;

            bool showNameFormatting = Settings.ShowNameFormatting;
            listing.CheckboxLabeled("Phinix_modSettings_showNameFormatting".Translate(), ref showNameFormatting);
            Settings.ShowNameFormatting = showNameFormatting;

            bool showChatFormatting = Settings.ShowChatFormatting;
            listing.CheckboxLabeled("Phinix_modSettings_showChatFormatting".Translate(), ref showChatFormatting);
            Settings.ShowChatFormatting = showChatFormatting;

            bool playNoiseOnMessageReceived = Settings.PlayNoiseOnMessageReceived;
            listing.CheckboxLabeled("Phinix_modSettings_playNoiseOnMessageReceived".Translate(), ref playNoiseOnMessageReceived);
            Settings.PlayNoiseOnMessageReceived = playNoiseOnMessageReceived;

            bool showUnreadMessageCount = Settings.ShowUnreadMessageCount;
            listing.CheckboxLabeled("Phinix_modSettings_showUnreadMessageCount".Translate(), ref showUnreadMessageCount);
            Settings.ShowUnreadMessageCount = showUnreadMessageCount;

            bool showBlockedUnreadMessageCount = Settings.ShowBlockedUnreadMessageCount;
            listing.CheckboxLabeled("Phinix_modSettings_showBlockedUnreadMessageCount".Translate(), ref showBlockedUnreadMessageCount);
            Settings.ShowBlockedUnreadMessageCount = showBlockedUnreadMessageCount;

            listing.Label("Phinix_modSettings_chatMessageLimit".Translate());
            string limitStr = Settings.ChatMessageLimit.ToString();
            limitStr = listing.TextEntry(limitStr);
            int.TryParse(limitStr, out int chatMessageLimit);
            Settings.ChatMessageLimit = chatMessageLimit;

            bool forceMessageFieldFocus = Settings.ForceMessageFieldFocus;
            listing.CheckboxLabeled("Phinix_modSettings_forceMessageFieldFocus".Translate(), ref forceMessageFieldFocus);
            Settings.ForceMessageFieldFocus = forceMessageFieldFocus;

            bool allItemsTradable = Settings.AllItemsTradable;
            listing.CheckboxLabeled("Phinix_modSettings_allItemsTradable".Translate(), ref allItemsTradable);
            Settings.AllItemsTradable = allItemsTradable;

            bool showBlockedTrades = Settings.ShowBlockedTrades;
            listing.CheckboxLabeled("Phinix_modSettings_showBlockedTrades".Translate(), ref showBlockedTrades);
            Settings.ShowBlockedTrades = showBlockedTrades;

            bool dropCurrentMap = Settings.DropCurrentMap;
            listing.CheckboxLabeled("Phinix_modSettings_dropCurrentMap".Translate(), ref dropCurrentMap);
            Settings.DropCurrentMap = dropCurrentMap;

            listing.End();
        }

        public override void WriteSettings()
        {
            if (!Settings.IsChanged) return;

            Settings.AcceptChanges();
            userManager.UpdateSelf(Settings.DisplayName, Settings.AcceptingTrades);
        }

        /// <summary>
        /// Adds a user's UUID to the blocked user list.
        /// </summary>
        /// <param name="senderUuid">UUID of user to block</param>
        public void BlockUser(string senderUuid)
        {
            if (!Settings.BlockedUsers.Add(senderUuid)) return;

            Settings.AcceptChanges();

            userEventStream.RaiseBlockedUsersChanged(new UserBlockStateChangedEventArgs(senderUuid, true));
        }

        /// <summary>
        /// Removes a user's UUID from the blocked user list.
        /// </summary>
        /// <param name="senderUuid">UUID of the user to unblock</param>
        public void UnBlockUser(string senderUuid)
        {
            if (!Settings.BlockedUsers.Remove(senderUuid)) return;

            Settings.AcceptChanges();

            userEventStream.RaiseBlockedUsersChanged(new UserBlockStateChangedEventArgs(senderUuid, false));
        }

        /// <summary>
        /// A hook into the main update loop. Periodically updates state.
        /// </summary>
        /// <seealso cref="Patches.RootPatch.Update"/>
        public void Update()
        {
            lock (soundQueueLock)
            {
                // Check if we have sounds to play
                while (soundQueue.Any())
                {
                    // Dequeue and play a sound
                    SoundDef sound = soundQueue.Pop();
                    sound.PlayOneShotOnCamera();
                }
            }
            mainThreadDispatcher?.DrainPendingActions();
        }

        /// <summary>
        /// Attempts to connect to the server at the given address and port.
        /// This will disconnect from the current server, if any.
        /// </summary>
        /// <param name="address">Server address</param>
        /// <param name="port">Server port</param>
        public void Connect(string address, int port)
        {
            if (Connected) Disconnect();

            try
            {
                netClient.Connect(address, port);
            }
            catch
            {
                Verse.Log.Message(string.Format("Could not connect to {0}:{1}", Settings.ServerAddress, Settings.ServerPort));

                Find.WindowStack.Add(new Dialog_MessageBox(title: "Phinix_error_connectionFailedTitle".Translate(), text: "Phinix_error_connectionFailedMessage".Translate(Settings.ServerAddress, Settings.ServerPort)));
            }
        }

        /// <summary>
        /// If connected, disconnects from the current server.
        /// </summary>
        public void Disconnect()
        {
            netClient.Disconnect();
        }

        /// <summary>
        /// Updates the user's display name locally and on the server.
        /// </summary>
        /// <param name="displayName">Display name</param>
        public void UpdateDisplayName(string displayName)
        {
            // Try to update within the user manager
            userManager.UpdateSelf(displayName);
        }

        private static IEnumerable<string> GetExtensionProbeDirectories()
        {
            string clientAssemblyDirectory = Path.GetDirectoryName(typeof(Client).Assembly.Location);
            string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            if (!string.IsNullOrEmpty(clientAssemblyDirectory))
            {
                yield return clientAssemblyDirectory;
                yield return Path.GetFullPath(Path.Combine(clientAssemblyDirectory, "..", "..", "Common", "Assemblies"));
                yield return Path.GetFullPath(Path.Combine(clientAssemblyDirectory, "..", "..", "Common", "Extensions"));
            }

            if (!string.IsNullOrEmpty(appBaseDirectory))
            {
                yield return appBaseDirectory;
            }
        }

        /// <summary>
        /// Handler for <see cref="ILoggable"/> <c>OnLogEvent</c> events.
        /// Raised by modules as a way to hook into the log.
        /// </summary>
        /// <param name="sender">Object that raised the event</param>
        /// <param name="args">Event arguments</param>
        private void ILoggableHandler(object sender, LogEventArgs args)
        {
            switch (args.LogLevel)
            {
                case LogLevel.DEBUG:
                    if (Prefs.DevMode) Verse.Log.Message(args.Message);
                    break;
                case LogLevel.WARNING:
                    Verse.Log.Warning(args.Message);
                    break;
                case LogLevel.ERROR:
                case LogLevel.FATAL:
                    Verse.Log.Error(args.Message);
                    break;
                case LogLevel.INFO:
                default:
                    Verse.Log.Message(args.Message);
                    break;
            }
        }

        /// <summary>
        /// Handles credential requests from the <see cref="ClientAuthenticator"/> module.
        /// This forwards the server details and a callback to the GUI for user input.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="serverName">Server name</param>
        /// <param name="serverDescription">Server description</param>
        /// <param name="authType">Authentication type</param>
        /// <param name="callback">Callback delegate to pass entered credentials to</param>
        private void getCredentials(string sessionId, string serverName, string serverDescription, AuthTypes authType, ClientAuthenticator.ReturnCredentialsDelegate callback)
        {
            if (Prefs.DevMode) Verse.Log.Message(string.Format("Authentication needs more credentials for the server \"{0}\" with authentication type \"{1}\"", serverName, authType.ToString()));

            Find.WindowStack.Add(new CredentialsWindow
            {
                SessionId = sessionId,
                ServerName = serverName,
                ServerDescription = serverDescription,
                AuthType = authType,
                CredentialsCallback = callback
            });
        }

        /// <summary>
        /// Launches the given <see cref="Thing"/>s in drop pods to a trade spot at the home colony.
        /// </summary>
        /// <param name="things">Collection of <see cref="Thing"/>s to drop</param>
        /// <returns>LookTarget for the drop location</returns>
        private LookTargets dropPods(IEnumerable<Thing> things)
        {
            // Launch drop pods to a trade spot on a home tile
            Map map = Settings.DropCurrentMap ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropSpot, map, things, canRoofPunch: false);

            return new LookTargets(dropSpot, map);
        }

        internal void EnqueueSound(SoundDef soundDef)
        {
            if (soundDef == null)
            {
                return;
            }

            lock (soundQueueLock)
            {
                soundQueue.Add(soundDef);
            }
        }

    }
}
