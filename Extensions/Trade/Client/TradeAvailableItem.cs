using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using Verse;

namespace Phinix.TradeExtension.Client
{
    internal sealed class TradeAvailableItem : IDisposable
    {
        private readonly StackedThings physical;
        private readonly Thing preview;

        private TradeAvailableItem(StackedThings physical, InventoryEntry inventoryEntry, Thing preview)
        {
            this.physical = physical;
            InventoryEntry = inventoryEntry;
            this.preview = preview;
        }

        public InventoryEntry InventoryEntry { get; }
        public bool IsInventory => InventoryEntry != null;
        public int Count => IsInventory
            ? (int)Math.Min(int.MaxValue, InventoryEntry.Quantity)
            : physical.Count;
        public string Label => IsInventory
            ? preview.LabelCapNoCount + "  [" + "Phinix_trade_inventorySourceBadge".Translate().ToString() + "]"
            : physical.Label;
        public ThingDef ThingDef => IsInventory ? preview.def : physical.ThingDef;
        public ThingDef StuffDef => IsInventory ? preview.Stuff : physical.StuffDef;
        public ThingStyleDef StyleDef => IsInventory ? preview.StyleDef : physical.StyleDef;

        public int Selected
        {
            get => IsInventory ? selected : physical.Selected;
            set
            {
                int clamped = Math.Max(0, Math.Min(value, Count));
                if (IsInventory) selected = clamped;
                else physical.Selected = clamped;
            }
        }
        private int selected;

        public static TradeAvailableItem FromPhysical(StackedThings stack)
        {
            return new TradeAvailableItem(stack ?? throw new ArgumentNullException(nameof(stack)), null, null);
        }

        public static TradeAvailableItem FromInventory(InventoryEntry entry, Thing preview)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (preview == null || preview.def == null)
            {
                Discard(preview);
                throw new InvalidOperationException("The inventory adapter returned an invalid preview.");
            }
            return new TradeAvailableItem(null, entry.Clone(), preview);
        }

        public IEnumerable<PoppedThing> PopSelectedPhysical()
        {
            return IsInventory ? Enumerable.Empty<PoppedThing>() : physical.PopSelectedWithOrigins();
        }

        public void Dispose()
        {
            if (IsInventory) Discard(preview);
        }

        private static void Discard(Thing thing)
        {
            if (thing != null && !thing.Destroyed && !thing.Spawned && thing.ParentHolder == null)
                thing.Destroy(DestroyMode.Vanish);
        }
    }
}
