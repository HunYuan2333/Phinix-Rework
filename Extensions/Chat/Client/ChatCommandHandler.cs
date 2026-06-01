using PhinixClient.Framework;
using Utils.Framework;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatCommandHandler : IClientCommandHandler, IClientOutgoingCommandHandler
    {
        private readonly IFrameworkChatClientApi chatApi;

        public ChatCommandHandler(IFrameworkChatClientApi chatApi)
        {
            this.chatApi = chatApi;
        }

        public int Priority => 1000;

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null && command.MessageType == FrameworkChatProtocol.HistorySyncCompleteType;
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            return new ClientIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            return chatApi != null &&
                   command != null &&
                   command.MessageType == FrameworkChatProtocol.HistoryRequestType;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            if (context.CompatibilityMode != FrameworkCompatibilityMode.FrameworkV2)
            {
                return new ClientOutgoingCommandResult
                {
                    Action = MessageHandlingResultAction.LegacyFallback
                };
            }

            return new ClientOutgoingCommandResult
            {
                Action = MessageHandlingResultAction.Handled,
                Command = command
            };
        }
    }
}
