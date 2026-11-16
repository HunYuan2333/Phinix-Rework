using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using PhinixClient.Storage;
using Phinix.InventoryExtension;
using Phinix.InventoryExtension.Client;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Trade;
using Utils.Framework;

public class TradeProxy : DispatchProxy
{
    private readonly Dictionary<string,Delegate> events=new Dictionary<string,Delegate>();
    protected override object Invoke(MethodInfo method,object[] args)
    {
        if(method.Name.StartsWith("add_")){string key=method.Name.Substring(4);events.TryGetValue(key,out var prior);events[key]=Delegate.Combine(prior,(Delegate)args[0]);}
        else if(method.Name.StartsWith("remove_")){string key=method.Name.Substring(7);events.TryGetValue(key,out var prior);events[key]=Delegate.Remove(prior,(Delegate)args[0]);}
        return method.ReturnType!=typeof(void)&&method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;
    }
    internal void Raise(string name,EventArgs args){if(events.TryGetValue(name,out var action))action?.DynamicInvoke(this,args);}
}
internal static partial class Program
{
    private static int checks;
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    private static void Throws<T>(Action action,string name)where T:Exception{try{action();}catch(T){checks++;return;}throw new Exception(name);}
    static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-business-sqlite-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            using(var service=new ClientDatabaseService(owner=>Path.Combine(root,owner)))
            { Inventory(service);Trade(service,root);QueryLifecycle(service,root);Operations();Attachment(service); }
            FailureScenarios(root);
            Console.WriteLine("BUSINESS SQLITE PASS "+checks+" checks; production sources, synthetic assets");
        }
        finally{Directory.Delete(root,true);}
    }
    private static void Inventory(IClientDatabaseService service)
    {
        var payload=new byte[3*1024*1024];for(int i=0;i<payload.Length;i++)payload[i]=255;
        var old=new InventoryState{Sequence=12};old.Entries.Add(new InventoryEntry{EntryId="legacy-entry",CodecId="synthetic",CodecVersion=1,Quantity=5,Label="Test",Source="test",Payload=payload});
        old.Commits.Add(new InventoryCommit{DepositId="legacy-deposit",ContentHash="legacy-hash",HashVersion=0});
        string identity;
        using(var store=new InventoryDatabaseStore(service))
        {
            identity=store.Identity;Check(store.Load("save-a").Entries.Count==0,"New shared inventory");
            Check(store.Import("save-a",old,out var warning,out bool firstImport)&&warning==null&&firstImport,"Old snapshot imports atomically and signals its first successful migration");
            var state=store.Load("save-a");Check(state.Sequence==1&&state.Entries.Single().Payload.SequenceEqual(payload),"Payload preserved as BLOB");
            Check(store.Import("save-a",old,out warning,out firstImport)&&warning==null&&!firstImport,"Uncleaned save is idempotent and does not repeat its upgrade popup");
            old.Entries[0].Quantity=7;Check(store.Import("save-a",old,out warning)&&warning!=null,"Older/forked save cannot create more assets");
            state=store.Load("save-b");Check(state.Entries.Single().Quantity==5,"Different game shares latest inventory");
            Check(!store.Import("different-source",old,out warning)&&warning!=null,"Cross-source overlap retained rather than added");
            state=store.Load("save-b");state.Entries[0].Quantity=6;state.Sequence++;
            Check(store.TryAppend(state),"Incremental balance commit");
            var stale=store.Load("save-b");
            using(var second=new InventoryDatabaseStore(service))
            {
                var other=second.Load("save-c");other.Entries[0].Quantity++;other.Sequence++;Check(second.TryAppend(other),"Second owner commits");
                stale.Entries[0].Quantity++;stale.Sequence++;Check(!store.TryAppend(stale),"Stale writer cannot overwrite newer balance");
            }
            state=store.Load("save-b");state.PendingExtraction=new InventoryPendingExtraction{EntryId="legacy-entry",Quantity=2};state.Sequence++;
            Check(store.TryAppend(state)&&store.AbandonInterruptedExtraction(),"Interrupted extraction can be abandoned");
            state=store.Load("save-z");Check(state.PendingExtraction==null&&state.Entries.Single().Quantity==5,"Abandon consumes only reserved quantity");
            var ledger=new InventoryLedger();ledger.Restore(state);
            var reserved=ledger.PrepareReservation(new InventoryReservationRequest{OperationId="test-operation",OwnerExtensionId="test",Purpose="test",Lines=new List<InventoryReservationLine>{new InventoryReservationLine{EntryId="legacy-entry",Quantity=1}}},out var next);
            Check(reserved.Succeeded&&store.TryAppend(next),"Reservation commits independently");ledger.Restore(next);
            var abandoned=ledger.PrepareReservationResolution(reserved.Reservation.ReservationId,InventoryReservationResolution.Abandon,"synthetic user choice",out next);
            Check(abandoned.Succeeded&&next.Reservations.Single().State==InventoryReservationState.Abandoned&&store.TryAppend(next),"Abandon is explicit, not remote success");ledger.Restore(next);
            Check(!ledger.PrepareReservationResolution(reserved.Reservation.ReservationId,InventoryReservationResolution.Restore,"late result",out _).Succeeded,"Abandoned reservation cannot later refund");
            using(var db=service.Open("builtin.inventory","inventory",InventoryDatabaseStore.Schema))
                Check(db.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT length(payload) FROM inventory_entries WHERE id='legacy-entry'")))==payload.Length,"Storage contains raw bytes, not full JSON payload copies");
        }
        using(var reopened=new InventoryDatabaseStore(service))
            Check(reopened.Identity==identity&&reopened.Load("new-game").Entries.Single().Quantity==4,"Committed shared balance survives reopen");
    }
    private static FrameworkPacket Offer(string id)
    {
        var packet=new FrameworkPacket{MessageId=id,MessageType=FrameworkTradeProtocol.OfferUpdateRequestType,
            PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateRequest{TradeId="synthetic-trade"})};
        packet.SetCorrelationId(id);return packet;
    }
    private static void Trade(IClientDatabaseService service,string root)
    {
        string directory=Path.Combine(root,"legacy-intents");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"broken.intent"),"not valid JSON");
        int warnings=0;
        using(var journal=new TradeIntentJournal(directory,service,_=>warnings++))
        {
            for(int i=0;i<40;i++)journal.Prepare("server","player",Offer("operation-"+i),Array.Empty<FrameworkPacket>());
            Check(warnings==1&&journal.Read("server","player").Length==40,"Bad old row isolated; forty offers not blocked by phantom preallocation");
            Check(journal.Read("other-server","player").Length==0,"Indexed endpoint scope");
            var packet=Offer("operation-0");var original=journal.Prepare("server","player",packet,Array.Empty<FrameworkPacket>());
            journal.BeginAttempt(original);Check(journal.State(original)=="OutcomeUnknown","Transport boundary persists before send");
            var response=new FrameworkPacket{MessageType=FrameworkTradeProtocol.OfferUpdateResponseType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateResponse{Success=true,TradeId="synthetic-trade"})};response.SetCorrelationId("operation-0");
            Check(journal.RecordResponse("server","player",response)&&journal.DurableOutcome(original)==true,"Original durable response settles without a separate query");
            journal.MarkSettled("server","player","operation-0");
            Check(journal.Read("server","player").Length==39,"Settled payload removed from active history");
            Throws<InvalidOperationException>(()=>journal.Prepare("server","player",packet,Array.Empty<FrameworkPacket>()),"Terminal receipt prevents same operation replay");
        }
        using(var journal=new TradeIntentJournal(directory,service,_=>warnings++))
            Check(warnings==1&&journal.Read("server","player").Length==39,"Legacy import is once-only across reopen");
        using(var inbox=new TradeCompletionInbox(Path.Combine(root,"legacy-inbox"),service))
        {
            Check(inbox.Record("server","player","complete",true,"other",Array.Empty<FrameworkItemPayload>()),"Completion accepted");
            Check(!inbox.Record("server","player","complete",true,"other",Array.Empty<FrameworkItemPayload>()),"Duplicate completion suppressed");
            inbox.CompleteLocally("server","player","complete",true);
            Check(inbox.ReadAll().Length==0&&!inbox.Record("server","player","complete",true,"other",Array.Empty<FrameworkItemPayload>()),"Completed payload compacted; minimal dedup retained");
        }
    }
    private static void QueryLifecycle(IClientDatabaseService service,string root)
    {
        using(var journal=new TradeIntentJournal(Path.Combine(root,"query-lifecycle"),service))
        {
            for(int i=0;i<66;i++)journal.Prepare("query-server","query-player",Offer("query-"+i),Array.Empty<FrameworkPacket>());
            long now=0;var sent=new List<FrameworkPacket>();
            var queries=new TradeOperationQueryClient(journal,()=>"query-server",()=>"query-player",()=>"query-session",()=>true,p=>{sent.Add(p);return true;},()=>now);
            for(int i=0;i<64;i++)queries.Query("query-"+i);
            Throws<InvalidOperationException>(()=>queries.Query("query-64"),"Live queries retain their bounded capacity");
            now=30000;queries.Query("query-64");
            Check(sent.Count==65,"Missing query responses cannot permanently fill the query limit");
            var old=sent[0];var late=new FrameworkPacket{MessageType=TradeOperationQueryProtocol.Response,SessionId="query-session",SenderUuid=FrameworkProtocol.SystemSenderUuid,
                PayloadJson=FrameworkSerialization.SerializePayload(new TradeOperationQueryResponse{OperationId="query-0",Status="Committed",Success=true,Failure=FrameworkTradeFailureReason.None,TradeId="synthetic-trade",SnapshotVersion=1})};
            late.SetCorrelationId(old.GetCorrelationId());queries.Handle(late);
            Check(!journal.DurableOutcome(journal.Read("query-server","query-player").Single(r=>r.OperationId=="query-0")).HasValue,"Expired query bookkeeping cannot invent a trade result");
            queries.Query("query-0");late.SetCorrelationId(sent.Last().GetCorrelationId());queries.Handle(late);
            Check(journal.DurableOutcome(journal.Read("query-server","query-player").Single(r=>r.OperationId=="query-0"))==true,"Fresh authoritative lookup can settle after an earlier query timed out");
        }
    }
    private static void Operations()
    {
        var service=DispatchProxy.Create<IClientTradeService,TradeProxy>();var proxy=(TradeProxy)(object)service;
        using(var operations=new TradeOfferOperations(service,action=>action(),null,()=>true))
        {
            operations.Start();int restored=0,unknown=0,committed=0;
            var token=operations.Begin("synthetic-trade",false,()=>TradeOfferSettlement.Done,()=>{restored++;return TradeOfferSettlement.Done;},()=>unknown++,()=>{restored++;return TradeOfferSettlement.Done;});
            Check(!operations.Submit(token,_=>throw new TradeRequestNotSentException(new IOException("synthetic storage refusal"))),"Pre-send failure reports false");
            Check(restored==1&&unknown==0&&operations.GetPending().Count==0,"Known NotSent restores local custody without Unknown");
            token=operations.Begin("synthetic-trade",false,()=>{committed++;return TradeOfferSettlement.Done;},()=>TradeOfferSettlement.Done,()=>unknown++);
            operations.Submit(token,_=>{});proxy.Raise("OnTradeCompleted",new TradeCompletionEventArgs("synthetic-trade",true,"other",Array.Empty<TradeItemSnapshot>()));
            proxy.Raise("OnTradeUpdateSuccess",new TradeUpdateEventArgs(new ClientTradeSnapshot("synthetic-trade",default(UserManagement.ImmutableUser)),token));
            Check(committed==1&&operations.GetPending().Count==0,"Exact late offer result settles after terminal event");
            token=operations.Begin("synthetic-trade",false,()=>TradeOfferSettlement.Done,()=>TradeOfferSettlement.Done,()=>unknown++);
            Check(!operations.Submit(token,_=>throw new IOException("transport may already have sent"))&&operations.GetPending().Count==1,"Uncertain actual send is not refunded");
        }
    }
    private static void Attachment(IClientDatabaseService service)
    {
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(BuiltInInventoryClientExtension);var active=type.GetField("active",fields);
        object oldActive=active.GetValue(null);var oldGame=Verse.Current.Game;
        object module=Activator.CreateInstance(type);
        try
        {
            var host=new ExtensionHostContext();host.AddService<IClientDatabaseService>(service);
            type.GetField("host",fields).SetValue(module,host);type.GetField("mainThreadId",fields).SetValue(module,System.Threading.Thread.CurrentThread.ManagedThreadId);
            active.SetValue(null,module);
            Func<string,string,Phinix.InventoryExtension.Client.InventoryGameComponent> component=(id,snapshot)=>{
                var game=(Verse.Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Game));game.components=new List<Verse.GameComponent>();Verse.Current.Game=game;
                var result=new Phinix.InventoryExtension.Client.InventoryGameComponent(game);game.components.Add(result);
                var c=result.GetType();c.GetField("saveId",fields).SetValue(result,id);c.GetField("snapshotJson",fields).SetValue(result,snapshot);c.GetProperty("Initialized",fields).SetValue(result,true);return result;
            };
            var first=component("attachment-a",null);var attach=type.GetMethod("Attach",fields);attach.Invoke(module,new object[]{first});
            var api=(IInventoryReadApi)module;
            Check(api.GetStatus().CanWrite,"Actual module attaches an unsaved game to shared database");
            var scope=((IInventoryReceiveScopeApi)module).CaptureReceiveScope();string identity=api.GetStatus().SaveIdentity;
            Check(first.GetType().GetField("snapshotJson",fields).GetValue(first)==null&&first.GetType().GetProperty("StorageVersion",fields).GetValue(first).Equals(1),"Actual component records upgrade without writing balance snapshot");
            var path=type.Assembly.GetType("Phinix.InventoryExtension.Client.InventorySaveIdentityPatch").GetProperty("CurrentPath",fields);
            var oldPath=path.GetValue(null);path.SetValue(null,Path.Combine(Path.GetTempPath(),"synthetic-save-as.rws"));
            try{attach.Invoke(module,new object[]{first});Check(scope.IsCurrent(),"Save As does not invalidate a shared inventory receive scope");}finally{path.SetValue(null,oldPath);}
            var second=component("attachment-b",null);attach.Invoke(module,new object[]{second});
            Check(api.GetStatus().CanWrite&&api.GetStatus().SaveIdentity==identity&&!scope.IsCurrent(),"Game change retains database balance identity but invalidates physical scope");
            var copied=component("attachment-c",null);copied.GetType().GetField("storageVersion",fields).SetValue(copied,1);copied.GetType().GetField("databaseIdentity",fields).SetValue(copied,Guid.NewGuid().ToString("N"));
            attach.Invoke(module,new object[]{copied});Check(api.GetStatus().CanWrite&&api.GetStatus().SaveIdentity==identity,"Copied world's marker cannot replace or freeze the local shared inventory");

            (type.GetField("store",fields).GetValue(module) as IDisposable)?.Dispose();type.GetField("store",fields).SetValue(module,null);
            var failures=new FaultService(service);var failureHost=new ExtensionHostContext();failureHost.AddService<IClientDatabaseService>(failures);type.GetField("host",fields).SetValue(module,failureHost);
            string snapshot=FrameworkSerialization.SerializePayload(OneEntry("component-migration-entry",3));
            var legacy=component("attachment-legacy",snapshot);failures.Next="rollback";attach.Invoke(module,new object[]{legacy});
            Check(!api.GetStatus().CanWrite&&legacy.GetType().GetField("snapshotJson",fields).GetValue(legacy).Equals(snapshot)&&legacy.GetType().GetProperty("StorageVersion",fields).GetValue(legacy).Equals(0),"Actual component preserves old inventory and upgrade version when import commit fails");
            attach.Invoke(module,new object[]{legacy});
            Check(api.GetStatus().CanWrite&&legacy.GetType().GetField("snapshotJson",fields).GetValue(legacy)==null&&legacy.GetType().GetProperty("StorageVersion",fields).GetValue(legacy).Equals(1),"Actual component clears legacy fields only after successful import retry");
            var duplicate=component("attachment-legacy",snapshot);attach.Invoke(module,new object[]{duplicate});
            Check(api.GetSnapshot().Single(entry=>entry.EntryId=="component-migration-entry").Quantity==3,"Reopening an uncleaned legacy component cannot import its balance twice");
            var malformed=component("attachment-malformed","not valid JSON");attach.Invoke(module,new object[]{malformed});
            Check(api.GetStatus().CanWrite&&malformed.GetType().GetField("snapshotJson",fields).GetValue(malformed).Equals("not valid JSON"),"Malformed legacy data is retained without freezing existing shared inventory");
        }
        finally
        {
            (type.GetField("store",fields).GetValue(module) as IDisposable)?.Dispose();active.SetValue(null,oldActive);Verse.Current.Game=oldGame;
        }
    }
}
