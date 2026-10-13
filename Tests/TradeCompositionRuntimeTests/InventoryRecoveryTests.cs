using System;
using System.Reflection;
using Phinix.InventoryExtension;
using Phinix.TradeExtension;

internal static partial class Program
{
    private static void CheckInventoryRecovery(Type module)
    {
        object game=new object();bool main=true;
        var type=module.Assembly.GetType("Phinix.TradeExtension.Client.TradeInventoryRecovery",true);
        var instance=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,
            new object[]{(Func<bool>)(()=>main),(Func<object>)(()=>game)},null);
        var api=(ITradeInventoryRecoveryApi)instance;var source=new InventoryHost();
        string token=Guid.NewGuid().ToString();
        var line=new InventoryReservedItem{EntryId="entry",Quantity=7};
        var record=new InventoryReservation{ReservationId="held",OperationId="trade-offer:trade:with:colon:"+token,OwnerExtensionId="builtin.trade",Purpose="trade.offer",State=InventoryReservationState.Reserved,Items=new[]{line}};
        source.Status=new InventoryStatus{Availability=InventoryAvailability.Ready};
        source.Reservations=new[]{record,new InventoryReservation{ReservationId="foreign",OwnerExtensionId="other",Purpose="trade.offer",State=InventoryReservationState.Uncertain},new InventoryReservation{ReservationId="terminal",OwnerExtensionId="builtin.trade",Purpose="trade.offer",State=InventoryReservationState.Committed}};
        type.GetMethod("Start",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(instance,new object[]{source,source});
        var rows=api.Capture();
        Assert(rows.Count==1&&rows[0].TradeId=="trade:with:colon"&&rows[0].CorrelationToken==token,"Recovery reader retains only active Trade-owned offer reservations, preserving original key identities.");
        int reads=source.ReservationReads;api.Capture();
        Assert(source.ReservationReads==reads,"Unchanged projection reuses cached records rather than copying the journal every UI tick.");
        line.Quantity=99;
        Assert(rows[0].Lines[0].Units==7,"Public recovery projection copies mutable inventory line values.");
        Assert(source.ResolutionCalls==0,"Recovery capture invokes no reservation commit/restore/mark-uncertain writes.");
        long revision=api.Revision;record.State=InventoryReservationState.Uncertain;source.RaiseInventoryChanged();
        Assert(api.Revision>revision&&api.Capture()[0].State=="Uncertain","Availability/change notification invalidates recovery facts.");
        record.OperationId="unrecognized-key";source.RaiseAvailability();
        Assert(api.Capture()[0].TradeId==null&&api.Capture()[0].OperationId=="unrecognized-key","Unrecognized operation key remains visible without guessing trade or token.");
        game=new object();source.Status.Availability=InventoryAvailability.UnsavedGame;
        Assert(api.Capture().Count==0,"Unbound new game cannot expose old ledger recovery records.");
        source.Status.Availability=InventoryAvailability.ReadOnlyFault;source.RaiseAvailability();
        Assert(api.Capture().Count==1,"A bound read-only inventory fault still permits inspection, not mutation.");
        main=false;bool denied=false;try{api.Capture();}catch(InvalidOperationException){denied=true;}
        Assert(denied,"Recovery reads enforce main-thread access.");main=true;
        ((IDisposable)instance).Dispose();
        Assert(source.AvailabilityCount==0&&source.ChangeCount==0&&api.Capture().Count==0,"Stop detaches inventory events and clears borrowed facts.");
    }
}
