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
        private readonly object unreadCacheLock = new object();
        private readonly HashSet<string> cachedBlockedUsers = new HashSet<string>();
        private int messageVersion;
        private int cachedMessageVersion = -1;
        private int cachedRawUnread = -1;
        private int cachedFilteredUnread;

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

        public int UnreadMessages
        {
            get
            {
                if (!settingsContext.Get("chat.showUnreadMessageCount", true))
                {
                    return 0;
                }

                int unread = messageStore.UnreadMessages;
                if (unread == 0 || settingsContext.Get("chat.showBlockedUnreadMessageCount", false))
                {
                    return unread;
                }

                lock (unreadCacheLock)
                {
                    int version = System.Threading.Volatile.Read(ref messageVersion);
                    IEnumerable<string> blocked = settingsContext.BlockedUsers ?? System.Array.Empty<string>();
                    if (version != cachedMessageVersion || unread != cachedRawUnread || !cachedBlockedUsers.SetEquals(blocked))
                    {
                        cachedBlockedUsers.Clear();
                        cachedBlockedUsers.UnionWith(blocked);
                        cachedFilteredUnread = cachedBlockedUsers.Count == 0
                            ? unread
                            : CountUnreadExcluding(cachedBlockedUsers);
                        cachedRawUnread = unread;
                        cachedMessageVersion = version;
                    }
                    return cachedFilteredUnread;
                }
            }
        }

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
            // Even a blocked message can evict an older unread message at capacity.
            System.Threading.Interlocked.Increment(ref messageVersion);
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
