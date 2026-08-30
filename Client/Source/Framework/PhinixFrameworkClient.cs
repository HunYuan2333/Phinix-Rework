using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Authentication;
using Connections;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;

namespace PhinixClient.Framework
{
    public class PhinixFrameworkClient : ILoggable, IFrameworkClientTransport, IFrameworkClientCommandTransport, IFrameworkClientLifecycle, IClientDisplayMessageStore, IClientDisplayMessageFeed, IDisplayMessageSink, IItemCodecProvider, IDisposable
    {
        public event EventHandler<LogEventArgs> OnLogEntry;

        public event EventHandler<FrameworkDisplayMessageEventArgs> OnDisplayMessageReceived;

        event EventHandler<FrameworkDisplayMessageEventArgs> IClientDisplayMessageFeed.DisplayMessageReceived
        {
            add => OnDisplayMessageReceived += value;
            remove => OnDisplayMessageReceived -= value;
        }

        public event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;

        public void RaiseLogEntry(LogEventArgs e)
        {
            if (e != null)
            {
                appendExtensionLog(e.Message, e.LogLevel);
            }

            OnLogEntry?.Invoke(this, e);
        }

        public FrameworkCompatibilityMode CompatibilityMode { get; private set; } = FrameworkCompatibilityMode.Unknown;

        private readonly NetClient netClient;
        private readonly ClientAuthenticator authenticator;
        private readonly ClientUserManager userManager;
        private readonly ExtensionHostContext extensionHostContext;
        private readonly DiscoveredPhinixExtensions discoveredExtensions;
        private readonly string[] capabilities;
        private readonly HashSet<string> remoteCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FrameworkDisplayMessage> displayMessages = new List<FrameworkDisplayMessage>();
        private const int MaxDisplayMessages = 1000;
        private readonly object displayMessagesLock = new object();
        private readonly Timer negotiationTimer;
        private readonly ElapsedEventHandler negotiationElapsedHandler;
        private readonly EventHandler disconnectHandler;
        private const int MaxExtensionLogEntries = 300;
        private readonly object extensionLogLock = new object();
        private readonly List<FrameworkLogEntry> extensionLog = new List<FrameworkLogEntry>();
        private int extensionLogVersion;
        private int displayMessageCountAtLastCheck;
        private bool disposed;

        public PhinixFrameworkClient(NetClient netClient, ClientAuthenticator authenticator, ClientUserManager userManager, ExtensionHostContext extensionHostContext = null)
        {
            this.netClient = netClient;
            this.authenticator = authenticator;
            this.userManager = userManager;
            this.extensionHostContext = extensionHostContext ?? ExtensionHostContext.Empty;
            this.extensionHostContext.AddService<IFrameworkClientTransport>(this);
            this.extensionHostContext.AddService<IFrameworkClientCommandTransport>(this);
            this.extensionHostContext.AddService<IFrameworkClientLifecycle>(this);
            this.extensionHostContext.AddService<IClientDisplayMessageStore>(this);
            this.extensionHostContext.AddService<IClientDisplayMessageFeed>(this);
            this.extensionHostContext.AddService<IDisplayMessageSink>(this);
            this.extensionHostContext.AddService<IItemCodecProvider>(this);
            this.discoveredExtensions = PhinixExtensionRegistry.DiscoverExtensions(this.extensionHostContext);
            this.capabilities = PhinixExtensionRegistry.CollectCapabilities(discoveredExtensions);
            PhinixExtensionRegistry.ActivateExtensions(discoveredExtensions, this.extensionHostContext);
            this.negotiationTimer = new Timer
            {
                AutoReset = false,
                Enabled = false,
                Interval = 3000
            };
            negotiationElapsedHandler = (_, __) => enterLegacyMode();
            this.negotiationTimer.Elapsed += negotiationElapsedHandler;

            netClient.RegisterPacketHandler(FrameworkProtocol.ModuleName, packetHandler);
            disconnectHandler = (_, __) => reset();
            netClient.OnDisconnect += disconnectHandler;

            // Log discovery summary through both channels: RaiseLogEntry for
            // subscribers (wired up after construction) and hostContext.Log so
            // the diagnostics are visible even if no one has hooked OnLogEntry yet.
            string summary = $"Discovered {discoveredExtensions.Extensions.Count} framework extension(s) and {capabilities.Length} capability/capabilities.";
            RaiseLogEntry(new LogEventArgs(summary));
            this.extensionHostContext.Log?.Invoke(summary, LogLevel.INFO);

            if (discoveredExtensions.Modules.Count > 0)
            {
                string moduleSummary =
                    $"Framework modules: {string.Join(", ", discoveredExtensions.Modules.Select(module => module.ExtensionId).OrderBy(extensionId => extensionId))}. " +
                    $"Client handlers={discoveredExtensions.ClientMessageHandlers.Count}, client commands={discoveredExtensions.ClientCommandHandlers.Count}, renderers={discoveredExtensions.MessageRenderers.Count}, item codecs={discoveredExtensions.ItemCodecs.Count}, " +
                    $"client item handlers={discoveredExtensions.ClientIncomingItemHandlers.Count}, client outgoing item handlers={discoveredExtensions.ClientOutgoingItemHandlers.Count}.";
                RaiseLogEntry(new LogEventArgs(moduleSummary));
                this.extensionHostContext.Log?.Invoke(moduleSummary, LogLevel.INFO);
            }

            // Settings panels summary: concise copyable machine-readable format
            IReadOnlyList<IClientSettingsPanelProvider> settingsPanels = GetSettingsPanels();
            if (settingsPanels.Count > 0)
            {
                string panelSummary = "SettingsPanels=" + string.Join(",", settingsPanels.OrderBy(p => p.Order).Select(p =>
                    $"{{SectionId:{p.SectionId},Order:{p.Order}}}"));
                RaiseLogEntry(new LogEventArgs(panelSummary, LogLevel.DEBUG));
                this.extensionHostContext.Log?.Invoke(panelSummary, LogLevel.DEBUG);
                // Also emit a human-readable version
                string humanSummary = $"Settings panels ({settingsPanels.Count}): {string.Join(" | ", settingsPanels.OrderBy(p => p.Order).Select(p => p.SectionId))}";
                RaiseLogEntry(new LogEventArgs(humanSummary, LogLevel.INFO));
                this.extensionHostContext.Log?.Invoke(humanSummary, LogLevel.INFO);
            }

            foreach (string diagnostic in discoveredExtensions.Diagnostics)
            {
                RaiseLogEntry(new LogEventArgs(diagnostic, LogLevel.DEBUG));
                this.extensionHostContext.Log?.Invoke(diagnostic, LogLevel.DEBUG);
            }
            foreach (string warning in discoveredExtensions.Warnings)
            {
                RaiseLogEntry(new LogEventArgs(warning, LogLevel.WARNING));
                this.extensionHostContext.Log?.Invoke(warning, LogLevel.WARNING);
            }
        }

        public IReadOnlyList<ExtensionDiscoveryResult> ExtensionResults => discoveredExtensions.ExtensionResults.AsReadOnly();

        public ExtensionDependencyGraph ExtensionDependencyGraph => discoveredExtensions.DependencyGraph;

        public IReadOnlyList<string> ExtensionDiagnostics => discoveredExtensions.Diagnostics.AsReadOnly();

        public IReadOnlyList<string> ExtensionWarnings => discoveredExtensions.Warnings.AsReadOnly();

        public bool HasWarnings => discoveredExtensions.Warnings.Count > 0;

        public int WarningCount => discoveredExtensions.Warnings.Count;

        /// <summary>
        /// 扩展管理面板日志缓冲版本号。UI 仅在版本变化时重取快照，避免每帧复制。
        /// 设计哲学 §3.6：缓冲有容量上限（<see cref="MaxExtensionLogEntries"/>），超出移除最旧条目。
        /// </summary>
        public int ExtensionLogVersion
        {
            get
            {
                lock (extensionLogLock)
                {
                    return extensionLogVersion;
                }
            }
        }

        /// <summary>
        /// 返回扩展管理日志快照（最新在前）。调用方应缓存，仅在 <see cref="ExtensionLogVersion"/> 变化时重取。
        /// </summary>
        public IReadOnlyList<FrameworkLogEntry> GetExtensionLogSnapshot()
        {
            lock (extensionLogLock)
            {
                return new List<FrameworkLogEntry>(extensionLog);
            }
        }

        public IReadOnlyList<IItemCodec> ItemCodecs => discoveredExtensions.ItemCodecs.AsReadOnly();

        public void BeginNegotiation()
        {
            if (disposed) return;

            reset();

            if (!authenticator.Authenticated || !userManager.LoggedIn) return;

            negotiationTimer.Start();
            RaiseLogEntry(new LogEventArgs($"[Phinix] Starting capability negotiation with server. Capabilities: {string.Join(", ", capabilities)}", LogLevel.INFO));
            sendPacket(new FrameworkPacket
            {
                Kind = FrameworkProtocol.KindHello,
                SessionId = authenticator.SessionId,
                SenderUuid = userManager.Uuid,
                PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkCapabilitiesPayload
                {
                    Capabilities = capabilities.ToList()
                })
            });
        }

        public bool TryHandleOutgoingMessage(string rawMessage)
        {
            if (disposed) return false;

            RaiseLogEntry(new LogEventArgs(
                $"[Phinix] TryHandleOutgoingMessage: mode={CompatibilityMode}, textLen={rawMessage?.Length ?? 0}, handlers={discoveredExtensions.ClientMessageHandlers.Count}",
                LogLevel.DEBUG));

            foreach (IClientMessageHandler handler in discoveredExtensions.ClientMessageHandlers.Where(handler => handler.CanHandleOutgoingText(rawMessage)))
            {
                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] TryHandleOutgoingMessage: handler={handler.GetType().Name}(P={handler.Priority}) claims it can handle",
                    LogLevel.DEBUG));

                ClientOutgoingMessageResult result = null;
                try
                {
                    result = handler.HandleOutgoingText(
                        rawMessage,
                        new ClientFrameworkContext
                        {
                            CompatibilityMode = CompatibilityMode,
                            SenderUuid = userManager.Uuid,
                            SessionId = authenticator.SessionId,
                            SendMessage = sendPacket,
                            RemoteCapabilities = remoteCapabilities.ToArray(),
                            HasRemoteCapability = hasRemoteCapability,
                            Log = (logMessage, level) => RaiseLogEntry(new LogEventArgs(logMessage, level))
                        }
                    );
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"Message handler {handler.GetType().FullName} threw for outgoing text: {ex}", LogLevel.ERROR));
                    continue;
                }

                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] TryHandleOutgoingMessage: handler={handler.GetType().Name} result action={result?.Action}, message={(result?.Message == null ? "null" : "present")}",
                    LogLevel.DEBUG));

                if (result == null)
                {
                    // null result 视为 continue，让下一个 handler 处理
                    continue;
                }

                if (result.Action == MessageHandlingResultAction.LegacyFallback)
                {
                    // Handler 声明自己不支持当前模式，继续尝试下一个 handler。
                    // 用于 Adapter 在 FrameworkV2 模式下放行消息到后续 Chat handler。
                    continue;
                }

                FrameworkPacket outgoingMessage = result.Message;
                if (outgoingMessage != null && !string.IsNullOrEmpty(outgoingMessage.MessageType) && !hasRemoteCapability(outgoingMessage.MessageType))
                {
                    showSystemMessage("Phinix_framework_remoteCapabilityUnavailable", outgoingMessage.MessageType);
                    return true;
                }

                if (outgoingMessage == null)
                {
                    if (result.Action == MessageHandlingResultAction.Continue) continue;
                    return true;
                }

                outgoingMessage.Kind = FrameworkProtocol.KindMessage;
                outgoingMessage.SessionId = authenticator.SessionId;
                outgoingMessage.SenderUuid = userManager.Uuid;
                sendPacket(outgoingMessage);

                if (result.Action != MessageHandlingResultAction.Continue)
                {
                    return true;
                }
            }

            RaiseLogEntry(new LogEventArgs(
                $"[Phinix] TryHandleOutgoingMessage: no handler ultimately processed the message — returning false",
                LogLevel.WARNING));
            return false;
        }

        public bool TryHandleOutgoingCommand(FrameworkPacket command)
        {
            if (disposed) return false;
            if (command == null) return false;

            RaiseLogEntry(new LogEventArgs(
                $"[Phinix] TryHandleOutgoingCommand: msgType={command.MessageType}, mode={CompatibilityMode}",
                LogLevel.DEBUG));

            ClientFrameworkContext context = new ClientFrameworkContext
            {
                CompatibilityMode = CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendMessage = sendPacket,
                RemoteCapabilities = remoteCapabilities.ToArray(),
                HasRemoteCapability = hasRemoteCapability,
                Log = (msg, level) => RaiseLogEntry(new LogEventArgs(msg, level))
            };

            // 从已发现扩展中筛选实现了 IClientOutgoingCommandHandler 的实例，
            // 按 Priority 排序。优先级数字越小越先执行。
            // 用 is 检测而非在 DiscoverExtensions 中预收集，因为
            // IClientOutgoingCommandHandler 是纯增量接口，不需要修改 registry。
            IEnumerable<IClientOutgoingCommandHandler> handlers = discoveredExtensions.Extensions
                .OfType<IClientOutgoingCommandHandler>()
                .OrderBy(h => h.Priority)
                .ToList();

            RaiseLogEntry(new LogEventArgs(
                $"[Phinix] TryHandleOutgoingCommand: found {handlers.Count()} handler(s) — " +
                string.Join(", ", handlers.Select(h => $"{h.GetType().Name}(P={h.Priority})")),
                LogLevel.DEBUG));

            bool anyHandlerTried = false;
            foreach (IClientOutgoingCommandHandler handler in handlers)
            {
                bool canHandle = handler.CanHandleOutgoingCommand(command);
                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix]   {handler.GetType().Name}(P={handler.Priority}).CanHandle → {canHandle}",
                    LogLevel.DEBUG));

                if (!canHandle) continue;

                anyHandlerTried = true;
                ClientOutgoingCommandResult result = null;
                try
                {
                    result = handler.HandleOutgoingCommand(command, context);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"[Phinix] Outgoing command handler {handler.GetType().FullName} threw: {ex}", LogLevel.ERROR));
                    continue;
                }

                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix]   {handler.GetType().Name}.HandleOutgoing → Action={result?.Action}, Command={(result?.Command == null ? "null" : "present")}",
                    LogLevel.DEBUG));

                if (result == null || result.Action == MessageHandlingResultAction.Continue)
                    continue;

                FrameworkPacket outgoingCommand = result.Command;
                if (outgoingCommand == null)
                {
                    // Handler 声明已处理（如 LegacyAdapter 翻译后经 ILegacyModuleTransport 发送），
                    // 框架不再发送 FrameworkPacket。
                    if (result.Action == MessageHandlingResultAction.Handled)
                    {
                        RaiseLogEntry(new LogEventArgs(
                            $"[Phinix] TryHandleOutgoingCommand: handled by {handler.GetType().Name} (no FrameworkPacket to send)",
                            LogLevel.DEBUG));
                        return true;
                    }
                    continue;
                }

                // 确保 Kind/SessionId/SenderUuid 正确设置
                outgoingCommand.Kind = FrameworkProtocol.KindCommand;
                outgoingCommand.SessionId = authenticator.SessionId;
                outgoingCommand.SenderUuid = userManager.Uuid;
                sendPacket(outgoingCommand);

                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] TryHandleOutgoingCommand: sent FrameworkPacket via sendPacket() from {handler.GetType().Name}",
                    LogLevel.DEBUG));

                if (result.Action != MessageHandlingResultAction.Continue) return true;
            }

            if (!anyHandlerTried)
            {
                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] TryHandleOutgoingCommand: NO handler claimed '{command.MessageType}' — returning false",
                    LogLevel.WARNING));
            }

            return false;
        }

        public bool TryHandleOutgoingItem(FrameworkItemPayload itemPayload)
        {
            if (disposed) return false;
            if (itemPayload == null || string.IsNullOrEmpty(itemPayload.CodecId)) return false;

            RaiseLogEntry(new LogEventArgs(
                $"[Phinix] TryHandleOutgoingItem: codecId={itemPayload.CodecId}, mode={CompatibilityMode}",
                LogLevel.DEBUG));

            ClientFrameworkContext context = new ClientFrameworkContext
            {
                CompatibilityMode = CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendMessage = sendPacket,
                RemoteCapabilities = remoteCapabilities.ToArray(),
                HasRemoteCapability = hasRemoteCapability,
                Log = (msg, level) => RaiseLogEntry(new LogEventArgs(msg, level))
            };

            bool anyHandlerTried = false;
            foreach (IClientOutgoingItemHandler handler in discoveredExtensions.ClientOutgoingItemHandlers)
            {
                bool canHandle;
                try
                {
                    canHandle = handler.CanHandleOutgoingItem(itemPayload);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"[Phinix] Outgoing item handler {handler.GetType().FullName}.CanHandle threw: {ex}", LogLevel.ERROR));
                    continue;
                }

                if (!canHandle) continue;

                anyHandlerTried = true;
                ClientOutgoingItemResult result = null;
                try
                {
                    result = handler.HandleOutgoingItem(itemPayload, context);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"[Phinix] Outgoing item handler {handler.GetType().FullName} threw: {ex}", LogLevel.ERROR));
                    continue;
                }

                if (result == null || result.Action == ItemHandlingResultAction.Continue)
                    continue;

                FrameworkPacket outgoingItem = result.Item;
                if (outgoingItem == null)
                {
                    if (result.Action == ItemHandlingResultAction.Handled)
                    {
                        RaiseLogEntry(new LogEventArgs(
                            $"[Phinix] TryHandleOutgoingItem: handled by {handler.GetType().Name} (no FrameworkPacket to send)",
                            LogLevel.DEBUG));
                        return true;
                    }
                    continue;
                }

                outgoingItem.Kind = FrameworkProtocol.KindItem;
                outgoingItem.Flow = global::Phinix.Framework.FrameworkFlow.Item;
                outgoingItem.SessionId = authenticator.SessionId;
                outgoingItem.SenderUuid = userManager.Uuid;
                sendPacket(outgoingItem);

                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] TryHandleOutgoingItem: sent FrameworkPacket via sendPacket() from {handler.GetType().Name}",
                    LogLevel.DEBUG));

                if (result.Action != ItemHandlingResultAction.Continue) return true;
            }

            if (!anyHandlerTried)
            {
                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] TryHandleOutgoingItem: NO handler claimed item payload (codecId='{itemPayload.CodecId}') — returning false",
                    LogLevel.WARNING));
            }

            return false;
        }

        public void MarkAsRead()
        {
            lock (displayMessagesLock)
            {
                displayMessageCountAtLastCheck = displayMessages.Count;
            }
        }

        public int UnreadMessages
        {
            get
            {
                lock (displayMessagesLock)
                {
                    return displayMessages.Count - displayMessageCountAtLastCheck;
                }
            }
        }

        public FrameworkDisplayMessage[] GetUnreadDisplayMessages(bool markAsRead = true)
        {
            lock (displayMessagesLock)
            {
                List<FrameworkDisplayMessage> unreadMessages = displayMessages
                    .Skip(displayMessageCountAtLastCheck)
                    .ToList();

                if (markAsRead)
                {
                    displayMessageCountAtLastCheck = displayMessages.Count;
                }

                return unreadMessages.ToArray();
            }
        }

        public FrameworkDisplayMessage[] GetDisplayMessages()
        {
            lock (displayMessagesLock)
            {
                return displayMessages.ToArray();
            }
        }

        public bool HasRemoteCapability(string capability)
        {
            return hasRemoteCapability(capability);
        }

        public void SendFrameworkPacket(FrameworkPacket packet)
        {
            if (disposed) return;

            if (packet == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(packet.SessionId))
            {
                packet.SessionId = authenticator.SessionId;
            }

            if (string.IsNullOrEmpty(packet.SenderUuid))
            {
                packet.SenderUuid = userManager.Uuid;
            }

            sendPacket(packet);
        }

        public bool TryResolveExtensionApi<T>(out T implementation) where T : class
        {
            if (discoveredExtensions.ApiRegistry != null)
            {
                return discoveredExtensions.ApiRegistry.TryResolve(out implementation);
            }

            implementation = null;
            return false;
        }

        public IReadOnlyList<T> ResolveExtensionApis<T>() where T : class
        {
            if (discoveredExtensions.ApiRegistry != null)
            {
                return discoveredExtensions.ApiRegistry.ResolveAll<T>();
            }

            return Array.Empty<T>();
        }

        public IReadOnlyList<IClientSettingsPanelProvider> GetSettingsPanels()
        {
            return ResolveExtensionApis<IClientSettingsPanelProvider>();
        }

        private void appendExtensionLog(string message, LogLevel level)
        {
#if !DEBUG
            // Release 构建不捕获 DEBUG 级日志，扩展面板日志与输出保持一致的分类
            if (level == LogLevel.DEBUG)
            {
                return;
            }
#endif

            lock (extensionLogLock)
            {
                extensionLog.Add(new FrameworkLogEntry(message, level, DateTime.UtcNow.Ticks));
                if (extensionLog.Count > MaxExtensionLogEntries)
                {
                    extensionLog.RemoveAt(0);
                }

                extensionLogVersion++;
            }
        }

        private void packetHandler(string module, string connectionId, byte[] data)
        {
            if (disposed) return;

            FrameworkPacket packet;
            try
            {
                packet = FrameworkSerialization.DeserializePacket(data);
            }
            catch (Exception exception)
            {
                RaiseLogEntry(new LogEventArgs($"Failed to deserialize framework packet: {exception.Message}", LogLevel.WARNING));
                return;
            }

            // 管线消息路由细节 → DEBUG（设计哲学 §3.8）：聊天等高频消息在 Release 构建下被过滤，
            // 避免每条消息都刷 RimWorld 日志（社区反馈"constant log spam from chatting"）
            RaiseLogEntry(new LogEventArgs($"[Phinix] packetHandler received: kind={packet.Kind}, type={packet.MessageType}, flow={packet.Flow}", LogLevel.DEBUG));

            switch (packet.Kind)
            {
                case FrameworkProtocol.KindCapabilities:
                    RaiseLogEntry(new LogEventArgs("[Phinix] Processing KindCapabilities from server...", LogLevel.INFO));
                    FrameworkCapabilitiesPayload capabilitiesPayload = FrameworkSerialization.DeserializePayload<FrameworkCapabilitiesPayload>(packet.PayloadJson);
                    lock (remoteCapabilities)
                    {
                        remoteCapabilities.Clear();
                        foreach (string capability in capabilitiesPayload.Capabilities ?? Enumerable.Empty<string>())
                        {
                            if (!string.IsNullOrEmpty(capability)) remoteCapabilities.Add(capability);
                        }
                    }

                    negotiationTimer.Stop();
                    RaiseLogEntry(new LogEventArgs($"[Phinix] KindCapabilities processed: {remoteCapabilities.Count} capabilities, setting FrameworkV2 mode.", LogLevel.INFO));
                    setCompatibilityMode(
                        FrameworkCompatibilityMode.FrameworkV2,
                        $"Framework capability negotiation succeeded with {remoteCapabilities.Count} negotiated remote capability/capabilities.",
                        "Phinix_framework_connectedFrameworkServer");
                    break;
                case FrameworkProtocol.KindMessage:
                    if (packet.Flow == global::Phinix.Framework.FrameworkFlow.Unspecified)
                    {
                        packet.Flow = global::Phinix.Framework.FrameworkFlow.Message;
                    }
                    handleMessage(packet);
                    break;
                case FrameworkProtocol.KindCommand:
                    if (packet.Flow == global::Phinix.Framework.FrameworkFlow.Unspecified)
                    {
                        packet.Flow = global::Phinix.Framework.FrameworkFlow.Command;
                    }
                    handleCommand(packet);
                    break;
                case FrameworkProtocol.KindItem:
                    if (packet.Flow == global::Phinix.Framework.FrameworkFlow.Unspecified)
                    {
                        packet.Flow = global::Phinix.Framework.FrameworkFlow.Item;
                    }
                    handleItem(packet);
                    break;
                default:
                    RaiseLogEntry(new LogEventArgs($"Unknown framework packet kind '{packet.Kind}'", LogLevel.DEBUG));
                    break;
            }
        }

        private void handleMessage(FrameworkPacket message)
        {
            bool matchedHandler = false;
            FrameworkPacket currentMessage = message;
            ClientFrameworkContext context = new ClientFrameworkContext
            {
                CompatibilityMode = CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendMessage = sendPacket,
                RemoteCapabilities = remoteCapabilities.ToArray(),
                HasRemoteCapability = hasRemoteCapability,
                Log = (logMessage, level) => RaiseLogEntry(new LogEventArgs(logMessage, level))
            };

            foreach (IClientMessageHandler handler in discoveredExtensions.ClientMessageHandlers.Where(handler => handler.CanHandleIncomingMessage(currentMessage)))
            {
                matchedHandler = true;
                ClientIncomingMessageResult result = null;
                try
                {
                    result = handler.HandleIncomingMessage(currentMessage, context);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"Message handler {handler.GetType().FullName} threw for '{currentMessage.MessageType}': {ex}",
                        LogLevel.ERROR));
                    continue;
                }
                if (result == null) continue;

                if (result.Action == MessageHandlingResultAction.ReplacePayload && result.Message != null)
                {
                    currentMessage = result.Message;
                    continue;
                }

                if (result.DisplayMessage != null)
                {
                    addDisplayMessage(result.DisplayMessage);
                }

                if (result.Action != MessageHandlingResultAction.Continue)
                {
                    if (result.DisplayMessage == null)
                    {
                        // 被消费但无显示输出：对通知/命令类消息属正常模式，属路由细节，不升为 WARNING 刷屏。
                        RaiseLogEntry(new LogEventArgs($"[Phinix] Framework message '{currentMessage?.MessageType ?? "unknown"}' was consumed without producing display output.", LogLevel.DEBUG));
                    }
                    return;
                }
            }

            foreach (IMessageRenderer renderer in discoveredExtensions.MessageRenderers.Where(renderer => renderer.CanRender(currentMessage)))
            {
                FrameworkDisplayMessage renderedMessage = null;
                try
                {
                    renderedMessage = renderer.Render(currentMessage);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"Message renderer {renderer.GetType().FullName} threw for '{currentMessage.MessageType}': {ex}",
                        LogLevel.ERROR));
                    continue;
                }
                if (renderedMessage != null)
                {
                    addDisplayMessage(renderedMessage);
                    return;
                }
            }

            if (!matchedHandler)
            {
                showSystemMessage("Phinix_framework_unsupportedIncomingMessageType", currentMessage.MessageType ?? "unknown");
            }
        }

        private void handleCommand(FrameworkPacket command)
        {
            // 命令的 kind/type/flow 已在 packetHandler received 记录，这里不再重复打。
            bool matchedHandler = false;
            FrameworkPacket currentCommand = command;
            ClientFrameworkContext context = new ClientFrameworkContext
            {
                CompatibilityMode = CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendMessage = sendPacket,
                RemoteCapabilities = remoteCapabilities.ToArray(),
                HasRemoteCapability = hasRemoteCapability,
                Log = (logMessage, level) => RaiseLogEntry(new LogEventArgs(logMessage, level))
            };

            foreach (IClientCommandHandler handler in discoveredExtensions.ClientCommandHandlers.Where(handler => handler.CanHandleIncomingCommand(currentCommand)))
            {
                matchedHandler = true;
                ClientIncomingCommandResult result = null;
                try
                {
                    result = handler.HandleIncomingCommand(currentCommand, context);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"Command handler {handler.GetType().FullName} threw for '{currentCommand.MessageType}': {ex}",
                        LogLevel.ERROR));
                    continue;
                }
                if (result == null) continue;

                if (result.Action == MessageHandlingResultAction.ReplacePayload && result.Command != null)
                {
                    currentCommand = result.Command;
                    continue;
                }

                if (result.DisplayMessage != null)
                {
                    addDisplayMessage(result.DisplayMessage);
                }

                if (result.Action != MessageHandlingResultAction.Continue)
                {
                    return;
                }
            }

            if (!matchedHandler)
            {
                RaiseLogEntry(new LogEventArgs($"No client framework command handler registered for command type '{currentCommand.MessageType ?? "unknown"}'.", LogLevel.DEBUG));
            }
        }

        private void handleItem(FrameworkPacket itemPacket)
        {
            bool matchedHandler = false;
            FrameworkPacket currentItem = itemPacket;
            ClientFrameworkContext context = new ClientFrameworkContext
            {
                CompatibilityMode = CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendMessage = sendPacket,
                RemoteCapabilities = remoteCapabilities.ToArray(),
                HasRemoteCapability = hasRemoteCapability,
                Log = (logMessage, level) => RaiseLogEntry(new LogEventArgs(logMessage, level))
            };

            foreach (IClientIncomingItemHandler handler in discoveredExtensions.ClientIncomingItemHandlers.Where(handler => handler.CanHandleIncomingItem(currentItem)))
            {
                matchedHandler = true;
                ClientIncomingItemResult result = null;
                try
                {
                    result = handler.HandleIncomingItem(currentItem, context);
                }
                catch (Exception ex)
                {
                    RaiseLogEntry(new LogEventArgs(
                        $"Item handler {handler.GetType().FullName} threw for '{currentItem.MessageType}': {ex}",
                        LogLevel.ERROR));
                    continue;
                }
                if (result == null) continue;

                if (result.Action == ItemHandlingResultAction.ReplacePayload && result.Item != null)
                {
                    currentItem = result.Item;
                    continue;
                }

                if (result.DisplayMessage != null)
                {
                    addDisplayMessage(result.DisplayMessage);
                }

                if (result.Action != ItemHandlingResultAction.Continue)
                {
                    return;
                }
            }

            if (!matchedHandler)
            {
                RaiseLogEntry(new LogEventArgs($"No client framework item handler registered for item type '{currentItem.MessageType ?? "unknown"}'.", LogLevel.DEBUG));
            }
        }

        private void addDisplayMessage(FrameworkDisplayMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (shouldSuppress(message))
            {
                return;
            }

            lock (displayMessagesLock)
            {
                if (displayMessages.Count >= MaxDisplayMessages)
                {
                    displayMessages.RemoveRange(0, displayMessages.Count - MaxDisplayMessages + 1);
                }
                displayMessages.Add(message);
            }

            OnDisplayMessageReceived?.Invoke(this, new FrameworkDisplayMessageEventArgs(message));
        }

        void IDisplayMessageSink.Enqueue(FrameworkDisplayMessage message)
        {
            addDisplayMessage(message);
        }

        private bool shouldSuppress(FrameworkDisplayMessage message)
        {
            if (message.SuppressDefaultDisplay) return true;

            foreach (IMessageInterceptor interceptor in discoveredExtensions.MessageInterceptors)
            {
                MessageHandlingResultAction action = interceptor.Intercept(message);
                if (action == MessageHandlingResultAction.SuppressDefault || action == MessageHandlingResultAction.Handled || action == MessageHandlingResultAction.StopPropagation)
                {
                    return true;
                }
            }

            return false;
        }

        private void sendPacket(FrameworkPacket packet)
        {
            if (packet == null) return;
            if (!netClient.Connected)
            {
                RaiseLogEntry(new LogEventArgs($"[Phinix] Dropping packet type={packet.MessageType} — netClient not connected", LogLevel.WARNING));
                return;
            }

            if (packet.Flow == global::Phinix.Framework.FrameworkFlow.Message || packet.Kind == FrameworkProtocol.KindMessage)
            {
                packet.Flow = global::Phinix.Framework.FrameworkFlow.Message;
                if (string.IsNullOrEmpty(packet.Kind)) packet.Kind = FrameworkProtocol.KindMessage;
            }
            else if (packet.Flow == global::Phinix.Framework.FrameworkFlow.Command || packet.Kind == FrameworkProtocol.KindCommand)
            {
                packet.Flow = global::Phinix.Framework.FrameworkFlow.Command;
                if (string.IsNullOrEmpty(packet.Kind)) packet.Kind = FrameworkProtocol.KindCommand;
            }
            else if (packet.Flow == global::Phinix.Framework.FrameworkFlow.Item || packet.Kind == FrameworkProtocol.KindItem)
            {
                packet.Flow = global::Phinix.Framework.FrameworkFlow.Item;
                if (string.IsNullOrEmpty(packet.Kind)) packet.Kind = FrameworkProtocol.KindItem;
            }

            // Use NetClient.Send which validates connection state atomically
            byte[] serialized = FrameworkSerialization.SerializePacket(packet);
            try
            {
                netClient.Send(FrameworkProtocol.ModuleName, serialized);
            }
            catch (Exception ex)
            {
                RaiseLogEntry(new LogEventArgs(
                    $"[Phinix] Failed to send packet type={packet.MessageType}: {ex.Message}",
                    LogLevel.ERROR));
            }
        }

        private void enterLegacyMode()
        {
            if (!authenticator.Authenticated || !userManager.LoggedIn) return;
            if (CompatibilityMode != FrameworkCompatibilityMode.Unknown) return;

            RaiseLogEntry(new LogEventArgs($"[Phinix] Negotiation timer fired. CompatibilityMode={CompatibilityMode}. Falling back to Legacy.", LogLevel.INFO));
            setCompatibilityMode(
                FrameworkCompatibilityMode.Legacy,
                "Framework capability negotiation timed out; falling back to legacy compatibility mode.",
                "Phinix_framework_connectedLegacyServer");
        }

        private void reset()
        {
            if (disposed) return;

            negotiationTimer.Stop();
            lock (remoteCapabilities)
            {
                remoteCapabilities.Clear();
            }
            lock (displayMessagesLock)
            {
                displayMessages.Clear();
                displayMessageCountAtLastCheck = 0;
            }

            setCompatibilityMode(FrameworkCompatibilityMode.Unknown);
        }

        private void setCompatibilityMode(FrameworkCompatibilityMode compatibilityMode, string logMessage = null, string systemMessageKey = null, params string[] translationArgs)
        {
            bool changed = CompatibilityMode != compatibilityMode;
            CompatibilityMode = compatibilityMode;

            if (!string.IsNullOrEmpty(logMessage))
            {
                RaiseLogEntry(new LogEventArgs(logMessage));
            }

            if (changed)
            {
                CompatibilityModeChanged?.Invoke(this, new FrameworkCompatibilityModeChangedEventArgs(CompatibilityMode));
            }

            if (!string.IsNullOrEmpty(systemMessageKey))
            {
                showSystemMessage(systemMessageKey, translationArgs);
            }
        }

        private bool hasRemoteCapability(string capability)
        {
            if (string.IsNullOrEmpty(capability)) return true;

            lock (remoteCapabilities)
            {
                return remoteCapabilities.Contains(capability);
            }
        }

        private void showSystemMessage(string translationKey, params string[] translationArgs)
        {
            addDisplayMessage(new FrameworkDisplayMessage
            {
                SenderUuid = FrameworkProtocol.SystemSenderUuid,
                Source = "system",
                TranslationKey = translationKey,
                TranslationArgs = (translationArgs ?? Array.Empty<string>()).ToList()
            });
        }

        public void Shutdown()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            try
            {
                negotiationTimer.Stop();
                negotiationTimer.Elapsed -= negotiationElapsedHandler;
                negotiationTimer.Dispose();
            }
            catch (Exception ex)
            {
                RaiseLogEntry(new LogEventArgs($"Failed to dispose framework negotiation timer: {ex}", LogLevel.ERROR));
            }

            try
            {
                netClient.UnregisterPacketHandler(FrameworkProtocol.ModuleName);
                netClient.OnDisconnect -= disconnectHandler;
            }
            catch (Exception ex)
            {
                RaiseLogEntry(new LogEventArgs($"Failed to unregister framework client handlers: {ex}", LogLevel.ERROR));
            }

            PhinixExtensionRegistry.ShutdownExtensions(discoveredExtensions, extensionHostContext);
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
