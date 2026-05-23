using System.Collections.Generic;
using Utils;
using Utils.Framework;

namespace PhinixClient.Extensions
{
    [PhinixExtension("builtin.chat")]
    public class BuiltInChatClientExtension : IPhinixExtension, ICapabilityProvider, IClientMessageHandler, IClientCommandHandler, IMessageRenderer
    {
        public string ExtensionId => "builtin.chat";

        public int Priority => 1000;

        public IEnumerable<string> GetCapabilities()
        {
            yield return FrameworkProtocol.BuiltInChatMessageType;
            yield return FrameworkProtocol.BuiltInChatHistoryRequestType;
            yield return FrameworkProtocol.BuiltInChatHistorySyncCompleteType;
        }

        public bool CanHandleOutgoingText(string rawMessage)
        {
            return Client.Instance != null &&
                   !string.IsNullOrWhiteSpace(rawMessage);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context)
        {
            return new ClientOutgoingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                Message = Client.Instance.FrameworkChatService.CreateOutgoingMessage(rawMessage, context)
            };
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            return message != null && message.MessageType == FrameworkProtocol.BuiltInChatMessageType;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ClientFrameworkContext context)
        {
            context.Log?.Invoke(
                $"Built-in chat client extension accepted incoming framework chat message '{message?.MessageId ?? "<null>"}' from '{message?.SenderUuid ?? "<null>"}'.",
                LogLevel.DEBUG);

            return new ClientIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                DisplayMessage = Client.Instance.FrameworkChatService.RenderMessage(message)
            };
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null && command.MessageType == FrameworkProtocol.BuiltInChatHistorySyncCompleteType;
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            if (Client.Instance != null)
            {
                Client.Instance.NotifyFrameworkChatSynced();
            }

            return new ClientIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }

        public bool CanRender(FrameworkPacket message)
        {
            return message != null && message.MessageType == FrameworkProtocol.BuiltInChatMessageType;
        }

        public FrameworkDisplayMessage Render(FrameworkPacket message)
        {
            return Client.Instance.FrameworkChatService.RenderMessage(message);
        }
    }
}
