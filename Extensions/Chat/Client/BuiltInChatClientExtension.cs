using System;
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
    public class BuiltInChatClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientOutgoingCommandHandler
    {
        private IFrameworkChatClientApi chatApi;
        private IClientChatService chatService;
        private ChatUiHostContext chatUiHostContext;
        private IChatTabContent chatTabContent;
        private IMainTabProvider chatMainTabProvider;
        private IServerSidebarProvider chatSidebarProvider;
        private IClientMessageHandler messageHandler;
        private IClientCommandHandler commandHandler;
        private IMessageRenderer messageRenderer;
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
            messageHandler = messageHandler ?? new ChatMessageHandler(chatApi);
            commandHandler = commandHandler ?? new ChatCommandHandler(chatApi);
            messageRenderer = messageRenderer ?? new ChatMessageRenderer(chatApi);
            builder.AddClientMessageHandler(messageHandler);
            builder.AddClientCommandHandler(commandHandler);
            builder.AddMessageRenderer(messageRenderer);

            chatMainTabProvider = chatMainTabProvider ?? new ChatMainTabProvider(
                chatUiHostContext,
                chatTabContent,
            // 设计哲学 §3.7：插件不得绕过通信管线直连底层传输。
            // 通过 IFrameworkClientTransport.TryHandleOutgoingMessage 走完整 handler 管线，
            // 保证 Priority 排序、拦截、替换、回退机制正常工作。
                message => builder.HostContext.GetRequiredService<IFrameworkClientTransport>().TryHandleOutgoingMessage(message));
            builder.RegisterApi<IMainTabProvider>(chatMainTabProvider);
            var settingsPanelProvider = new ChatSettingsPanelProvider();
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanelProvider);
            builder.RegisterApi<IClientLegacySettingsMigrator>(settingsPanelProvider);
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
                        settingsContext.Get("chat.playNoiseOnMessageReceived", true),
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

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            return (commandHandler as IClientOutgoingCommandHandler)?.CanHandleOutgoingCommand(command) == true;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            return (commandHandler as IClientOutgoingCommandHandler)?.HandleOutgoingCommand(command, context)
                ?? new ClientOutgoingCommandResult { Action = MessageHandlingResultAction.Continue };
        }

    }
}
