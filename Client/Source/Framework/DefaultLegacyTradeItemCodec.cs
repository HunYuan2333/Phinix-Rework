using System;
using System.Collections.Generic;
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
        public const string DefaultCodecId = "core.item.vanilla";

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

            global::Phinix.Framework.FrameworkVanillaItemData payload = toPayload(protoThing);
            return new FrameworkItemPayload
            {
                CodecId = CodecId,
                PayloadBytes = FrameworkSerialization.SerializeItemData(payload),
                Metadata = new List<FrameworkMetadataEntry>()
            };
        }

        public bool CanDecode(FrameworkItemPayload payload, ItemCodecContext context)
        {
            return payload != null &&
                   string.Equals(payload.CodecId, CodecId, StringComparison.OrdinalIgnoreCase) &&
                   ((payload.PayloadBytes != null && payload.PayloadBytes.Length > 0) ||
                    !string.IsNullOrEmpty(payload.PayloadJson));
        }

        public object Decode(FrameworkItemPayload payload, ItemCodecContext context)
        {
            if (!CanDecode(payload, context))
            {
                throw new InvalidOperationException($"Payload cannot be decoded by codec '{CodecId}'.");
            }

            ProtoThing protoThing;
            if (payload.PayloadBytes != null && payload.PayloadBytes.Length > 0)
            {
                global::Phinix.Framework.FrameworkVanillaItemData deserializedPayload = FrameworkSerialization.DeserializeItemData(payload.PayloadBytes);
                protoThing = fromPayload(deserializedPayload);
            }
            else
            {
                LegacyProtoThingPayload legacyPayload = FrameworkSerialization.DeserializePayload<LegacyProtoThingPayload>(payload.PayloadJson);
                protoThing = fromLegacyPayload(legacyPayload);
            }

            return TradingThingConverter.ConvertThingFromProtoOrUnknown(protoThing);
        }

        private static global::Phinix.Framework.FrameworkVanillaItemData toPayload(ProtoThing protoThing)
        {
            if (protoThing == null) return null;

            return new global::Phinix.Framework.FrameworkVanillaItemData
            {
                DefName = protoThing.DefName,
                StackCount = protoThing.StackCount,
                HitPoints = protoThing.HitPoints,
                Quality = toFrameworkQuality(protoThing.Quality),
                StuffDefName = protoThing.StuffDefName,
                InnerItem = toPayload(protoThing.InnerProtoThing)
            };
        }

        private static ProtoThing fromPayload(global::Phinix.Framework.FrameworkVanillaItemData payload)
        {
            if (payload == null) return null;

            return new ProtoThing
            {
                DefName = payload.DefName,
                StackCount = payload.StackCount,
                HitPoints = payload.HitPoints,
                Quality = fromFrameworkQuality(payload.Quality),
                StuffDefName = payload.StuffDefName,
                InnerProtoThing = fromPayload(payload.InnerItem)
            };
        }

        private static ProtoThing fromLegacyPayload(LegacyProtoThingPayload payload)
        {
            if (payload == null) return null;

            return new ProtoThing
            {
                DefName = payload.DefName,
                StackCount = payload.StackCount,
                HitPoints = payload.HitPoints,
                Quality = payload.Quality,
                StuffDefName = payload.StuffDefName,
                InnerProtoThing = fromLegacyPayload(payload.InnerProtoThing)
            };
        }

        private static global::Phinix.Framework.FrameworkItemQuality toFrameworkQuality(Quality quality)
        {
            switch (quality)
            {
                case Quality.Awful: return global::Phinix.Framework.FrameworkItemQuality.Awful;
                case Quality.Poor: return global::Phinix.Framework.FrameworkItemQuality.Poor;
                case Quality.Normal: return global::Phinix.Framework.FrameworkItemQuality.Normal;
                case Quality.Good: return global::Phinix.Framework.FrameworkItemQuality.Good;
                case Quality.Excellent: return global::Phinix.Framework.FrameworkItemQuality.Excellent;
                case Quality.Masterwork: return global::Phinix.Framework.FrameworkItemQuality.Masterwork;
                case Quality.Legendary: return global::Phinix.Framework.FrameworkItemQuality.Legendary;
                case Quality.None: return global::Phinix.Framework.FrameworkItemQuality.None;
                default: return global::Phinix.Framework.FrameworkItemQuality.Unspecified;
            }
        }

        private static Quality fromFrameworkQuality(global::Phinix.Framework.FrameworkItemQuality quality)
        {
            switch (quality)
            {
                case global::Phinix.Framework.FrameworkItemQuality.Awful: return Quality.Awful;
                case global::Phinix.Framework.FrameworkItemQuality.Poor: return Quality.Poor;
                case global::Phinix.Framework.FrameworkItemQuality.Normal: return Quality.Normal;
                case global::Phinix.Framework.FrameworkItemQuality.Good: return Quality.Good;
                case global::Phinix.Framework.FrameworkItemQuality.Excellent: return Quality.Excellent;
                case global::Phinix.Framework.FrameworkItemQuality.Masterwork: return Quality.Masterwork;
                case global::Phinix.Framework.FrameworkItemQuality.Legendary: return Quality.Legendary;
                case global::Phinix.Framework.FrameworkItemQuality.None: return Quality.None;
                default: return Quality.None;
            }
        }

        [System.Runtime.Serialization.DataContract]
        private sealed class LegacyProtoThingPayload
        {
            [System.Runtime.Serialization.DataMember(Order = 0)]
            public string DefName { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 1)]
            public int StackCount { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 2)]
            public int HitPoints { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 3)]
            public Quality Quality { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 4)]
            public string StuffDefName { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 5)]
            public LegacyProtoThingPayload InnerProtoThing { get; set; }
        }
    }
}
