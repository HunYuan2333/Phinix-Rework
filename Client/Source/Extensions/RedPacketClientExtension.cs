using System.Collections.Generic;
using Utils;
using Utils.Framework;
using Verse;

namespace PhinixClient.Extensions
{
    [PhinixExtension("sample.red_packet")]
    public class RedPacketClientExtension : IPhinixExtension, ICapabilityProvider, IClientMessageHandler, IMessageRenderer
    {
        private const string Prefix = "/redpacket ";

        public string ExtensionId => "sample.red_packet";

        public int Priority => 100;

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
                Envelope = new FrameworkEnvelope
                {
                    MessageType = ExtensionId,
                    PayloadJson = FrameworkSerialization.SerializePayload(new RedPacketPayload
                    {
                        Body = body
                    })
                }
            };
        }

        public bool CanHandleIncomingEnvelope(FrameworkEnvelope envelope)
        {
            return false;
        }

        public ClientIncomingMessageResult HandleIncomingEnvelope(FrameworkEnvelope envelope, ClientFrameworkContext context)
        {
            return null;
        }

        public bool CanRender(FrameworkEnvelope envelope)
        {
            return envelope != null && envelope.MessageType == ExtensionId;
        }

        public FrameworkDisplayMessage Render(FrameworkEnvelope envelope)
        {
            RedPacketPayload payload = FrameworkSerialization.DeserializePayload<RedPacketPayload>(envelope.PayloadJson);
            return new FrameworkDisplayMessage
            {
                MessageId = envelope.MessageId,
                SenderUuid = envelope.SenderUuid,
                TimestampUtcTicks = envelope.TimestampUtcTicks,
                Source = "red_packet",
                TranslationKey = "Phinix_framework_redPacketMessage",
                TranslationArgs = new List<string> { payload.Body }
            };
        }
    }
}
