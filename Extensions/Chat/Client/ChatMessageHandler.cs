using System;
using PhinixClient.Framework;
using Utils.Framework;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatMessageHandler : IClientMessageHandler
    {
        private readonly IFrameworkChatClientApi chatApi;

        public ChatMessageHandler(IFrameworkChatClientApi chatApi)
        {
            this.chatApi = chatApi;
        }

        public int Priority => 1000;

        public bool CanHandleOutgoingText(string rawMessage)
        {
            return chatApi != null && !string.IsNullOrWhiteSpace(rawMessage);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context)
        {
            if (context.CompatibilityMode != FrameworkCompatibilityMode.FrameworkV2)
            {
                return new ClientOutgoingMessageResult
                {
                    Action = MessageHandlingResultAction.LegacyFallback
                };
            }

            return new ClientOutgoingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                Message = chatApi.CreateOutgoingMessage(rawMessage, context)
            };
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            return message != null && message.MessageType == FrameworkChatProtocol.MessageType;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ClientFrameworkContext context)
        {
            return new ClientIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                DisplayMessage = chatApi.RenderMessage(message)
            };
        }
    }
}
