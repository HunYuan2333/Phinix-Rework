using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class ChatMessageList : IChatTabContent
    {
        private const float SCROLLBAR_WIDTH = 16f;
        private const float NAME_LINE_HEIGHT = 18f;
        private const float MESSAGE_TOP_PADDING = 1f;
        private const float MESSAGE_BOTTOM_PADDING = 6f;
        private const float GROUP_TOP_PADDING = 2f;
        private const float GROUP_BOTTOM_PADDING = 2f;
        private const float GROUP_INDENT = 16f;
        private const float QUOTE_LINE_WIDTH = 3f;
        private const float QUOTE_INDENT = 10f;
        private const float QUOTE_HEIGHT = 18f;
        private const float NOTICE_LINE_WIDTH = 3f;
        private const float TIMESTAMP_RIGHT_MARGIN = 6f;
        private const float HIGHLIGHT_BORDER_WIDTH = 2f;
        private const float HIGHLIGHT_FLASH_SECONDS = 1.2f;
        private const long GROUP_TIME_THRESHOLD_SECONDS = 60;

        private static readonly Regex UrlRegex = new Regex(@"https?:\/\/\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MentionRegex = new Regex(@"@(\S+)", RegexOptions.Compiled);

        private readonly List<UIChatMessage> filteredMessages = new List<UIChatMessage>();
        private readonly List<UIChatMessage> messages = new List<UIChatMessage>();
        private readonly object messagesLock = new object();
        private readonly Dictionary<string, Rect> messageRectCache = new Dictionary<string, Rect>();
        private readonly Dictionary<string, CachedMessageDisplay> displayCache = new Dictionary<string, CachedMessageDisplay>();
        private readonly IChatUiHostContext hostContext;

        private bool messagesChanged;
        private Vector2 chatScroll = new Vector2(0, 0);
        private float oldHeight;
        private float cachedTotalHeight;
        private bool scrollToBottom;
        private bool stickyScroll = true;
        private bool clearMessages;

        private float viewportHeight;
        private string hoveredReplyTargetId;
        private string flashHighlightId;
        private float flashHighlightUntil;

        private struct CachedMessageDisplay
        {
            public string MessageText;
            public Vector2 TimestampSize;
            public Vector2 DisplayNameSize;
            public bool ShowNameFormatting;
            public bool ShowChatFormatting;
            public UIChatMessageStatus Status;
            public string ReplyQuoteText;
            public string ReplyQuoteSenderName;
            public bool IsNotice;
            public bool IsSelf;
            public bool IsSystem;
            public bool IsGrouped;
            public string TimestampText;
            public string SenderDisplayName;
            public string DisplayNameFormatted;
            public bool HasMentions;
            public float NameLineHeight;
        }

        public ChatMessageList(IChatUiHostContext hostContext)
        {
            this.hostContext = hostContext;

            hostContext.ChatService.OnChatMessageReceived += ChatMessageReceivedEventHandler;
            hostContext.OnUserDisplayNameChanged += UserChangedEventHandler;
            hostContext.OnBlockedUsersChanged += (s, e) => ReplaceWithBuffer();
            hostContext.OnDisconnect += (s, e) => Clear();

            ReplaceWithBuffer();
        }

        public void Draw(Rect inRect)
        {
            if (clearMessages)
            {
                filteredMessages.Clear();
                clearMessages = false;
                recalculateMessageRects(inRect);
            }

            if (messagesChanged)
            {
                if (Monitor.TryEnter(messagesLock))
                {
                    filteredMessages.Clear();
                    filteredMessages.AddRange(messages);
                    messagesChanged = false;

                    hostContext.ChatService.MarkAsRead();

                    Monitor.Exit(messagesLock);
                    recalculateMessageRects(inRect);
                }
            }

            if (filteredMessages.Count == 0)
            {
                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inRect, "Phinix_chat_emptyState".Translate().Colorize(ChatTheme.ReplyQuoteText));
                Text.Anchor = oldAnchor;
                return;
            }

            Rect innerContainer = new Rect(
                x: inRect.xMin,
                y: inRect.yMin,
                width: inRect.width - SCROLLBAR_WIDTH,
                height: cachedTotalHeight);

            Vector2 oldChatScroll = new Vector2(chatScroll.x, chatScroll.y);

            viewportHeight = inRect.height;
            hoveredReplyTargetId = null;

            Widgets.BeginScrollView(inRect, ref chatScroll, innerContainer);

            foreach (UIChatMessage chatMessage in filteredMessages)
            {
                drawChatMessage(messageRectCache[chatMessage.MessageId], chatMessage);
            }

            Widgets.EndScrollView();

            if (flashHighlightId != null && Time.realtimeSinceStartup > flashHighlightUntil)
            {
                flashHighlightId = null;
            }

            bool scrolledToBottom = chatScroll.y.Equals(innerContainer.height - inRect.height);
            bool scrollChanged = !chatScroll.y.Equals(oldChatScroll.y);
            bool heightChanged = !(oldHeight - innerContainer.height).Equals(0f);

            if (scrollChanged)
            {
                stickyScroll = scrolledToBottom;
            }
            else if ((heightChanged && stickyScroll) || scrollToBottom)
            {
                chatScroll.y = innerContainer.height - inRect.height;
                scrollToBottom = false;
            }

            oldHeight = innerContainer.height;
        }

        public void ScrollToBottom()
        {
            scrollToBottom = true;
        }

        public void Clear()
        {
            lock (messagesLock)
            {
                messages.Clear();
                clearMessages = true;
            }
        }

        public void ReplaceWithBuffer()
        {
            lock (messagesLock)
            {
                Clear();
                messages.AddRange(
                    hostContext.ChatService.GetChatMessages()
                        .Skip(Math.Max(0, messages.Count() - hostContext.ChatMessageLimit)));
                messagesChanged = true;
            }
        }

        private void ChatMessageReceivedEventHandler(object sender, UIChatMessageEventArgs args)
        {
            lock (messagesLock)
            {
                messages.Add(args.Message);
                messagesChanged = true;
                messages.RemoveRange(0, Math.Max(0, messages.Count - hostContext.ChatMessageLimit));
            }
        }

        private void UserChangedEventHandler(object sender, UserDisplayNameChangedEventArgs args)
        {
            lock (messagesLock)
            {
                foreach (UIChatMessage chatMessage in messages.Where(m => m.User.Uuid == args.Uuid))
                {
                    chatMessage.User = new ImmutableUser(chatMessage.User.Uuid, args.NewDisplayName, chatMessage.User.LoggedIn, chatMessage.User.AcceptingTrades);
                }

                messagesChanged = true;
            }
        }

        private void recalculateMessageRects(Rect inRect)
        {
            messageRectCache.Clear();
            displayCache.Clear();

            string localUuid = hostContext.Uuid;
            float currentY = inRect.yMin;
            bool showName = hostContext.ShowNameFormatting;
            bool showChat = hostContext.ShowChatFormatting;
            float contentWidth = inRect.width - SCROLLBAR_WIDTH;

            string lastSenderUuid = null;
            DateTime lastTimestamp = DateTime.MinValue;

            foreach (UIChatMessage chatMessage in filteredMessages)
            {
                bool isSelf = chatMessage.SenderUuid == localUuid;
                bool isSystem = chatMessage.SenderUuid == FrameworkProtocol.SystemSenderUuid;
                bool isNotice = chatMessage.IsNotice;

                bool isGrouped = !isSystem && !isNotice
                    && chatMessage.SenderUuid == lastSenderUuid
                    && (chatMessage.Timestamp - lastTimestamp).TotalSeconds < GROUP_TIME_THRESHOLD_SECONDS;

                if (!isGrouped)
                {
                    lastSenderUuid = chatMessage.SenderUuid;
                    lastTimestamp = chatMessage.Timestamp;
                }

                string displayName = (showName && chatMessage.Status == UIChatMessageStatus.Confirmed && !isSystem)
                    ? chatMessage.User.DisplayName
                    : TextHelper.StripRichText(chatMessage.User.DisplayName);
                string messageText = (showChat && chatMessage.Status == UIChatMessageStatus.Confirmed)
                    ? chatMessage.Message
                    : TextHelper.StripRichText(chatMessage.Message);

                string timestampText = string.Format("{0:HH:mm}", chatMessage.Timestamp.ToLocalTime());
                Vector2 timestampSize = Text.CalcSize(timestampText);
                Vector2 displayNameSize = Text.CalcSize(displayName);
                float nameLineHeight = Math.Max(displayNameSize.y, NAME_LINE_HEIGHT);

                bool hasMentions = !isSystem && !isNotice &&
                    chatMessage.MentionedUuids != null &&
                    chatMessage.MentionedUuids.Count > 0;

                Color nameColor = isSelf ? ChatTheme.SelfName : ChatTheme.GetNameColor(chatMessage.SenderUuid);
                string displayNameFormatted = (showName && chatMessage.Status == UIChatMessageStatus.Confirmed && !isSystem)
                    ? ChatTheme.FormatDisplayName(chatMessage.User.DisplayName, chatMessage.SenderUuid, nameColor)
                    : TextHelper.StripRichText(chatMessage.User.DisplayName);

                string replyQuoteText = null;
                string replyQuoteSenderName = null;
                if (!string.IsNullOrEmpty(chatMessage.ReplyToMessageId))
                {
                    UIChatMessage original = null;
                    bool hasOriginal = hostContext.ChatService.TryGetMessage(chatMessage.ReplyToMessageId, out original);
                    if (!string.IsNullOrEmpty(chatMessage.ReplyToSnippet))
                    {
                        replyQuoteText = chatMessage.ReplyToSnippet;
                    }
                    else if (hasOriginal)
                    {
                        string origText = TextHelper.StripRichText(original.Message ?? "");
                        replyQuoteText = origText.Length > 50 ? origText.Substring(0, 50) + "..." : origText;
                    }

                    if (hasOriginal && original.SenderUuid != FrameworkProtocol.SystemSenderUuid)
                    {
                        Color origColor = original.SenderUuid == localUuid
                            ? ChatTheme.SelfName
                            : ChatTheme.GetNameColor(original.SenderUuid);
                        replyQuoteSenderName = ChatTheme.FormatDisplayName(original.User.DisplayName, original.SenderUuid, origColor);
                    }
                }

                float height;
                if (isSystem || isNotice)
                {
                    float textHeight = Text.CalcHeight(messageText, contentWidth - 20f);
                    height = textHeight + MESSAGE_TOP_PADDING + MESSAGE_BOTTOM_PADDING;
                }
                else if (isGrouped)
                {
                    float textWidth = contentWidth - GROUP_INDENT;
                    float textHeight = Text.CalcHeight(messageText, textWidth);
                    height = textHeight + GROUP_TOP_PADDING + GROUP_BOTTOM_PADDING;
                }
                else
                {
                    float textHeight = Text.CalcHeight(messageText, contentWidth);
                    height = nameLineHeight + MESSAGE_TOP_PADDING + textHeight + MESSAGE_BOTTOM_PADDING;
                }

                if (!string.IsNullOrEmpty(replyQuoteText))
                {
                    height += QUOTE_HEIGHT;
                }

                Rect messageRect = new Rect(
                    x: inRect.x,
                    y: currentY,
                    width: contentWidth,
                    height: height);

                displayCache[chatMessage.MessageId] = new CachedMessageDisplay
                {
                    MessageText = messageText,
                    TimestampSize = timestampSize,
                    DisplayNameSize = displayNameSize,
                    ShowNameFormatting = showName,
                    ShowChatFormatting = showChat,
                    Status = chatMessage.Status,
                    ReplyQuoteText = replyQuoteText,
                    ReplyQuoteSenderName = replyQuoteSenderName,
                    IsNotice = isNotice,
                    IsSelf = isSelf,
                    IsSystem = isSystem,
                    IsGrouped = isGrouped,
                    TimestampText = timestampText,
                    SenderDisplayName = displayName,
                    DisplayNameFormatted = displayNameFormatted,
                    HasMentions = hasMentions,
                    NameLineHeight = nameLineHeight,
                };

                try
                {
                    messageRectCache.Add(chatMessage.MessageId, messageRect);
                }
                catch (ArgumentException)
                {
                    hostContext.Log(new LogEventArgs(string.Format("Found existing chat message with key {0} when recalculating messageRectCache. Chat may fail to draw messages with this ID until it's updated again!", chatMessage.MessageId), LogLevel.ERROR));
                }

                currentY += messageRect.height;
            }

            cachedTotalHeight = currentY - inRect.yMin;
        }

        private void drawChatMessage(Rect inRect, UIChatMessage chatMessage)
        {
            if (!displayCache.TryGetValue(chatMessage.MessageId, out CachedMessageDisplay cached)
                || cached.ShowNameFormatting != hostContext.ShowNameFormatting
                || cached.ShowChatFormatting != hostContext.ShowChatFormatting
                || cached.Status != chatMessage.Status)
            {
                drawChatMessageFallback(inRect, chatMessage);
                return;
            }

            bool isHoverTarget = hoveredReplyTargetId == chatMessage.MessageId;
            bool isFlashTarget = flashHighlightId == chatMessage.MessageId;
            if (isHoverTarget || isFlashTarget)
            {
                Color borderColor = ChatTheme.MentionText;
                float alpha = isFlashTarget
                    ? Mathf.Clamp01((flashHighlightUntil - Time.realtimeSinceStartup) / HIGHLIGHT_FLASH_SECONDS)
                    : 0.7f;
                borderColor.a = alpha;
                DrawRectBorder(inRect, borderColor, HIGHLIGHT_BORDER_WIDTH);
            }

            Rect mainRect = inRect;

            if (cached.IsNotice)
            {
                drawNoticeMessage(mainRect, chatMessage, cached);
                return;
            }

            if (cached.IsSystem)
            {
                drawSystemMessage(mainRect, chatMessage, cached);
                return;
            }

            drawNormalMessage(mainRect, chatMessage, cached);
        }

        private void JumpToMessage(string messageId)
        {
            if (!messageRectCache.TryGetValue(messageId, out Rect targetRect)) return;

            float targetScroll = targetRect.y - viewportHeight / 2f + targetRect.height / 2f;
            if (targetScroll < 0f) targetScroll = 0f;
            chatScroll.y = targetScroll;
            stickyScroll = false;

            flashHighlightId = messageId;
            flashHighlightUntil = Time.realtimeSinceStartup + HIGHLIGHT_FLASH_SECONDS;
        }

        private static void DrawRectBorder(Rect rect, Color color, float width)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, width), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, width, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }

        private void drawNoticeMessage(Rect inRect, UIChatMessage chatMessage, CachedMessageDisplay cached)
        {
            if (Mouse.IsOver(inRect))
            {
                Widgets.DrawBoxSolid(inRect, ChatTheme.RowHoverBg);
            }

            Widgets.DrawBoxSolid(new Rect(inRect.x, inRect.y, inRect.width, inRect.height), ChatTheme.NoticeBg);

            Rect accentBar = new Rect(inRect.x, inRect.y, NOTICE_LINE_WIDTH, inRect.height);
            Widgets.DrawBoxSolid(accentBar, ChatTheme.NoticeAccent);

            float textX = inRect.x + NOTICE_LINE_WIDTH + 10f;
            float textWidth = inRect.width - NOTICE_LINE_WIDTH - 20f;
            Rect textRect = new Rect(textX, inRect.y + MESSAGE_TOP_PADDING, textWidth, inRect.height - MESSAGE_TOP_PADDING - MESSAGE_BOTTOM_PADDING);

            string text = cached.MessageText;
            if (cached.Status == UIChatMessageStatus.Pending)
                text = TextHelper.StripRichText(text).Colorize(ChatTheme.PendingMessage);
            else if (cached.Status == UIChatMessageStatus.Denied)
                text = TextHelper.StripRichText(text).Colorize(ChatTheme.DeniedMessage);

            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(textRect, text);
            Text.Anchor = oldAnchor;

            if (Widgets.ButtonInvisible(inRect, false))
            {
                drawMessageContextMenu(chatMessage);
            }
        }

        private void drawSystemMessage(Rect inRect, UIChatMessage chatMessage, CachedMessageDisplay cached)
        {
            if (Mouse.IsOver(inRect))
            {
                Widgets.DrawBoxSolid(inRect, ChatTheme.RowHoverBg);
            }

            float textWidth = inRect.width - 20f;
            Rect textRect = new Rect(inRect.x + 10f, inRect.y + MESSAGE_TOP_PADDING, textWidth, inRect.height - MESSAGE_TOP_PADDING - MESSAGE_BOTTOM_PADDING);

            string text = cached.MessageText;
            if (cached.Status == UIChatMessageStatus.Pending)
                text = TextHelper.StripRichText(text).Colorize(ChatTheme.PendingMessage);
            else if (cached.Status == UIChatMessageStatus.Denied)
                text = TextHelper.StripRichText(text).Colorize(ChatTheme.DeniedMessage);
            else
                text = text.Colorize(ChatTheme.ReplyQuoteText);

            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(textRect, text);
            Text.Anchor = oldAnchor;

            if (Widgets.ButtonInvisible(inRect, false))
            {
                drawMessageContextMenu(chatMessage);
            }
        }

        private void drawNormalMessage(Rect inRect, UIChatMessage chatMessage, CachedMessageDisplay cached)
        {
            bool isSelf = cached.IsSelf;
            bool mentionSelf = cached.HasMentions && chatMessage.MentionedUuids != null
                && chatMessage.MentionedUuids.Contains(hostContext.Uuid);

            if (Mouse.IsOver(inRect))
            {
                Widgets.DrawBoxSolid(inRect, ChatTheme.RowHoverBg);
            }

            if (isSelf)
            {
                Widgets.DrawBoxSolid(inRect, ChatTheme.SelfMessageBg);
            }
            else if (mentionSelf)
            {
                Widgets.DrawBoxSolid(inRect, ChatTheme.MentionSelfBg);
            }

            if (cached.IsGrouped)
            {
                Rect indentLine = new Rect(inRect.x + 6f, inRect.y, 2f, inRect.height);
                Widgets.DrawBoxSolid(indentLine, ChatTheme.GroupIndentLine);

                float cursorY = inRect.y + GROUP_TOP_PADDING;
                float textX = inRect.x + GROUP_INDENT;
                float textWidth = inRect.width - GROUP_INDENT;

                cursorY = DrawReplyQuote(inRect.x, cursorY, textWidth, chatMessage, cached);

                float textHeight = inRect.y + inRect.height - GROUP_BOTTOM_PADDING - cursorY;
                Rect textRect = new Rect(textX, cursorY, textWidth, textHeight);

                string messageText = cached.MessageText;
                if (!cached.ShowChatFormatting || cached.Status != UIChatMessageStatus.Confirmed)
                    messageText = TextHelper.StripRichText(messageText);

                if (cached.HasMentions)
                    messageText = HighlightMentions(messageText);

                if (cached.Status == UIChatMessageStatus.Pending)
                    messageText = TextHelper.StripRichText(messageText).Colorize(ChatTheme.PendingMessage);
                else if (cached.Status == UIChatMessageStatus.Denied)
                    messageText = TextHelper.StripRichText(messageText).Colorize(ChatTheme.DeniedMessage);

                Widgets.Label(textRect, messageText);

                if (Widgets.ButtonInvisible(textRect, false))
                {
                    drawMessageContextMenu(chatMessage);
                }
            }
            else
            {
                float nameY = inRect.y;
                Rect nameRect = new Rect(inRect.x, nameY, inRect.width - cached.TimestampSize.x - TIMESTAMP_RIGHT_MARGIN, cached.NameLineHeight);

                string displayNameText = cached.DisplayNameFormatted;
                if (cached.Status == UIChatMessageStatus.Pending)
                    displayNameText = TextHelper.StripRichText(cached.SenderDisplayName).Colorize(ChatTheme.PendingMessage);
                else if (cached.Status == UIChatMessageStatus.Denied)
                    displayNameText = TextHelper.StripRichText(cached.SenderDisplayName).Colorize(ChatTheme.DeniedMessage);

                Widgets.Label(nameRect, displayNameText);

                if (Widgets.ButtonInvisible(nameRect, true))
                {
                    drawNameContextMenu(chatMessage.User);
                }

                float tsX = inRect.x + inRect.width - cached.TimestampSize.x - TIMESTAMP_RIGHT_MARGIN;
                Rect tsRect = new Rect(tsX, nameY, cached.TimestampSize.x, cached.NameLineHeight);
                Widgets.Label(tsRect, cached.TimestampText.Colorize(ChatTheme.ReplyQuoteText));
                if (Widgets.ButtonInvisible(tsRect, false)) { }

                float cursorY = nameY + cached.NameLineHeight + MESSAGE_TOP_PADDING;
                cursorY = DrawReplyQuote(inRect.x, cursorY, inRect.width, chatMessage, cached);

                float msgHeight = inRect.y + inRect.height - MESSAGE_BOTTOM_PADDING - cursorY;
                Rect msgRect = new Rect(inRect.x, cursorY, inRect.width, msgHeight);

                string messageText = cached.MessageText;
                if (!cached.ShowChatFormatting || cached.Status != UIChatMessageStatus.Confirmed)
                    messageText = TextHelper.StripRichText(messageText);

                if (cached.HasMentions)
                    messageText = HighlightMentions(messageText);

                if (cached.Status == UIChatMessageStatus.Pending)
                    messageText = TextHelper.StripRichText(messageText).Colorize(ChatTheme.PendingMessage);
                else if (cached.Status == UIChatMessageStatus.Denied)
                    messageText = TextHelper.StripRichText(messageText).Colorize(ChatTheme.DeniedMessage);

                Widgets.Label(msgRect, messageText);

                if (Widgets.ButtonInvisible(msgRect, false))
                {
                    drawMessageContextMenu(chatMessage);
                }
            }
        }

        private float DrawReplyQuote(float x, float y, float width, UIChatMessage chatMessage, CachedMessageDisplay cached)
        {
            if (string.IsNullOrEmpty(chatMessage.ReplyToMessageId) || string.IsNullOrEmpty(cached.ReplyQuoteText))
                return y;

            Rect quoteRect = new Rect(x + QUOTE_INDENT, y, width - QUOTE_INDENT, QUOTE_HEIGHT);
            Rect quoteLineRect = new Rect(x, y, QUOTE_LINE_WIDTH, QUOTE_HEIGHT);

            Widgets.DrawBoxSolid(quoteLineRect, ChatTheme.ReplyQuoteBorder);

            string quoteDisplay;
            if (!string.IsNullOrEmpty(cached.ReplyQuoteSenderName))
            {
                quoteDisplay = "↩ " + cached.ReplyQuoteSenderName + ": " + cached.ReplyQuoteText.Colorize(ChatTheme.ReplyQuoteText);
            }
            else
            {
                quoteDisplay = ("↩ " + cached.ReplyQuoteText).Colorize(ChatTheme.ReplyQuoteText);
            }
            Widgets.Label(quoteRect, quoteDisplay);

            if (Mouse.IsOver(quoteRect))
            {
                hoveredReplyTargetId = chatMessage.ReplyToMessageId;
                if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    JumpToMessage(chatMessage.ReplyToMessageId);
                    Event.current.Use();
                }
            }

            return y + QUOTE_HEIGHT;
        }

        private static string HighlightMentions(string messageText)
        {
            if (string.IsNullOrEmpty(messageText)) return messageText;
            return MentionRegex.Replace(messageText, match =>
            {
                return "<color=" + ColorUtility.ToHtmlStringRGB(ChatTheme.MentionText) + ">@" + match.Groups[1].Value + "</color>";
            });
        }

        private void drawChatMessageFallback(Rect inRect, UIChatMessage chatMessage)
        {
            if (Mouse.IsOver(inRect))
            {
                Widgets.DrawBoxSolid(inRect, ChatTheme.RowHoverBg);
            }

            string fallbackTimestamp = string.Format("[{0:HH:mm}] ", chatMessage.Timestamp.ToLocalTime());
            string fallbackName = hostContext.ShowNameFormatting ? chatMessage.User.DisplayName : TextHelper.StripRichText(chatMessage.User.DisplayName);
            string fallbackMsg = hostContext.ShowChatFormatting ? chatMessage.Message : TextHelper.StripRichText(chatMessage.Message);
            string fallbackText = string.Format("{0}{1}: {2}", fallbackTimestamp, fallbackName, fallbackMsg);
            switch (chatMessage.Status)
            {
                case UIChatMessageStatus.Pending:
                    fallbackText = TextHelper.StripRichText(fallbackText).Colorize(ChatTheme.PendingMessage);
                    break;
                case UIChatMessageStatus.Denied:
                    fallbackText = TextHelper.StripRichText(fallbackText).Colorize(ChatTheme.DeniedMessage);
                    break;
            }

            Widgets.Label(inRect, fallbackText);

            float tsWidth = Text.CalcSize(fallbackTimestamp).x;
            Rect fallbackTsRect = new Rect(inRect.x, inRect.y, tsWidth, inRect.height);
            float dnWidth = Text.CalcSize(fallbackName).x;
            Rect fallbackDnRect = new Rect(inRect.x + tsWidth, inRect.y, dnWidth, inRect.height);

            if (Widgets.ButtonInvisible(fallbackTsRect, false)) { }
            else if (Widgets.ButtonInvisible(fallbackDnRect, true))
            {
                drawNameContextMenu(chatMessage.User);
            }
            else if (Widgets.ButtonInvisible(inRect, false))
            {
                drawMessageContextMenu(chatMessage);
            }
        }

        private void drawNameContextMenu(ImmutableUser user)
        {
            List<FloatMenuOption> items = new List<FloatMenuOption>();

            if (user.Uuid != hostContext.Uuid)
            {
                items.Add(new FloatMenuOption("Phinix_chat_contextMenu_tradeWith".Translate(TextHelper.StripRichText(user.DisplayName)), () => hostContext.CreateTrade(user.Uuid)));

                if (hostContext.BlockedUsers.Contains(user.Uuid))
                {
                    items.Add(new FloatMenuOption("Phinix_chat_contextMenu_unblockUser".Translate(), () => hostContext.UnBlockUser(user.Uuid)));
                }
                else
                {
                    items.Add(new FloatMenuOption("Phinix_chat_contextMenu_blockUser".Translate(), () => hostContext.BlockUser(user.Uuid)));
                }
            }

            if (items.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(items));
            }
        }

        private void drawMessageContextMenu(UIChatMessage chatMessage)
        {
            List<FloatMenuOption> items = new List<FloatMenuOption>();

            foreach (string url in parseUrls(chatMessage.Message.StripTags()))
            {
                string ellipsisedUrl = url.Length > 100 ? $"{url.Substring(0, 100)}..." : url;
                items.Add(new FloatMenuOption(string.Format("Phinix_chat_contextMenu_openInBrowser".Translate(), ellipsisedUrl), () => Application.OpenURL(url)));
            }

            items.Add(new FloatMenuOption("Phinix_chat_contextMenu_copyToClipboard".Translate(), () => GUIUtility.systemCopyBuffer = chatMessage.Message));

            items.Add(new FloatMenuOption("Phinix_chat_contextMenu_reply".Translate(), () =>
            {
                hostContext.SetReplyTarget(chatMessage);
            }));

            if (items.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(items));
            }
        }

        private IEnumerable<string> parseUrls(string message)
        {
            if (message == null || message.Length == 0)
            {
                yield break;
            }

            MatchCollection matches = UrlRegex.Matches(message);
            foreach (Match match in matches)
            {
                if (Uri.TryCreate(match.Value, UriKind.Absolute, out Uri matchUri))
                {
                    yield return matchUri.ToString();
                }
            }
        }
    }
}
