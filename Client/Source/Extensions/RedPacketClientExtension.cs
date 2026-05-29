using System.Collections.Generic;
using Utils;
using Utils.Framework;
using Verse;

namespace PhinixClient.Extensions
{
    [PhinixExtension("sample.red_packet")]
    public sealed class RedPacketClientExtension : IPhinixExtensionModule, ICapabilityProvider, IClientMessageHandler, IMessageRenderer
    {
        private const string Prefix = "/redpacket ";

        public string ExtensionId => "sample.red_packet";

        public int Priority => 100;

        public void Register(IExtensionBuilder builder)
        {
            builder.AddCapabilityProvider(this);
            builder.AddClientMessageHandler(this);
            builder.AddMessageRenderer(this);
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return ExtensionId;
        }

        public bool CanHandleOutgoingText(string rawMessage)
        {
            return rawMessage != null && rawMessage.StartsWith(Prefix);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context)
        {
            string body = rawMessage.Substring(Prefix.Length).Trim();
            if (string.IsNullOrEmpty(body))
            {
                context.Log?.Invoke("Phinix_framework_redPacketEmptyWarning".Translate(), LogLevel.WARNING);
                return new ClientOutgoingMessageResult
                {
                    Action = MessageHandlingResultAction.Handled
                };
            }

            return new ClientOutgoingMessageResult
            {
                Action = MessageHandlingResultAction.Handled,
                Message = new FrameworkPacket
                {
                    Flow = global::Phinix.Framework.FrameworkFlow.Message,
                    MessageType = ExtensionId,
                    PayloadJson = FrameworkSerialization.SerializePayload(new RedPacketPayload
                    {
                        Body = body
                    })
                }
            };
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            return false;
        }

        public ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ClientFrameworkContext context)
        {
            return null;
        }

        public bool CanRender(FrameworkPacket message)
        {
            return message != null && message.MessageType == ExtensionId;
        }

        public FrameworkDisplayMessage Render(FrameworkPacket message)
        {
            RedPacketPayload payload = FrameworkSerialization.DeserializePayload<RedPacketPayload>(message.PayloadJson);
            return new FrameworkDisplayMessage
            {
                MessageId = message.MessageId,
                SenderUuid = message.SenderUuid,
                TimestampUtcTicks = message.TimestampUtcTicks,
                Source = "red_packet",
                TranslationKey = "Phinix_framework_redPacketMessage",
                TranslationArgs = new List<string> { payload.Body }
            };
        }
    }
}
