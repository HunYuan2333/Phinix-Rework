using System;
using System.Collections.Generic;
using UserManagement;
using Utils;
using Utils.Framework;
using PhinixClient;
using PhinixClient.Framework;
namespace Phinix.ChatExtension.Client
{
    public sealed class ChatMessageIntent
    {
        public ChatMessageIntent(string text, IEnumerable<string> mentionedUuids = null, string replyToMessageId = null, string replyToSnippet = null)
        {
            Text = text;
            MentionedUuids = new List<string>(mentionedUuids ?? new string[0]).AsReadOnly();
            ReplyToMessageId = replyToMessageId; ReplyToSnippet = replyToSnippet;
        }
        public string Text { get; }
        public IReadOnlyList<string> MentionedUuids { get; }
        public string ReplyToMessageId { get; }
        public string ReplyToSnippet { get; }
    }

    /// <summary>Current chat intent API. A successful send means dispatched, not acknowledged.</summary>
    public interface IChatMessageSender
    {
        bool TrySend(ChatMessageIntent intent);
    }

    public interface IFrameworkChatClientApi
    {
        FrameworkPacket CreateOutgoingMessage(string rawMessage, ClientFrameworkContext context);

        FrameworkPacket CreateOutgoingMessage(string rawMessage, ClientFrameworkContext context, IEnumerable<string> mentionedUuids, string replyToMessageId, string replyToSnippet);

        FrameworkDisplayMessage RenderMessage(FrameworkPacket message);

        FrameworkPacket CreateHistoryRequestPacket(string sessionId, string senderUuid);

        UIChatMessage[] BuildUiMessages(IEnumerable<FrameworkDisplayMessage> messages, IClientUserDirectory userDirectory);

        bool TryGetUiMessage(IEnumerable<FrameworkDisplayMessage> messages, string messageId, IClientUserDirectory userDirectory, out UIChatMessage message);

        int CountUnreadExcluding(IEnumerable<FrameworkDisplayMessage> messages, IEnumerable<string> excludedUuids);

        bool ShouldDisplayChatMessage(UIChatMessage message, IEnumerable<string> blockedUserUuids, bool includeBlockedMessages);

        bool ShouldPlayNotification(UIChatMessage message, string localUuid, bool playNoiseOnMessageReceived, bool isInGame, IEnumerable<string> blockedUserUuids);

        UIChatMessage ToUiMessage(FrameworkDisplayMessage message, IClientUserDirectory userDirectory);
    }

    public interface IChatUiHostContext
    {
        IClientChatService ChatService { get; }

        string Uuid { get; }

        int ChatMessageLimit { get; }

        bool ShowNameFormatting { get; }

        bool ShowChatFormatting { get; }
        bool TimestampOnRight { get; }
        bool ShowImages { get; }

        float MaxImageHeight { get; }

        ISet<string> BlockedUsers { get; }

        /// <summary>
        /// 当前会话是否已连接并登录（Authenticated &amp;&amp; LoggedIn）。
        /// 用于让聊天列表在"上线"瞬间从消息存储重新同步一次，
        /// 避免启动时就链接、进入存档后 UI 未初始化（UI 与后端不同步）。
        /// </summary>
        bool IsOnline { get; }

        event EventHandler OnDisconnect;

        event EventHandler OnUsersChanged;

        event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;

        event EventHandler<UserBlockStateChangedEventArgs> OnBlockedUsersChanged;

        void CreateTrade(string uuid);

        void BlockUser(string uuid);

        void UnBlockUser(string uuid);

        void Log(LogEventArgs args);

        UIChatMessage ReplyTarget { get; }

        void SetReplyTarget(UIChatMessage message);

        void ClearReplyTarget();

        event EventHandler ReplyTargetChanged;

        bool SendChatMessage(string text, IEnumerable<string> mentionedUuids);
    }

}

namespace Phinix.ChatExtension.Client
{
    // Public context for third-party member actions; no game object or Host business knowledge.
    public sealed class ChatMemberActionContext
    {
        public const string PointId = "builtin.chat.member.actions";
        public ChatMemberActionContext(string uuid, string displayName, bool blocked, bool online)
        { Uuid = uuid; DisplayName = displayName; Blocked = blocked; Online = online; }
        // Uuid is null when no member is selected; contributors must inspect HasSelection.
        public bool HasSelection => Uuid != null;
        public string Uuid { get; }
        public string DisplayName { get; }
        public bool Blocked { get; }
        // Local session state; never target presence/idle/away.
        public bool Online { get; }
    }
}

namespace Phinix.ChatExtension.Client
{
    public sealed class ChatComposerActionContext
    {
        public const string PointId = "builtin.chat.composer.actions";
        public ChatComposerActionContext(string text, string replyToId) { Text = text; ReplyToId = replyToId; }
        public string Text { get; }
        public string ReplyToId { get; }
    }
    // Snapshot supplied by Chat; contributors do not receive mutable messages or storage ownership.
    public sealed class ChatMessageActionContext
    {
        public const string PointId = "builtin.chat.message.actions";
        public ChatMessageActionContext(string messageId, string senderUuid, string text, string source,
            UIChatMessageStatus status, IEnumerable<string> urls, int urlPage = 0)
            : this(messageId, senderUuid, text, source, status, urls, urlPage, null, false) { }
        public ChatMessageActionContext(string messageId, string senderUuid, string text, string source,
            UIChatMessageStatus status, IEnumerable<string> urls, int urlPage, string selectedMediaUrl, bool canRetryMedia)
        {
            SelectedMediaUrl = selectedMediaUrl; CanRetryMedia = canRetryMedia;
            if (urlPage < 0) throw new ArgumentOutOfRangeException(nameof(urlPage));
            MessageId = messageId; SenderUuid = senderUuid; Text = text; Source = source; Status = status;
            Urls = new List<string>(urls ?? new string[0]).AsReadOnly(); UrlPage = urlPage;
        }
        public string MessageId { get; }
        public string SenderUuid { get; }
        public string Text { get; }
        public string Source { get; }
        public UIChatMessageStatus Status { get; }
        public IReadOnlyList<string> Urls { get; }
        public int UrlPage { get; }
        public string SelectedMediaUrl { get; }
        public bool CanRetryMedia { get; }
    }

}
