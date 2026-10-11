using System;
using PhinixClient.Framework;
using Utils.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    // Converts current intent to the transport boundary; never retries through a second route.
    internal sealed class ChatMessageSubmissionAdapter : IChatMessageSender
    {
        private readonly IFrameworkChatClientApi chat;
        private readonly IClientMessageSubmission submission;
        private readonly IClientSessionContext session;
        public ChatMessageSubmissionAdapter(IFrameworkChatClientApi chat, IClientMessageSubmission submission, IClientSessionContext session)
        { this.chat = chat; this.submission = submission; this.session = session; }
        public bool TrySend(ChatMessageIntent intent)
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Chat submission requires the game main thread.");
            if (intent == null || string.IsNullOrWhiteSpace(intent.Text) || !session.Authenticated || !session.LoggedIn) return false;
            return submission.TrySubmitMessage(chat.CreateOutgoingMessage(intent.Text,
                new ClientFrameworkContext { SenderUuid = session.Uuid, SessionId = session.SessionId },
                intent.MentionedUuids, intent.ReplyToMessageId, intent.ReplyToSnippet));
        }
    }
}
