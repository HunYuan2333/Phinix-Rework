using PhinixClient.Framework;
using Utils.Framework;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatMessageRenderer : IMessageRenderer
    {
        private readonly IFrameworkChatClientApi chatApi;

        public ChatMessageRenderer(IFrameworkChatClientApi chatApi)
        {
            this.chatApi = chatApi;
        }

        public bool CanRender(FrameworkPacket message)
        {
            return chatApi != null &&
                   message != null &&
                   message.MessageType == FrameworkChatProtocol.MessageType;
        }

        public FrameworkDisplayMessage Render(FrameworkPacket message)
        {
            return chatApi.RenderMessage(message);
        }
    }
}
