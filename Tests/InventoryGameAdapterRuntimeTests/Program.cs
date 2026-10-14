using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Phinix.InventoryExtension;
using Verse;

internal static class Program
{
    private static int assertions;
    private static void Assert(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static int Main()
    {
        try
        {
            var contract = typeof(IInventoryCodec).Assembly;
            var adapter = typeof(InventoryDropDelivery).Assembly;
            Assert(adapter.GetName().Name == "InventoryExtension.GameAdapter", "One shared game adapter owns delivery implementation.");
            Assert(contract.GetType(typeof(InventoryDropDelivery).FullName) == null, "Contracts do not implement drop-pod delivery.");
            Assert(!contract.GetReferencedAssemblies().Any(a => a.Name == adapter.GetName().Name), "Contract does not depend on its game implementation.");
            Assert(!adapter.GetReferencedAssemblies().Any(a => a.Name == "PhinixClient" || a.Name.EndsWith(".Client")), "Shared game implementation does not depend on Host or business clients.");
            Assert(!Assembly.Load("PhinixClient").GetReferencedAssemblies().Any(a => a.Name.StartsWith("InventoryExtension")), "Host has no Inventory business dependency.");
            foreach (string consumer in new[] { "InventoryExtension.Client", "TradeExtension.Client", "LegacyRedPacketExtension.Client" })
            {
                Assert(Assembly.Load(consumer).GetReferencedAssemblies().Any(a => a.Name == adapter.GetName().Name), consumer + " binds the shared game adapter.");
            }
            Assert(typeof(IInventoryCodec).GetMethod("DeliverVerified").ReturnType == typeof(InventoryDeliveryResult), "Codec keeps explicit custody authority.");
            Assert(typeof(InventoryMaterializationResult).GetProperty("Things").PropertyType == typeof(IList<Thing>), "Plugin game materialization remains a Thing boundary.");

            IntVec3 spot;
            string reason;
            Assert(!InventoryDropDelivery.TryResolveTradeDropTarget(null, out spot, out reason) && !spot.IsValid && !string.IsNullOrEmpty(reason), "No map cannot resolve a drop target.");
            Assert(InventoryDropDelivery.DeliverThings(null, null, IntVec3.Invalid).Status == InventoryDeliveryStatus.Rejected, "An empty delivery cannot succeed.");
            Assert(InventoryDropDelivery.DeliverThings(new Thing[] { null }, null, IntVec3.Invalid).Status == InventoryDeliveryStatus.Rejected, "An invalid batch is rejected before game calls.");
            var loose = new Thing { def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef)), stackCount = 1 };
            Assert(InventoryDropDelivery.DeliverThings(new[] { loose }, null, IntVec3.Invalid).Status == InventoryDeliveryStatus.Rejected, "Missing target cannot transfer custody.");
            var map = (Map)FormatterServices.GetUninitializedObject(typeof(Map));
            Assert(InventoryDropDelivery.InspectCustody(new[] { loose }, map, IntVec3.Invalid).Status == InventoryDeliveryStatus.Rejected, "No custody remains a rejected delivery.");
            Assert(InventoryDropDelivery.InspectCustody(new Thing[] { null }, map, IntVec3.Invalid).Status == InventoryDeliveryStatus.Uncertain, "A missing item cannot authorize restoration.");
            var holder = new Holder();
            // Populate a held-item fixture without TryAdd notifications, which require
            // the game tick manager/Def resources. This tests inspection, not transfer.
            var holdingOwner = typeof(Thing).GetField("holdingOwner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(holdingOwner != null, "The real Thing exposes the custody field used by this fixture.");
            holdingOwner.SetValue(loose, holder.Items);
            Assert(InventoryDropDelivery.InspectCustody(new[] { loose }, map, IntVec3.Invalid).Status == InventoryDeliveryStatus.Uncertain, "Custody without the target map cannot be accepted or refunded.");
            Assert(InventoryDropDelivery.InspectCustody(new[] { loose, new Thing() }, map, IntVec3.Invalid).Status == InventoryDeliveryStatus.Uncertain, "Partial custody remains uncertain for the whole batch.");
            Console.WriteLine("Inventory game adapter passed: " + assertions + " assertions; no native drop-pod or GUI execution.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private sealed class Holder : IThingHolder
    {
        internal readonly ThingOwner<Thing> Items;
        internal Holder() { Items = new ThingOwner<Thing>(this); }
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => Items;
        public void GetChildHolders(List<IThingHolder> children) { }
    }
}
