using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;

namespace Phinix.ChatExtension.Client
{
    internal sealed class FrameworkClientChatServiceAdapter : IClientChatService
    {
        private readonly IFrameworkChatClientApi chatApi;
        private readonly IClientDisplayMessageFeed messageFeed;
        private readonly IClientDisplayMessageStore messageStore;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientSettingsContext settingsContext;

        public FrameworkClientChatServiceAdapter(
            IFrameworkChatClientApi chatApi,
            IClientDisplayMessageFeed messageFeed,
            IClientDisplayMessageStore messageStore,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext)
        {
            this.chatApi = chatApi;
            this.messageFeed = messageFeed;
            this.messageStore = messageStore;
            this.userDirectory = userDirectory;
            this.settingsContext = settingsContext;
            if (this.messageFeed != null)
            {
                this.messageFeed.DisplayMessageReceived += onDisplayMessageReceived;
            }
        }

        public event System.EventHandler<UIChatMessageEventArgs> OnChatMessageReceived;

        public int UnreadMessages => messageStore.UnreadMessages;

        public UIChatMessage[] GetChatMessages(bool markAsRead = true, bool unreadOnly = false)
        {
            if (unreadOnly)
            {
                return chatApi.BuildUiMessages(messageStore.GetUnreadDisplayMessages(markAsRead), userDirectory);
            }

            if (markAsRead)
            {
                messageStore.MarkAsRead();
            }

            return chatApi.BuildUiMessages(messageStore.GetDisplayMessages(), userDirectory);
        }

        public bool TryGetMessage(string messageId, out UIChatMessage message)
        {
            return chatApi.TryGetUiMessage(messageStore.GetDisplayMessages(), messageId, userDirectory, out message);
        }

        public int CountUnreadExcluding(IEnumerable<string> excludedUuids)
        {
            return chatApi.CountUnreadExcluding(messageStore.GetUnreadDisplayMessages(false), excludedUuids);
        }

        public void MarkAsRead()
        {
            messageStore.MarkAsRead();
        }

        public bool ShouldDisplayChatMessage(UIChatMessage message, IEnumerable<string> blockedUserUuids, bool includeBlockedMessages)
        {
            return chatApi.ShouldDisplayChatMessage(message, blockedUserUuids, includeBlockedMessages);
        }

        public bool ShouldPlayNotification(UIChatMessage message, string localUuid, bool playNoiseOnMessageReceived, bool isInGame, IEnumerable<string> blockedUserUuids)
        {
            return chatApi.ShouldPlayNotification(message, localUuid, playNoiseOnMessageReceived, isInGame, blockedUserUuids);
        }

        private void onDisplayMessageReceived(object sender, FrameworkDisplayMessageEventArgs args)
        {
            if (args?.Message == null)
            {
                return;
            }

            UIChatMessage uiMessage = chatApi.ToUiMessage(args.Message, userDirectory);
            if (uiMessage == null)
            {
                return;
            }

            if (!chatApi.ShouldDisplayChatMessage(uiMessage, settingsContext.BlockedUsers, false))
            {
                return;
            }

            OnChatMessageReceived?.Invoke(sender, new UIChatMessageEventArgs(uiMessage));
        }
    }
}
