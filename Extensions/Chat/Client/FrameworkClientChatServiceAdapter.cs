using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;

namespace Phinix.ChatExtension.Client
{
    internal sealed class FrameworkClientChatServiceAdapter : IClientChatService, System.IDisposable
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
        private volatile bool started;
        private readonly IClientMainThreadDispatcher dispatcher;
        private readonly System.Action<string, Utils.LogLevel> log;
        private int generation;

        public FrameworkClientChatServiceAdapter(
            IFrameworkChatClientApi chatApi,
            IClientDisplayMessageFeed messageFeed,
            IClientDisplayMessageStore messageStore,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext,
            IClientMainThreadDispatcher dispatcher,
            System.Action<string, Utils.LogLevel> log)
        {
            this.chatApi = chatApi ?? throw new System.ArgumentNullException(nameof(chatApi));
            this.messageFeed = messageFeed ?? throw new System.ArgumentNullException(nameof(messageFeed));
            this.messageStore = messageStore ?? throw new System.ArgumentNullException(nameof(messageStore));
            this.userDirectory = userDirectory ?? throw new System.ArgumentNullException(nameof(userDirectory));
            this.settingsContext = settingsContext ?? throw new System.ArgumentNullException(nameof(settingsContext));
            this.dispatcher = dispatcher ?? throw new System.ArgumentNullException(nameof(dispatcher));
            this.log = log ?? throw new System.ArgumentNullException(nameof(log));
        }

        public void Dispose() { Stop(); }

        public void Start()
        {
            if (started) return;
            started = true;
            messageFeed.DisplayMessageReceived += onDisplayMessageReceived;
        }

        public void Stop()
        {
            if (!started) return;
            started = false;
            System.Threading.Interlocked.Increment(ref generation);
            messageFeed.DisplayMessageReceived -= onDisplayMessageReceived;
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
            if (!started) return;
            int current = System.Threading.Volatile.Read(ref generation);
            dispatcher.Enqueue(() =>
            {
                if (!started || current != System.Threading.Volatile.Read(ref generation)) return;
                try { Deliver(sender, args); }
                catch (System.Exception ex) { Report("Chat message conversion failed: " + ex); }
            });
        }

        private void Report(string message)
        {
            try { log(message, Utils.LogLevel.WARNING); } catch { }
        }

        private void Deliver(object sender, FrameworkDisplayMessageEventArgs args)
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

            var handlers = OnChatMessageReceived;
            if (handlers == null) return;
            var messageArgs = new UIChatMessageEventArgs(uiMessage);
            foreach (System.EventHandler<UIChatMessageEventArgs> handler in handlers.GetInvocationList())
            {
                if (!started) break;
                try { handler(sender, messageArgs); }
                catch (System.Exception ex) { Report("Chat message subscriber failed: " + ex); }
            }
        }
    }
}
