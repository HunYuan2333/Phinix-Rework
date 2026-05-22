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
            sendEnvelope(new FrameworkEnvelope
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
                        SendEnvelope = sendEnvelope,
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

                FrameworkEnvelope envelope = result.Envelope;
                if (envelope != null && !string.IsNullOrEmpty(envelope.MessageType) && !hasRemoteCapability(envelope.MessageType))
                {
                    showSystemMessage("Phinix_framework_remoteCapabilityUnavailable", envelope.MessageType);
                    return true;
                }

                if (envelope == null)
                {
                    if (result.Action == MessageHandlingResultAction.Continue) continue;
                    return true;
                }

                envelope.Kind = FrameworkProtocol.KindExtension;
                envelope.SessionId = authenticator.SessionId;
                envelope.SenderUuid = userManager.Uuid;
                sendEnvelope(envelope);

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
            FrameworkEnvelope envelope;
            try
            {
                envelope = FrameworkSerialization.DeserializeEnvelope(data);
            }
            catch (Exception exception)
            {
                RaiseLogEntry(new LogEventArgs($"Failed to deserialize framework envelope: {exception.Message}", LogLevel.WARNING));
                return;
            }

            switch (envelope.Kind)
            {
                case FrameworkProtocol.KindCapabilities:
                    FrameworkCapabilitiesPayload capabilitiesPayload = FrameworkSerialization.DeserializePayload<FrameworkCapabilitiesPayload>(envelope.PayloadJson);
                    lock (remoteCapabilities)
                    {
                        remoteCapabilities.Clear();
                        foreach (string capability in capabilitiesPayload.Capabilities ?? Enumerable.Empty<string>())
                        {
                            if (!string.IsNullOrEmpty(capability)) remoteCapabilities.Add(capability);
                        }
                    }

                    CompatibilityMode = FrameworkCompatibilityMode.FrameworkV2;
                    negotiationTimer.Stop();
                    OnCompatibilityModeChanged?.Invoke(this, CompatibilityMode);
                    showSystemMessage("Phinix_framework_connectedFrameworkServer");
                    break;
                case FrameworkProtocol.KindExtension:
                    handleExtension(envelope);
                    break;
                default:
                    RaiseLogEntry(new LogEventArgs($"Unknown framework envelope kind '{envelope.Kind}'", LogLevel.DEBUG));
                    break;
            }
        }

        private void handleExtension(FrameworkEnvelope envelope)
        {
            bool matchedHandler = false;
            FrameworkEnvelope currentEnvelope = envelope;
            ClientFrameworkContext context = new ClientFrameworkContext
            {
                CompatibilityMode = CompatibilityMode,
                SenderUuid = userManager.Uuid,
                SessionId = authenticator.SessionId,
                SendEnvelope = sendEnvelope,
                RemoteCapabilities = remoteCapabilities.ToArray(),
                HasRemoteCapability = hasRemoteCapability,
                Log = (message, level) => RaiseLogEntry(new LogEventArgs(message, level))
            };

            foreach (IClientMessageHandler handler in discoveredExtensions.ClientMessageHandlers.Where(handler => handler.CanHandleIncomingEnvelope(currentEnvelope)))
            {
                matchedHandler = true;
                ClientIncomingMessageResult result = handler.HandleIncomingEnvelope(currentEnvelope, context);
                if (result == null) continue;

                if (result.Action == MessageHandlingResultAction.ReplacePayload && result.Envelope != null)
                {
                    currentEnvelope = result.Envelope;
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

            foreach (IMessageRenderer renderer in discoveredExtensions.MessageRenderers.Where(renderer => renderer.CanRender(currentEnvelope)))
            {
                FrameworkDisplayMessage renderedMessage = renderer.Render(currentEnvelope);
                if (renderedMessage != null)
                {
                    addDisplayMessage(renderedMessage);
                    return;
                }
            }

            if (!matchedHandler)
            {
                showSystemMessage("Phinix_framework_unsupportedIncomingMessageType", currentEnvelope.MessageType ?? "unknown");
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

        private void sendEnvelope(FrameworkEnvelope envelope)
        {
            if (envelope == null || !netClient.Connected) return;

            netClient.Send(FrameworkProtocol.ModuleName, FrameworkSerialization.SerializeEnvelope(envelope));
        }

        private void enterLegacyMode()
        {
            CompatibilityMode = FrameworkCompatibilityMode.Legacy;
            OnCompatibilityModeChanged?.Invoke(this, CompatibilityMode);
            showSystemMessage("Phinix_framework_connectedLegacyServer");
        }

        private void reset()
        {
            negotiationTimer.Stop();
            CompatibilityMode = FrameworkCompatibilityMode.Unknown;
            lock (remoteCapabilities)
            {
                remoteCapabilities.Clear();
            }
            lock (displayMessagesLock)
            {
                displayMessages.Clear();
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
