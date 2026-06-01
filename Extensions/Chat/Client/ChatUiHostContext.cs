using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using Utils;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatUiHostContext : IChatUiHostContext
    {
        private sealed class ReadOnlyBlockedUserSet : ISet<string>
        {
            private readonly ChatUiHostContext owner;

            public ReadOnlyBlockedUserSet(ChatUiHostContext owner)
            {
                this.owner = owner;
            }

            public int Count
            {
                get
                {
                    lock (owner.blockedUsersLock)
                    {
                        return owner.blockedUsers.Count;
                    }
                }
            }

            public bool IsReadOnly => true;

            public bool Contains(string item)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.Contains(item);
                }
            }

            public IEnumerator<string> GetEnumerator()
            {
                lock (owner.blockedUsersLock)
                {
                    return new List<string>(owner.blockedUsers).GetEnumerator();
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

            public void CopyTo(string[] array, int arrayIndex)
            {
                lock (owner.blockedUsersLock)
                {
                    owner.blockedUsers.CopyTo(array, arrayIndex);
                }
            }

            public bool Add(string item) => throw new NotSupportedException();
            void ICollection<string>.Add(string item) => throw new NotSupportedException();
            public bool Remove(string item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();

            public void ExceptWith(IEnumerable<string> other) => throw new NotSupportedException();
            public void IntersectWith(IEnumerable<string> other) => throw new NotSupportedException();
            public void SymmetricExceptWith(IEnumerable<string> other) => throw new NotSupportedException();
            public void UnionWith(IEnumerable<string> other) => throw new NotSupportedException();

            public bool IsProperSubsetOf(IEnumerable<string> other)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.IsProperSubsetOf(other);
                }
            }

            public bool IsProperSupersetOf(IEnumerable<string> other)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.IsProperSupersetOf(other);
                }
            }

            public bool IsSubsetOf(IEnumerable<string> other)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.IsSubsetOf(other);
                }
            }

            public bool IsSupersetOf(IEnumerable<string> other)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.IsSupersetOf(other);
                }
            }

            public bool Overlaps(IEnumerable<string> other)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.Overlaps(other);
                }
            }

            public bool SetEquals(IEnumerable<string> other)
            {
                lock (owner.blockedUsersLock)
                {
                    return owner.blockedUsers.SetEquals(other);
                }
            }
        }

        private readonly IClientChatService chatService;
        private readonly IClientSessionContext session;
        private readonly IClientSettingsContext settings;
        private readonly IClientUserEventStream userEvents;
        private readonly HashSet<string> blockedUsers = new HashSet<string>();
        private readonly object blockedUsersLock = new object();
        private readonly ISet<string> blockedUsersView;
        private readonly Action<string> createTrade;
        private readonly Action<LogEventArgs> log;

        public ChatUiHostContext(
            IClientChatService chatService,
            IClientSessionContext session,
            IClientSettingsContext settings,
            IClientUserEventStream userEvents,
            Action<string> createTrade,
            Action<LogEventArgs> log)
        {
            this.chatService = chatService;
            this.session = session;
            this.settings = settings;
            this.userEvents = userEvents;
            this.createTrade = createTrade;
            this.log = log;
            this.blockedUsersView = new ReadOnlyBlockedUserSet(this);
            refreshBlockedUsers();
            this.userEvents.BlockedUsersChanged += (_, __) => refreshBlockedUsers();
        }

        public IClientChatService ChatService => chatService;

        public string Uuid => session.Uuid;

        public int ChatMessageLimit => settings.Get<int>("chat.messageLimit", 100);

        public bool ShowNameFormatting => settings.Get<bool>("chat.showNameFormatting", true);

        public bool ShowChatFormatting => settings.Get<bool>("chat.showChatFormatting", true);

        public ISet<string> BlockedUsers => blockedUsersView;

        public event EventHandler OnDisconnect
        {
            add => userEvents.Disconnected += value;
            remove => userEvents.Disconnected -= value;
        }

        public event EventHandler OnUsersChanged
        {
            add => userEvents.UsersChanged += value;
            remove => userEvents.UsersChanged -= value;
        }

        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged
        {
            add => userEvents.UserDisplayNameChanged += value;
            remove => userEvents.UserDisplayNameChanged -= value;
        }

        public event EventHandler<UserBlockStateChangedEventArgs> OnBlockedUsersChanged
        {
            add => userEvents.BlockedUsersChanged += value;
            remove => userEvents.BlockedUsersChanged -= value;
        }

        public void CreateTrade(string uuid) => createTrade?.Invoke(uuid);

        public void BlockUser(string uuid)
        {
            settings.BlockUser(uuid);
            refreshBlockedUsers();
        }

        public void UnBlockUser(string uuid)
        {
            settings.UnBlockUser(uuid);
            refreshBlockedUsers();
        }

        public void Log(LogEventArgs args) => log?.Invoke(args);

        private void refreshBlockedUsers()
        {
            lock (blockedUsersLock)
            {
                blockedUsers.Clear();
                foreach (string uuid in settings.BlockedUsers ?? Array.Empty<string>())
                {
                    blockedUsers.Add(uuid);
                }
            }
        }
    }
}
