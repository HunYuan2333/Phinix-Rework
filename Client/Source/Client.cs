using Authentication;
using Connections;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.TradeExtension;
using PhinixClient.Trade;
using PhinixClientTrade = PhinixClient.Trade;
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
    public class Client : Mod, IClientTradeFacade
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
        public bool ShouldDisplayChatMessage(UIChatMessage message) => frameworkChatService.ShouldDisplayChatMessage(message, Settings.BlockedUsers, false);
        public bool ShouldPlayChatNotification(UIChatMessage message) => frameworkChatService.ShouldPlayNotification(message, Uuid, Settings.PlayNoiseOnMessageReceived, Current.Game != null, Settings.BlockedUsers);
        public void MarkAsRead()
        {
            frameworkClient.MarkAsRead();
        }
        public UIChatMessage[] GetUnreadChatMessages(bool markAsRead = true) => GetChatMessages(markAsRead, true);
        public int UnreadMessages => frameworkClient.UnreadMessages;
        public int UnreadMessagesExcludingBlocked => frameworkChatService.CountUnreadExcluding(frameworkClient.GetUnreadDisplayMessages(false), Settings.BlockedUsers);
        public event EventHandler<UIChatMessageEventArgs> OnChatMessageReceived;
        public event EventHandler OnChatSync;

        private IClientTradeService frameworkTradeAdapter;
        public void CancelTrade(string tradeId)
        {
            ActiveTradeService.CancelTrade(tradeId);
        }
        public string[] GetTradeIds() => ActiveTradeService.GetTradeIds();
        public string[] GetTradeIdsExceptWith(IEnumerable<string> otherPartyUuids)
        {
            return ActiveTradeService.GetTradesExceptWith(otherPartyUuids).Select(trade => trade.TradeId).ToArray();
        }
        public ClientTradeSnapshot[] GetTrades() => ActiveTradeService.GetTrades();
        public bool TryGetTrade(string tradeId, out ClientTradeSnapshot trade) => ActiveTradeService.TryGetTrade(tradeId, out trade);
        public ClientTradeSnapshot[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids) => ActiveTradeService.GetTradesExceptWith(otherPartyUuids);
        public bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid) => ActiveTradeService.TryGetOtherPartyUuid(tradeId, out otherPartyUuid);
        public bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted) => ActiveTradeService.TryGetOtherPartyAccepted(tradeId, out otherPartyAccepted);
        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted) => ActiveTradeService.TryGetPartyAccepted(tradeId, partyUuid, out accepted);
        public bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<TradeItemSnapshot> items) => ActiveTradeService.TryGetItemsOnOffer(tradeId, uuid, out items);
        public void UpdateTradeItems(string tradeId, IEnumerable<TradeItemSnapshot> items, string token = "")
        {
            ActiveTradeService.UpdateTradeItems(tradeId, items, token);
        }
        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null)
        {
            ActiveTradeService.UpdateTradeStatus(tradeId, accepted, cancelled);
        }
        public LookTargets DropPods(IEnumerable<Thing> verseThings) => dropPods(verseThings);
        public event EventHandler<TradeCreationEventArgs> OnTradeCreationSuccess;
        public event EventHandler<TradeCreationEventArgs> OnTradeCreationFailure;
        public event EventHandler<TradeCompletionEventArgs> OnTradeCompleted;
        public event EventHandler<TradeCompletionEventArgs> OnTradeCancelled;
        public event EventHandler<PhinixClientTrade.TradeUpdateEventArgs> OnTradeUpdateSuccess;
        public event EventHandler<PhinixClientTrade.TradeUpdateEventArgs> OnTradeUpdateFailure;
        public event EventHandler<PhinixClientTrade.TradesSyncedEventArgs> OnTradesSynced;
        public ITradeUiFacade TradeUi { get; }
        #endregion

        private PhinixFrameworkClient frameworkClient;
        private IFrameworkChatClientApi frameworkChatService;
        private IFrameworkTradeClientApi frameworkTradeService;
        private IClientUserDirectory frameworkUserDirectory;
        private PhinixClientItemPipeline itemPipeline;
        private PhinixDefaultTradeBehaviour defaultTradeBehaviour;
        private PhinixClientTradeCompletionPipeline tradeCompletionPipeline;

        private IClientTradeService ActiveTradeService => frameworkTradeAdapter;

        public event EventHandler<BlockedUsersChangedEventArgs> OnBlockedUsersChanged;
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

        /// <summary>
        /// Collection of UUIDs that we have created trades with and are waiting for a confirmation from the server for.
        /// Used to display the trade immediately once it's confirmed.
        /// </summary>
        private HashSet<string> waitingForTradeCreationWith = new HashSet<string>();
        /// <summary>
        /// Lock object protecting <see cref="waitingForTradeCreationWith"/>
        /// </summary>
        private object waitingForTradeCreationWithLock = new object();

        /// <summary>
        /// Collection of trades queued to be opened on the next frame.
        /// Necessary because textures and other assets can only be gotten on the main Unity thread.
        /// </summary>
        private List<ClientTradeSnapshot> tradeWindowQueue = new List<ClientTradeSnapshot>();
        /// <summary>
        /// Lock object protecting <see cref="tradeWindowQueue"/>.
        /// </summary>
        private object tradeWindowQueueLock = new object();

        public Client(ModContentPack content) : base(content)
        {
            Instance = this;

            // Apply Harmony patches
            new HarmonyLib.Harmony(PackageId).PatchAll();

            // Load in Settings
            Settings = GetSettings<Settings>();
            if (!Settings.Migrated) Settings.MigrateFromHugsLib();
            TradeUi = new ClientTradeUiFacade(this);

            // Set up our module instances
            netClient = new NetClient();
            authenticator = new ClientAuthenticator(netClient, getCredentials);
            userManager = new ClientUserManager(netClient, authenticator);
            frameworkUserDirectory = new ClientFrameworkUserDirectoryAdapter(userManager);
            itemPipeline = new PhinixClientItemPipeline(Log, FrameworkCompatibilityMode.Unknown);
            ExtensionHostContext extensionHostContext = new ExtensionHostContext
            {
                HostKind = "client",
                Log = (message, level) => Log(new LogEventArgs(message, level)),
                StorageProvider = new FileSystemExtensionStorageProvider(System.IO.Path.Combine("framework-extensions", "client"))
            };
            extensionHostContext.AddService(itemPipeline);
            extensionHostContext.AddService<ITradeItemPayloadEncoder>(itemPipeline);
            extensionHostContext.AddService(userManager);
            extensionHostContext.AddService(frameworkUserDirectory);
            frameworkClient = new PhinixFrameworkClient(netClient, authenticator, userManager, extensionHostContext);
            if (!frameworkClient.TryResolveExtensionApi(out frameworkChatService))
            {
                throw new InvalidOperationException("The built-in chat client extension did not register IFrameworkChatClientApi.");
            }

            if (!frameworkClient.TryResolveExtensionApi(out frameworkTradeService))
            {
                throw new InvalidOperationException("The built-in trade client extension did not register IFrameworkTradeClientApi.");
            }

            frameworkTradeAdapter = new FrameworkClientTradeServiceAdapter(frameworkTradeService, frameworkClient, authenticator, userManager, createFrameworkContext, Log);
            defaultTradeBehaviour = new PhinixDefaultTradeBehaviour(this, userManager, itemPipeline, dropPods, Log);
            tradeCompletionPipeline = new PhinixClientTradeCompletionPipeline(itemPipeline, Log, new DefaultTradeCompletionHandler(defaultTradeBehaviour));

            // Subscribe to log events
            authenticator.OnLogEntry += ILoggableHandler;
            userManager.OnLogEntry += ILoggableHandler;
            frameworkClient.OnLogEntry += ILoggableHandler;
            frameworkClient.OnCompatibilityModeChanged += (_, mode) =>
            {
                itemPipeline.SetCompatibilityMode(mode);
                if (mode == FrameworkCompatibilityMode.FrameworkV2)
                {
                    frameworkTradeAdapter.RequestInitialSync();
                }
            };
            frameworkChatService.HistorySynced += (_, __) => OnChatSync?.Invoke(this, EventArgs.Empty);

            #region Module Event Handlers
            // Subscribe to connection events
            netClient.OnDisconnect += (sender, args) =>
            {
                // Clear the waiting list for opening trades
                lock (waitingForTradeCreationWithLock) waitingForTradeCreationWith.Clear();
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
                if (Prefs.DevMode) Verse.Log.Message(string.Format("User with UUID {0} changed their display name from \"{1}\" to \"{2}\"", args.Uuid, args.OldDisplayName, args.NewDisplayName));
            };
            userManager.OnUserLoggedIn += (sender, args) =>
            {
                if (Prefs.DevMode) Verse.Log.Message(string.Format("User {0} logged in", args.Uuid));
            };
            userManager.OnUserLoggedOut += (sender, args) =>
            {
                if (Prefs.DevMode) Verse.Log.Message(string.Format("User {0} logged out", args.Uuid));
            };
            userManager.OnUserCreated += (sender, args) =>
            {
                if (Prefs.DevMode) Verse.Log.Message(string.Format("New user created: {0} ({1}) - {2}ogged in", args.DisplayName, args.Uuid, args.LoggedIn ? "L" : "Not l"));
            };

            // Subscribe to chat events
            frameworkClient.OnDisplayMessageReceived += (sender, args) =>
            {
                if (ShouldPlayChatNotification(args.Message))
                {
                    lock (soundQueueLock)
                    {
                        soundQueue.Add(SoundDefOf.Tick_Tiny);
                    }
                }
            };
            frameworkClient.OnCompatibilityModeChanged += (_, compatibilityMode) =>
            {
                itemPipeline.SetCompatibilityMode(compatibilityMode);
                if (compatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                {
                    requestFrameworkChatHistory();
                }
            };

            // Subscribe to trading events
            frameworkTradeAdapter.OnTradeCreationSuccess += (sender, args) => handleTradeCreationSuccess("framework trade", args);
            frameworkTradeAdapter.OnTradeCreationFailure += (sender, args) => handleTradeCreationFailure("framework trade", args);
            frameworkTradeAdapter.OnTradeCompleted += (sender, args) => handleTradeCompleted("framework trade", args);
            frameworkTradeAdapter.OnTradeCancelled += (sender, args) => handleTradeCancelled("framework trade", args);
            frameworkTradeAdapter.OnTradeUpdateFailure += (sender, args) =>
            {
                defaultTradeBehaviour.HandleTradeUpdateFailure(args);
            };
            frameworkTradeAdapter.OnTradesSynced += (sender, args) => logTradeSync("framework trade", args);
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
            frameworkClient.OnDisplayMessageReceived += (sender, e) =>
            {
                if (ShouldDisplayChatMessage(e.Message))
                {
                    OnChatMessageReceived?.Invoke(sender, e);
                }
            };
            frameworkTradeAdapter.OnTradeCreationSuccess += (sender, e) => { OnTradeCreationSuccess?.Invoke(sender, e); };
            frameworkTradeAdapter.OnTradeCreationFailure += (sender, e) => { OnTradeCreationFailure?.Invoke(sender, e); };
            frameworkTradeAdapter.OnTradeCompleted += (sender, e) => { OnTradeCompleted?.Invoke(sender, e); };
            frameworkTradeAdapter.OnTradeCancelled += (sender, e) => { OnTradeCancelled?.Invoke(sender, e); };
            frameworkTradeAdapter.OnTradeUpdateSuccess += (sender, e) => { OnTradeUpdateSuccess?.Invoke(sender, e); };
            frameworkTradeAdapter.OnTradeUpdateFailure += (sender, e) => { OnTradeUpdateFailure?.Invoke(sender, e); };
            frameworkTradeAdapter.OnTradesSynced += (sender, e) => { OnTradesSynced?.Invoke(sender, e); };

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

            OnBlockedUsersChanged?.Invoke(this, new BlockedUsersChangedEventArgs(senderUuid, true));
        }

        /// <summary>
        /// Removes a user's UUID from the blocked user list.
        /// </summary>
        /// <param name="senderUuid">UUID of the user to unblock</param>
        public void UnBlockUser(string senderUuid)
        {
            if (!Settings.BlockedUsers.Remove(senderUuid)) return;

            Settings.AcceptChanges();

            OnBlockedUsersChanged?.Invoke(this, new BlockedUsersChangedEventArgs(senderUuid, false));
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

            lock (tradeWindowQueueLock)
            {
                // Check if we have any trade windows to open
                while (tradeWindowQueue.Any())
                {
                    // Dequeue and open the window
                    Find.WindowStack.Add(new TradeWindow(tradeWindowQueue.Pop()));
                }
            }
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

        /// <summary>
        /// Gets the current chat message buffer, optionally marking them as read.
        /// </summary>
        /// <param name="markAsRead">Whether to mark the messages as read</param>
        /// <param name="unreadOnly">Whether to only get unread messages</param>
        /// <returns>List of chat messages</returns>
        public UIChatMessage[] GetChatMessages(bool markAsRead = true, bool unreadOnly = false)
        {
            if (unreadOnly)
            {
                return frameworkChatService.BuildUiMessages(frameworkClient.GetUnreadDisplayMessages(markAsRead), frameworkUserDirectory);
            }

            if (markAsRead)
            {
                frameworkClient.MarkAsRead();
            }

            return frameworkChatService.BuildUiMessages(frameworkClient.GetDisplayMessages(), frameworkUserDirectory);
        }

        /// <summary>
        /// Tries to get the chat message with the given ID.
        /// </summary>
        /// <param name="messageId">ID of the chat message to retrieve</param>
        /// <param name="message">Chat message output</param>
        /// <returns>Whether the chat message was retrieved successfully</returns>
        public bool TryGetMessage(string messageId, out UIChatMessage message)
        {
            return frameworkChatService.TryGetUiMessage(frameworkClient.GetDisplayMessages(), messageId, frameworkUserDirectory, out message);
        }

        /// <summary>
        /// Creates a trade with the given user.
        /// </summary>
        /// <param name="uuid">Other party's UUID</param>
        /// <exception cref="ArgumentException">UUID cannot be null or empty</exception>
        public void CreateTrade(string uuid)
        {
            if (string.IsNullOrEmpty(uuid))
            {
                throw new ArgumentException("UUID cannot be null or empty", nameof(uuid));
            }

            // Add the other party to the waiting list so we can open it immediately
            lock (waitingForTradeCreationWithLock)
            {
                waitingForTradeCreationWith.Add(uuid);
            }

            ActiveTradeService.CreateTrade(uuid);
        }

        private void handleTradeCreationSuccess(string logLabel, TradeCreationEventArgs args)
        {
            if (Prefs.DevMode) Verse.Log.Message(string.Format("Created {0} {1} with {2}", logLabel, args.TradeId, args.OtherPartyUuid));
            defaultTradeBehaviour.HandleTradeCreationSuccess(args, Settings.ShowBlockedTrades, Settings.BlockedUsers, waitingForTradeCreationWith, waitingForTradeCreationWithLock, tradeWindowQueue, tradeWindowQueueLock);
        }

        private void handleTradeCreationFailure(string logLabel, TradeCreationEventArgs args)
        {
            if (Prefs.DevMode) Verse.Log.Message(string.Format("Failed to create {0} with {1}: {2} ({3})", logLabel, args.OtherPartyUuid, args.FailureMessage, args.FailureReason.ToString()));

            Find.WindowStack.Add(new Dialog_MessageBox(title: "Phinix_error_tradeCreationFailedTitle".Translate(), text: "Phinix_error_tradeCreationFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString())));

            lock (waitingForTradeCreationWithLock) waitingForTradeCreationWith.Remove(args.OtherPartyUuid);
        }

        private void handleTradeCompleted(string logLabel, TradeCompletionEventArgs args)
        {
            tradeCompletionPipeline.HandleTradeCompleted(args);

            if (Prefs.DevMode) Verse.Log.Message(string.Format("{0} with {1} completed successfully", capitalize(logLabel), args.OtherPartyUuid));
        }

        private void handleTradeCancelled(string logLabel, TradeCompletionEventArgs args)
        {
            defaultTradeBehaviour.HandleTradeCancelled(args, Settings.ShowBlockedTrades, Settings.BlockedUsers);

            if (Prefs.DevMode) Verse.Log.Message(string.Format("{0} with {1} cancelled", capitalize(logLabel), args.OtherPartyUuid));
        }

        private static void logTradeSync(string logLabel, PhinixClientTrade.TradesSyncedEventArgs args)
        {
            if (Prefs.DevMode) Verse.Log.Message(string.Format("Synced {0} {1}{2} from server", args.TradeIds.Length, logLabel, args.TradeIds.Length != 1 ? "s" : ""));
        }

        private static string capitalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length == 1)
            {
                return value.ToUpperInvariant();
            }

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private ClientFrameworkContext createFrameworkContext()
        {
            return new ClientFrameworkContext
            {
                CompatibilityMode = frameworkClient.CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendMessage = frameworkClient.SendFrameworkPacket,
                RemoteCapabilities = Array.Empty<string>(),
                HasRemoteCapability = frameworkClient.HasRemoteCapability,
                Log = (message, level) => Log(new LogEventArgs(message, level))
            };
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

        private void requestFrameworkChatHistory()
        {
            frameworkChatService.RequestHistory(frameworkClient, Authenticated, LoggedIn, SessionId, Uuid);
        }
    }
}
