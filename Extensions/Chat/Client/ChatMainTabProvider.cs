using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    public class ChatMainTabProvider : IMainTabProvider, IBadgeProvider
    {
        private const float CHAT_TEXTBOX_HEIGHT = 30f;
        private const float CHAT_SEND_BUTTON_WIDTH = 80f;
        private const float DEFAULT_SPACING = 10f;

        private readonly IChatUiHostContext hostContext;
        private readonly IChatTabContent chatMessageList;
        private readonly Action<string> sendMessage;

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

        public ChatMainTabProvider(IChatUiHostContext hostContext, IChatTabContent chatMessageList, Action<string> sendMessage)
        {
            this.hostContext = hostContext;
            this.chatMessageList = chatMessageList;
            this.sendMessage = sendMessage;
        }

        public void Draw(Rect inRect)
        {
            Rect sendButtonRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).RightPartPixels(CHAT_SEND_BUTTON_WIDTH);
            Rect messageBoxRect = inRect.BottomPartPixels(CHAT_TEXTBOX_HEIGHT).LeftPartPixels(inRect.width - (CHAT_SEND_BUTTON_WIDTH + DEFAULT_SPACING));
            Rect chatRect = inRect.TopPartPixels(inRect.height - (messageBoxRect.height + DEFAULT_SPACING));

            chatMessageList.Draw(chatRect);

            // Save Enter before TextField consumes it.
            bool enterPressed = Event.current != null &&
                Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            message = Widgets.TextField(messageBoxRect, message);

            if (enterPressed && !string.IsNullOrEmpty(message))
            {
                sendChatMessage();
                // Use() changes type to Used but leaves keyCode intact.
                // RimWorld may check keyCode directly; wipe it to prevent
                // the window shortcut from picking up Enter.
                Event.current.Use();
                Event.current.keyCode = KeyCode.None;
            }

            if (Widgets.ButtonText(sendButtonRect, "Phinix_chat_sendButton".Translate()))
            {
                sendChatMessage();
            }
        }

        private void sendChatMessage()
        {
            if (!string.IsNullOrEmpty(message))
            {
                sendMessage(message);
                chatMessageList.ScrollToBottom();
                message = "";
            }
        }
    }
}
