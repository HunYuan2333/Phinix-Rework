using System;
using System.Linq;
using PhinixClient.Trade;
using Utils.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    internal sealed class ScribedTradeItemCodec : IItemCodec
    {
        public string CodecId => StatefulTradeItemProtocol.ScribeCodecId;

        public bool CanEncode(object item, ItemCodecContext context)
        {
            TradeItemSnapshot snapshot = item as TradeItemSnapshot;
            return snapshot != null &&
                   string.Equals(snapshot.StateCodecId, CodecId, StringComparison.OrdinalIgnoreCase) &&
                   snapshot.StatePayload != null && snapshot.StatePayload.Length > 0;
        }

        public FrameworkItemPayload Encode(object item, ItemCodecContext context)
        {
            TradeItemSnapshot snapshot = item as TradeItemSnapshot;
            if (!CanEncode(snapshot, context))
            {
                throw new InvalidOperationException($"Item has no state for codec '{CodecId}'.");
            }

            FrameworkItemPayload payload = new FrameworkItemPayload
            {
                CodecId = CodecId,
                PayloadBytes = snapshot.StatePayload.ToArray()
            };
            StatefulTradeItemProtocol.SetPreview(payload, snapshot);
            return payload;
        }

        public bool CanDecode(FrameworkItemPayload payload, ItemCodecContext context)
        {
            return payload != null &&
                   string.Equals(payload.CodecId, CodecId, StringComparison.OrdinalIgnoreCase) &&
                   payload.PayloadBytes != null && payload.PayloadBytes.Length > 0;
        }

        public object Decode(FrameworkItemPayload payload, ItemCodecContext context)
        {
            if (!CanDecode(payload, context) || !StatefulTradeItemProtocol.TryGetPreview(payload, out TradeItemSnapshot preview))
            {
                throw new InvalidOperationException($"Payload cannot be decoded by codec '{CodecId}'.");
            }

            Thing thing = TradeThingStateSerializer.Deserialize(payload.PayloadBytes);
            if (!string.Equals(thing.def?.defName, preview.DefName, StringComparison.Ordinal) ||
                thing.stackCount != preview.StackCount)
            {
                throw new InvalidOperationException(
                    $"Stateful trade item does not match its preview (expected '{preview.DefName}' x{preview.StackCount}, got '{thing.def?.defName ?? "unknown"}' x{thing.stackCount}).");
            }

            return thing;
        }
    }
}
