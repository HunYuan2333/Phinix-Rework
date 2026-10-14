using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using UnityEngine;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class ChatMainTabProvider : IMainTabProvider, IResponsiveMainTabProvider, IBadgeProvider, IUiAcceptKeyHandler, IClientViewLifecycle, IClientNavigationMetadata, IDisposable
    {
        private const float MINIMUM_REPLY_BAR_HEIGHT = 24f;
        private const float MAXIMUM_REPLY_BAR_HEIGHT = 84f;
        private const float REPLY_LINE_WIDTH = 3f;
        private const float REPLY_CLOSE_WIDTH = 24f;
        private const float REPLY_TEXT_PADDING = 4f;
        private string CHAT_INPUT_CONTROL => composer?.ComposerControlName ?? "PhinixChatMessageInput";

        private static readonly UiLayoutHints CachedLayoutHints = new UiLayoutHints(
            new Vector2(360f, 260f),
            new Vector2(800f, 560f),
            true);

        private readonly IChatUiHostContext hostContext;
        private readonly IChatTabContent chatMessageList;
        private readonly IClientUserDirectory userDirectory;

        private string message = "";
        private string lastAutocompleteText = "";
        private long replyThemeVersion = -1;
        private UIChatMessage cachedReplyTarget;
        private object cachedReplyLanguage;
        private float cachedReplyWidth = -1f;
        private float cachedReplyHeight;
        private string cachedReplyLabel;
        private string cachedReplyTooltip;

        public string TabLabel => "Phinix_tabs_chat".Translate();
        public float TabOrder => 0;
        public UiLayoutHints LayoutHints => CachedLayoutHints;

        public bool WantsAcceptKey => false; // Host composer consumes Enter/IME before shell accept handling.
        public string NavigationId => "chat";
        private readonly IClientPresentationService presentation;
        private readonly IClientExtensionPointService points;
        private IClientExtensionPointScope actionScope;
        private IClientActionPoint<ChatComposerActionContext> composerPoint;
        private IPhinixExtensionModule module;
        private IClientPresentationSession composer;
        private long composerRevision, publishedPoints = -1;
        private string publishedMessage;
        private UIChatMessage publishedReply;
        private object publishedLanguage;
        private bool publishedOnline;
        private ClientActionExecutionResult publishedResult;
        private Texture2D sendIcon;
        internal void Start(IPhinixExtensionModule owner)
        {
            module = owner; actionScope = points.ForModule(owner);
            composerPoint = actionScope.DefineActions<ChatComposerActionContext>(ChatComposerActionContext.PointId);
            actionScope.ContributeActions<ChatComposerActionContext>(ChatComposerActionContext.PointId, context => new[] {
                new ClientActionDefinition("send", "Phinix_chat_sendButton".Translate(), sendChatMessage,
                    !hostContext.IsOnline ? "Phinix_chat_sendUnavailable".Translate().ToString() : string.IsNullOrEmpty(context.Text) ? "Phinix_chat_composePlaceholder".Translate().ToString() : null)
            });
            sendIcon = ContentFinder<Texture2D>.Get("UI/Phinix/Send", false);
        }
        public void ViewOpened()
        {
            if (module == null || composer != null && !composer.IsClosed) return;
            composer = presentation.Open(module, "chat-composer"); publishedMessage = null; RefreshComposer(true);
        }
        public void ViewClosed()
        {
            (chatMessageList as ChatMessageList)?.ViewClosed();
            var previous = composer;
            if (previous != null && !previous.IsClosed) message = previous.ComposerDraft;
            composer = null; previous?.Dispose();
        }
        public void ViewUpdate()
        {
            if (composer == null || composer.IsClosed) return;
            (chatMessageList as ChatMessageList)?.ViewUpdate();
            bool submit = false; string latestDraft = composer.ComposerDraft;
            while (composer.TryDequeueInput(out var input))
            {
                if (input.Id != "message") continue;
                latestDraft = input.Value;
                if (input.Kind == ClientPresentationInputKind.SubmitRequested) submit = true;
            }
            message = latestDraft;
            RefreshComposer(submit);
            if (submit)
            {
                composer.InvokeAction("builtin.chat.send");
                composer.RequestComposerFocus();
                RefreshComposer(true);
            }
        }
        private void RefreshComposer(bool force)
        {
            var reply = hostContext.ReplyTarget; var language = LanguageDatabase.activeLanguage;
            bool online = hostContext.IsOnline; var result = composer.LastActionResult;
            if (!force && publishedMessage == message && ReferenceEquals(reply, publishedReply) && ReferenceEquals(language, publishedLanguage) &&
                publishedOnline == online && publishedPoints == points.Revision && ReferenceEquals(result, publishedResult)) return;
            string text = message; long contributionRevision = points.Revision;
            Func<bool> current = () => composer != null && !composer.IsClosed && message == text && ReferenceEquals(hostContext.ReplyTarget, reply) &&
                hostContext.IsOnline == online && ReferenceEquals(LanguageDatabase.activeLanguage, language) && contributionRevision == points.Revision;
            var actions = composerPoint.Collect(new ChatComposerActionContext(text, reply?.MessageId), current);
            try
            {
                IClientContributedAction send = null; var extras = new List<ClientPresentationAction>();
                foreach (var action in actions.Actions) if (action.OwnerId == module.ExtensionId && action.Id == "send") send = action; else extras.Add(new ClientPresentationAction(action.OwnerId + "." + action.Id, action));
                if (send == null) throw new InvalidOperationException("ComposerSendMissing");
                composer.Publish(new ClientPresentationPage("chat-composer", "", ++composerRevision, null,
                    new ClientPresentationComposer(new ClientPresentationTextField("message", "", text, "Phinix_chat_composePlaceholder".Translate(), 4096),
                        new ClientPresentationAction("builtin.chat.send", send, ClientButtonKind.Primary), sendIcon, extras)), actions, current);
            }
            catch { actions.Dispose(); throw; }
            publishedMessage = message; publishedReply = reply; publishedLanguage = language; publishedOnline = online; publishedResult = result; publishedPoints = contributionRevision;
        }
        public void Dispose() { ViewClosed(); actionScope?.Dispose(); actionScope = null; composerPoint = null; module = null; sendIcon = null; }

        public string BadgeText
        {
            get
            {
                int unread = hostContext.ChatService.UnreadMessages;
                if (unread <= 0) return null;
                return unread > 99 ? "99+" : unread.ToString();
            }
        }

        public ChatMainTabProvider(IChatUiHostContext hostContext, IChatTabContent chatMessageList, IClientUserDirectory userDirectory, IClientPresentationService presentation, IClientExtensionPointService points)
        {
            this.hostContext = hostContext;
            this.chatMessageList = chatMessageList;
            this.userDirectory = userDirectory; this.presentation = presentation; this.points = points;
        }


        public void Draw(Rect inRect)
        {
            using (ChatTheme.BeginDraw())
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.color = ChatTheme.Current.PrimaryText;
                DrawContents(inRect);
            }
        }

        private void DrawContents(Rect inRect)
        {
            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);

            UIChatMessage replyTarget = hostContext.ReplyTarget;
            float replyBarHeight = replyTarget != null ? GetReplyBarHeight(replyTarget, inRect.width) : 0f;
            float inputAreaHeight = composer != null && !composer.IsClosed && inRect.width > 0 ? composer.MeasureComposerHeight(inRect.width) : 64;
            Rect inputAreaRect = inRect.BottomPartPixels(Mathf.Min(inputAreaHeight, inRect.height));
            Rect replyBarRect = replyBarHeight > 0f
                ? inRect.BottomPartPixels(Mathf.Min(inputAreaHeight + replyBarHeight, inRect.height)).TopPartPixels(Mathf.Min(replyBarHeight, Mathf.Max(0, inRect.height - inputAreaHeight))) : default;
            Rect chatRect = inRect.TopPartPixels(Mathf.Max(0, inRect.height - inputAreaHeight - replyBarHeight - 8));
            // IMGUI text editing is owned by a numeric control ID, not just the
            // control name. Allocate the composer before the changing feed/reply
            // controls so incoming messages cannot shift its keyboard/IME state.
            composer?.Draw(inputAreaRect);
            chatMessageList.Draw(chatRect);
            if (replyTarget != null && replyBarRect.height > 0f)
            {
                Rect lineRect = new Rect(replyBarRect.xMin, replyBarRect.yMin, REPLY_LINE_WIDTH, replyBarRect.height);
                Widgets.DrawBoxSolid(lineRect, ChatTheme.InputReplyBorder);

                Rect bgRect = new Rect(replyBarRect.xMin + REPLY_LINE_WIDTH, replyBarRect.yMin, Mathf.Max(0f, replyBarRect.width - REPLY_LINE_WIDTH), replyBarRect.height);
                Widgets.DrawBoxSolid(bgRect, ChatTheme.InputReplyBg);

                Rect labelRect = new Rect(
                    replyBarRect.xMin + REPLY_LINE_WIDTH + 4f,
                    replyBarRect.yMin,
                    Mathf.Max(0f, replyBarRect.width - REPLY_LINE_WIDTH - REPLY_CLOSE_WIDTH - REPLY_TEXT_PADDING * 2f),
                    replyBarRect.height);
                Widgets.Label(labelRect, cachedReplyLabel);
                TooltipHandler.TipRegion(labelRect, cachedReplyTooltip);

                Rect closeRect = replyBarRect.RightPartPixels(Mathf.Min(REPLY_CLOSE_WIDTH, replyBarRect.width));
                if (ClientWidgets.Button(closeRect, "×", ChatTheme.Current))
                {
                    hostContext.ClearReplyTarget();
                }
            }

            HandleAtAutocomplete();
        }

        private float GetReplyBarHeight(UIChatMessage replyTarget, float availableWidth)
        {
            object language = LanguageDatabase.activeLanguage;
            if (replyThemeVersion != ChatTheme.Version || !ReferenceEquals(replyTarget, cachedReplyTarget) ||
                !ReferenceEquals(language, cachedReplyLanguage) ||
                !Mathf.Approximately(availableWidth, cachedReplyWidth))
            {
                string snippet = TextHelper.StripRichText(replyTarget.Message ?? string.Empty);
                if (snippet.Length > 200)
                {
                    snippet = snippet.Substring(0, 200) + "...";
                }

                string displayName = TextHelper.StripRichText(replyTarget.User.DisplayName);
                cachedReplyTooltip = "↩ " + displayName + ": " + TextHelper.StripRichText(replyTarget.Message ?? string.Empty);
                cachedReplyLabel = ("↩ " + displayName + ": " + snippet).Colorize(ChatTheme.ReplyQuoteText);
                float textWidth = Mathf.Max(1f, availableWidth - REPLY_LINE_WIDTH - REPLY_CLOSE_WIDTH - REPLY_TEXT_PADDING * 2f);
                cachedReplyHeight = Mathf.Clamp(Text.CalcHeight(cachedReplyLabel, textWidth) + 4f, MINIMUM_REPLY_BAR_HEIGHT, MAXIMUM_REPLY_BAR_HEIGHT);
                replyThemeVersion = ChatTheme.Version;
                cachedReplyTarget = replyTarget;
                cachedReplyLanguage = language;
                cachedReplyWidth = availableWidth;
            }

            return cachedReplyHeight;
        }

        public bool TryHandleAcceptKey() => false;

        private void HandleAtAutocomplete()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            string previousText = lastAutocompleteText;
            string draft = composer?.ComposerDraft ?? message;
            lastAutocompleteText = draft;

            Window currentWindow = Find.WindowStack.currentlyDrawnWindow;
            bool canOpen = GUI.GetNameOfFocusedControl() == CHAT_INPUT_CONTROL &&
                currentWindow != null && Find.WindowStack.GetsInput(currentWindow) &&
                Find.WindowStack.FloatMenu == null;
            if (!ChatMentionUtility.TryGetAutocompletePartial(draft, previousText, canOpen, out string partial)) return;

            if (userDirectory == null) return;
            ImmutableUser[] onlineUsers = userDirectory.GetUsers(loggedIn: true);
            if (onlineUsers.Length == 0) return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            string localUuid = hostContext.Uuid;

            foreach (ImmutableUser user in onlineUsers)
            {
                if (user.Uuid == localUuid) continue;
                string displayName = TextHelper.StripRichText(user.DisplayName);
                if (string.IsNullOrEmpty(displayName)) continue;
                if (displayName.IndexOf(partial, StringComparison.InvariantCultureIgnoreCase) < 0) continue;

                string capturedName = displayName;
                string capturedMessage = draft;
                options.Add(new FloatMenuOption("@" + capturedName, () =>
                {
                    if (composer == null || composer.IsClosed || composer.ComposerDraft != capturedMessage) return;
                    composer.SetComposerDraft(ChatMentionUtility.ReplaceAtPartial(capturedMessage, capturedName));
                    composer.RequestComposerFocus();
                }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void sendChatMessage()
        {
            if (!string.IsNullOrEmpty(message))
            {
                HashSet<string> mentionedUuids = ParseMentions(message);
                if (!hostContext.SendChatMessage(message, mentionedUuids))
                {
                    Messages.Message("Phinix_chat_sendUnavailable".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                    return;
                }
                chatMessageList.ScrollToBottom();
                message = "";
            }
        }

        private HashSet<string> ParseMentions(string text)
        {
            HashSet<string> result = new HashSet<string>();
            if (userDirectory == null) return result;

            ImmutableUser[] onlineUsers = userDirectory.GetUsers(loggedIn: true);
            foreach (ImmutableUser user in onlineUsers)
            {
                string displayName = TextHelper.StripRichText(user.DisplayName);
                if (string.IsNullOrEmpty(displayName)) continue;
                if (text.Contains("@" + displayName))
                {
                    result.Add(user.Uuid);
                }
            }

            return result;
        }
    }
}
