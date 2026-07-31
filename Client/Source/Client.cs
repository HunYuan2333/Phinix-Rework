using System.Reflection;
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
using TaggedString = Verse.TaggedString;
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

        public void SendMessage(string message)
        {
            frameworkClient.TryHandleOutgoingMessage(message);
        }
        // 缓存解析结果——设计哲学 §8.3 要求属性 getter 不做实时查询/分配。
        // 扩展注册后不会变化，因此解析一次后永不过期。
        private IReadOnlyList<IMainTabProvider> cachedMainTabProviders;
        private IReadOnlyList<IServerSidebarProvider> cachedSidebarProviders;
        private IReadOnlyList<INoticeBannerProvider> cachedBannerProviders;

        public IReadOnlyList<IMainTabProvider> MainTabProviders
        {
            get
            {
                if (cachedMainTabProviders == null)
                    cachedMainTabProviders = frameworkClient?.ResolveExtensionApis<IMainTabProvider>() ?? (IReadOnlyList<IMainTabProvider>)Array.Empty<IMainTabProvider>();
                return cachedMainTabProviders;
            }
        }
        public IReadOnlyList<IServerSidebarProvider> SidebarProviders
        {
            get
            {
                if (cachedSidebarProviders == null)
                    cachedSidebarProviders = frameworkClient?.ResolveExtensionApis<IServerSidebarProvider>() ?? (IReadOnlyList<IServerSidebarProvider>)Array.Empty<IServerSidebarProvider>();
                return cachedSidebarProviders;
            }
        }
        public IReadOnlyList<INoticeBannerProvider> BannerProviders
        {
            get
            {
                if (cachedBannerProviders == null)
                    cachedBannerProviders = frameworkClient?.ResolveExtensionApis<INoticeBannerProvider>() ?? (IReadOnlyList<INoticeBannerProvider>)Array.Empty<INoticeBannerProvider>();
                return cachedBannerProviders;
            }
        }
        #endregion

        private PhinixFrameworkClient frameworkClient;
        public PhinixFrameworkClient FrameworkClient => frameworkClient;
        private ClientUserEventStream userEventStream;
        private ClientMainThreadDispatcher mainThreadDispatcher;
        private readonly IClientWindowService windowService;
        private readonly IClientSettingsContext settingsContext;
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
            // Set up our module instances
            netClient = new NetClient();
            authenticator = new ClientAuthenticator(netClient, getCredentials);
            userManager = new ClientUserManager(netClient, authenticator);
            IClientUserDirectory frameworkUserDirectory = new ClientFrameworkUserDirectoryAdapter(userManager);
            IClientSessionContext sessionContext = new ClientSessionContextAdapter(authenticator, userManager);
            settingsContext = new ClientSettingsContextAdapter(this);
            windowService = new ClientWindowService();
            userEventStream = new ClientUserEventStream();
            mainThreadDispatcher = new ClientMainThreadDispatcher();
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
            extensionHostContext.AddService<Action<bool>>(acceptingTrades => userManager.UpdateSelf(acceptingTrades: acceptingTrades));
            // 注册原始模块传输能力 —— 任何插件都能用此接口直接操作 NetClient 的原始模块通信
            extensionHostContext.AddService<ILegacyModuleTransport>(new NetClientLegacyTransportAdapter(netClient));
            extensionHostContext.ResolveSourcePackageId = ResolveModPackageId;
            string modRoot = content.RootDir?.ToString();
            IUiTheme uiTheme = new UiTheme(modRoot);
            extensionHostContext.AddService<IUiTheme>(uiTheme);
            Verse.Log.Message($"[Phinix] Loading extensions, probe dirs: {string.Join("; ", GetExtensionProbeDirectories(modRoot))}");
            ExtensionAssemblyLoader.LoadAssemblies(
                GetExtensionProbeDirectories(modRoot),
                (message, level) =>
                {
                    // Always pass through to the log handler so warnings/errors are visible
                    // even when DevMode is off
                    Log(new LogEventArgs(message, level));
                });

            // 构建扩展依赖图（纯反射，不实例化模块）——在 DiscoverExtensions 之前就绪
            // 《插件启用禁用与扩展管理实施方案》§4.7：host 先 ScanCandidateModuleTypes
            // → Build graph → 构造 policy → 注册服务
            List<Type> candidateModuleTypes = PhinixExtensionRegistry.ScanCandidateModuleTypes();
            ExtensionDependencyGraph dependencyGraph = ExtensionDependencyGraph.Build(candidateModuleTypes);
            foreach (string graphWarning in dependencyGraph.BuildWarnings)
            {
                Verse.Log.Warning($"[Phinix] {graphWarning}");
            }

            // 构造 v1 静态激活策略并注册为服务——DiscoverExtensions 将从 hostContext 解析此策略
            // 设计哲学 §1.1 插件平权：全部扩展可禁用，host 不硬编码"谁不可禁用"
            var activationPolicy = new StaticActivationPolicy(Settings.DisabledExtensions, dependencyGraph);
            extensionHostContext.AddService<IExtensionActivationPolicy>(activationPolicy);

            // 注册 host 核心的 ExtensionManagerTab——必须在 new PhinixFrameworkClient 之前注册，
            // 因为 DiscoverExtensions 内部 module.Register(builder) 会向同一个 ApiRegistry 注册插件的 provider，
            // 随后 MainTabProviders 的 lazy cache 在首次被读取时一次性解析全部 provider。
            // 如果在此之后注册，cache 已被填充为仅含 Chat/Trade，ExtensionManagerTab 会被遗漏。
            extensionHostContext.ApiRegistry.RegisterApi<IMainTabProvider>("builtin.host", new ExtensionManagerTab());
            // host 核心的扩展管理设置面板（Mod Settings 页，Order=50）。
            // 与 ExtensionManagerTab 共用 ExtensionDisplayState 静态计算，勾选后两处显示一致。
            extensionHostContext.ApiRegistry.RegisterApi<IClientSettingsPanelProvider>(
                "builtin.host", new ExtensionControlSettingsPanelProvider());
            // 通用 UI 主题同时注册为 API——扩展管理 Tab 等 host UI 通过 ResolveExtensionApis 解析。
            // 插件仍通过 GetRequiredService&lt;IUiTheme&gt; 使用服务方式，两种通道并存，互不冲突。
            extensionHostContext.ApiRegistry.RegisterApi<IUiTheme>("builtin.host", uiTheme);

            Verse.Log.Message("[Phinix] Constructing framework client and discovering extensions...");
            frameworkClient = new PhinixFrameworkClient(netClient, authenticator, userManager, extensionHostContext);
            Verse.Log.Message($"[Phinix] Framework client ready. MainTabProviders={MainTabProviders.Count}, SidebarProviders={SidebarProviders.Count}");
            if (!Settings.Migrated)
            {
                Settings.MigrateLegacySettings(settingsContext, frameworkClient.ResolveExtensionApis<IClientLegacySettingsMigrator>());
            }
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
                userManager.SendLogin(displayName: Settings.DisplayName);
            };
            authenticator.OnAuthenticationFailure += (sender, args) =>
            {
                Verse.Log.Message(string.Format("Failed to authenticate with server: {0} ({1})", args.FailureMessage, args.FailureReason.ToString()));

                enqueueWindowOpen(mainThreadDispatcher, windowService, new Dialog_MessageBox(
                    title: "Phinix_error_authFailedTitle".Translate(),
                    text: "Phinix_error_authFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString())));

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

                enqueueWindowOpen(mainThreadDispatcher, windowService, new Dialog_MessageBox(
                    title: "Phinix_error_loginFailedTitle".Translate(),
                    text: "Phinix_error_loginFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString())));

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

            // Show warning notification if extensions had issues during loading
            if (frameworkClient.HasWarnings)
            {
                Verse.Log.Warning($"[Phinix] {frameworkClient.WarningCount} extension warning(s) during startup:");
                foreach (string warning in frameworkClient.ExtensionWarnings)
                {
                    Verse.Log.Warning($"  [Phinix] {warning}");
                }
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Host 核心设置：连接、显示名称、音效等通用配置。
            // 设计哲学 §1.3：host 只做通用服务；§2.3：减少硬编码。
            float listingWidth = Math.Min(600f, inRect.width / 2);

            Listing_Standard listing = new Listing_Standard()
            {
                ColumnWidth = listingWidth
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

            // 插件化设置面板：动态收集各扩展注册的 IClientSettingsPanelProvider，
            // 按 Order 排序后在同一 listing 流内绘制。不再硬编码 Chat/Trade 设置键。
            if (frameworkClient != null)
            {
                IReadOnlyList<IClientSettingsPanelProvider> panels = frameworkClient.GetSettingsPanels();
                bool firstPanel = true;
                foreach (IClientSettingsPanelProvider panel in panels.OrderBy(p => p.Order))
                {
                    if (!panel.IsVisible(settingsContext))
                    {
                        continue;
                    }

                    try
                    {
                        if (firstPanel)
                        {
                            firstPanel = false;
                            listing.GapLine();
                        }
                        else
                        {
                            listing.Gap(6f);
                        }

                        // SectionId 可能直接是翻译键（如扩展管理面板），先 Translate 再展示；
                        // 非键值（如 "chat.display"）Translate 原样返回，行为不变。
                        TaggedString sectionLabel = panel.SectionId.ToString().Translate();
                        listing.Label(sectionLabel, -1f, TaggedString.Empty);
                        panel.DrawSettings(listing, settingsContext);
                    }
                    catch (Exception ex)
                    {
                        Verse.Log.Error($"[Phinix] Settings panel '{panel.SectionId}' (Order={panel.Order}) threw: {ex}");
                    }
                }
            }

            listing.End();
        }

        public override void WriteSettings()
        {
            if (!Settings.IsChanged) return;

            Settings.AcceptChanges();
            userManager.UpdateSelf(Settings.DisplayName);
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
            catch (Exception ex)
            {
                Verse.Log.Error($"[Phinix] Could not connect to {Settings.ServerAddress}:{Settings.ServerPort}: {ex}");

                enqueueWindowOpen(mainThreadDispatcher, windowService, new Dialog_MessageBox(
                    title: "Phinix_error_connectionFailedTitle".Translate(),
                    text: "Phinix_error_connectionFailedMessage".Translate(Settings.ServerAddress, Settings.ServerPort)));
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

        private static IEnumerable<string> GetExtensionProbeDirectories(string modRootDir = null)
        {
            // Assembly.Location 在 Prepatcher / AssemblyLoadContext 等环境下可能返回 ""
            // 导致 Path.GetDirectoryName("") 抛出 ArgumentException。
            // 设计哲学 §3.9：启动期文件定位优先用 RimWorld 框架提供的稳定入口。
            string clientAssemblyDirectory = null;
            try { clientAssemblyDirectory = Path.GetDirectoryName(typeof(Client).Assembly.Location); }
            catch (ArgumentException) { }

            string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            if (!string.IsNullOrEmpty(clientAssemblyDirectory))
            {
                // 正常路径：行为完全不变
                yield return clientAssemblyDirectory;
                yield return Path.GetFullPath(Path.Combine(clientAssemblyDirectory, "..", "..", "Common", "Assemblies"));
                yield return Path.GetFullPath(Path.Combine(clientAssemblyDirectory, "..", "..", "Common", "Extensions"));
            }
            else if (!string.IsNullOrEmpty(modRootDir))
            {
                // 降级路径：从 ModContentPack.RootDir 直接推导
                yield return Path.Combine(modRootDir, "Common", "Assemblies");
                yield return Path.Combine(modRootDir, "Common", "Extensions");
            }

            if (!string.IsNullOrEmpty(appBaseDirectory))
            {
                yield return appBaseDirectory;
            }

            // 新增：扫描所有活跃 mod 的 Assemblies 目录（第三方 submod 发现）
            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod == null || !mod.Active) continue;
                if (string.Equals(mod.PackageId, PackageId, StringComparison.OrdinalIgnoreCase)) continue;

                string asmDir = System.IO.Path.Combine(mod.RootDir?.ToString() ?? "", "Assemblies");
                if (System.IO.Directory.Exists(asmDir)) yield return asmDir;
            }
        }

        /// <summary>
        /// Resolves the RimWorld Mod packageId for a given assembly by checking
        /// which installed mod's directory contains the assembly file.
        /// </summary>
        private static string ResolveModPackageId(Assembly assembly)
        {
            try
            {
                string assemblyPath = assembly.Location;
                if (string.IsNullOrEmpty(assemblyPath)) return null;

                foreach (ModMetaData mod in ModLister.AllInstalledMods)
                {
                    string rootDir = mod.RootDir?.FullName ?? mod.RootDir?.ToString();
                    if (!string.IsNullOrEmpty(rootDir) &&
                        assemblyPath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return mod.PackageId;
                    }
                }
            }
            catch (Exception ex)
            {
                // Assembly.Location can throw in some contexts; log and return null
                Verse.Log.Warning($"[Phinix] ResolveModPackageId failed for assembly '{assembly.FullName}': {ex.Message}");
            }

            return null;
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
#if DEBUG
                    if (Prefs.DevMode) Verse.Log.Message(args.Message);
#else
                    // Release 构建隐藏 DEBUG 级日志（管线消息路由、布局诊断、用户包等），
                    // 避免聊天消息与连接/断开日志刷屏；INFO/WARNING/ERROR 保留
#endif
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

            enqueueWindowOpen(mainThreadDispatcher, windowService, new CredentialsWindow
            {
                SessionId = sessionId,
                ServerName = serverName,
                ServerDescription = serverDescription,
                AuthType = authType,
                CredentialsCallback = callback
            });
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

        private static void enqueueWindowOpen(IClientMainThreadDispatcher dispatcher, IClientWindowService windowService, Window window)
        {
            if (window == null)
            {
                return;
            }

            dispatcher?.Enqueue(() => windowService?.Open(window));
        }

    }
}
