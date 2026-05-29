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

        public static Thing ConvertThingFromSnapshot(TradeItemSnapshot item)
        {
            ThingDef thingDef;
            try
            {
                thingDef = DefDatabase<ThingDef>.AllDefs.Single(def => def.defName == item.DefName);
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException(string.Format("Could not find a single def that matches def name '{0}'", item.DefName), e);
            }

            ThingDef stuffDef = null;
            try
            {
                if (!string.IsNullOrEmpty(item.StuffDefName))
                {
                    stuffDef = DefDatabase<ThingDef>.AllDefs.Single(def => def.defName == item.StuffDefName);
                }
            }
            catch (InvalidOperationException e)
            {
                throw new InvalidOperationException(string.Format("Could not find a single def that matches stuff def name '{0}'", item.StuffDefName), e);
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
                ThingDef thingDef = DefDatabase<ThingDef>.AllDefs.Single(def => def.defName == "UnknownItem");

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
