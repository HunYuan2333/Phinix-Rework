using System.Collections.Generic;

namespace Phinix.InventoryExtension
{
    // Plugin-owned game boundary; these types are not generic Host presentation contracts.
    public sealed class InventoryDeliveryResult
    {
        public InventoryDeliveryStatus Status { get; set; }
        public string Reason { get; set; }
        public Verse.Map Map { get; set; }
        public Verse.IntVec3 DropSpot { get; set; }
        public bool Succeeded => Status == InventoryDeliveryStatus.Delivered;
    }

    public sealed class InventoryMaterializationResult
    {
        public bool Succeeded { get; set; }
        public InventoryFailureCode FailureCode { get; set; }
        public string Reason { get; set; }
        public IList<Verse.Thing> Things { get; set; } = new List<Verse.Thing>();
    }

    /// <summary>Inventory invokes codecs on the RimWorld main thread only.</summary>
    public interface IInventoryCodec
    {
        string CodecId { get; }
        int Version { get; }
        bool CanStore(InventoryItem item);
        // Null means the payload cannot be split or combined without losing state.
        string GetAggregationKey(InventoryItem item);
        int MaximumStackCount(InventoryEntry entry);
        Verse.Thing Materialize(InventoryEntry entry, int count);
        // Explicit custody result is required; an uncertain result retains the reservation.
        InventoryDeliveryResult DeliverVerified(Verse.Thing thing, Verse.Map map, Verse.IntVec3 dropSpot);
    }

}
