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
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class ChatMessageList : IChatTabContent
    {
        private const float SCROLLBAR_WIDTH = 16f;

        private static readonly Regex UrlRegex = new Regex(@"https?:\/\/\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly Color pendingMessageColour = new Color(1f, 1f, 1f, 0.8f);
        private readonly Color deniedMessageColour = new Color(0.94f, 0.28f, 0.28f);
        private readonly Color backgroundHighlightColour = new Color(1f, 1f, 1f, 0.1f);
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

        /// <summary>
        /// 缓存每条消息的显示文本和计时器尺寸，避免每帧 string.Format / StripRichText / GUIContent 分配。
        /// </summary>
        private struct CachedMessageDisplay
        {
            public string FormattedMessage;
            public Vector2 TimestampSize;
            public Vector2 DisplayNameSize;
            public bool ShowNameFormatting;
            public bool ShowChatFormatting;
            public UIChatMessageStatus Status;
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

            Rect innerContainer = new Rect(
                x: inRect.xMin,
                y: inRect.yMin,
                width: inRect.width - SCROLLBAR_WIDTH,
                height: cachedTotalHeight);

            Vector2 oldChatScroll = new Vector2(chatScroll.x, chatScroll.y);

            Widgets.BeginScrollView(inRect, ref chatScroll, innerContainer);

            foreach (UIChatMessage chatMessage in filteredMessages)
            {
                drawChatMessage(messageRectCache[chatMessage.MessageId], chatMessage);
            }

            Widgets.EndScrollView();

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

            float currentY = inRect.yMin;
            bool showName = hostContext.ShowNameFormatting;
            bool showChat = hostContext.ShowChatFormatting;
            foreach (UIChatMessage chatMessage in filteredMessages)
            {
                string displayName = showName && chatMessage.Status == UIChatMessageStatus.Confirmed
                    ? chatMessage.User.DisplayName
                    : TextHelper.StripRichText(chatMessage.User.DisplayName);
                string messageText = showChat && chatMessage.Status == UIChatMessageStatus.Confirmed
                    ? chatMessage.Message
                    : TextHelper.StripRichText(chatMessage.Message);
                string formattedMessage = string.Format(
                    "[{0:HH:mm}] {1}: {2}",
                    chatMessage.Timestamp,
                    displayName,
                    messageText);

                Rect messageRect = new Rect(
                    x: inRect.x,
                    y: currentY,
                    width: inRect.width - SCROLLBAR_WIDTH,
                    height: Text.CalcHeight(formattedMessage, inRect.width));

                // Cache display data so drawChatMessage doesn't recompute per frame
                GUIContent tsContent = new GUIContent(string.Format("[{0:HH:mm}] ", chatMessage.Timestamp.ToLocalTime()));
                GUIContent dnContent = new GUIContent(displayName);
                displayCache[chatMessage.MessageId] = new CachedMessageDisplay
                {
                    FormattedMessage = formattedMessage,
                    TimestampSize = Text.CurFontStyle.CalcSize(tsContent),
                    DisplayNameSize = Text.CurFontStyle.CalcSize(dnContent),
                    ShowNameFormatting = showName,
                    ShowChatFormatting = showChat,
                    Status = chatMessage.Status,
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
            // 从缓存读取预计算的格式化文本；若缓存缺失回退到实时计算（兼容手动 Clear 后未重建缓存的场景）
            if (!displayCache.TryGetValue(chatMessage.MessageId, out CachedMessageDisplay cached)
                || cached.ShowNameFormatting != hostContext.ShowNameFormatting
                || cached.ShowChatFormatting != hostContext.ShowChatFormatting
                || cached.Status != chatMessage.Status)
            {
                // 缓存失效：实时计算（罕见路径）
                string fallbackTimestamp = string.Format("[{0:HH:mm}] ", chatMessage.Timestamp.ToLocalTime());
                string fallbackName = hostContext.ShowNameFormatting ? chatMessage.User.DisplayName : TextHelper.StripRichText(chatMessage.User.DisplayName);
                string fallbackMsg = hostContext.ShowChatFormatting ? chatMessage.Message : TextHelper.StripRichText(chatMessage.Message);
                string fallbackText = string.Format("{0}{1}: {2}", fallbackTimestamp, fallbackName, fallbackMsg);
                switch (chatMessage.Status)
                {
                    case UIChatMessageStatus.Pending:
                        fallbackText = TextHelper.StripRichText(fallbackText).Colorize(pendingMessageColour);
                        break;
                    case UIChatMessageStatus.Denied:
                        fallbackText = TextHelper.StripRichText(fallbackText).Colorize(deniedMessageColour);
                        break;
                }

                if (Mouse.IsOver(inRect))
                {
                    Widgets.DrawHighlight(inRect);
                }

                Widgets.Label(inRect, fallbackText);

                GUIContent fbTsContent = new GUIContent(fallbackName);
                float tsWidth = Text.CurFontStyle.CalcSize(fbTsContent).x;
                Rect fallbackTsRect = new Rect(inRect.x, inRect.y, tsWidth, inRect.height);
                float dnWidth = tsWidth;
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

                return;
            }

            // 正常路径：使用缓存数据，零分配（除 Rect 栈分配）
            string formattedText = cached.FormattedMessage;
            Vector2 timestampSize = cached.TimestampSize;
            Rect timestampRect = new Rect(inRect.x, inRect.y, timestampSize.x, inRect.height);
            Rect displayNameRect = new Rect(inRect.x + timestampSize.x, inRect.y, cached.DisplayNameSize.x, inRect.height);

            switch (chatMessage.Status)
            {
                case UIChatMessageStatus.Pending:
                    formattedText = TextHelper.StripRichText(formattedText).Colorize(pendingMessageColour);
                    break;
                case UIChatMessageStatus.Denied:
                    formattedText = TextHelper.StripRichText(formattedText).Colorize(deniedMessageColour);
                    break;
            }

            if (Mouse.IsOver(inRect))
            {
                Widgets.DrawHighlight(inRect);
            }

            Widgets.Label(inRect, formattedText);

            if (Widgets.ButtonInvisible(timestampRect, false)) { }
            else if (Widgets.ButtonInvisible(displayNameRect, true))
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
