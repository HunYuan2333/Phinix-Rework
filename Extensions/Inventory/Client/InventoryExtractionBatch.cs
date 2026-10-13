using System;
using System.Collections.Generic;

namespace Phinix.InventoryExtension.Client
{
    // Pure cursor: one legal stack per Advance. Success means journal + verified delivery completed.
    internal sealed class InventoryExtractionBatch
    {
        internal delegate bool Deliver(string id, long units, out string reason);
        private readonly InventoryBatchItem[] items;
        private readonly int[] limits;
        private int index;
        private long currentUnits, completedUnits;
        private bool running = true;
        private string reason;
        internal InventoryExtractionBatch(InventoryBatchItem[] items, int[] limits)
        { this.items = items; this.limits = limits; }
        internal InventoryBatchProgress Progress => new InventoryBatchProgress(running, items.Length, index, completedUnits, reason);
        internal void Stop(string explanation) { running = false; reason = explanation; }
        internal void Advance(Deliver deliver)
        {
            if (!running) return;
            long units = Math.Min(items[index].Quantity - currentUnits, limits[index]);
            if (!deliver(items[index].EntryId, units, out string failure)) { Stop(failure); return; }
            completedUnits = checked(completedUnits + units); currentUnits += units;
            if (currentUnits == items[index].Quantity) { index++; currentUnits = 0; }
            if (index == items.Length) running = false;
        }
    }
}
