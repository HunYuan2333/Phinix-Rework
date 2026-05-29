using System;
using Phinix.TradeExtension;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using RimWorld;
using Verse;
using Verse.Sound;

namespace Phinix.ChatExtension.Client
{
    [PhinixExtension("builtin.chat")]
    public class BuiltInChatClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientMessageHandler, IClientCommandHandler, IMessageRenderer
    {
        private IFrameworkChatClientApi chatApi;
        private IClientChatService chatService;
        private ChatUiHostContext chatUiHostContext;
        private IChatTabContent chatTabContent;
        private IMainTabProvider chatMainTabProvider;
        private IServerSidebarProvider chatSidebarProvider;
        private IFrameworkClientTransport frameworkClient;
        private IFrameworkClientLifecycle lifecycle;
        private IClientSessionContext sessionContext;
        private IClientSettingsContext settingsContext;
        private IClientSoundService soundService;
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChangedHandler;
        private EventHandler<UIChatMessageEventArgs> chatNotificationHandler;

        public string ExtensionId => "builtin.chat";

        public int Priority => 1000;

        public void Register(IExtensionBuilder builder)
        {
            PhinixFrameworkChatService chatModule = chatApi as PhinixFrameworkChatService ?? new PhinixFrameworkChatService();
            chatApi = chatModule;
            chatService = chatService ?? new FrameworkClientChatServiceAdapter(
                chatApi,
                builder.HostContext.GetRequiredService<IClientDisplayMessageFeed>(),
                builder.HostContext.GetRequiredService<IClientDisplayMessageStore>(),
                builder.HostContext.GetRequiredService<IClientUserDirectory>(),
                builder.HostContext.GetRequiredService<IClientSettingsContext>());
            chatUiHostContext = chatUiHostContext ?? new ChatUiHostContext(
                chatService,
                builder.HostContext.GetRequiredService<IClientSessionContext>(),
                builder.HostContext.GetRequiredService<IClientSettingsContext>(),
                builder.HostContext.GetRequiredService<IClientUserEventStream>(),
                uuid =>
                {
                    if (builder.HostContext.ApiRegistry.TryResolve<ITradeRequestApi>(out var tradeRequestApi))
                    {
                        tradeRequestApi.CreateTrade(uuid);
                    }
                },
                args => builder.HostContext.Log?.Invoke(args.Message, args.LogLevel));
            chatTabContent = chatTabContent ?? new ChatMessageList(
                chatUiHostContext);
            builder.RegisterApi(chatApi);
            builder.RegisterApi(chatService);
            builder.RegisterApi<IChatUiHostContext>(chatUiHostContext);
            builder.RegisterApi(chatTabContent);
            chatSidebarProvider = chatSidebarProvider ?? new ChatSidebarProvider(
                chatUiHostContext,
                builder.HostContext.GetRequiredService<IClientSessionContext>(),
                builder.HostContext.GetRequiredService<IClientUserDirectory>(),
                builder.HostContext.GetRequiredService<IClientSettingsContext>(),
                builder.HostContext.GetRequiredService<Action>());
            builder.RegisterApi<IServerSidebarProvider>(chatSidebarProvider);
            builder.AddCapabilityProvider(this);
            builder.AddClientMessageHandler(this);
            builder.AddClientCommandHandler(this);
            builder.AddMessageRenderer(this);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (chatApi == null || hostContext == null)
            {
                return;
            }

            frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
            settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
            soundService = hostContext.GetRequiredService<IClientSoundService>();

            chatMainTabProvider = chatMainTabProvider ?? new ChatMainTabProvider(
                chatUiHostContext,
                chatTabContent,
                message =>
                {
                    var context = new ClientFrameworkContext
                    {
                        SessionId = sessionContext.SessionId,
                        SenderUuid = sessionContext.Uuid,
                        CompatibilityMode = lifecycle.CompatibilityMode,
                        SendMessage = pkt => frameworkClient.SendFrameworkPacket(pkt),
                        HasRemoteCapability = cap => frameworkClient.HasRemoteCapability(cap),
                        Log = (msg, level) => chatUiHostContext.Log(new LogEventArgs(msg, level))
                    };
                    var outgoingPacket = chatApi.CreateOutgoingMessage(message, context);
                    frameworkClient.SendFrameworkPacket(outgoingPacket);
                });
            hostContext.ApiRegistry.RegisterApi<IMainTabProvider>("builtin.chat", chatMainTabProvider);

            if (chatNotificationHandler == null)
            {
                chatNotificationHandler = (_, args) =>
                {
                    if (chatService.ShouldPlayNotification(
                        args.Message,
                        sessionContext.Uuid,
                        settingsContext.PlayNoiseOnMessageReceived,
                        Current.Game != null,
                        settingsContext.BlockedUsers))
                    {
                        soundService.Enqueue(SoundDefOf.Tick_Tiny);
                    }
                };
            }

            chatService.OnChatMessageReceived -= chatNotificationHandler;
            chatService.OnChatMessageReceived += chatNotificationHandler;

            if (compatibilityChangedHandler == null)
            {
                compatibilityChangedHandler = (_, args) =>
                {
                    if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                    {
                        chatApi.RequestHistory(
                            frameworkClient,
                            sessionContext.Authenticated,
                            sessionContext.LoggedIn,
                            sessionContext.SessionId,
                            sessionContext.Uuid);
                    }
                };
            }

            lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            lifecycle.CompatibilityModeChanged += compatibilityChangedHandler;

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
            {
                compatibilityChangedHandler(this, new FrameworkCompatibilityModeChangedEventArgs(lifecycle.CompatibilityMode));
            }
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (lifecycle != null && compatibilityChangedHandler != null)
            {
                lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            }

            if (chatService != null && chatNotificationHandler != null)
            {
                chatService.OnChatMessageReceived -= chatNotificationHandler;
            }
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return FrameworkChatProtocol.MessageType;
            yield return FrameworkChatProtocol.HistoryRequestType;
            yield return FrameworkChatProtocol.HistorySyncCompleteType;
        }

        public bool CanHandleOutgoingText(string rawMessage)
        {
            return chatApi != null &&
                   !string.IsNullOrWhiteSpace(rawMessage);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context)
        {
            return new ClientOutgoingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                Message = chatApi.CreateOutgoingMessage(rawMessage, context)
            };
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            return message != null && message.MessageType == FrameworkChatProtocol.MessageType;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ClientFrameworkContext context)
        {
            return new ClientIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                DisplayMessage = chatApi.RenderMessage(message)
            };
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null && command.MessageType == FrameworkChatProtocol.HistorySyncCompleteType;
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            return new ClientIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }

        public bool CanRender(FrameworkPacket message)
        {
            return message != null && message.MessageType == FrameworkChatProtocol.MessageType;
        }

        public FrameworkDisplayMessage Render(FrameworkPacket message)
        {
            return chatApi.RenderMessage(message);
        }
    }
}
