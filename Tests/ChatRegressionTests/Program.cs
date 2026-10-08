using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Phinix.ChatExtension;
using Phinix.ChatExtension.Client;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using UserManagement;
using UnityEngine;

internal static class Program
{
    private static readonly Type MentionUtility = typeof(PhinixFrameworkChatService).Assembly
        .GetType("Phinix.ChatExtension.Client.ChatMentionUtility", true);

    private static int Main()
    {
        try
        {
            AssertCompatibilitySubscriberIsolation();
            AssertRegistrationRequiresPreparedHost();
            AssertReplayDoesNotRepeatNotifications();
            AssertMessageIdentityIsSourceScoped();
            AssertEvictionKeepsReadCursorAndAllowsReplay();
            AssertReplyLookupToleratesDuplicates();
            AssertMissingReplyIsSafe();
            AssertHighlightPreservesMarkup();
            AssertAutocompleteRequiresNewOwnedInput();
            AssertCompletionPreservesMessagePrefix();
            ChatImageDownloadScenarios.Run();
            Console.WriteLine("All 17 chat regression scenarios passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void AssertCompatibilitySubscriberIsolation()
    {
        var client = CreateMessageStore();
        int legacyReceivers = 0;
        client.CompatibilityModeChanged += (_, __) => { throw new Exception("broken plugin"); };
        client.CompatibilityModeChanged += (_, args) =>
        {
            if (args.CompatibilityMode == FrameworkCompatibilityMode.Legacy) legacyReceivers++;
        };
        // Even a failing diagnostic observer must not interrupt protocol activation.
        client.OnLogEntry += (_, __) => { throw new Exception("broken logger"); };
        var change = typeof(PhinixFrameworkClient).GetMethod("setCompatibilityMode", BindingFlags.NonPublic | BindingFlags.Instance);
        change.Invoke(client, new object[] { FrameworkCompatibilityMode.Legacy, null, null, new string[0] });
        change.Invoke(client, new object[] { FrameworkCompatibilityMode.Legacy, null, null, new string[0] });
        Assert(client.CompatibilityMode == FrameworkCompatibilityMode.Legacy && legacyReceivers == 1,
            "A failing compatibility subscriber cannot block later protocol registration or repeat an unchanged mode.");
    }

    private static void AssertRegistrationRequiresPreparedHost()
    {
        var discovered = new DiscoveredPhinixExtensions();
        var apiRegistry = new ExtensionApiRegistry();
        Type builderType = typeof(PhinixExtensionRegistry).GetNestedType("ExtensionBuilder", BindingFlags.NonPublic);
        var builder = (IExtensionBuilder)Activator.CreateInstance(
            builderType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { "builtin.chat", new ExtensionHostContext { HostKind = "client-test" }, discovered, apiRegistry, null },
            null);

        var module = new BuiltInChatClientExtension();
        bool missing = false;
        try { module.Compose(builder); } catch (InvalidOperationException) { missing = true; }
        Assert(missing && apiRegistry.ResolveAll<IMainTabProvider>().Count == 0,
            "Chat must reject an unprepared host before publishing APIs.");
        using (var composition = new ClientCompositionFactory(() => true, error => { throw error; }))
        {
            builder.HostContext.AddService<IClientCompositionFactory>(composition);
            var host = new RegistrationHost();
            builder.HostContext.AddService<IClientDisplayMessageFeed>(host);
            builder.HostContext.AddService<IClientDisplayMessageStore>(host);
            builder.HostContext.AddService<IClientSessionContext>(host);
            builder.HostContext.AddService<IClientSettingsContext>(host);
            builder.HostContext.AddService<IClientUserDirectory>(host);
            builder.HostContext.AddService<IClientUserEventStream>(host);
            builder.HostContext.AddService<IClientSettingsWindowService>(host);
            builder.HostContext.AddService<IClientMainThreadDispatcher>(host);
            builder.HostContext.AddService<IFrameworkClientTransport>(host);
            builder.HostContext.AddService<IUiTheme>(host);
            module.Compose(builder);
            Assert(host.SubscriptionCount == 0,
                "The actual complete Chat graph must construct without starting host subscriptions.");
            Assert(apiRegistry.ResolveAll<IMainTabProvider>().Count == 1,
                "Chat registration must expose its main tab before Activate.");
            Assert(discovered.ClientMessageHandlers.Count == 1 && discovered.MessageRenderers.Count == 1,
                "Chat registration must expose its handlers before Activate.");
            module.Shutdown(builder.HostContext);
            module.Shutdown(builder.HostContext);
            Assert(host.SubscriptionCount == 0, "Stopping an unactivated complete Chat graph must be safe.");
        }
    }

    private sealed class RegistrationHost : IClientDisplayMessageFeed, IClientDisplayMessageStore,
        IClientSessionContext, IClientSettingsContext, IClientUserDirectory, IClientUserEventStream,
        IClientSettingsWindowService, IClientMainThreadDispatcher, IFrameworkClientTransport, IUiTheme
    {
        public event EventHandler<FrameworkDisplayMessageEventArgs> DisplayMessageReceived;
        public event EventHandler Disconnected;
        public event EventHandler UsersChanged;
        public event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;
        public event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
        public event Action<string, object> OnSettingChanged { add { } remove { } }
        internal int SubscriptionCount => Count(DisplayMessageReceived) + Count(Disconnected) + Count(UsersChanged)
            + Count(UserDisplayNameChanged) + Count(BlockedUsersChanged);
        private static int Count(Delegate handler) => handler == null ? 0 : handler.GetInvocationList().Length;
        public bool Authenticated => false;
        public bool LoggedIn => false;
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
        public bool HasRemoteCapability(string capability) => false;
        public void SendFrameworkPacket(FrameworkPacket packet) { throw new Exception("Unexpected registration send"); }
        public bool TryHandleOutgoingMessage(string rawMessage) => false;
        public bool TryHandleOutgoingItem(FrameworkItemPayload payload) => false;
        public void OpenSettingsWindow() { throw new Exception("Unexpected registration window"); }
        public void Enqueue(Action action) { throw new Exception("Unexpected registration dispatch"); }
        public Color PrimaryText => default(Color);
        public Color SecondaryText => default(Color);
        public Color Background => default(Color);
        public Color Surface => default(Color);
        public Color Separator => default(Color);
        public Color HoverHighlight => default(Color);
        public Color Pending => default(Color);
        public Color Error => default(Color);
        public Color Success => default(Color);
        public Color Warning => default(Color);
        public void RegisterColor(string key, Color color) { throw new Exception("Unexpected registration theme"); }
        public Color GetColor(string key) => default(Color);
        public bool TryGetColor(string key, out Color color) { color = default(Color); return false; }
        public void RegisterFloat(string key, float value) { throw new Exception("Unexpected registration theme"); }
        public float GetFloat(string key, float value = 0f) => value;
        public void Reload() { throw new Exception("Unexpected registration theme reload"); }
    }

    private static void AssertReplayDoesNotRepeatNotifications()
    {
        var client = CreateMessageStore();
        int notifications = 0;
        client.OnDisplayMessageReceived += (_, __) => notifications++;
        var original = Message("original");
        original.MentionedUuids.Add("local-user");
        ((IDisplayMessageSink)client).Enqueue(original);
        client.MarkAsRead();
        ((IDisplayMessageSink)client).Enqueue(Message("original"));
        Assert(client.GetDisplayMessages().Length == 1, "History replay must not duplicate the row.");
        Assert(notifications == 1 && client.UnreadMessages == 0, "Replay must not repeat mention notifications or unread counts.");
        Assert(ReferenceEquals(client.GetDisplayMessages()[0], original), "Replay must retain the first delivered message and its metadata.");
        ((IDisplayMessageSink)client).Enqueue(Message("reply"));
        Assert(notifications == 2 && client.UnreadMessages == 1, "New messages must still notify normally.");
    }

    private static void AssertMessageIdentityIsSourceScoped()
    {
        var client = CreateMessageStore();
        ((IDisplayMessageSink)client).Enqueue(Message("shared", "first-extension"));
        ((IDisplayMessageSink)client).Enqueue(Message("shared", "second-extension"));
        ((IDisplayMessageSink)client).Enqueue(Message(null));
        ((IDisplayMessageSink)client).Enqueue(Message(null));
        ((IDisplayMessageSink)client).Enqueue(Message(""));
        ((IDisplayMessageSink)client).Enqueue(Message(""));
        Assert(client.GetDisplayMessages().Length == 6, "Other sources and unidentified messages must not be suppressed.");
    }

    private static void AssertEvictionKeepsReadCursorAndAllowsReplay()
    {
        var client = CreateMessageStore();
        int capacity = (int)typeof(PhinixFrameworkClient).GetField("MaxDisplayMessages", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
        for (int i = 0; i < capacity; i++) ((IDisplayMessageSink)client).Enqueue(Message(i.ToString()));
        client.MarkAsRead();
        ((IDisplayMessageSink)client).Enqueue(Message((capacity - 1).ToString()));
        Assert(client.GetDisplayMessages()[0].MessageId == "0" && client.UnreadMessages == 0, "A duplicate must not evict messages at capacity.");
        ((IDisplayMessageSink)client).Enqueue(Message("new"));
        Assert(client.GetDisplayMessages().Length == capacity && client.GetUnreadDisplayMessages(false).Single().MessageId == "new",
            "Eviction must keep the unread cursor aligned.");
        ((IDisplayMessageSink)client).Enqueue(Message("0"));
        Assert(client.GetDisplayMessages().Last().MessageId == "0", "An evicted identity must not remain suppressed forever.");
    }

    private static void AssertReplyLookupToleratesDuplicates()
    {
        var service = new PhinixFrameworkChatService((_, __) => { });
        var original = Message("original");
        original.Text = "first delivery";
        original.ReplyToMessageId = "earlier";
        original.ReplyToSnippet = "quoted text";
        original.MentionedUuids.Add("local-user");
        UIChatMessage result;
        Assert(service.TryGetUiMessage(new[] { null, original, Message("original") }, "original", null, out result),
            "Reply lookup must tolerate replayed IDs and empty entries from alternate stores.");
        Assert(result.Message == "first delivery" && result.ReplyToMessageId == "earlier" &&
            result.ReplyToSnippet == "quoted text" && result.MentionedUuids.Contains("local-user"),
            "Reply lookup must preserve the selected message's content and metadata.");
    }

    private static void AssertMissingReplyIsSafe()
    {
        var service = new PhinixFrameworkChatService((_, __) => { });
        UIChatMessage result;
        Assert(!service.TryGetUiMessage(null, "missing", null, out result) && result == null, "Missing history must return false.");
        Assert(!service.TryGetUiMessage(new[] { Message("other") }, "missing", null, out result) && result == null, "Evicted originals must return false.");
        Assert(!service.TryGetUiMessage(new[] { Message(null) }, null, null, out result), "An absent reply ID must not select an unidentified message.");
    }

    private static void AssertHighlightPreservesMarkup()
    {
        Assert(Highlight("@Alice hello") == "<color=#73BFFF>@Alice</color> hello", "Hex colors require #.");
        Assert(Highlight("<b>@Alice</b> hello") == "<b><color=#73BFFF>@Alice</color></b> hello", "Mention spans must stop at closing tags.");
        Assert(Highlight("<color=red><i>@Alice</i></color> @Bob") ==
            "<color=red><i><color=#73BFFF>@Alice</color></i></color> <color=#73BFFF>@Bob</color>", "Nested formatting must remain balanced.");
        Assert(Highlight("<link=\"@Alice\">@Bob</link>") == "<link=\"@Alice\"><color=#73BFFF>@Bob</color></link>", "Tag attributes must not be rewritten.");
        Assert(Highlight("@玩家\n@Bob") == "<color=#73BFFF>@玩家</color>\n<color=#73BFFF>@Bob</color>", "Unicode mentions and line breaks must survive.");
        Assert(Highlight("plain <b>text</b>") == "plain <b>text</b>" && Highlight("") == "" && Highlight(null) == null,
            "Text without mentions must be unchanged.");
    }

    private static void AssertAutocompleteRequiresNewOwnedInput()
    {
        Assert(CanComplete("@Al", "@A", true), "Editing a focused input should offer completion.");
        Assert(!CanComplete("@Al", "@Al", true), "Repaint or menu cancellation must not reopen the same query.");
        Assert(!CanComplete("@Al", "@A", false), "Other windows and existing menus must retain input ownership.");
        Assert(!CanComplete("@", "", true) && !CanComplete("@Alice ", "@Al", true) && !CanComplete("", "@Al", true),
            "Bare @, completed names, and cleared input must not open menus.");
        Assert(CanComplete("@Al", "", true), "Clearing and retyping must allow completion again.");
    }

    private static void AssertCompletionPreservesMessagePrefix()
    {
        var method = MentionUtility.GetMethod("ReplaceAtPartial", BindingFlags.NonPublic | BindingFlags.Static);
        Assert((string)method.Invoke(null, new object[] { "hello @玩家 @Al", "Alice Smith" }) == "hello @玩家 @Alice Smith ",
            "Completion must replace only the trailing partial and preserve spaces in display names.");
        Assert((string)method.Invoke(null, new object[] { "finished text", "Alice" }) == "finished text", "A stale partial must leave text unchanged.");
    }

    private static string Highlight(string text) => (string)MentionUtility.GetMethod("Highlight", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new object[] { text, "73BFFF" });

    private static bool CanComplete(string text, string previousText, bool canOpen)
    {
        object[] args = { text, previousText, canOpen, null };
        return (bool)MentionUtility.GetMethod("TryGetAutocompletePartial", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }

    private static FrameworkDisplayMessage Message(string id, string source = "builtin_chat") => new FrameworkDisplayMessage
    {
        MessageId = id,
        Source = source,
        SenderUuid = "sender",
        Text = "message"
    };

    private static PhinixFrameworkClient CreateMessageStore()
    {
        // Exercise the real store and event path without starting networking,
        // discovering plugins, or initializing RimWorld/Unity in a test process.
        return new PhinixFrameworkClient(null, null, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
