using System;
using System.Collections.Generic;
using System.Linq;
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
            TradeItemQuality quality = verseThing.TryGetQuality(out QualityCategory gottenQuality)
                ? toTradeItemQuality(gottenQuality)
                : TradeItemQuality.None;

            TradeItemSnapshot innerItem = null;
            if (verseThing is MinifiedThing minifiedVerseThing)
            {
                innerItem = ConvertThingFromVerse(minifiedVerseThing.InnerThing);
            }

            return new TradeItemSnapshot(
                verseThing.def.defName,
                verseThing.stackCount,
                verseThing.HitPoints,
                quality,
                verseThing.Stuff?.defName,
                innerItem);
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
                minifiedVerseThing.InnerThing = item.InnerItem != null ? ConvertThingFromSnapshot(item.InnerItem) : null;
            }

            return verseThing;
        }

        public static Thing ConvertThingFromSnapshotOrUnknown(TradeItemSnapshot item)
        {
            try
            {
                return ConvertThingFromSnapshot(item);
            }
            catch (InvalidOperationException)
            {
                ThingDef thingDef = GetThingDefByName("UnknownItem");

                UnknownItem verseThing = (UnknownItem)ThingMaker.MakeThing(thingDef);
                verseThing.stackCount = item?.StackCount ?? 1;
                verseThing.HitPoints = item?.HitPoints ?? verseThing.MaxHitPoints;
                verseThing.OriginalLabel = getInnerDefName(item);
                return verseThing;
            }
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
