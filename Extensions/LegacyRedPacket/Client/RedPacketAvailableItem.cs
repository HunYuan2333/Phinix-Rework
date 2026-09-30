using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using Phinix.TradeExtension.Client;
using RimWorld;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal sealed class RedPacketAvailableItem : IDisposable
    {
        private readonly StackedThings physical;
        private readonly Thing preview;
        private int selected;

        private RedPacketAvailableItem(StackedThings physical, InventoryEntry entry, Thing preview)
        {
            this.physical = physical;
            Entry = entry;
            this.preview = preview;
        }

        public InventoryEntry Entry { get; }
        public bool IsInventory => Entry != null;
        private bool Combinable => !string.IsNullOrEmpty(Entry?.AggregationKey);
        // An opaque entry can represent one complete stack (e.g. 1000 rice),
        // whereas an aggregatable entry counts individual items.
        public int Count => IsInventory
            ? (int)Math.Min(int.MaxValue, Combinable ? Entry.Quantity : preview.stackCount)
            : physical.Count;
        public long ReservationQuantity => Combinable ? Selected : Entry.Quantity;
        public string Label => IsInventory
            ? preview.LabelCapNoCount + "  [" + "Phinix_legacyRedpacket_inventorySource".Translate().ToString() + "]"
            : physical.Label;
        public ThingDef ThingDef => IsInventory ? preview.def : physical.ThingDef;
        public ThingDef StuffDef => IsInventory ? preview.Stuff : physical.StuffDef;
        public ThingStyleDef StyleDef => IsInventory ? preview.StyleDef : physical.StyleDef;
        public int Selected
        {
            get => IsInventory ? selected : physical.Selected;
            set
            {
                int count = Math.Max(0, Math.Min(value, Count));
                if (IsInventory) selected = count;
                else physical.Selected = count;
            }
        }

        public static RedPacketAvailableItem FromPhysical(StackedThings stack)
        {
            return new RedPacketAvailableItem(stack, null, null);
        }

        public static RedPacketAvailableItem FromInventory(InventoryEntry entry, Thing preview)
        {
            if (entry == null || preview == null || preview.def == null || preview.stackCount < 1 ||
                preview is MinifiedThing || preview.def.category != ThingCategory.Item || preview.def.IsCorpse ||
                (string.IsNullOrEmpty(entry.AggregationKey) && entry.Quantity != 1))
                throw new InvalidOperationException("This inventory entry cannot be sent in a red packet.");
            return new RedPacketAvailableItem(null, entry.Clone(), preview);
        }

        public IEnumerable<PoppedThing> PopSelectedPhysical()
        {
            return IsInventory ? Enumerable.Empty<PoppedThing>() : physical.PopSelectedWithOrigins();
        }

        public void Dispose() { if (IsInventory) Discard(new[] { preview }); }

        internal static void Discard(IEnumerable<Thing> things)
        {
            foreach (Thing thing in things ?? Enumerable.Empty<Thing>())
                if (thing != null && !thing.Destroyed && !thing.Spawned && thing.ParentHolder == null)
                    thing.Destroy(DestroyMode.Vanish);
        }
    }
}
