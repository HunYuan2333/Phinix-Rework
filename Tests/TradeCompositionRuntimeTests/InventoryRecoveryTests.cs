using System;
using System.Reflection;
using System.Linq;
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
        string inboxRoot=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"phinix-recovery-view-"+Guid.NewGuid().ToString("N"));
        try
        {
            var inboxType=type.Assembly.GetType("Phinix.TradeExtension.Client.TradeCompletionInbox",true);
            var inbox=Activator.CreateInstance(inboxType,new object[]{inboxRoot});
            string actor="local",server="synthetic-server",save=new string('a',64);
            source.Reservations=Array.Empty<InventoryReservation>();source.Status.SaveIdentity=save;
            var recordMethod=inboxType.GetMethod("Record");
            recordMethod.Invoke(inbox,new object[]{server,actor,"completion",true,"remote",Array.Empty<Utils.Framework.FrameworkItemPayload>()});
            recordMethod.Invoke(inbox,new object[]{server,"foreign","foreign-completion",true,"remote",Array.Empty<Utils.Framework.FrameworkItemPayload>()});
            type.GetMethod("ConfigureCompletions",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(instance,new object[]{inbox,(Func<string>)(()=>actor),(Func<string>)(()=>server)});
            rows=api.Capture();
            Assert(rows.Count==1&&rows[0].RecordKind=="Completion"&&rows[0].State=="Received"&&rows[0].SaveBinding=="Unbound","Read-only completion view filters recipient and preserves unbound history.");
            inboxType.GetMethod("BindSave").Invoke(inbox,new object[]{server,actor,"completion",true,save});
            Assert(api.Capture()[0].SaveBinding=="Current","New binding invalidates cached recovery projection.");
            source.Status.SaveIdentity=new string('b',64);
            Assert(api.Capture()[0].SaveBinding=="Different","Changing save identity updates projection without adopting receipt.");
            actor="foreign";
            Assert(api.Capture().Count==1&&api.Capture()[0].TradeId=="foreign-completion","Switching principal cannot expose previous principal receipts.");
            server="another-server";Assert(api.Capture().Count==0,"Another endpoint cannot expose receipt payloads or identities.");
            Assert(source.ResolutionCalls==0,"Completion inspection never resolves or delivers inventory assets.");
            var listType=type.Assembly.GetType("Phinix.TradeExtension.Client.TradeList",true);
            var list=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(listType);
            listType.GetField("sync",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(list,new object());
            listType.GetField("trades",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(list,new System.Collections.Generic.List<PhinixClient.Trade.ClientTradeSnapshot>());
            var context=new TradePresentationActionContext("trade-completion-recovery",1,"completion",false,false,0,0,tradeAvailable:false);
            var actions=(System.Collections.IEnumerable)listType.GetMethod("Actions",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(list,new object[]{context});
            Assert(!actions.Cast<object>().Any(),"Completion review context provides no product retry, cancel or refund action.");
        }
        finally {if(System.IO.Directory.Exists(inboxRoot))System.IO.Directory.Delete(inboxRoot,true);}
        main=false;bool denied=false;try{api.Capture();}catch(InvalidOperationException){denied=true;}
        Assert(denied,"Recovery reads enforce main-thread access.");main=true;
        ((IDisposable)instance).Dispose();
        Assert(source.AvailabilityCount==0&&source.ChangeCount==0&&api.Capture().Count==0,"Stop detaches inventory events and clears borrowed facts.");
    }
}
