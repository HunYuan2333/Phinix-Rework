using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;
using Verse;
using Thing = Verse.Thing;

namespace PhinixClient.Framework
{
    public sealed class PhinixClientItemPipeline
    {
        private readonly List<IItemCodec> codecs;
        private readonly ItemCodecContext codecContext;

        public PhinixClientItemPipeline(Action<LogEventArgs> log, FrameworkCompatibilityMode compatibilityMode)
        {
            this.codecContext = new ItemCodecContext
            {
                CompatibilityMode = compatibilityMode,
                Log = (message, level) => log?.Invoke(new LogEventArgs(message, level))
            };

            this.codecs = new List<IItemCodec> { new DefaultLegacyTradeItemCodec() };

            DiscoveredPhinixExtensions discoveredExtensions = PhinixExtensionRegistry.DiscoverExtensions();
            foreach (IItemCodec codec in discoveredExtensions.ItemCodecs)
            {
                if (codec == null || string.IsNullOrEmpty(codec.CodecId)) continue;
                if (codecs.Any(existing => string.Equals(existing.CodecId, codec.CodecId, StringComparison.OrdinalIgnoreCase))) continue;
                codecs.Add(codec);
            }
        }

        public void SetCompatibilityMode(FrameworkCompatibilityMode compatibilityMode)
        {
            codecContext.CompatibilityMode = compatibilityMode;
        }

        public Thing[] DecodeTradeItems(IEnumerable<TradeItemSnapshot> tradeItems)
        {
            return DecodeItems(EncodeTradeItems(tradeItems));
        }

        public FrameworkItemPayload[] EncodeTradeItems(IEnumerable<TradeItemSnapshot> tradeItems)
        {
            return (tradeItems ?? Enumerable.Empty<TradeItemSnapshot>())
                .Select(encodeTradeItem)
                .Where(payload => payload != null)
                .ToArray();
        }

        public Thing[] DecodeItems(IEnumerable<FrameworkItemPayload> itemPayloads)
        {
            return (itemPayloads ?? Enumerable.Empty<FrameworkItemPayload>())
                .Select(decodePayloadOrUnknown)
                .Where(thing => thing != null)
                .ToArray();
        }

        private FrameworkItemPayload encodeTradeItem(TradeItemSnapshot item)
        {
            IItemCodec codec = codecs.FirstOrDefault(candidate => candidate.CanEncode(item, codecContext));
            if (codec == null)
            {
                codecContext.Log?.Invoke("No item codec could encode trade item payload; dropping item.", LogLevel.WARNING);
                return null;
            }

            try
            {
                return codec.Encode(item, codecContext);
            }
            catch (Exception exception)
            {
                codecContext.Log?.Invoke($"Failed to encode item with codec '{codec.CodecId}': {exception.Message}", LogLevel.WARNING);
                return null;
            }
        }

        private Thing decodePayloadOrUnknown(FrameworkItemPayload payload)
        {
            IItemCodec codec = codecs.FirstOrDefault(candidate => candidate.CanDecode(payload, codecContext));
            if (codec == null)
            {
                codecContext.Log?.Invoke($"No item codec could decode payload for codec '{payload?.CodecId ?? "unknown"}'; creating UnknownItem.", LogLevel.WARNING);
                return buildUnknownItem(payload?.CodecId ?? "UnknownCodec");
            }

            try
            {
                return codec.Decode(payload, codecContext) as Thing ?? buildUnknownItem(payload.CodecId);
            }
            catch (Exception exception)
            {
                codecContext.Log?.Invoke($"Failed to decode item payload with codec '{codec.CodecId}': {exception.Message}", LogLevel.WARNING);
                return buildUnknownItem(payload?.CodecId ?? codec.CodecId);
            }
        }

        private static Thing buildUnknownItem(string label)
        {
            TradeItemSnapshot unknownItem = DefaultLegacyTradeItemCodec.CreateUnknownTradeItemSnapshot(label);

            return TradeItemConverter.ConvertThingFromSnapshotOrUnknown(unknownItem);
        }
    }
}
