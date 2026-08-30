using System;
using System.Collections.Generic;
using Phinix.ChatExtension;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using Utils;
using Utils.Framework;

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
        private readonly IFrameworkChatClientApi chatApi;
        private readonly IFrameworkClientTransport transport;
        private readonly IClientUserDirectory userDirectory;

        public UIChatMessage ReplyTarget { get; private set; }
        public event EventHandler ReplyTargetChanged;

        public ChatUiHostContext(
            IClientChatService chatService,
            IClientSessionContext session,
            IClientSettingsContext settings,
            IClientUserEventStream userEvents,
            Action<string> createTrade,
            Action<LogEventArgs> log,
            IFrameworkChatClientApi chatApi = null,
            IFrameworkClientTransport transport = null,
            IClientUserDirectory userDirectory = null)
        {
            this.chatService = chatService;
            this.session = session;
            this.settings = settings;
            this.userEvents = userEvents;
            this.createTrade = createTrade;
            this.log = log;
            this.chatApi = chatApi;
            this.transport = transport;
            this.userDirectory = userDirectory;
            this.blockedUsersView = new ReadOnlyBlockedUserSet(this);
            refreshBlockedUsers();
            this.userEvents.BlockedUsersChanged += (_, __) => refreshBlockedUsers();
        }

        public IClientChatService ChatService => chatService;

        public string Uuid => session.Uuid;

        public bool IsOnline => session.Authenticated && session.LoggedIn;

        public int ChatMessageLimit => settings.Get<int>("chat.messageLimit", 100);

        public bool ShowNameFormatting => settings.Get<bool>("chat.showNameFormatting", true);

        public bool ShowChatFormatting => settings.Get<bool>("chat.showChatFormatting", true);
        public bool ShowImages => settings.Get<bool>("chat.images.enabled", true);

        public float MaxImageHeight
        {
            get
            {
                float maxHeight = settings.Get<float>("chat.images.maxHeight", 240f);
                return maxHeight < 48f ? 48f : (maxHeight > 512f ? 512f : maxHeight);
            }
        }

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

        public void SetReplyTarget(UIChatMessage message)
        {
            ReplyTarget = message;
            ReplyTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearReplyTarget()
        {
            ReplyTarget = null;
            ReplyTargetChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SendChatMessage(string text, IEnumerable<string> mentionedUuids)
        {
            if (chatApi == null || transport == null) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            // 设计哲学 §3.7：优先走 handler 管线。
            // - Legacy 模式：通过 TryHandleOutgoingMessage → LegacyAdapter 翻译协议
            // - FrameworkV2 模式：因为 ChatMessageHandler 当前不支持 mentionedUuids/reply
            //   参数，暂时走直接路径。TODO: 扩展 IClientMessageHandler.HandleOutgoingText
            //   使其支持附加元数据后，统一走管线。
            if (!transport.HasRemoteCapability(FrameworkChatProtocol.MessageType))
            {
                log?.Invoke(new LogEventArgs(
                    "[ChatUiHostContext] Server lacks framework chat capability, routing through handler pipeline (Legacy mode).",
                    LogLevel.DEBUG));
                if (transport.TryHandleOutgoingMessage(text))
                {
                    ClearReplyTarget();
                    return;
                }
                log?.Invoke(new LogEventArgs(
                    "[ChatUiHostContext] TryHandleOutgoingMessage returned false — falling through to direct FrameworkPacket send.",
                    LogLevel.WARNING));
            }

            string replyToId = ReplyTarget?.MessageId;
            string replyToSnippet = null;
            if (ReplyTarget != null)
            {
                string snippet = TextHelper.StripRichText(ReplyTarget.Message ?? string.Empty);
                replyToSnippet = snippet.Length > 50 ? snippet.Substring(0, 50) + "..." : snippet;
            }

            FrameworkPacket packet = chatApi.CreateOutgoingMessage(text, new ClientFrameworkContext
            {
                SessionId = session.SessionId,
                SenderUuid = session.Uuid,
                CompatibilityMode = FrameworkCompatibilityMode.FrameworkV2
            }, mentionedUuids, replyToId, replyToSnippet);

            transport.SendFrameworkPacket(packet);
        }

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
