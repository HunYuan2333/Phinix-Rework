using System;
using System.Collections.Generic;
using Phinix.ChatExtension.Client;
using PhinixClient;
using PhinixClient.Framework;
using UserManagement;
using Utils;
using Utils.Framework;

internal static partial class Program
{
    private static void ProbeChatServices()
    {
        var host = new ChatHost();
        var api = new ChatApi();
        using (var factory = new ClientCompositionFactory(() => true, _ => { }))
        {
            var scope = factory.CreateScope(local =>
            {
                local.Borrow<IFrameworkChatClientApi>(api);
                local.Borrow<IClientDisplayMessageFeed>(host);
                local.Borrow<IClientMainThreadDispatcher>(host);
                local.Borrow<Action<string, LogLevel>>((_, __) => host.Warnings++);
                local.Borrow<IClientDisplayMessageStore>(host);
                local.Borrow<IClientUserDirectory>(host);
                local.Borrow<IClientSessionContext>(host);
                local.Borrow<IClientSettingsContext>(host);
                local.Borrow<IClientUserEventStream>(host);
                local.Borrow<IFrameworkClientTransport>(host);
                local.Borrow<Action<string>>(_ => { });
                local.Borrow<Action<LogEventArgs>>(_ => { });
                local.Register<IClientChatService, FrameworkClientChatServiceAdapter>();
                local.Register<IChatUiHostContext, ChatUiHostContext>();
            });
            var ui = scope.Resolve<ChatUiHostContext>();
            var adapter = scope.Resolve<FrameworkClientChatServiceAdapter>();
            Assert(host.SubscriptionCount == 0, "Actual Chat constructors must not subscribe to host events.");
            int messages = 0, disconnects = 0;
            adapter.OnChatMessageReceived += (_, __) => messages++;
            ui.OnDisconnect += (_, __) => disconnects++;
            adapter.Start(); adapter.Start(); ui.Start(); ui.Start();
            Assert(host.SubscriptionCount == 5, "Actual Chat Start must subscribe once to feed and user events.");
            host.Publish();
            Assert(messages == 0, "Chat conversion waits for the main-thread dispatcher.");
            host.Drain(); host.Disconnect();
            Assert(messages == 1 && disconnects == 1, "Actual Chat services forward events once.");
            ui.SendChatMessage("hello", null);
            Assert(host.Sends == 1, "Actual Chat constructor-injected transport sends via existing capability behavior.");
            EventHandler<UIChatMessageEventArgs> broken = (_, __) => { throw new Exception("subscriber"); };
            adapter.OnChatMessageReceived += broken;
            int later = 0;
            adapter.OnChatMessageReceived += (_, __) => later++;
            host.Publish(); host.Drain();
            Assert(messages == 2 && later == 1 && host.Warnings == 1, "A broken Chat subscriber cannot hide messages from later subscribers.");
            host.Publish(); // Queued before shutdown; must be discarded after disposal.
            var staleFeed = host.Feed;
            var staleDisconnect = host.DisconnectSnapshot;
            scope.Dispose(); scope.Dispose();
            Assert(host.SubscriptionCount == 0 && !host.Disposed, "Actual Chat disposal unsubscribes all events and keeps borrowed services alive.");
            staleFeed(host, new FrameworkDisplayMessageEventArgs(new FrameworkDisplayMessage()));
            staleDisconnect(host, EventArgs.Empty);
            host.Drain();
            ui.SendChatMessage("stale", null);
            Assert(messages == 2 && disconnects == 1 && host.Sends == 1,
                "Captured old Chat callbacks and sends are ignored after shutdown.");
        }
        var partialHost = new ChatHost { FailUserSubscription = true };
        var partial = new ChatUiHostContext(new FrameworkClientChatServiceAdapter(api, partialHost, partialHost, partialHost, partialHost, partialHost, (_, __) => { }),
            partialHost, partialHost, partialHost, _ => { }, _ => { }, api, partialHost, partialHost);
        ExpectFailure(partial.Start, "Actual Chat activation failure must remain observable.");
        partial.Dispose();
        Assert(partialHost.SubscriptionCount == 0, "Actual Chat partial Start cleanup removes earlier subscriptions.");
    }

    private sealed class ChatHost : IClientDisplayMessageFeed, IClientDisplayMessageStore,
        IClientUserDirectory, IClientSessionContext, IClientSettingsContext, IClientUserEventStream,
        IFrameworkClientTransport, IClientMainThreadDispatcher, IDisposable
    {
        internal EventHandler<FrameworkDisplayMessageEventArgs> Feed;
        public event EventHandler<FrameworkDisplayMessageEventArgs> DisplayMessageReceived
        { add { Feed += value; } remove { Feed -= value; } }
        public event EventHandler Disconnected;
        internal EventHandler DisconnectSnapshot => Disconnected;
        private EventHandler users;
        public event EventHandler UsersChanged
        { add { if (FailUserSubscription) throw new Exception("subscription"); users += value; } remove { users -= value; } }
        public event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;
        public event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
        public event Action<string, object> OnSettingChanged { add { } remove { } }
        internal bool Disposed, FailUserSubscription;
        internal int Sends, Warnings;
        private readonly Queue<Action> pending = new Queue<Action>();
        public void Enqueue(Action action) { pending.Enqueue(action); }
        internal void Drain() { while (pending.Count != 0) pending.Dequeue()(); }
        internal int SubscriptionCount => Count(Feed) + Count(Disconnected) + Count(users)
            + Count(UserDisplayNameChanged) + Count(BlockedUsersChanged);
        private static int Count(Delegate handler) => handler == null ? 0 : handler.GetInvocationList().Length;
        internal void Publish() { Feed?.Invoke(this, new FrameworkDisplayMessageEventArgs(new FrameworkDisplayMessage())); }
        internal void Disconnect() { Disconnected?.Invoke(this, EventArgs.Empty); }
        public bool Authenticated => true;
        public bool LoggedIn => true;
        public string SessionId => "test";
        public string Uuid => "test-user";
        public int UnreadMessages => 0;
        public void MarkAsRead() { }
        public FrameworkDisplayMessage[] GetUnreadDisplayMessages(bool markAsRead = true) => new FrameworkDisplayMessage[0];
        public FrameworkDisplayMessage[] GetDisplayMessages() => new FrameworkDisplayMessage[0];
        public ImmutableUser[] GetUsers(bool loggedIn = false) => new ImmutableUser[0];
        public bool TryGetUser(string uuid, out ImmutableUser user) { user = default(ImmutableUser); return false; }
        public T Get<T>(string key, T defaultValue = default(T)) => defaultValue;
        public void Set<T>(string key, T value) { }
        public IEnumerable<string> BlockedUsers => new string[0];
        public bool CollapseBlockedUsers { get; set; }
        public void BlockUser(string uuid) { }
        public void UnBlockUser(string uuid) { }
        public bool HasRemoteCapability(string capability) => true;
        public void SendFrameworkPacket(FrameworkPacket packet) { Sends++; }
        public bool TryHandleOutgoingMessage(string rawMessage) => false;
        public bool TryHandleOutgoingItem(FrameworkItemPayload payload) => false;
        public void Dispose() { Disposed = true; }
    }

    private sealed class ChatApi : IFrameworkChatClientApi
    {
        public FrameworkPacket CreateOutgoingMessage(string text, ClientFrameworkContext context) => new FrameworkPacket();
        public FrameworkPacket CreateOutgoingMessage(string text, ClientFrameworkContext context, IEnumerable<string> mentions, string replyId, string snippet) => new FrameworkPacket();
        public FrameworkDisplayMessage RenderMessage(FrameworkPacket message) => new FrameworkDisplayMessage();
        public FrameworkPacket CreateHistoryRequestPacket(string sessionId, string senderUuid) => new FrameworkPacket();
        public UIChatMessage[] BuildUiMessages(IEnumerable<FrameworkDisplayMessage> messages, IClientUserDirectory users) => new UIChatMessage[0];
        public bool TryGetUiMessage(IEnumerable<FrameworkDisplayMessage> messages, string id, IClientUserDirectory users, out UIChatMessage message) { message = null; return false; }
        public int CountUnreadExcluding(IEnumerable<FrameworkDisplayMessage> messages, IEnumerable<string> users) => 0;
        public bool ShouldDisplayChatMessage(UIChatMessage message, IEnumerable<string> blocked, bool include) => true;
        public bool ShouldPlayNotification(UIChatMessage message, string uuid, bool noise, bool inGame, IEnumerable<string> blocked) => false;
        public UIChatMessage ToUiMessage(FrameworkDisplayMessage message, IClientUserDirectory users) => new UIChatMessage("id", "sender", "text", DateTime.UtcNow, UIChatMessageStatus.Confirmed, default(ImmutableUser));
    }
}
