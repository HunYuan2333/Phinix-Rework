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
        private readonly IChatUiHostContext hostContext;

        private bool messagesChanged;
        private Vector2 chatScroll = new Vector2(0, 0);
        private float oldHeight;
        private bool scrollToBottom;
        private bool stickyScroll = true;
        private bool clearMessages;

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
                height: messageRectCache.Values.Sum(r => r.height));

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

            float currentY = inRect.yMin;
            foreach (UIChatMessage chatMessage in filteredMessages)
            {
                string formattedMessage = string.Format(
                    "[{0:HH:mm}] {1}: {2}",
                    chatMessage.Timestamp,
                    hostContext.ShowNameFormatting && chatMessage.Status == UIChatMessageStatus.Confirmed ? chatMessage.User.DisplayName : TextHelper.StripRichText(chatMessage.User.DisplayName),
                    hostContext.ShowChatFormatting && chatMessage.Status == UIChatMessageStatus.Confirmed ? chatMessage.Message : TextHelper.StripRichText(chatMessage.Message));

                Rect messageRect = new Rect(
                    x: inRect.x,
                    y: currentY,
                    width: inRect.width - SCROLLBAR_WIDTH,
                    height: Text.CalcHeight(formattedMessage, inRect.width));

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
        }

        private void drawChatMessage(Rect inRect, UIChatMessage chatMessage)
        {
            string timestamp = string.Format("[{0:HH:mm}] ", chatMessage.Timestamp.ToLocalTime());
            Vector2 timestampSize = Text.CurFontStyle.CalcSize(new GUIContent(timestamp));
            Rect timestampRect = new Rect(inRect.x, inRect.y, timestampSize.x, timestampSize.y);

            string displayName = hostContext.ShowNameFormatting ? chatMessage.User.DisplayName : TextHelper.StripRichText(chatMessage.User.DisplayName);
            Vector2 displayNameSize = Text.CurFontStyle.CalcSize(new GUIContent(displayName));
            Rect displayNameRect = new Rect(inRect.x + timestampRect.width, inRect.y, displayNameSize.x, displayNameSize.y);

            string message = chatMessage.Message;
            if (!hostContext.ShowChatFormatting)
            {
                message = TextHelper.StripRichText(message);
            }

            string formattedText = string.Format("{0}{1}: {2}", timestamp, displayName, message);

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

            if (Widgets.ButtonInvisible(timestampRect, false))
            {
            }
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
