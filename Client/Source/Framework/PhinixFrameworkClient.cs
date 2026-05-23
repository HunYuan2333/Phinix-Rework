using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Authentication;
using Chat;
using Connections;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;

namespace PhinixClient.Framework
{
    public class PhinixFrameworkClient : ILoggable
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
        private readonly DiscoveredPhinixExtensions discoveredExtensions;
        private readonly string[] capabilities;
        private readonly HashSet<string> remoteCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FrameworkDisplayMessage> displayMessages = new List<FrameworkDisplayMessage>();
        private readonly object displayMessagesLock = new object();
        private readonly Timer negotiationTimer;

        public PhinixFrameworkClient(NetClient netClient, ClientAuthenticator authenticator, ClientUserManager userManager)
        {
            this.netClient = netClient;
            this.authenticator = authenticator;
            this.userManager = userManager;
            this.discoveredExtensions = PhinixExtensionRegistry.DiscoverExtensions();
            this.capabilities = PhinixExtensionRegistry.CollectCapabilities(discoveredExtensions);
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
                        Log = (message, level) => RaiseLogEntry(new LogEventArgs(message, level))
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

                FrameworkPacket message = result.Message;
                if (message != null && !string.IsNullOrEmpty(message.MessageType) && !hasRemoteCapability(message.MessageType))
                {
                    showSystemMessage("Phinix_framework_remoteCapabilityUnavailable", message.MessageType);
                    return true;
                }

                if (message == null)
                {
                    if (result.Action == MessageHandlingResultAction.Continue) continue;
                    return true;
                }

                message.Kind = FrameworkProtocol.KindMessage;
                message.SessionId = authenticator.SessionId;
                message.SenderUuid = userManager.Uuid;
                sendPacket(message);

                if (result.Action != MessageHandlingResultAction.Continue)
                {
                    return true;
                }
            }

            return false;
        }

        public bool ShouldSuppressLegacyMessage(UIChatMessage message)
        {
            FrameworkDisplayMessage displayMessage = new FrameworkDisplayMessage
            {
                MessageId = message.MessageId,
                SenderUuid = message.SenderUuid,
                Text = message.Message,
                TimestampUtcTicks = message.Timestamp.ToUniversalTime().Ticks,
                Source = "legacy"
            };

            return shouldSuppress(displayMessage);
        }

        public UIChatMessage[] GetDisplayMessages()
        {
            lock (displayMessagesLock)
            {
                return displayMessages
                    .Select(toUiMessage)
                    .OrderBy(message => message.Timestamp)
                    .ToArray();
            }
        }

        public UIChatMessage[] BuildChatFeed(IEnumerable<UIChatMessage> legacyMessages)
        {
            IEnumerable<UIChatMessage> visibleLegacyMessages = (legacyMessages ?? Enumerable.Empty<UIChatMessage>())
                .Where(message => !ShouldSuppressLegacyMessage(message));

            return visibleLegacyMessages
                .Concat(GetDisplayMessages())
                .OrderBy(message => message.Timestamp)
                .ToArray();
        }

        public bool TryBuildLegacyDisplayMessage(UIChatMessage legacyMessage, out UIChatMessage displayMessage)
        {
            displayMessage = null;

            if (legacyMessage == null || ShouldSuppressLegacyMessage(legacyMessage))
            {
                return false;
            }

            displayMessage = legacyMessage;
            return true;
        }

        public bool ShouldDisplayChatMessage(UIChatMessage message, IEnumerable<string> blockedUserUuids, bool includeBlockedMessages)
        {
            if (message == null) return false;
            if (ShouldSuppressLegacyMessage(message)) return false;
            if (includeBlockedMessages) return true;

            HashSet<string> blockedUsers = new HashSet<string>(blockedUserUuids ?? Enumerable.Empty<string>());
            return !blockedUsers.Contains(message.SenderUuid);
        }

        public bool ShouldPlayNotification(UIChatMessage message, string localUuid, bool playNoiseOnMessageReceived, bool isInGame, IEnumerable<string> blockedUserUuids)
        {
            if (message == null || !playNoiseOnMessageReceived || !isInGame) return false;
            if (message.SenderUuid == localUuid || message.SenderUuid == FrameworkProtocol.SystemSenderUuid) return false;

            HashSet<string> blockedUsers = new HashSet<string>(blockedUserUuids ?? Enumerable.Empty<string>());
            return !blockedUsers.Contains(message.SenderUuid);
        }

        public bool TryGetDisplayMessage(string messageId, out UIChatMessage message)
        {
            lock (displayMessagesLock)
            {
                FrameworkDisplayMessage frameworkMessage = displayMessages.SingleOrDefault(candidate => candidate.MessageId == messageId);
                if (frameworkMessage == null)
                {
                    message = null;
                    return false;
                }

                message = toUiMessage(frameworkMessage);
                return true;
            }
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
                    handleMessage(packet);
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
                Log = (message, level) => RaiseLogEntry(new LogEventArgs(message, level))
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

        private void addDisplayMessage(FrameworkDisplayMessage message)
        {
            if (message == null || shouldSuppress(message)) return;

            lock (displayMessagesLock)
            {
                displayMessages.Add(message);
            }

            OnDisplayMessageReceived?.Invoke(this, new UIChatMessageEventArgs(toUiMessage(message)));
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

        private UIChatMessage toUiMessage(FrameworkDisplayMessage message)
        {
            ImmutableUser user;
            if (message.SenderUuid == FrameworkProtocol.SystemSenderUuid)
            {
                user = new ImmutableUser(FrameworkProtocol.SystemSenderUuid, "Phinix", true, false);
            }
            else if (!userManager.TryGetUser(message.SenderUuid, out user))
            {
                user = new ImmutableUser(message.SenderUuid);
            }

            ClientChatMessage clientChatMessage = new ClientChatMessage(
                message.MessageId,
                message.SenderUuid,
                resolveDisplayText(message),
                new DateTime(message.TimestampUtcTicks, DateTimeKind.Utc),
                ChatMessageStatus.CONFIRMED
            );

            return new UIChatMessage(clientChatMessage, user);
        }

        private bool hasRemoteCapability(string capability)
        {
            if (string.IsNullOrEmpty(capability)) return true;

            lock (remoteCapabilities)
            {
                return remoteCapabilities.Contains(capability);
            }
        }

        private string resolveDisplayText(FrameworkDisplayMessage message)
        {
            if (!string.IsNullOrEmpty(message.TranslationKey))
            {
                List<string> translationArgs = message.TranslationArgs ?? new List<string>();
                if (translationArgs.Any())
                {
                    return message.TranslationKey.Translate(translationArgs.Cast<object>().ToArray());
                }

                return message.TranslationKey.Translate();
            }

            return message.Text ?? string.Empty;
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
