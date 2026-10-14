using System;
using System.Collections.Generic;

namespace Phinix.InventoryExtension
{
    public static class InventoryDropDelivery
    {
        public static bool TryResolveTradeDropTarget(Verse.Map map, out Verse.IntVec3 dropSpot, out string reason)
        {
            dropSpot = Verse.IntVec3.Invalid;
            if (map == null || map.Disposed)
            {
                reason = "The delivery map is unavailable.";
                return false;
            }
            if (Verse.Current.Game != null && Verse.Current.Game.Gravship != null)
            {
                reason = "Wait until the gravship has finished landing before requesting a delivery.";
                return false;
            }

            try
            {
                Verse.IntVec3 requested = RimWorld.DropCellFinder.TradeDropSpot(map);
                if (!requested.IsValid || !IsInBounds(requested, map) ||
                    !RimWorld.DropCellFinder.TryFindDropSpotNear(requested, map, out dropSpot,
                        allowFogged: true, canRoofPunch: false))
                {
                    dropSpot = Verse.IntVec3.Invalid;
                    reason = "No valid drop-pod landing cell is available.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "A safe drop-pod landing cell could not be resolved: " + exception.Message;
                return false;
            }

            reason = null;
            return true;
        }

        public static InventoryDeliveryResult DeliverThings(
            IEnumerable<Verse.Thing> things, Verse.Map map, Verse.IntVec3 dropSpot)
        {
            List<Verse.Thing> materialized = things == null
                ? new List<Verse.Thing>()
                : new List<Verse.Thing>(things);
            if (materialized.Count == 0 || materialized.Exists(thing => thing == null || thing.def == null))
                return Rejected("There are no valid items to deliver.", map, dropSpot);
            if (map == null || map.Disposed || !dropSpot.IsValid || !IsInBounds(dropSpot, map))
                return Rejected("The delivery target is unavailable.", map, dropSpot);
            if (Verse.Current.Game != null && Verse.Current.Game.Gravship != null)
                return Rejected("Wait until the gravship has finished landing before requesting a delivery.", map, dropSpot);

            try
            {
                RimWorld.DropPodUtility.DropThingsNear(dropSpot, map, materialized, canRoofPunch: false);
            }
            catch (Exception exception)
            {
                InventoryDeliveryResult custody = InspectCustody(materialized, map, dropSpot);
                custody.Status = custody.Status == InventoryDeliveryStatus.Rejected
                    ? InventoryDeliveryStatus.Rejected
                    : InventoryDeliveryStatus.Uncertain;
                custody.Reason = "Drop-pod delivery threw an exception: " + exception.Message;
                return custody;
            }

            return InspectCustody(materialized, map, dropSpot);
        }

        public static InventoryDeliveryResult InspectCustody(
            IEnumerable<Verse.Thing> things, Verse.Map map, Verse.IntVec3 dropSpot)
        {
            bool anyCustody = false;
            bool allOnTargetMap = true;
            bool anyDestroyed = false;
            foreach (Verse.Thing thing in things ?? new Verse.Thing[0])
            {
                if (thing == null || thing.Destroyed)
                {
                    anyDestroyed = true;
                    allOnTargetMap = false;
                    continue;
                }
                bool hasCustody = thing.ParentHolder != null || thing.SpawnedOrAnyParentSpawned;
                anyCustody |= hasCustody;
                if (!hasCustody || thing.MapHeld != map) allOnTargetMap = false;
            }

            if (anyCustody && allOnTargetMap && !anyDestroyed)
                return Delivered(map, dropSpot);
            if (!anyCustody && !anyDestroyed)
                return Rejected("The drop-pod API did not take custody of the items.", map, dropSpot);
            return new InventoryDeliveryResult
            {
                Status = InventoryDeliveryStatus.Uncertain,
                Reason = "Item custody could not be verified after drop-pod delivery.",
                Map = map,
                DropSpot = dropSpot
            };
        }

        private static InventoryDeliveryResult Delivered(Verse.Map map, Verse.IntVec3 dropSpot)
        {
            return new InventoryDeliveryResult
            {
                Status = InventoryDeliveryStatus.Delivered,
                Map = map,
                DropSpot = dropSpot
            };
        }

        private static bool IsInBounds(Verse.IntVec3 cell, Verse.Map map)
        {
            return map != null && cell.x >= 0 && cell.z >= 0 &&
                   cell.x < map.Size.x && cell.z < map.Size.z;
        }

        private static InventoryDeliveryResult Rejected(string reason, Verse.Map map, Verse.IntVec3 dropSpot)
        {
            return new InventoryDeliveryResult
            {
                Status = InventoryDeliveryStatus.Rejected,
                Reason = reason,
                Map = map,
                DropSpot = dropSpot
            };
        }
    }

}
