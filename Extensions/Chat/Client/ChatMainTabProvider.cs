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

            UnityEngine.GUI.SetNextControlName("Phinix_chatMessageField");
            message = Widgets.TextField(messageBoxRect, message);
            if (isMessageFieldSubmitEvent(Event.current))
            {
                sendChatMessage();
                Event.current.Use();
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

        private static bool isMessageFieldSubmitEvent(Event currentEvent)
        {
            return currentEvent != null &&
                   currentEvent.type == EventType.KeyDown &&
                   (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter) &&
                   UnityEngine.GUI.GetNameOfFocusedControl() == "Phinix_chatMessageField";
        }
    }
}
