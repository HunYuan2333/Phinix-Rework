using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.TradeExtension.Client;
using RimWorld;
using Verse;

namespace PhinixClient.Trade
{
    public static class TradeItemConverter
    {
        public static TradeItemSnapshot ConvertToTradeItem(this Thing verseThing) => ConvertThingFromVerse(verseThing);

        public static Thing ConvertToVerse(this TradeItemSnapshot item) => ConvertThingFromSnapshot(item);

        public static Thing ConvertToVerseOrUnknown(this TradeItemSnapshot item) => ConvertThingFromSnapshotOrUnknown(item);

        public static IEnumerable<TradeItemSnapshot> ConvertToTradeItems(this IEnumerable<Thing> verseThings) => verseThings.Select(ConvertThingFromVerse);

        public static IEnumerable<Thing> ConvertToVerse(this IEnumerable<TradeItemSnapshot> items) => items.Select(ConvertThingFromSnapshot);

        public static IEnumerable<Thing> ConvertToVerseOrUnknown(this IEnumerable<TradeItemSnapshot> items) => items.Select(ConvertThingFromSnapshotOrUnknown);

        public static TradeItemSnapshot ConvertThingFromVerse(Thing verseThing)
        {
            return ConvertThingFromVerse(verseThing, true);
        }

        private static TradeItemSnapshot ConvertThingFromVerse(Thing verseThing, bool captureState)
        {
            if (verseThing == null) throw new ArgumentNullException(nameof(verseThing));

            TradeItemQuality quality = verseThing.TryGetQuality(out QualityCategory gottenQuality)
                ? toTradeItemQuality(gottenQuality)
                : TradeItemQuality.None;

            TradeItemSnapshot innerItem = null;
            if (verseThing is MinifiedThing minifiedVerseThing)
            {
                innerItem = ConvertThingFromVerse(minifiedVerseThing.InnerThing, false);
            }

            byte[] statePayload = captureState
                ? TradeThingStateSerializer.Serialize(verseThing)
                : Array.Empty<byte>();

            return new TradeItemSnapshot(
                verseThing.def.defName,
                verseThing.stackCount,
                verseThing.HitPoints,
                quality,
                verseThing.Stuff?.defName,
                innerItem,
                captureState ? StatefulTradeItemProtocol.ScribeCodecId : string.Empty,
                statePayload);
        }

        private static readonly Dictionary<string, ThingDef> thingDefCache = new Dictionary<string, ThingDef>();

        static TradeItemConverter()
        {
            // 预构建 defName → ThingDef 字典，避免每次转换做 O(n) 全表扫描
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (!string.IsNullOrEmpty(def.defName))
                    thingDefCache[def.defName] = def;
            }
        }

        private static ThingDef GetThingDefByName(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            thingDefCache.TryGetValue(defName, out ThingDef def);
            return def;
        }

        public static Thing ConvertThingFromSnapshot(TradeItemSnapshot item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (item.StatePayload != null && item.StatePayload.Length > 0)
            {
                if (!string.Equals(item.StateCodecId, StatefulTradeItemProtocol.ScribeCodecId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported trade item state codec '{item.StateCodecId}'.");
                }

                Thing restoredThing = TradeThingStateSerializer.Deserialize(item.StatePayload);
                if (!string.Equals(restoredThing.def?.defName, item.DefName, StringComparison.Ordinal) ||
                    restoredThing.stackCount != item.StackCount)
                {
                    throw new InvalidOperationException(
                        $"Restored trade item does not match snapshot (expected '{item.DefName}' x{item.StackCount}, got '{restoredThing.def?.defName ?? "unknown"}' x{restoredThing.stackCount}).");
                }
                return restoredThing;
            }

            return ConvertThingFromPreview(item);
        }

        /// <summary>
        /// Builds a non-authoritative UI preview without touching the opaque Scribe payload.
        /// Remote state may reference defs unavailable on this client; list rendering must not
        /// deserialize that state repeatedly or imply that a lossy reconstruction is deliverable.
        /// </summary>
        public static Thing ConvertThingFromSnapshotPreviewOrUnknown(TradeItemSnapshot item)
        {
            try
            {
                return ConvertThingFromPreview(item);
            }
            catch (InvalidOperationException)
            {
                return CreateUnknownItem(item);
            }
        }

        public static IReadOnlyList<string> FindMissingDependencies(IEnumerable<TradeItemSnapshot> items)
        {
            HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);
            foreach (TradeItemSnapshot item in items ?? Enumerable.Empty<TradeItemSnapshot>())
            {
                CollectMissingDependencies(item, missing);
            }
            return missing.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static void CollectMissingDependencies(TradeItemSnapshot item, ISet<string> missing)
        {
            if (item == null) return;
            if (!string.IsNullOrEmpty(item.DefName) && GetThingDefByName(item.DefName) == null)
                missing.Add("ThingDef: " + item.DefName);
            if (!string.IsNullOrEmpty(item.StuffDefName) && GetThingDefByName(item.StuffDefName) == null)
                missing.Add("ThingDef: " + item.StuffDefName);
            CollectMissingDependencies(item.InnerItem, missing);

            if (item.StatePayload == null || item.StatePayload.Length == 0) return;
            if (!string.Equals(item.StateCodecId, StatefulTradeItemProtocol.ScribeCodecId,
                StringComparison.OrdinalIgnoreCase))
            {
                missing.Add("Codec: " + (item.StateCodecId ?? "<empty>"));
                return;
            }

            try
            {
                HashSet<string> missingDefs = new HashSet<string>(StringComparer.Ordinal);
                TradeThingStateSerializer.CollectMissingThingDefs(item.StatePayload, missingDefs);
                foreach (string defName in missingDefs) missing.Add("ThingDef: " + defName);
            }
            catch (Exception)
            {
                missing.Add("State: " + (item.DefName ?? "<unknown>"));
            }
        }

        private static Thing ConvertThingFromPreview(TradeItemSnapshot item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            ThingDef thingDef = GetThingDefByName(item.DefName);

            if (thingDef == null)
                throw new InvalidOperationException(string.Format("Could not find a def that matches def name '{0}'", item.DefName));

            ThingDef stuffDef = null;
            if (!string.IsNullOrEmpty(item.StuffDefName))
            {
                stuffDef = GetThingDefByName(item.StuffDefName);
            }

            Thing verseThing = ThingMaker.MakeThing(thingDef, stuffDef);
            verseThing.stackCount = item.StackCount;
            verseThing.HitPoints = item.HitPoints;

            if (item.Quality != TradeItemQuality.None)
            {
                verseThing.TryGetComp<CompQuality>()?.SetQuality(toQualityCategory(item.Quality), ArtGenerationContext.Outsider);
            }

            if (verseThing is MinifiedThing minifiedVerseThing)
            {
                minifiedVerseThing.InnerThing = item.InnerItem != null ? ConvertThingFromPreview(item.InnerItem) : null;
            }

            return verseThing;
        }

        /// <summary>
        /// Restores one proportional part of a stateful stack. The opaque Scribe
        /// state remains authoritative; only stackCount is reduced, and callers
        /// must ensure all distributed parts sum to the original stack.
        /// </summary>
        public static Thing ConvertStatefulStackFromSnapshot(TradeItemSnapshot item, int stackCount)
        {
            if (item == null || item.StatePayload == null || item.StatePayload.Length == 0 ||
                !string.Equals(item.StateCodecId, StatefulTradeItemProtocol.ScribeCodecId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The item has no supported stateful stack payload.");
            if (stackCount < 1 || stackCount > item.StackCount)
                throw new ArgumentOutOfRangeException(nameof(stackCount));

            Thing restoredThing = TradeThingStateSerializer.Deserialize(item.StatePayload);
            if (!string.Equals(restoredThing.def?.defName, item.DefName, StringComparison.Ordinal) ||
                restoredThing.stackCount != item.StackCount || stackCount > Math.Max(1, restoredThing.def.stackLimit))
                throw new InvalidOperationException("The restored stateful stack does not match its red-packet preview.");
            restoredThing.stackCount = stackCount;
            return restoredThing;
        }

        public static Thing ConvertThingFromSnapshotOrUnknown(TradeItemSnapshot item)
        {
            try
            {
                return ConvertThingFromSnapshot(item);
            }
            catch (InvalidOperationException)
            {
                if (item?.StatePayload != null && item.StatePayload.Length > 0)
                {
                    throw;
                }
                return CreateUnknownItem(item);
            }
        }

        private static Thing CreateUnknownItem(TradeItemSnapshot item)
        {
            ThingDef thingDef = GetThingDefByName("UnknownItem");
            if (thingDef == null)
                throw new InvalidOperationException("Could not find the Phinix UnknownItem definition.");

            UnknownItem verseThing = (UnknownItem)ThingMaker.MakeThing(thingDef);
            verseThing.stackCount = item?.StackCount ?? 1;
            verseThing.HitPoints = item?.HitPoints ?? verseThing.MaxHitPoints;
            verseThing.OriginalLabel = getInnerDefName(item);
            return verseThing;
        }

        public static bool CompareThings(Thing thing, Thing other)
        {
            if (thing == null && other == null)
                return true;

            if (thing == null || other == null)
                return false;

            if (thing.def.defName != other.def.defName)
                return false;

            if (thing.HitPoints != other.HitPoints)
                return false;

            if (thing.Stuff?.defName != other.Stuff?.defName)
                return false;

            if (!thing.TryGetQuality(out QualityCategory q1) || !other.TryGetQuality(out QualityCategory q2))
                return false;

            if (q1 != q2)
                return false;

            if (!CompareThings(thing.GetInnerIfMinified(), other.GetInnerIfMinified()))
                return false;

            return true;
        }

        private static string getInnerDefName(TradeItemSnapshot item)
        {
            if (item?.InnerItem != null)
            {
                return getInnerDefName(item.InnerItem);
            }

            return item?.DefName ?? "UnknownItem";
        }

        private static TradeItemQuality toTradeItemQuality(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful: return TradeItemQuality.Awful;
                case QualityCategory.Poor: return TradeItemQuality.Poor;
                case QualityCategory.Normal: return TradeItemQuality.Normal;
                case QualityCategory.Good: return TradeItemQuality.Good;
                case QualityCategory.Excellent: return TradeItemQuality.Excellent;
                case QualityCategory.Masterwork: return TradeItemQuality.Masterwork;
                case QualityCategory.Legendary: return TradeItemQuality.Legendary;
                default: return TradeItemQuality.None;
            }
        }

        private static QualityCategory toQualityCategory(TradeItemQuality quality)
        {
            switch (quality)
            {
                case TradeItemQuality.Awful: return QualityCategory.Awful;
                case TradeItemQuality.Poor: return QualityCategory.Poor;
                case TradeItemQuality.Normal: return QualityCategory.Normal;
                case TradeItemQuality.Good: return QualityCategory.Good;
                case TradeItemQuality.Excellent: return QualityCategory.Excellent;
                case TradeItemQuality.Masterwork: return QualityCategory.Masterwork;
                case TradeItemQuality.Legendary: return QualityCategory.Legendary;
                default: return QualityCategory.Normal;
            }
        }
    }
}
