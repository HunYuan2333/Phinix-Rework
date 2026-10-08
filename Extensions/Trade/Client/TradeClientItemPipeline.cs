using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;
using Verse;
using Thing = Verse.Thing;

namespace Phinix.TradeExtension.Client
{
    internal sealed class TradeClientItemPipeline : ITradeItemPayloadEncoder
    {
        private readonly List<IItemCodec> codecs;
        private readonly ItemCodecContext codecContext;

        public TradeClientItemPipeline(Action<LogEventArgs> log)
            : this(log, FrameworkCompatibilityMode.Unknown)
        {
        }

        public TradeClientItemPipeline(Action<LogEventArgs> log, FrameworkCompatibilityMode compatibilityMode, IEnumerable<IItemCodec> extensionCodecs = null)
        {
            codecContext = new ItemCodecContext
            {
                CompatibilityMode = compatibilityMode,
                Log = (message, level) => log?.Invoke(new LogEventArgs(message, level))
            };

            codecs = new List<IItemCodec> { new ScribedTradeItemCodec(), new DefaultLegacyTradeItemCodec() };
            foreach (IItemCodec codec in extensionCodecs ?? Enumerable.Empty<IItemCodec>())
            {
                if (codec == null || string.IsNullOrEmpty(codec.CodecId))
                {
                    continue;
                }

                if (codecs.Any(existing => string.Equals(existing.CodecId, codec.CodecId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                codecs.Insert(0, codec);
            }
        }

        public void SetCompatibilityMode(FrameworkCompatibilityMode compatibilityMode)
        {
            codecContext.CompatibilityMode = compatibilityMode;
        }

        public void SetExtensionCodecs(IEnumerable<IItemCodec> extensionCodecs)
        {
            if (extensionCodecs == null) return;

            foreach (IItemCodec codec in extensionCodecs)
            {
                if (codec == null || string.IsNullOrEmpty(codec.CodecId))
                {
                    continue;
                }

                if (codecs.Any(existing => string.Equals(existing.CodecId, codec.CodecId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                codecs.Insert(Math.Max(0, codecs.FindIndex(candidate => candidate is ScribedTradeItemCodec)), codec);
            }
        }

        public Thing[] DecodeTradeItems(IEnumerable<TradeItemSnapshot> tradeItems)
        {
            return DecodeItems(EncodeTradeItems(tradeItems));
        }

        public FrameworkItemPayload[] EncodeTradeItems(IEnumerable<TradeItemSnapshot> tradeItems)
        {
            return (tradeItems ?? Enumerable.Empty<TradeItemSnapshot>())
                .Select(encodeTradeItem)
                .ToArray();
        }

        public Thing[] DecodeItems(IEnumerable<FrameworkItemPayload> itemPayloads)
        {
            return (itemPayloads ?? Enumerable.Empty<FrameworkItemPayload>())
                .Select(decodePayloadOrUnknown)
                .Where(thing => thing != null)
                .ToArray();
        }

        public bool CanDecodeItemExactly(FrameworkItemPayload payload)
        {
            return payload != null && codecs.Any(candidate => candidate.CanDecode(payload, codecContext));
        }

        public Thing DecodeItemExactly(FrameworkItemPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            IItemCodec codec = codecs.FirstOrDefault(candidate => candidate.CanDecode(payload, codecContext));
            if (codec == null)
                throw new InvalidOperationException($"No item codec can decode payload '{payload.CodecId ?? "unknown"}' without loss.");
            Thing thing = codec.Decode(payload, codecContext) as Thing;
            if (thing == null || thing.def == null || thing.def.defName == "UnknownItem")
                throw new InvalidOperationException($"Item codec '{codec.CodecId}' did not restore an exact game item.");
            return thing;
        }

        private FrameworkItemPayload encodeTradeItem(TradeItemSnapshot item)
        {
            IItemCodec codec = null;
            try
            {
                codec = codecs.FirstOrDefault(candidate => candidate.CanEncode(item, codecContext));
                if (codec == null)
                {
                    throw new InvalidOperationException("No item codec can encode this trade item.");
                }

                return codec.Encode(item, codecContext)
                    ?? throw new InvalidOperationException($"Item codec '{codec.CodecId}' returned no payload.");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"The entire trade update was rejected because item encoding failed (codec '{codec?.CodecId ?? "unresolved"}').", exception);
            }
        }

        private Thing decodePayloadOrUnknown(FrameworkItemPayload payload)
        {
            IItemCodec codec = codecs.FirstOrDefault(candidate => candidate.CanDecode(payload, codecContext));
            if (codec == null)
            {
                if (string.Equals(payload?.CodecId, StatefulTradeItemProtocol.ScribeCodecId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"No item codec could decode required stateful payload '{payload.CodecId}'.");
                }
                codecContext.Log?.Invoke($"No item codec could decode payload for codec '{payload?.CodecId ?? "unknown"}'; creating UnknownItem.", LogLevel.WARNING);
                return buildUnknownItem(payload?.CodecId ?? "UnknownCodec");
            }

            try
            {
                return codec.Decode(payload, codecContext) as Thing ?? buildUnknownItem(payload.CodecId);
            }
            catch (Exception exception)
            {
                if (string.Equals(payload?.CodecId, StatefulTradeItemProtocol.ScribeCodecId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Stateful trade item decoding failed; delivery was rejected to prevent item-state loss.", exception);
                }
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
