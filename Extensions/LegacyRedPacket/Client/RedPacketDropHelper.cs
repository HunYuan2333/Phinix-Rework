using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包空投帮助类（解耦：不依赖 Client.Instance.DropPods）。
    /// 语义与原 mod dropPods 一致：TradeDropSpot + DropThingsNear，组数上限 100。
    /// </summary>
    internal static class RedPacketDropHelper
    {
        private const int MaxDropPodCountPerDelivery = 100;

        public static void DropPods(IEnumerable<Thing> things, bool dropCurrentMap)
        {
            Map map = dropCurrentMap ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropSpot, map, things, canRoofPunch: false);
        }

        public static void DropPodsWithLimit(IEnumerable<Thing> things, bool dropCurrentMap)
        {
            List<Thing> validThings = things.Where(thing => thing != null && !thing.Destroyed).ToList();
            if (validThings.Count == 0) return;

            if (validThings.Count <= MaxDropPodCountPerDelivery)
            {
                DropPods(validThings, dropCurrentMap);
                return;
            }

            Map map = ResolveDropMap(dropCurrentMap);
            if (map == null)
            {
                DropPods(validThings, dropCurrentMap);
                return;
            }

            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            List<List<Thing>> groups = BuildDropThingGroups(validThings, MaxDropPodCountPerDelivery);
            DropPodUtility.DropThingGroupsNear(dropSpot, map, groups, canRoofPunch: false);
        }

        private static Map ResolveDropMap(bool dropCurrentMap)
        {
            Map map = dropCurrentMap ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map != null) return map;
            if (Current.Game == null || Current.Game.Maps == null || Current.Game.Maps.Count == 0) return null;

            return Current.Game.Maps.FirstOrDefault(candidate => candidate != null && candidate.IsPlayerHome)
                ?? Current.Game.Maps.FirstOrDefault(candidate => candidate != null);
        }

        private static List<List<Thing>> BuildDropThingGroups(List<Thing> things, int maxGroups)
        {
            int groupCount = Mathf.Min(Mathf.Max(1, maxGroups), things.Count);
            List<List<Thing>> groups = new List<List<Thing>>(groupCount);
            for (int i = 0; i < groupCount; i++)
            {
                groups.Add(new List<Thing>());
            }

            for (int i = 0; i < things.Count; i++)
            {
                groups[i % groupCount].Add(things[i]);
            }

            return groups;
        }
    }
}
