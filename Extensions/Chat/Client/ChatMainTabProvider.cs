using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using UnityEngine;
using Utils;
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class ChatMainTabProvider : IMainTabProvider, IResponsiveMainTabProvider, IBadgeProvider, IUiAcceptKeyHandler
    {
        private const float CHAT_TEXTBOX_HEIGHT = 30f;
        private const float CHAT_SEND_BUTTON_WIDTH = 80f;
        private const float DEFAULT_SPACING = 10f;
        private const float MINIMUM_INPUT_WIDTH = 180f;
        private const float MINIMUM_REPLY_BAR_HEIGHT = 24f;
        private const float MAXIMUM_REPLY_BAR_HEIGHT = 84f;
        private const float REPLY_LINE_WIDTH = 3f;
        private const float REPLY_CLOSE_WIDTH = 24f;
        private const float REPLY_TEXT_PADDING = 4f;
        private const string CHAT_INPUT_CONTROL = "PhinixChatMessageInput";
        private const int REFOCUS_ATTEMPTS = 2;

        private static readonly UiLayoutHints CachedLayoutHints = new UiLayoutHints(
            new Vector2(360f, 260f),
            new Vector2(800f, 560f),
            true);

        private readonly IChatUiHostContext hostContext;
        private readonly IChatTabContent chatMessageList;
        private readonly IClientUserDirectory userDirectory;

        private string message = "";
        private string lastAutocompleteText = "";
        private bool chatInputOwned;
        private int refocusAttemptsRemaining;
        private UIChatMessage cachedReplyTarget;
        private object cachedReplyLanguage;
        private float cachedReplyWidth = -1f;
        private float cachedReplyHeight;
        private string cachedReplyLabel;
        private string cachedReplyTooltip;
        private object cachedSendLanguage;
        private string cachedSendLabel;
        private float cachedSendWidth;

        public string TabLabel => "Phinix_tabs_chat".Translate();
        public float TabOrder => 0;
        public UiLayoutHints LayoutHints => CachedLayoutHints;

        public bool WantsAcceptKey =>
            chatInputOwned &&
            !string.IsNullOrEmpty(message) &&
            Find.WindowStack.FloatMenu == null;

        public string BadgeText
        {
            get
            {
                int unread = hostContext.ChatService.UnreadMessages;
                if (unread <= 0) return null;
                return unread > 99 ? "99+" : unread.ToString();
            }
        }

        public ChatMainTabProvider(IChatUiHostContext hostContext, IChatTabContent chatMessageList, IClientUserDirectory userDirectory = null)
        {
            this.hostContext = hostContext;
            this.chatMessageList = chatMessageList;
            this.userDirectory = userDirectory;
        }

        public void Draw(Rect inRect)
        {
            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);

            UIChatMessage replyTarget = hostContext.ReplyTarget;
            float replyBarHeight = replyTarget != null ? GetReplyBarHeight(replyTarget, inRect.width) : 0f;
            EnsureSendLabelCache();
            float sendButtonWidth = Mathf.Min(inRect.width, cachedSendWidth);
            bool stackInput = inRect.width < sendButtonWidth + DEFAULT_SPACING + MINIMUM_INPUT_WIDTH;
            float inputAreaHeight = stackInput
                ? CHAT_TEXTBOX_HEIGHT * 2f + DEFAULT_SPACING
                : CHAT_TEXTBOX_HEIGHT;

            Rect inputAreaRect = inRect.BottomPartPixels(Mathf.Min(inputAreaHeight, inRect.height));
            Rect messageBoxRect;
            Rect sendButtonRect;
            if (stackInput)
            {
                messageBoxRect = new Rect(inputAreaRect.xMin, inputAreaRect.yMin, inputAreaRect.width, Mathf.Min(CHAT_TEXTBOX_HEIGHT, inputAreaRect.height));
                float buttonY = messageBoxRect.yMax + DEFAULT_SPACING;
                sendButtonRect = new Rect(
                    inputAreaRect.xMax - sendButtonWidth,
                    buttonY,
                    sendButtonWidth,
                    Mathf.Min(CHAT_TEXTBOX_HEIGHT, Mathf.Max(0f, inputAreaRect.yMax - buttonY)));
            }
            else
            {
                sendButtonRect = inputAreaRect.RightPartPixels(sendButtonWidth);
                messageBoxRect = new Rect(
                    inputAreaRect.xMin,
                    inputAreaRect.yMin,
                    Mathf.Max(0f, inputAreaRect.width - sendButtonWidth - DEFAULT_SPACING),
                    inputAreaRect.height);
            }

            Rect replyBarRect = replyBarHeight > 0f
                ? inRect.BottomPartPixels(Mathf.Min(inputAreaHeight + replyBarHeight, inRect.height)).TopPartPixels(Mathf.Min(replyBarHeight, Mathf.Max(0f, inRect.height - inputAreaHeight)))
                : default;

            float reservedHeight = inputAreaHeight + replyBarHeight + DEFAULT_SPACING;
            Rect chatRect = inRect.TopPartPixels(Mathf.Max(0f, inRect.height - reservedHeight));

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
                if (Widgets.ButtonText(closeRect, "×"))
                {
                    hostContext.ClearReplyTarget();
                }
            }

            GUI.SetNextControlName(CHAT_INPUT_CONTROL);
            message = Widgets.TextField(messageBoxRect, message);
            UpdateInputOwnership(messageBoxRect);
            TryRefocusAfterAccept();

            if (sendButtonRect.width > 0f && sendButtonRect.height > 0f && Widgets.ButtonText(sendButtonRect, cachedSendLabel))
            {
                sendChatMessage();
            }

            HandleAtAutocomplete();
        }

        private float GetReplyBarHeight(UIChatMessage replyTarget, float availableWidth)
        {
            object language = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(replyTarget, cachedReplyTarget) ||
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
                cachedReplyTarget = replyTarget;
                cachedReplyLanguage = language;
                cachedReplyWidth = availableWidth;
            }

            return cachedReplyHeight;
        }

        private void EnsureSendLabelCache()
        {
            object language = LanguageDatabase.activeLanguage;
            if (ReferenceEquals(language, cachedSendLanguage) && cachedSendLabel != null)
            {
                return;
            }

            cachedSendLanguage = language;
            cachedSendLabel = "Phinix_chat_sendButton".Translate();
            cachedSendWidth = Mathf.Max(CHAT_SEND_BUTTON_WIDTH, Text.CalcSize(cachedSendLabel).x + 24f);
        }

        public bool TryHandleAcceptKey()
        {
            if (!WantsAcceptKey)
            {
                return false;
            }

            sendChatMessage();
            refocusAttemptsRemaining = REFOCUS_ATTEMPTS;
            return true;
        }

        private void UpdateInputOwnership(Rect messageBoxRect)
        {
            Event current = Event.current;
            bool mouseDown = current != null &&
                (current.type == EventType.MouseDown || current.rawType == EventType.MouseDown);

            if (mouseDown)
            {
                chatInputOwned = current.button == 0 && Mouse.IsOver(messageBoxRect);
                refocusAttemptsRemaining = 0;
                return;
            }

            bool currentlyFocused = GUI.GetNameOfFocusedControl() == CHAT_INPUT_CONTROL;
            if (currentlyFocused)
            {
                chatInputOwned = true;
            }
        }

        private void TryRefocusAfterAccept()
        {
            if (refocusAttemptsRemaining <= 0)
            {
                return;
            }

            Event current = Event.current;
            bool mouseDown = current != null &&
                (current.type == EventType.MouseDown || current.rawType == EventType.MouseDown);
            Window currentWindow = Find.WindowStack.currentlyDrawnWindow;
            if (mouseDown ||
                Find.WindowStack.FloatMenu != null ||
                currentWindow == null ||
                !Find.WindowStack.GetsInput(currentWindow))
            {
                refocusAttemptsRemaining = 0;
                return;
            }

            if (current == null || current.type != EventType.Repaint)
            {
                return;
            }

            GUI.FocusControl(CHAT_INPUT_CONTROL);
            refocusAttemptsRemaining--;
            chatInputOwned = true;
            if (GUI.GetNameOfFocusedControl() == CHAT_INPUT_CONTROL)
            {
                refocusAttemptsRemaining = 0;
            }
        }

        private void HandleAtAutocomplete()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            string previousText = lastAutocompleteText;
            lastAutocompleteText = message;

            Window currentWindow = Find.WindowStack.currentlyDrawnWindow;
            bool canOpen = GUI.GetNameOfFocusedControl() == CHAT_INPUT_CONTROL &&
                currentWindow != null && Find.WindowStack.GetsInput(currentWindow) &&
                Find.WindowStack.FloatMenu == null;
            if (!ChatMentionUtility.TryGetAutocompletePartial(message, previousText, canOpen, out string partial)) return;

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
                string capturedMessage = message;
                options.Add(new FloatMenuOption("@" + capturedName, () =>
                {
                    if (message != capturedMessage) return;
                    message = ChatMentionUtility.ReplaceAtPartial(message, capturedName);
                    refocusAttemptsRemaining = REFOCUS_ATTEMPTS;
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
                hostContext.SendChatMessage(message, mentionedUuids);
                chatMessageList.ScrollToBottom();
                message = "";
                hostContext.ClearReplyTarget();
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
