using System;
using System.Collections.Generic;
using PhinixClient.Trade;
using Utils.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    internal sealed class DefaultLegacyTradeItemCodec : IItemCodec
    {
        public const string DefaultCodecId = "core.item.vanilla";

        public string CodecId => DefaultCodecId;

        public bool CanEncode(object item, ItemCodecContext context)
        {
            return item is TradeItemSnapshot;
        }

        public FrameworkItemPayload Encode(object item, ItemCodecContext context)
        {
            if (!(item is TradeItemSnapshot tradeItem))
            {
                throw new InvalidOperationException($"Item is not a {nameof(TradeItemSnapshot)} and cannot be encoded by codec '{CodecId}'.");
            }

            global::Phinix.Framework.FrameworkVanillaItemData payload = toPayload(tradeItem);
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

            TradeItemSnapshot item = decodeToTradeItemSnapshot(payload);
            return TradeItemConverter.ConvertThingFromSnapshotOrUnknown(item);
        }

        public static bool TryDecodeToTradeItemSnapshot(FrameworkItemPayload payload, out TradeItemSnapshot item)
        {
            item = null;
            if (payload == null ||
                !string.Equals(payload.CodecId, DefaultCodecId, StringComparison.OrdinalIgnoreCase) ||
                ((payload.PayloadBytes == null || payload.PayloadBytes.Length == 0) && string.IsNullOrEmpty(payload.PayloadJson)))
            {
                return false;
            }

            item = decodeToTradeItemSnapshot(payload);
            return item != null;
        }

        public static TradeItemSnapshot CreateUnknownTradeItemSnapshot(string label)
        {
            return new TradeItemSnapshot(label ?? "UnknownItem", 1, 1, TradeItemQuality.None);
        }

        private static TradeItemSnapshot decodeToTradeItemSnapshot(FrameworkItemPayload payload)
        {
            if (payload.PayloadBytes != null && payload.PayloadBytes.Length > 0)
            {
                global::Phinix.Framework.FrameworkVanillaItemData deserializedPayload = FrameworkSerialization.DeserializeItemData(payload.PayloadBytes);
                return fromPayload(deserializedPayload);
            }

            LegacyProtoThingPayload legacyPayload = FrameworkSerialization.DeserializePayload<LegacyProtoThingPayload>(payload.PayloadJson);
            return fromLegacyPayload(legacyPayload);
        }

        private static global::Phinix.Framework.FrameworkVanillaItemData toPayload(TradeItemSnapshot item)
        {
            if (item == null)
            {
                return null;
            }

            return new global::Phinix.Framework.FrameworkVanillaItemData
            {
                DefName = item.DefName,
                StackCount = item.StackCount,
                HitPoints = item.HitPoints,
                Quality = toFrameworkQuality(item.Quality),
                StuffDefName = item.StuffDefName,
                InnerItem = toPayload(item.InnerItem)
            };
        }

        private static TradeItemSnapshot fromPayload(global::Phinix.Framework.FrameworkVanillaItemData payload)
        {
            if (payload == null)
            {
                return null;
            }

            return new TradeItemSnapshot(
                payload.DefName,
                payload.StackCount,
                payload.HitPoints,
                fromFrameworkQuality(payload.Quality),
                payload.StuffDefName,
                fromPayload(payload.InnerItem));
        }

        private static TradeItemSnapshot fromLegacyPayload(LegacyProtoThingPayload payload)
        {
            if (payload == null)
            {
                return null;
            }

            return new TradeItemSnapshot(
                payload.DefName,
                payload.StackCount,
                payload.HitPoints,
                payload.Quality,
                payload.StuffDefName,
                fromLegacyPayload(payload.InnerProtoThing));
        }

        private static global::Phinix.Framework.FrameworkItemQuality toFrameworkQuality(TradeItemQuality quality)
        {
            switch (quality)
            {
                case TradeItemQuality.Awful: return global::Phinix.Framework.FrameworkItemQuality.Awful;
                case TradeItemQuality.Poor: return global::Phinix.Framework.FrameworkItemQuality.Poor;
                case TradeItemQuality.Normal: return global::Phinix.Framework.FrameworkItemQuality.Normal;
                case TradeItemQuality.Good: return global::Phinix.Framework.FrameworkItemQuality.Good;
                case TradeItemQuality.Excellent: return global::Phinix.Framework.FrameworkItemQuality.Excellent;
                case TradeItemQuality.Masterwork: return global::Phinix.Framework.FrameworkItemQuality.Masterwork;
                case TradeItemQuality.Legendary: return global::Phinix.Framework.FrameworkItemQuality.Legendary;
                case TradeItemQuality.None: return global::Phinix.Framework.FrameworkItemQuality.None;
                default: return global::Phinix.Framework.FrameworkItemQuality.Unspecified;
            }
        }

        private static TradeItemQuality fromFrameworkQuality(global::Phinix.Framework.FrameworkItemQuality quality)
        {
            switch (quality)
            {
                case global::Phinix.Framework.FrameworkItemQuality.Awful: return TradeItemQuality.Awful;
                case global::Phinix.Framework.FrameworkItemQuality.Poor: return TradeItemQuality.Poor;
                case global::Phinix.Framework.FrameworkItemQuality.Normal: return TradeItemQuality.Normal;
                case global::Phinix.Framework.FrameworkItemQuality.Good: return TradeItemQuality.Good;
                case global::Phinix.Framework.FrameworkItemQuality.Excellent: return TradeItemQuality.Excellent;
                case global::Phinix.Framework.FrameworkItemQuality.Masterwork: return TradeItemQuality.Masterwork;
                case global::Phinix.Framework.FrameworkItemQuality.Legendary: return TradeItemQuality.Legendary;
                case global::Phinix.Framework.FrameworkItemQuality.None: return TradeItemQuality.None;
                default: return TradeItemQuality.None;
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
            public TradeItemQuality Quality { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 4)]
            public string StuffDefName { get; set; }

            [System.Runtime.Serialization.DataMember(Order = 5)]
            public LegacyProtoThingPayload InnerProtoThing { get; set; }
        }
    }
}
