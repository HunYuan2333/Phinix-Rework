using System;

namespace PhinixClient.Framework
{
    public sealed class BuiltInChatClientHostServices
    {
        public BuiltInChatClientHostServices(PhinixFrameworkChatService chatService, Action notifyChatSynced)
        {
            ChatService = chatService;
            NotifyChatSynced = notifyChatSynced;
        }

        public PhinixFrameworkChatService ChatService { get; }

        public Action NotifyChatSynced { get; }
    }
}
