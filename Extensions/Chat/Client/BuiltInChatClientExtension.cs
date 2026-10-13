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
    public class BuiltInChatClientExtension : ClientExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientOutgoingCommandHandler, IClientOutgoingMessageHandler
    {
        private IClientCompositionScope composition;
        private IClientTheme currentTheme;
        private IClientThemeRegistration themeRegistration;
        private Action themeChanged;
        private volatile bool active;
        private int callbackGeneration;
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
        private Action<string, LogLevel> hostLog;
        private ChatSettingsPanelProvider settingsPanelProvider;

        public override string ExtensionId => "builtin.chat";

        public int Priority => 1000;

        public override void Compose(IExtensionBuilder builder)
        {
            if (composition != null) throw new InvalidOperationException("Chat is already composed.");
            ExtensionHostContext host = builder.HostContext;
            composition = host.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
            {
                local.Borrow(host.GetRequiredService<IClientDisplayMessageFeed>());
                local.Borrow(host.GetRequiredService<IClientDisplayMessageStore>());
                local.Borrow(host.GetRequiredService<IClientSessionContext>());
                local.Borrow(host.GetRequiredService<IClientSettingsContext>());
                local.Borrow(host.GetRequiredService<IClientUserDirectory>());
                local.Borrow(host.GetRequiredService<IClientUserEventStream>());
                local.Borrow(host.GetRequiredService<IClientSettingsWindowService>());
                local.Borrow(host.GetRequiredService<IClientPresentationService>());
                local.Borrow(host.GetRequiredService<IClientExtensionPointService>());
                local.Borrow(host.GetRequiredService<IClientApiDirectory>());
                local.Borrow(host.GetRequiredService<IClientMainThreadDispatcher>());
                local.Borrow<Action<string, LogLevel>>((message, level) => host.Log?.Invoke(message, level));
                local.Borrow(host.GetRequiredService<IClientMessageSubmission>());
                local.Borrow<Action<string>>(uuid =>
                {
                    if (active && host.TryResolveApi<ITradeRequestApi>(out var api)) api.CreateTrade(uuid);
                });
                local.Borrow<Action<LogEventArgs>>(args => host.Log?.Invoke(args.Message, args.LogLevel));
                local.Register<IFrameworkChatClientApi, PhinixFrameworkChatService>();
                local.Register<IClientChatService, FrameworkClientChatServiceAdapter>();
                local.Register<IChatMessageSender, ChatMessageSubmissionAdapter>();
                local.Register<IChatUiHostContext, ChatUiHostContext>();
                local.Register<ChatMessageActions, ChatMessageActions>();
                local.Register<IChatTabContent, ChatMessageList>();
                local.Register<UserList, UserList>();
                local.Register<ChatSidebarProvider, ChatSidebarProvider>();
                local.Register<ChatMainTabProvider, ChatMainTabProvider>();
                local.Register<NoticeBannerProvider, NoticeBannerProvider>();
                local.Register<NoticeSidebarProvider, NoticeSidebarProvider>();
                local.Register<IClientMessageHandler, ChatMessageHandler>();
                local.Register<IClientCommandHandler, ChatCommandHandler>();
                local.Register<IMessageRenderer, ChatMessageRenderer>();
                local.Register<ChatSettingsPanelProvider, ChatSettingsPanelProvider>();
            });
            chatApi = composition.Resolve<IFrameworkChatClientApi>();
            chatService = composition.Resolve<IClientChatService>();
            chatUiHostContext = composition.Resolve<ChatUiHostContext>();
            chatTabContent = composition.Resolve<IChatTabContent>();
            (chatTabContent as ChatMessageList).MessageActions = composition.Resolve<ChatMessageActions>();
            (chatTabContent as ChatMessageList).FeedRenderer = host.GetRequiredService<IClientPresentationService>();
            chatSidebarProvider = composition.Resolve<ChatSidebarProvider>();
            (chatTabContent as ChatMessageList).MemberActions = (chatSidebarProvider as ChatSidebarProvider).OpenMemberActions;
            chatMainTabProvider = composition.Resolve<ChatMainTabProvider>();
            noticeBannerProvider = composition.Resolve<NoticeBannerProvider>();
            noticeSidebarProvider = composition.Resolve<NoticeSidebarProvider>();

            builder.RegisterApi(chatApi);
            builder.RegisterApi(chatService);
            builder.RegisterApi<IChatMessageSender>(composition.Resolve<IChatMessageSender>());
            builder.RegisterApi<IChatUiHostContext>(chatUiHostContext);
            builder.RegisterApi(chatTabContent);
            builder.RegisterApi<IMainTabProvider>(chatMainTabProvider);
            builder.RegisterApi<IServerSidebarProvider>(chatSidebarProvider);
            builder.RegisterApi<INoticeBannerProvider>(noticeBannerProvider);
            builder.RegisterApi<IServerSidebarProvider>(noticeSidebarProvider);
            builder.AddCapabilityProvider(this);
            messageHandler = composition.Resolve<IClientMessageHandler>();
            commandHandler = composition.Resolve<IClientCommandHandler>();
            messageRenderer = composition.Resolve<IMessageRenderer>();
            builder.AddClientMessageHandler(messageHandler);
            builder.AddClientCommandHandler(commandHandler);
            builder.AddMessageRenderer(messageRenderer);

            settingsPanelProvider = composition.Resolve<ChatSettingsPanelProvider>();
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanelProvider);
            builder.RegisterApi<IClientLegacySettingsMigrator>(new ChatSettingsInputAdapter());
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (active) return;
            if (chatApi == null || hostContext == null)
            {
                return;
            }

            hostLog = hostContext?.Log;

            IClientTheme theme = hostContext.GetRequiredService<IClientTheme>();
            themeRegistration = hostContext.GetRequiredService<IClientThemeRegistry>().RegisterModule(this, "chat", "Phinix_theme_owner_chat");
            RegisterThemeDefaults(themeRegistration);
            themeRegistration.Commit();
            currentTheme = theme;
            themeChanged = () => ChatTheme.Refresh(theme);
            theme.Changed += themeChanged;
            ChatTheme.Refresh(theme);

            frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
            commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
            settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
            soundService = hostContext.GetRequiredService<IClientSoundService>();
            dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            userDirectory = hostContext.GetRequiredService<IClientUserDirectory>();

            active = true;
            (chatService as FrameworkClientChatServiceAdapter)?.Start();
            chatUiHostContext?.Start();
            (chatSidebarProvider as ChatSidebarProvider)?.Start(this);
            (chatMainTabProvider as ChatMainTabProvider)?.Start(this);
            composition.Resolve<ChatMessageActions>().Start(this);
            noticeSidebarProvider?.Start();
            (chatTabContent as ChatMessageList)?.Start();
            (chatTabContent as ChatMessageList)?.StartFeed(this);

            if (chatNotificationHandler == null)
            {
                chatNotificationHandler = (_, args) =>
                {
                    if (!active || args?.Message == null) return;
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

                        int generation = System.Threading.Volatile.Read(ref callbackGeneration);
                        Game game = Current.Game;
                        dispatcher.Enqueue(() =>
                        {
                            if (!active || generation != System.Threading.Volatile.Read(ref callbackGeneration)
                                || !ReferenceEquals(Current.Game, game)) return;
                            try
                            {
                                // 尚未进入存档时 Find.LetterStack 为 null，直接弹信会 NRE，故做空保护。
                                LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("TradeCreated");
                                if (letterDef != null && Find.LetterStack != null)
                                {
                                    Find.LetterStack.ReceiveLetter(
                                        "Phinix_chat_mentionLetter_label".Translate(senderName),
                                        "Phinix_chat_mentionLetter_description".Translate(senderName, snippet),
                                        letterDef);
                                }
                            }
                            catch (Exception ex)
                            {
                                hostLog?.Invoke($"Failed to raise chat mention letter: {ex.Message}", LogLevel.WARNING);
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
                    System.Threading.Interlocked.Increment(ref callbackGeneration);
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
                    if (!active) return;
                    int callback = System.Threading.Volatile.Read(ref callbackGeneration);
                    dispatcher.Enqueue(() =>
                    {
                        if (!active || callback != System.Threading.Volatile.Read(ref callbackGeneration)) return;
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
                            int generation = System.Threading.Volatile.Read(ref callbackGeneration);
                            Game game = Current.Game;
                            dispatcher.Enqueue(() =>
                            {
                                if (!active || generation != System.Threading.Volatile.Read(ref callbackGeneration)
                                    || !ReferenceEquals(Current.Game, game)) return;
                                try
                                {
                                    // 尚未进入存档时 Find.LetterStack 为 null，直接弹信会 NRE，故做空保护。
                                    LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("TradeCreated");
                                    if (letterDef != null && Find.LetterStack != null)
                                    {
                                        Find.LetterStack.ReceiveLetter(
                                            "Phinix_chat_legacyModeLetter_label".Translate(),
                                            "Phinix_chat_legacyModeLetter_description".Translate(),
                                            letterDef);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    hostLog?.Invoke($"Failed to raise legacy-mode chat letter: {ex.Message}", LogLevel.WARNING);
                                }
                            });
                        }
                    });
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
            active = false;
            System.Threading.Interlocked.Increment(ref callbackGeneration);
            if (lifecycle != null && compatibilityChangedHandler != null)
            {
                Cleanup(() => lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler, hostContext);
            }

            if (chatService != null && chatNotificationHandler != null)
            {
                Cleanup(() => chatService.OnChatMessageReceived -= chatNotificationHandler, hostContext);
            }

            if (disconnectHandler != null && hostContext != null
                && hostContext.TryGetService<IClientUserEventStream>(out var userEventStream))
            {
                Cleanup(() => userEventStream.Disconnected -= disconnectHandler, hostContext);
            }

            if (currentTheme != null && themeChanged != null) currentTheme.Changed -= themeChanged;
            currentTheme = null; themeChanged = null;
            ChatTheme.Release();
            themeRegistration?.Dispose(); themeRegistration = null;
            composition?.Dispose();
            composition = null;
        }

        private static void Cleanup(Action cleanup, ExtensionHostContext host)
        {
            try { cleanup(); }
            catch (Exception error)
            {
                try { host?.Log?.Invoke("Chat cleanup failed: " + error, LogLevel.WARNING); } catch { }
            }
        }

        private static void RegisterThemeDefaults(IClientThemeRegistration theme)
        {
            theme.RegisterColor("chat.messageText", Color.white, fallbackKey: "ui.text.primary", label: "Phinix_theme_label_chat_messageText", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_chat_messageText");
            theme.RegisterColor("chat.inputText", Color.white, fallbackKey: "ui.text.primary", label: "Phinix_theme_label_chat_inputText", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_chat_inputText");
            theme.RegisterFloat("chat.nameColor.saturation", .55f, label: "Phinix_theme_label_chat_nameColor_saturation", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_chat_nameColor_saturation");
            theme.RegisterFloat("chat.nameColor.value", .85f, label: "Phinix_theme_label_chat_nameColor_value", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_chat_nameColor_value");
            theme.RegisterColor("chat.mentionText", new Color(0.45f, 0.75f, 1.0f, 1.0f), label: "Phinix_theme_label_chat_mentionText", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_chat_mentionText");
            theme.RegisterColor("chat.mentionSelfBg", new Color(0.35f, 0.35f, 0.15f, 0.12f), label: "Phinix_theme_label_chat_mentionSelfBg", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_chat_mentionSelfBg");
            theme.RegisterColor("chat.selfName", new Color(0.55f, 0.75f, 1.0f, 1.0f), label: "Phinix_theme_label_chat_selfName", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_chat_selfName");
            theme.RegisterColor("chat.selfMessageBg", new Color(0.15f, 0.25f, 0.4f, 0.1f), label: "Phinix_theme_label_chat_selfMessageBg", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_chat_selfMessageBg");
            theme.RegisterColor("chat.rowHoverBg", new Color(1f, 1f, 1f, 0.04f), fallbackKey: "ui.hover", label: "Phinix_theme_label_chat_rowHoverBg", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_chat_rowHoverBg");
            theme.RegisterColor("chat.groupIndentLine", new Color(1f, 1f, 1f, 0.08f), fallbackKey: "ui.separator", label: "Phinix_theme_label_chat_groupIndentLine", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_chat_groupIndentLine");
            theme.RegisterColor("chat.replyQuoteBorder", new Color(0.3f, 0.5f, 0.75f, 0.6f), label: "Phinix_theme_label_chat_replyQuoteBorder", group: "Phinix_theme_group_reply", description: "Phinix_theme_desc_chat_replyQuoteBorder");
            theme.RegisterColor("chat.replyQuoteBg", new Color(1f, 1f, 1f, 0.03f), label: "Phinix_theme_label_chat_replyQuoteBg", group: "Phinix_theme_group_reply", description: "Phinix_theme_desc_chat_replyQuoteBg");
            theme.RegisterColor("chat.replyQuoteText", new Color(0.55f, 0.52f, 0.48f, 0.7f), fallbackKey: "ui.text.secondary", label: "Phinix_theme_label_chat_replyQuoteText", group: "Phinix_theme_group_reply", description: "Phinix_theme_desc_chat_replyQuoteText");
            theme.RegisterColor("chat.noticeAccent", new Color(0.9f, 0.72f, 0.25f, 0.9f), label: "Phinix_theme_label_chat_noticeAccent", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_noticeAccent");
            theme.RegisterColor("chat.noticeBg", new Color(0.25f, 0.2f, 0.08f, 0.12f), label: "Phinix_theme_label_chat_noticeBg", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_noticeBg");
            theme.RegisterColor("chat.noticeBannerBg", new Color(0.12f, 0.1f, 0.06f, 0.9f), label: "Phinix_theme_label_chat_noticeBannerBg", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_noticeBannerBg");
            theme.RegisterColor("chat.noticeProgress", new Color(0.9f, 0.72f, 0.25f, 0.7f), label: "Phinix_theme_label_chat_noticeProgress", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_noticeProgress");
            theme.RegisterColor("chat.inputReplyBorder", new Color(0.3f, 0.5f, 0.9f, 0.7f), label: "Phinix_theme_label_chat_inputReplyBorder", group: "Phinix_theme_group_reply", description: "Phinix_theme_desc_chat_inputReplyBorder");
            theme.RegisterColor("chat.inputReplyBg", new Color(0.15f, 0.25f, 0.45f, 0.08f), label: "Phinix_theme_label_chat_inputReplyBg", group: "Phinix_theme_group_reply", description: "Phinix_theme_desc_chat_inputReplyBg");
            theme.RegisterColor("chat.blockedBg", new Color(0f, 0f, 0f, 0.35f), label: "Phinix_theme_label_chat_blockedBg", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_blockedBg");
            theme.RegisterColor("chat.blockedName", new Color(0.6f, 0.6f, 0.6f), label: "Phinix_theme_label_chat_blockedName", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_blockedName");
            theme.RegisterColor("chat.pendingMessage", new Color(1f, 1f, 1f, 0.6f), fallbackKey: "ui.pending", label: "Phinix_theme_label_chat_pendingMessage", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_pendingMessage");
            theme.RegisterColor("chat.deniedMessage", new Color(0.94f, 0.28f, 0.28f), fallbackKey: "ui.error", label: "Phinix_theme_label_chat_deniedMessage", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_chat_deniedMessage");
            theme.RegisterColor("chat.imagePlaceholderBg", new Color(1f, 1f, 1f, 0.05f), label: "Phinix_theme_label_chat_imagePlaceholderBg", group: "Phinix_theme_group_image", description: "Phinix_theme_desc_chat_imagePlaceholderBg");
            theme.RegisterColor("chat.imageFailedText", new Color(0.7f, 0.4f, 0.4f, 0.8f), fallbackKey: "ui.error", label: "Phinix_theme_label_chat_imageFailedText", group: "Phinix_theme_group_image", description: "Phinix_theme_desc_chat_imageFailedText");
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return FrameworkChatProtocol.MessageType;
            yield return FrameworkChatProtocol.HistoryRequestType;
            yield return FrameworkChatProtocol.HistorySyncCompleteType;
        }

        public bool CanHandleOutgoingMessage(FrameworkPacket message)
            => (messageHandler as IClientOutgoingMessageHandler)?.CanHandleOutgoingMessage(message) == true;
        public ClientOutgoingMessageResult HandleOutgoingMessage(FrameworkPacket message, ClientFrameworkContext context)
            => ((IClientOutgoingMessageHandler)messageHandler).HandleOutgoingMessage(message, context);

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
