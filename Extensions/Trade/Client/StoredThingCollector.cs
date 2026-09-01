using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Utils;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public static class StoredThingCollector
    {
        public static List<Thing> Collect(
            IEnumerable<Map> maps,
            bool includeAllMapItems,
            Action<string, LogLevel> log = null)
        {
            List<Thing> collectedThings = new List<Thing>();
            HashSet<int> collectedThingIds = new HashSet<int>();
            HashSet<Thing> collectedUnassignedThings = new HashSet<Thing>();
            List<Map> mapSnapshot;

            try
            {
                mapSnapshot = maps?.Where(map => map != null).ToList() ?? new List<Map>();
            }
            catch (Exception exception)
            {
                LogFailure(log, "Failed to enumerate maps while collecting tradable items.", exception);
                return collectedThings;
            }

            foreach (Map map in mapSnapshot)
            {
                if (includeAllMapItems)
                {
                    CollectAllMapItems(map, collectedThings, collectedThingIds, collectedUnassignedThings, log);
                    continue;
                }

                CollectStoredMapItems(map, collectedThings, collectedThingIds, collectedUnassignedThings, log);
            }

            return collectedThings;
        }

        private static void CollectAllMapItems(
            Map map,
            ICollection<Thing> collectedThings,
            ISet<int> collectedThingIds,
            ISet<Thing> collectedUnassignedThings,
            Action<string, LogLevel> log)
        {
            try
            {
                foreach (Thing thing in map.listerThings.AllThings.ToList())
                {
                    TryAddThing(thing, map, collectedThings, collectedThingIds, collectedUnassignedThings);
                }
            }
            catch (Exception exception)
            {
                LogFailure(log, $"Failed to enumerate all things on map '{map}'.", exception);
            }
        }

        private static void CollectStoredMapItems(
            Map map,
            ICollection<Thing> collectedThings,
            ISet<int> collectedThingIds,
            ISet<Thing> collectedUnassignedThings,
            Action<string, LogLevel> log)
        {
            List<SlotGroup> groups;

            try
            {
                groups = map.haulDestinationManager?.AllGroups?
                    .Where(group => group != null)
                    .ToList() ?? new List<SlotGroup>();
            }
            catch (Exception exception)
            {
                LogFailure(log, $"Failed to enumerate storage groups on map '{map}'; using map-level fallback.", exception);
                CollectStoredMapItemsFallback(map, collectedThings, collectedThingIds, collectedUnassignedThings, log);
                return;
            }

            bool fallbackRequired = false;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                SlotGroup group = groups[groupIndex];
                try
                {
                    foreach (Thing thing in group.HeldThings.ToList())
                    {
                        TryAddThing(thing, map, collectedThings, collectedThingIds, collectedUnassignedThings);
                    }
                }
                catch (Exception exception)
                {
                    fallbackRequired = true;
                    LogFailure(
                        log,
                        $"Failed to enumerate storage group {groupIndex} on map '{map}'; the group was isolated.",
                        exception);
                }
            }

            if (fallbackRequired)
            {
                CollectStoredMapItemsFallback(map, collectedThings, collectedThingIds, collectedUnassignedThings, log);
            }
        }

        private static void CollectStoredMapItemsFallback(
            Map map,
            ICollection<Thing> collectedThings,
            ISet<int> collectedThingIds,
            ISet<Thing> collectedUnassignedThings,
            Action<string, LogLevel> log)
        {
            List<Thing> mapThings;
            try
            {
                mapThings = map.listerThings.AllThings.ToList();
            }
            catch (Exception exception)
            {
                LogFailure(log, $"Failed to snapshot things for storage fallback on map '{map}'.", exception);
                return;
            }

            int failedPredicates = 0;
            Exception firstFailure = null;
            foreach (Thing thing in mapThings)
            {
                if (!IsValidMapThing(thing, map))
                {
                    continue;
                }

                try
                {
                    if (StoreUtility.IsInValidStorage(thing))
                    {
                        TryAddThing(thing, map, collectedThings, collectedThingIds, collectedUnassignedThings);
                    }
                }
                catch (Exception exception)
                {
                    failedPredicates++;
                    firstFailure = firstFailure ?? exception;
                }
            }

            if (firstFailure != null)
            {
                LogFailure(
                    log,
                    $"Storage fallback skipped {failedPredicates} thing(s) whose storage predicate failed on map '{map}'.",
                    firstFailure);
            }
        }

        private static void TryAddThing(
            Thing thing,
            Map expectedMap,
            ICollection<Thing> collectedThings,
            ISet<int> collectedThingIds,
            ISet<Thing> collectedUnassignedThings)
        {
            if (!IsValidMapThing(thing, expectedMap))
            {
                return;
            }

            bool added = thing.thingIDNumber > 0
                ? collectedThingIds.Add(thing.thingIDNumber)
                : collectedUnassignedThings.Add(thing);
            if (added)
            {
                collectedThings.Add(thing);
            }
        }

        private static bool IsValidMapThing(Thing thing, Map expectedMap)
        {
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.def == null)
            {
                return false;
            }

            return thing.Map == expectedMap && thing.Position.IsValid && thing.Position.InBounds(expectedMap);
        }

        private static void LogFailure(Action<string, LogLevel> log, string message, Exception exception)
        {
            log?.Invoke($"[StoredThingCollector] {message}{Environment.NewLine}{exception}", LogLevel.WARNING);
        }
    }
}
