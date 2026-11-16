using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using PhinixClient.Storage;
using Phinix.InventoryExtension;
using Phinix.InventoryExtension.Client;
using Phinix.TradeExtension.Client;
using PhinixClient.Trade;
using Utils.Framework;

internal static partial class Program
{
    // Faults are injected at the real transaction boundary, not by replacing the ledger.
    private sealed class FaultService : IClientDatabaseService
    {
        internal readonly IClientDatabaseService Inner;
        internal string Next;
        internal FaultService(IClientDatabaseService inner) { Inner=inner; }
        public IClientDatabase Open(string owner,string name,DatabaseSchema schema)
            => new FaultDatabase(this,Inner.Open(owner,name,schema));
        private sealed class FaultDatabase : IClientDatabase
        {
            private readonly FaultService service;
            private readonly IClientDatabase inner;
            internal FaultDatabase(FaultService service,IClientDatabase inner){this.service=service;this.inner=inner;}
            public string Identity=>inner.Identity;
            public T Read<T>(Func<DbConnection,DbTransaction,T> read)=>inner.Read(read);
            public T Write<T>(Func<DbConnection,DbTransaction,T> work)
            {
                string fault=service.Next;service.Next=null;
                T result=inner.Write((c,t)=>{
                    T value=work(c,t);
                    if(fault=="rollback")throw new ClientDatabaseException(DatabaseFailure.Unavailable,"Synthetic failure before commit");
                    return value;
                });
                if(fault=="unknown")throw new ClientDatabaseException(DatabaseFailure.OutcomeUnknown,"Synthetic missing commit acknowledgement");
                return result;
            }
            public void Backup(string destination)=>inner.Backup(destination);
            public void Dispose()=>inner.Dispose();
        }
    }

    private static InventoryState OneEntry(string id,long quantity=10)
    {
        var state=new InventoryState();
        state.Entries.Add(new InventoryEntry{EntryId=id,CodecId="synthetic",CodecVersion=1,Quantity=quantity,Label=id,Source="test",Payload=new byte[]{1,2,3}});
        return state;
    }
    private static void FailureScenarios(string root)
    {
        using(var service=new ClientDatabaseService(owner=>Path.Combine(root,"fault-tests",owner)))
        {
            InventoryFailureBoundaries(service);
            MigrationFailureBoundaries(service);
            CorruptTradeEvidence(service,root);
            ReconciliationBoundaries(service,root);
            TerminalCompletionBoundaries(service,root);
            ErrorDiagnostics(service,root);
        }
        OfferResultBoundaries();OfferDiagnostics();
    }
    private static void InventoryFailureBoundaries(IClientDatabaseService service)
    {
        var faults=new FaultService(service);
        var reports=new System.Collections.Generic.List<Tuple<string,Exception>>();
        using(var store=new InventoryDatabaseStore(faults,(operation,error)=>reports.Add(Tuple.Create(operation,error))))
        {
            store.Load("fault-game");store.Import("fault-game",OneEntry("fault-entry"),out _);
            var old=store.Load("fault-game");var next=InventoryLedger.Clone(old);next.Sequence++;next.Entries[0].Quantity++;
            faults.Next="rollback";
            Check(!store.TryAppend(next)&&!store.Faulted,"A pre-commit storage failure does not permanently freeze inventory");
            Check(reports.Any(r=>r.Item1=="InventoryAppendUnavailable" && r.Item2 is ClientDatabaseException) && !store.LastError.Contains("Synthetic"),"Inventory failed append retains original typed diagnostics and only exposes a safe status");
            var loaded=store.Load("fault-game");
            Check(loaded.Sequence==old.Sequence&&loaded.Entries[0].Quantity==old.Entries[0].Quantity,"Failed transaction publishes neither new revision nor balance");
            Check(store.TryAppend(next),"Explicit retry after known rollback commits once");
            next=InventoryLedger.Clone(next);next.Sequence++;next.Entries[0].Quantity++;
            faults.Next="unknown";
            Check(!store.TryAppend(next)&&store.Faulted,"Unconfirmed commit cannot be reported as success");
            Check(reports.Any(r=>r.Item1=="InventoryAppendOutcomeUnknown") && store.LastError.Contains("unknown"),"Inventory unknown result is recorded as unknown rather than claiming rollback");
            Check(!store.TryAppend(next),"Unconfirmed commit blocks blind replay until reload");
            loaded=store.Load("fault-game");
            Check(!store.Faulted&&loaded.Sequence==next.Sequence&&loaded.Entries[0].Quantity==next.Entries[0].Quantity,"Reload observes a committed but unacknowledged operation exactly once");
            next=InventoryLedger.Clone(loaded);next.Sequence++;next.PendingExtraction=new InventoryPendingExtraction{EntryId="fault-entry",Quantity=1};
            Check(store.TryAppend(next),"Interrupted extraction diagnostic fixture reserves once");
            faults.Next="rollback";IOException abandoned=null;
            try{store.AbandonInterruptedExtraction();}catch(IOException error){abandoned=error;}
            Check(abandoned?.InnerException is ClientDatabaseException && reports.Any(r=>ReferenceEquals(r.Item2,abandoned.InnerException)),"Interrupted extraction failure wrapper preserves the same original database cause already reported");
            Check(store.AbandonInterruptedExtraction(),"Explicit abandonment after a known rollback clears only the reserved test extraction");
        }
    }
    private static void MigrationFailureBoundaries(IClientDatabaseService service)
    {
        using(var store=new InventoryDatabaseStore(service))
        using(var database=service.Open("builtin.inventory","inventory",InventoryDatabaseStore.Schema))
        {
            store.Load("migration-fault");
            long before=database.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT revision FROM inventory_meta")));
            database.Write((c,t)=>DatabaseSql.Execute(c,t,"CREATE TRIGGER reject_import BEFORE INSERT ON inventory_imports BEGIN SELECT RAISE(ABORT,'synthetic import receipt refusal'); END;"));
            var legacy=OneEntry("migration-fault-entry");
            Throws<ClientDatabaseException>(()=>store.Import("migration-fault",legacy,out _),"Failure writing import receipt rolls back the import");
            Check(database.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM inventory_entries WHERE id='migration-fault-entry'")))==0 &&
                database.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT revision FROM inventory_meta")))==before,"Import failure cannot leave assets without their source receipt");
            database.Write((c,t)=>DatabaseSql.Execute(c,t,"DROP TRIGGER reject_import"));
            Check(store.Import("migration-fault",legacy,out _)&&store.Import("migration-fault",legacy,out _),"Failed migration can retry and then safely re-enter");
            Check(store.Load("migration-fault").Entries.Count(e=>e.EntryId=="migration-fault-entry")==1,"Retried migration credits one copy");

            var pending=OneEntry("old-pending-entry");pending.PendingExtraction=new InventoryPendingExtraction{EntryId="old-pending-entry",Quantity=2};
            Check(store.Import("old-pending",pending,out var warning)&&warning=="Phinix_inventory_interruptedAbandoned","Legacy pending extraction is explicitly abandoned with warning");
            var loaded=store.Load("another-game");
            Check(loaded.PendingExtraction==null&&loaded.Entries.Single(e=>e.EntryId=="old-pending-entry").Quantity==8,"Legacy abandonment consumes only its related quantity");
        }
    }
    private static void CorruptTradeEvidence(IClientDatabaseService service,string root)
    {
        int warnings=0;
        using(var journal=new TradeIntentJournal(Path.Combine(root,"corrupt-intents"),service,_=>warnings++))
        {
            var damaged=journal.Prepare("fault-server","fault-player",Offer("damaged-evidence"),Array.Empty<FrameworkPacket>());
            journal.BeginAttempt(damaged);
            journal.Prepare("fault-server","fault-player",Offer("healthy-evidence"),Array.Empty<FrameworkPacket>());
            using(var records=new TradeSqliteRecords(service,Path.Combine(root,"corrupt-intents"),"intents",null))
            {
                var schema=typeof(TradeSqliteRecords).GetField("Schema",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null) as DatabaseSchema;
                using(var database=service.Open("builtin.trade","trade",schema))
                    database.Write((c,t)=>DatabaseSql.Execute(c,t,"UPDATE trade_records SET body=$body WHERE bucket='intents' AND kind='.attempt'","$body",new byte[]{0}));
            }
            var remaining=journal.Read("fault-server","fault-player");
            Check(warnings==1&&remaining.Length==1&&remaining[0].OperationId=="healthy-evidence","Corrupt auxiliary evidence isolates one operation and preserves healthy trading");
            journal.Read("fault-server","fault-player");Check(warnings==1,"Quarantined evidence does not warn on every read");
            journal.Prepare("fault-server","fault-player",Offer("after-damage"),Array.Empty<FrameworkPacket>());
            Check(journal.Read("fault-server","fault-player").Length==2,"New offers are accepted after isolating a damaged historical operation");
        }
    }
    private static void OfferResultBoundaries()
    {
        var service=System.Reflection.DispatchProxy.Create<IClientTradeService,TradeProxy>();var proxy=(TradeProxy)(object)service;
        using(var operations=new TradeOfferOperations(service,work=>work(),null,()=>true))
        {
            operations.Start();int committed=0,restored=0,attempts=0;
            var token=operations.Begin("synthetic-trade",false,()=>{committed++;return TradeOfferSettlement.Done;},()=>{restored++;return TradeOfferSettlement.Done;},()=>{});
            operations.Submit(token,_=>{});
            Action<string> success=id=>proxy.Raise("OnTradeUpdateSuccess",new TradeUpdateEventArgs(new ClientTradeSnapshot("synthetic-trade",default(UserManagement.ImmutableUser)),id));
            success("unrelated-token");Check(committed==0&&operations.GetPending().Count==1,"Unrelated correlation cannot settle an offer");
            success(token);success(token);Check(committed==1&&restored==0,"Duplicate matching responses cannot repeat a successful local settlement");
            token=operations.Begin("synthetic-trade",false,()=>{attempts++;return attempts==1?TradeOfferSettlement.Blocked:TradeOfferSettlement.Done;},()=>{restored++;return TradeOfferSettlement.Done;},()=>{});
            operations.Submit(token,_=>{});success(token);
            Check(operations.GetPending().Count==1&&restored==0,"Known success with blocked local commit is not refunded");
            Check(operations.RetryLocalSettlement(token)&&attempts==2,"Known success can explicitly retry only its local settlement");
            token=operations.Begin("synthetic-trade",false,()=>{committed++;return TradeOfferSettlement.Done;},()=>{restored++;return TradeOfferSettlement.Done;},()=>{});
            operations.Submit(token,_=>{success(token);throw new IOException("synthetic throw after synchronous response");});
            Check(committed==2&&restored==0&&operations.GetPending().Count==0,"Transport exception after a known response cannot reverse that result");
        }
    }

    private static void TerminalCompletionBoundaries(IClientDatabaseService service,string root)
    {
        using(var inbox=new TradeCompletionInbox(Path.Combine(root,"terminal-inbox"),service))
        {
            inbox.Record("terminal-server","player","terminal-trade",true,"other",Array.Empty<FrameworkItemPayload>());
            inbox.CompleteLocally("terminal-server","player","terminal-trade",true);
            Throws<InvalidDataException>(()=>inbox.Record("terminal-server","player","terminal-trade",false,"other",Array.Empty<FrameworkItemPayload>()),"Compacted success cannot later be accepted as a fresh cancellation");
            inbox.Record("terminal-server","player","cancelled-trade",false,"other",Array.Empty<FrameworkItemPayload>());
            inbox.CompleteLocally("terminal-server","player","cancelled-trade",false);
            Throws<InvalidDataException>(()=>inbox.Record("terminal-server","player","cancelled-trade",true,"other",Array.Empty<FrameworkItemPayload>()),"Compacted cancellation cannot later become a fresh success");

            inbox.Record("terminal-server","player","modern-trade",true,"other",Array.Empty<FrameworkItemPayload>());
            var body=new Phinix.TradeExtension.FrameworkTradeCompletionEvent{TradeId="modern-trade",OtherPartyUuid="other",Items=new System.Collections.Generic.List<FrameworkItemPayload>()};
            string deliveryId=Guid.NewGuid().ToString("N");
            var request=new TradeDeliveryReceiptRequest{DeliveryId=deliveryId,TradeId="modern-trade",Kind="InboxStored",PayloadFingerprint=TradeDeliveryProtocol.Fingerprint(deliveryId,"player",body)};
            inbox.BindDelivery("terminal-server","player",request);inbox.CompleteLocally("terminal-server","player","modern-trade",true);
            Check(inbox.ReadAll().Length==1,"Negotiated delivery keeps payload pending server confirmation");
            inbox.RecordServerObservation("terminal-server","player",request,new TradeDeliveryReceiptResponse{DeliveryId=deliveryId,Accepted=true,State="OutcomeUnknown"});
            Check(inbox.ReadAll().Length==1,"Uncertain server observation cannot compact pending delivery evidence");
            inbox.RecordServerObservation("terminal-server","player",request,new TradeDeliveryReceiptResponse{DeliveryId=deliveryId,Accepted=true,State="Confirmed"});
            Check(inbox.ReadAll().Length==0&&!inbox.Record("terminal-server","player","modern-trade",true,"other",Array.Empty<FrameworkItemPayload>()),"Confirmed negotiated delivery compacts payload while retaining deduplication");
        }
    }
}
