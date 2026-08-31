using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using UnityEngine;
using Utils;
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class ChatMainTabProvider : IMainTabProvider, IBadgeProvider
    {
        private const float CHAT_TEXTBOX_HEIGHT = 30f;
        private const float CHAT_SEND_BUTTON_WIDTH = 80f;
        private const float DEFAULT_SPACING = 10f;
        private const float REPLY_BAR_HEIGHT = 24f;
        private const float REPLY_LINE_WIDTH = 3f;

        private static readonly Regex AtPartialRegex = new Regex(@"@([^\s]*)$", RegexOptions.Compiled);

        private readonly IChatUiHostContext hostContext;
        private readonly IChatTabContent chatMessageList;
        private readonly IClientUserDirectory userDirectory;

        private string message = "";

        public string TabLabel => "Phinix_tabs_chat".Translate();
        public float TabOrder => 0;

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
            float replyBarHeight = hostContext.ReplyTarget != null ? REPLY_BAR_HEIGHT : 0f;

            Rect inputAreaRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT);
            Rect sendButtonRect = inputAreaRect.RightPartPixels(CHAT_SEND_BUTTON_WIDTH);
            Rect messageBoxRect = inputAreaRect.LeftPartPixels(inRect.width - (CHAT_SEND_BUTTON_WIDTH + DEFAULT_SPACING));

            Rect replyBarRect = replyBarHeight > 0f
                ? inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT + replyBarHeight).TopPartPixels(replyBarHeight)
                : default;

            Rect chatRect = inRect.TopPartPixels(inRect.height - (CHAT_TEXTBOX_HEIGHT + replyBarHeight + DEFAULT_SPACING));

            chatMessageList.Draw(chatRect);

            if (hostContext.ReplyTarget != null)
            {
                string snippet = TextHelper.StripRichText(hostContext.ReplyTarget.Message ?? "");
                if (snippet.Length > 50) snippet = snippet.Substring(0, 50) + "...";
                string displayName = TextHelper.StripRichText(hostContext.ReplyTarget.User.DisplayName);

                Rect lineRect = new Rect(replyBarRect.xMin, replyBarRect.yMin, REPLY_LINE_WIDTH, replyBarRect.height);
                Widgets.DrawBoxSolid(lineRect, ChatTheme.InputReplyBorder);

                Rect bgRect = new Rect(replyBarRect.xMin + REPLY_LINE_WIDTH, replyBarRect.yMin, replyBarRect.width - REPLY_LINE_WIDTH, replyBarRect.height);
                Widgets.DrawBoxSolid(bgRect, ChatTheme.InputReplyBg);

                Rect labelRect = new Rect(
                    replyBarRect.xMin + REPLY_LINE_WIDTH + 4f,
                    replyBarRect.yMin,
                    replyBarRect.width - REPLY_LINE_WIDTH - 28f,
                    REPLY_BAR_HEIGHT);
                Widgets.Label(labelRect, ("↩ " + displayName + ": " + snippet).Colorize(ChatTheme.ReplyQuoteText));

                Rect closeRect = replyBarRect.RightPartPixels(24f);
                if (Widgets.ButtonText(closeRect, "×"))
                {
                    hostContext.ClearReplyTarget();
                }
            }

            bool enterPressed = Event.current != null &&
                Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            message = Widgets.TextField(messageBoxRect, message);

            if (enterPressed && !string.IsNullOrEmpty(message))
            {
                sendChatMessage();
                // Fully claim the Enter/KeypadEnter event here so it neither
                // bubbles up to RimWorld's Window layer (which could close this
                // MainTabWindow on Enter) nor leaks to other mods that draw later
                // in the same frame. This is intentionally scoped to "we actually
                // sent": when the message is empty we leave the event untouched,
                // so RimTalk & co. still get their own Enter-to-send.
                Event.current.Use();
                Event.current.keyCode = KeyCode.None;
            }

            if (Widgets.ButtonText(sendButtonRect, "Phinix_chat_sendButton".Translate()))
            {
                sendChatMessage();
            }

            HandleAtAutocomplete(messageBoxRect);
        }

        private void HandleAtAutocomplete(Rect textFieldRect)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (string.IsNullOrEmpty(message)) return;

            Match match = AtPartialRegex.Match(message);
            if (!match.Success) return;

            string partial = match.Groups[1].Value;
            if (string.IsNullOrEmpty(partial) || partial.Length < 1) return;

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
                options.Add(new FloatMenuOption("@" + capturedName, () =>
                {
                    message = ReplaceAtPartial(message, capturedName);
                }));
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private static string ReplaceAtPartial(string text, string fullName)
        {
            Match match = AtPartialRegex.Match(text);
            if (!match.Success) return text;

            int atIndex = match.Index;
            return text.Substring(0, atIndex + 1) + fullName + " " + text.Substring(atIndex + 1 + match.Groups[1].Value.Length);
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
