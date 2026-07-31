using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using Utils;
using Utils.Framework;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

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
        private IClientMainThreadDispatcher dispatcher;
        private IClientUserDirectory userDirectory;
        private NoticeBannerProvider noticeBannerProvider;
        private NoticeSidebarProvider noticeSidebarProvider;
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChangedHandler;
        private EventHandler<UIChatMessageEventArgs> chatNotificationHandler;
        private EventHandler disconnectHandler;
        private float connectionEstablishedTime = -100f;

        public string ExtensionId => "builtin.chat";

        public int Priority => 1000;

        public void Register(IExtensionBuilder builder)
        {
            IUiTheme theme = builder.HostContext.GetRequiredService<IUiTheme>();
            theme.RegisterColor("chat.mentionText", new Color(0.45f, 0.75f, 1.0f, 1.0f));
            theme.RegisterColor("chat.mentionSelfBg", new Color(0.35f, 0.35f, 0.15f, 0.12f));
            theme.RegisterColor("chat.selfName", new Color(0.55f, 0.75f, 1.0f, 1.0f));
            theme.RegisterColor("chat.selfMessageBg", new Color(0.15f, 0.25f, 0.4f, 0.1f));
            theme.RegisterColor("chat.rowHoverBg", new Color(1f, 1f, 1f, 0.04f));
            theme.RegisterColor("chat.groupIndentLine", new Color(1f, 1f, 1f, 0.08f));
            theme.RegisterColor("chat.replyQuoteBorder", new Color(0.3f, 0.5f, 0.75f, 0.6f));
            theme.RegisterColor("chat.replyQuoteBg", new Color(1f, 1f, 1f, 0.03f));
            theme.RegisterColor("chat.replyQuoteText", new Color(0.55f, 0.52f, 0.48f, 0.7f));
            theme.RegisterColor("chat.noticeAccent", new Color(0.9f, 0.72f, 0.25f, 0.9f));
            theme.RegisterColor("chat.noticeBg", new Color(0.25f, 0.2f, 0.08f, 0.12f));
            theme.RegisterColor("chat.noticeBannerBg", new Color(0.12f, 0.1f, 0.06f, 0.9f));
            theme.RegisterColor("chat.noticeProgress", new Color(0.9f, 0.72f, 0.25f, 0.7f));
            theme.RegisterColor("chat.inputReplyBorder", new Color(0.3f, 0.5f, 0.9f, 0.7f));
            theme.RegisterColor("chat.inputReplyBg", new Color(0.15f, 0.25f, 0.45f, 0.08f));
            theme.RegisterColor("chat.blockedBg", new Color(0f, 0f, 0f, 0.35f));
            theme.RegisterColor("chat.blockedName", new Color(0.6f, 0.6f, 0.6f));
            theme.RegisterColor("chat.pendingMessage", new Color(1f, 1f, 1f, 0.6f));
            theme.RegisterColor("chat.deniedMessage", new Color(0.94f, 0.28f, 0.28f));

            PhinixFrameworkChatService chatModule = chatApi as PhinixFrameworkChatService ?? new PhinixFrameworkChatService();
            if (chatModule.Log == null)
            {
                chatModule.Log = (message, level) => builder.HostContext.Log?.Invoke(message, level);
            }
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
                args => builder.HostContext.Log?.Invoke(args.Message, args.LogLevel),
                chatApi,
                builder.HostContext.GetRequiredService<IFrameworkClientTransport>(),
                builder.HostContext.GetRequiredService<IClientUserDirectory>());
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
                builder.HostContext.GetRequiredService<IClientUserDirectory>());
            builder.RegisterApi<IMainTabProvider>(chatMainTabProvider);
            var settingsPanelProvider = new ChatSettingsPanelProvider(theme);
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanelProvider);
            builder.RegisterApi<IClientLegacySettingsMigrator>(settingsPanelProvider);
            noticeBannerProvider = noticeBannerProvider ?? new NoticeBannerProvider();
            builder.RegisterApi<INoticeBannerProvider>(noticeBannerProvider);

            noticeSidebarProvider = noticeSidebarProvider ?? new NoticeSidebarProvider(chatUiHostContext);
            builder.RegisterApi<IServerSidebarProvider>(noticeSidebarProvider);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (chatApi == null || hostContext == null)
            {
                return;
            }

            IUiTheme theme = hostContext.GetRequiredService<IUiTheme>();
            theme.Reload();
            ChatTheme.Refresh(theme);

            frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
            commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
            settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
            soundService = hostContext.GetRequiredService<IClientSoundService>();
            dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            userDirectory = hostContext.GetRequiredService<IClientUserDirectory>();

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

                    if (args.Message.IsNotice)
                    {
                        noticeSidebarProvider?.Add(args.Message);
                        if (Time.realtimeSinceStartup - connectionEstablishedTime > 5f)
                            noticeBannerProvider?.Enqueue(args.Message);
                    }

                    if (args.Message.MentionedUuids != null && args.Message.MentionedUuids.Contains(sessionContext.Uuid))
                    {
                        string senderName = "???";
                        if (userDirectory.TryGetUser(args.Message.SenderUuid, out ImmutableUser mentionSender))
                        {
                            senderName = Utils.TextHelper.StripRichText(mentionSender.DisplayName);
                        }

                        string snippet = Utils.TextHelper.StripRichText(args.Message.Message ?? "");
                        if (snippet.Length > 100) snippet = snippet.Substring(0, 100) + "...";

                        dispatcher.Enqueue(() =>
                        {
                            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeCreated");
                            if (letterDef != null)
                            {
                                Find.LetterStack.ReceiveLetter(
                                    "Phinix_chat_mentionLetter_label".Translate(senderName),
                                    "Phinix_chat_mentionLetter_description".Translate(senderName, snippet),
                                    letterDef);
                            }
                        });
                    }
                };
            }

            chatService.OnChatMessageReceived -= chatNotificationHandler;
            chatService.OnChatMessageReceived += chatNotificationHandler;

            if (disconnectHandler == null)
            {
                disconnectHandler = (_, __) =>
                {
                    noticeBannerProvider?.Clear();
                    noticeSidebarProvider?.Clear();
                };
            }

            IClientUserEventStream userEventStream = hostContext.GetRequiredService<IClientUserEventStream>();
            userEventStream.Disconnected -= disconnectHandler;
            userEventStream.Disconnected += disconnectHandler;

            if (compatibilityChangedHandler == null)
            {
                compatibilityChangedHandler = (_, args) =>
                {
                    connectionEstablishedTime = Time.realtimeSinceStartup;

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
                    else if (args.CompatibilityMode == FrameworkCompatibilityMode.Legacy)
                    {
                        dispatcher.Enqueue(() =>
                        {
                            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeCreated");
                            if (letterDef != null)
                            {
                                Find.LetterStack.ReceiveLetter(
                                    "Phinix_chat_legacyModeLetter_label".Translate(),
                                    "Phinix_chat_legacyModeLetter_description".Translate(),
                                    letterDef);
                            }
                        });
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

            if (disconnectHandler != null && hostContext != null
                && hostContext.TryGetService<IClientUserEventStream>(out var userEventStream))
            {
                userEventStream.Disconnected -= disconnectHandler;
            }

            if (noticeSidebarProvider != null)
            {
                noticeSidebarProvider.Shutdown();
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
