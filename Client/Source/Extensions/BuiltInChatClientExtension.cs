using System.Collections.Generic;
using PhinixClient.Framework;
using Utils.Framework;

namespace PhinixClient.Extensions
{
    [PhinixExtension("builtin.chat")]
    public class BuiltInChatClientExtension : IPhinixExtensionModule, ICapabilityProvider, IClientMessageHandler, IClientCommandHandler, IMessageRenderer
    {
        private BuiltInChatClientHostServices hostServices;

        public string ExtensionId => "builtin.chat";

        public int Priority => 1000;

        public void Register(IExtensionComponentSink sink, ExtensionHostContext hostContext)
        {
            hostServices = hostContext.GetRequiredService<BuiltInChatClientHostServices>();
            sink.AddCapabilityProvider(this);
            sink.AddClientMessageHandler(this);
            sink.AddClientCommandHandler(this);
            sink.AddMessageRenderer(this);
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return FrameworkProtocol.BuiltInChatMessageType;
            yield return FrameworkProtocol.BuiltInChatHistoryRequestType;
            yield return FrameworkProtocol.BuiltInChatHistorySyncCompleteType;
        }

        public bool CanHandleOutgoingText(string rawMessage)
        {
            return hostServices?.ChatService != null &&
                   !string.IsNullOrWhiteSpace(rawMessage);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context)
        {
            return new ClientOutgoingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                Message = hostServices.ChatService.CreateOutgoingMessage(rawMessage, context)
            };
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            return message != null && message.MessageType == FrameworkProtocol.BuiltInChatMessageType;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ClientFrameworkContext context)
        {
            return new ClientIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                DisplayMessage = hostServices.ChatService.RenderMessage(message)
            };
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null && command.MessageType == FrameworkProtocol.BuiltInChatHistorySyncCompleteType;
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            hostServices?.NotifyChatSynced?.Invoke();

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
            return hostServices.ChatService.RenderMessage(message);
        }
    }
}
