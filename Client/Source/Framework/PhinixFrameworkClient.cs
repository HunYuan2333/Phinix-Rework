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
    public class PhinixFrameworkClient : ILoggable, IFrameworkClientTransport
    {
        public delegate void CompatibilityModeChangedDelegate(object sender, FrameworkCompatibilityMode compatibilityMode);

        public event EventHandler<LogEventArgs> OnLogEntry;

        public event EventHandler<UIChatMessageEventArgs> OnDisplayMessageReceived;

        public event CompatibilityModeChangedDelegate OnCompatibilityModeChanged;

        public void RaiseLogEntry(LogEventArgs e) => OnLogEntry?.Invoke(this, e);

        public FrameworkCompatibilityMode CompatibilityMode { get; private set; } = FrameworkCompatibilityMode.Unknown;

        private readonly NetClient netClient;
        private readonly ClientAuthenticator authenticator;
        private readonly ClientUserManager userManager;
        private readonly ExtensionHostContext extensionHostContext;
        private readonly DiscoveredPhinixExtensions discoveredExtensions;
        private readonly string[] capabilities;
        private readonly HashSet<string> remoteCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FrameworkDisplayMessage> displayMessages = new List<FrameworkDisplayMessage>();
        private readonly object displayMessagesLock = new object();
        private readonly Timer negotiationTimer;
        private int displayMessageCountAtLastCheck;
        public PhinixFrameworkClient(NetClient netClient, ClientAuthenticator authenticator, ClientUserManager userManager, ExtensionHostContext extensionHostContext = null)
        {
            this.netClient = netClient;
            this.authenticator = authenticator;
            this.userManager = userManager;
            this.extensionHostContext = extensionHostContext ?? ExtensionHostContext.Empty;
            this.discoveredExtensions = PhinixExtensionRegistry.DiscoverExtensions(this.extensionHostContext);
            this.capabilities = PhinixExtensionRegistry.CollectCapabilities(discoveredExtensions);
            PhinixExtensionRegistry.ActivateExtensions(discoveredExtensions, this.extensionHostContext);
            this.negotiationTimer = new Timer
            {
                AutoReset = false,
                Enabled = false,
                Interval = 3000
            };
            this.negotiationTimer.Elapsed += (_, __) => enterLegacyMode();

            netClient.RegisterPacketHandler(FrameworkProtocol.ModuleName, packetHandler);
            netClient.OnDisconnect += (_, __) => reset();

            RaiseLogEntry(new LogEventArgs($"Discovered {discoveredExtensions.Extensions.Count} framework extension(s) and {capabilities.Length} capability/capabilities."));
            if (discoveredExtensions.Modules.Count > 0)
            {
                RaiseLogEntry(new LogEventArgs(
                    $"Framework modules: {string.Join(", ", discoveredExtensions.Modules.Select(module => module.ExtensionId).OrderBy(extensionId => extensionId))}. " +
                    $"Client handlers={discoveredExtensions.ClientMessageHandlers.Count}, client commands={discoveredExtensions.ClientCommandHandlers.Count}, renderers={discoveredExtensions.MessageRenderers.Count}, item codecs={discoveredExtensions.ItemCodecs.Count}."));
            }
            foreach (string diagnostic in discoveredExtensions.Diagnostics)
            {
                RaiseLogEntry(new LogEventArgs(diagnostic, LogLevel.DEBUG));
            }
            foreach (string warning in discoveredExtensions.Warnings)
            {
                RaiseLogEntry(new LogEventArgs(warning, LogLevel.WARNING));
            }
        }

        public void BeginNegotiation()
        {
            reset();

            if (!authenticator.Authenticated || !userManager.LoggedIn) return;

            negotiationTimer.Start();
            RaiseLogEntry(new LogEventArgs("Starting framework capability negotiation with connected server.", LogLevel.DEBUG));
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
            foreach (IClientMessageHandler handler in discoveredExtensions.ClientMessageHandlers.Where(handler => handler.CanHandleOutgoingText(rawMessage)))
            {
                if (CompatibilityMode != FrameworkCompatibilityMode.FrameworkV2)
                {
                    showSystemMessage("Phinix_framework_legacyExtensionUnavailable");
                    return true;
                }

                ClientOutgoingMessageResult result = handler.HandleOutgoingText(
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

                if (result == null)
                {
                    return true;
                }

                if (result.Action == MessageHandlingResultAction.LegacyFallback)
                {
                    return false;
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
            return discoveredExtensions.ApiRegistry?.ResolveAll<T>() ?? Array.Empty<T>();
        }

        private void packetHandler(string module, string connectionId, byte[] data)
        {
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

            switch (packet.Kind)
            {
                case FrameworkProtocol.KindCapabilities:
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
                ClientIncomingMessageResult result = handler.HandleIncomingMessage(currentMessage, context);
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
                        RaiseLogEntry(new LogEventArgs($"Framework message '{currentMessage?.MessageType ?? "unknown"}' was consumed without producing display output. Silent consumption should move to a future command/control flow.", LogLevel.WARNING));
                    }
                    return;
                }
            }

            foreach (IMessageRenderer renderer in discoveredExtensions.MessageRenderers.Where(renderer => renderer.CanRender(currentMessage)))
            {
                FrameworkDisplayMessage renderedMessage = renderer.Render(currentMessage);
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
                ClientIncomingCommandResult result = handler.HandleIncomingCommand(currentCommand, context);
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
                displayMessages.Add(message);
            }

            UIChatMessage uiMessage = TryResolveExtensionApi(out IFrameworkChatClientApi chatApi)
                ? chatApi.ToUiMessage(message, extensionHostContext.GetRequiredService<IClientUserDirectory>())
                : new UIChatMessage(
                    message.MessageId,
                    message.SenderUuid,
                    message.Text ?? string.Empty,
                    new DateTime(message.TimestampUtcTicks, DateTimeKind.Utc),
                    UIChatMessageStatus.Confirmed,
                    new ImmutableUser(message.SenderUuid),
                    message.Source);

            OnDisplayMessageReceived?.Invoke(this, new UIChatMessageEventArgs(uiMessage));
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
            if (packet == null || !netClient.Connected) return;

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

            netClient.Send(FrameworkProtocol.ModuleName, FrameworkSerialization.SerializePacket(packet));
        }

        private void enterLegacyMode()
        {
            if (!authenticator.Authenticated || !userManager.LoggedIn) return;
            if (CompatibilityMode != FrameworkCompatibilityMode.Unknown) return;

            setCompatibilityMode(
                FrameworkCompatibilityMode.Legacy,
                "Framework capability negotiation timed out; falling back to legacy compatibility mode.",
                "Phinix_framework_connectedLegacyServer");
        }

        private void reset()
        {
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
                OnCompatibilityModeChanged?.Invoke(this, CompatibilityMode);
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
    }
}
