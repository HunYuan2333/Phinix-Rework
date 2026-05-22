using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Trading;
using Utils.Framework;
using Verse;

namespace PhinixClient.Framework
{
    /// <summary>
    /// Default codec that bridges current trade <see cref="ProtoThing"/> payloads into the framework item pipeline.
    /// </summary>
    public sealed class DefaultLegacyTradeItemCodec : IItemCodec
    {
        public const string DefaultCodecId = "core.trade-item.legacy-proto";

        public string CodecId => DefaultCodecId;

        public bool CanEncode(object item, ItemCodecContext context)
        {
            return item is ProtoThing;
        }

        public FrameworkItemPayload Encode(object item, ItemCodecContext context)
        {
            if (!(item is ProtoThing protoThing))
            {
                throw new InvalidOperationException($"Item is not a {nameof(ProtoThing)} and cannot be encoded by codec '{CodecId}'.");
            }

            LegacyProtoThingPayload payload = toPayload(protoThing);
            return new FrameworkItemPayload
            {
                CodecId = CodecId,
                PayloadJson = FrameworkSerialization.SerializePayload(payload),
                Metadata = new List<FrameworkMetadataEntry>()
            };
        }

        public bool CanDecode(FrameworkItemPayload payload, ItemCodecContext context)
        {
            return payload != null &&
                   string.Equals(payload.CodecId, CodecId, StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrEmpty(payload.PayloadJson);
        }

        public object Decode(FrameworkItemPayload payload, ItemCodecContext context)
        {
            if (!CanDecode(payload, context))
            {
                throw new InvalidOperationException($"Payload cannot be decoded by codec '{CodecId}'.");
            }

            LegacyProtoThingPayload deserializedPayload = FrameworkSerialization.DeserializePayload<LegacyProtoThingPayload>(payload.PayloadJson);
            ProtoThing protoThing = fromPayload(deserializedPayload);
            return TradingThingConverter.ConvertThingFromProtoOrUnknown(protoThing);
        }

        private static LegacyProtoThingPayload toPayload(ProtoThing protoThing)
        {
            if (protoThing == null) return null;

            return new LegacyProtoThingPayload
            {
                DefName = protoThing.DefName,
                StackCount = protoThing.StackCount,
                HitPoints = protoThing.HitPoints,
                Quality = protoThing.Quality,
                StuffDefName = protoThing.StuffDefName,
                InnerProtoThing = toPayload(protoThing.InnerProtoThing)
            };
        }

        private static ProtoThing fromPayload(LegacyProtoThingPayload payload)
        {
            if (payload == null) return null;

            return new ProtoThing
            {
                DefName = payload.DefName,
                StackCount = payload.StackCount,
                HitPoints = payload.HitPoints,
                Quality = payload.Quality,
                StuffDefName = payload.StuffDefName,
                InnerProtoThing = fromPayload(payload.InnerProtoThing)
            };
        }

        [DataContract]
        private sealed class LegacyProtoThingPayload
        {
            [DataMember(Order = 0)]
            public string DefName { get; set; }

            [DataMember(Order = 1)]
            public int StackCount { get; set; }

            [DataMember(Order = 2)]
            public int HitPoints { get; set; }

            [DataMember(Order = 3)]
            public Quality Quality { get; set; }

            [DataMember(Order = 4)]
            public string StuffDefName { get; set; }

            [DataMember(Order = 5)]
            public LegacyProtoThingPayload InnerProtoThing { get; set; }
        }
    }
}
