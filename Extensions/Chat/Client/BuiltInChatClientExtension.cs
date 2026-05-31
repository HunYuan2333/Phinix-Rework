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
    public class BuiltInChatClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientMessageHandler, IClientCommandHandler, IClientOutgoingCommandHandler, IMessageRenderer
    {
        private IFrameworkChatClientApi chatApi;
        private IClientChatService chatService;
        private ChatUiHostContext chatUiHostContext;
        private IChatTabContent chatTabContent;
        private IMainTabProvider chatMainTabProvider;
        private IServerSidebarProvider chatSidebarProvider;
        private IFrameworkClientTransport frameworkClient;
        private IFrameworkClientCommandTransport commandTransport;
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

            chatMainTabProvider = chatMainTabProvider ?? new ChatMainTabProvider(
                chatUiHostContext,
                chatTabContent,
            // 设计哲学 §3.7：插件不得绕过通信管线直连底层传输。
            // 通过 IFrameworkClientTransport.TryHandleOutgoingMessage 走完整 handler 管线，
            // 保证 Priority 排序、拦截、替换、回退机制正常工作。
                message => builder.HostContext.GetRequiredService<IFrameworkClientTransport>().TryHandleOutgoingMessage(message));
            builder.RegisterApi<IMainTabProvider>(chatMainTabProvider);
            builder.RegisterApi<IClientSettingsPanelProvider>(new ChatSettingsPanelProvider());
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (chatApi == null || hostContext == null)
            {
                return;
            }

            frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
            commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
            settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
            soundService = hostContext.GetRequiredService<IClientSoundService>();

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
                        if (sessionContext.Authenticated &&
                            sessionContext.LoggedIn &&
                            frameworkClient.HasRemoteCapability(FrameworkChatProtocol.HistoryRequestType))
                        {
                            FrameworkPacket historyRequest = chatApi.CreateHistoryRequestPacket(
                                sessionContext.SessionId,
                                sessionContext.Uuid);
                            commandTransport.TryHandleOutgoingCommand(historyRequest);
                        }
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
            // 仅在 FrameworkV2 模式下处理 —— Legacy 模式由 LegacyAdapter(priority=500) 接管。
            // 返回 true 但 HandleOutgoingText 里检查 CompatibilityMode: 非 FrameworkV2 时返回 LegacyFallback。
            return chatApi != null &&
                   !string.IsNullOrWhiteSpace(rawMessage);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context)
        {
            // 非 FrameworkV2 模式：声明无力处理，让管线继续到下一个 handler
            // （正常情况 LegacyAdapter 已在 priority=500 处理完毕，此路径不应到达）。
            // 如果到达（无 LegacyAdapter），消息被框架丢弃并显示"不支持"的系统消息。
            if (context.CompatibilityMode != FrameworkCompatibilityMode.FrameworkV2)
            {
                return new ClientOutgoingMessageResult
                {
                    Action = MessageHandlingResultAction.LegacyFallback
                };
            }

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

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            return command != null && command.MessageType == FrameworkChatProtocol.HistoryRequestType;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            if (context.CompatibilityMode != FrameworkCompatibilityMode.FrameworkV2)
            {
                return new ClientOutgoingCommandResult
                {
                    Action = MessageHandlingResultAction.LegacyFallback
                };
            }

            return new ClientOutgoingCommandResult
            {
                Action = MessageHandlingResultAction.Handled,
                Command = command
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
