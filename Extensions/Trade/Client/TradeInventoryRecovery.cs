using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Phinix.InventoryExtension;

namespace Phinix.TradeExtension.Client
{
    // Read existing durable records, never materialize or resolve a reservation.
    internal sealed class TradeInventoryRecovery : ITradeInventoryRecoveryApi, ITradeOperationQueryApi, IDisposable
    {
        private readonly Func<bool> mainThread;
        private readonly Func<object> game;
        private IInventoryReadApi read;
        private IInventoryReservationApi reservations;
        private object observedGame;
        private TradeCompletionInbox inbox;
        private Func<string> recipient,endpoint;
        private string observedRecipient,observedEndpoint,observedSave;
        private long observedInbox=-1;
        private TradeIntentJournal intents;
        private Func<bool> canQuery;
        private Action<string> query;
        private long observedIntents=-1;
        private long revision,shown=-1;
        private IReadOnlyList<TradeInventoryRecoveryRecord> snapshot=Array.Empty<TradeInventoryRecoveryRecord>();
        internal TradeInventoryRecovery(Func<bool> mainThread,Func<object> game){this.mainThread=mainThread;this.game=game;}
        private void Main(){if(!mainThread())throw new InvalidOperationException("Trade inventory recovery requires main thread.");}
        internal void Start(IInventoryReadApi read,IInventoryReservationApi reservations)
        {
            // Activation only borrows ports/subscribes; game facts are read through guarded Capture.
            Dispose();this.read=read;this.reservations=reservations;
            try {read.InventoryChanged+=Changed;read.AvailabilityChanged+=Changed;Interlocked.Increment(ref revision);}
            catch {Dispose();throw;}
        }
        private void Changed(object sender,EventArgs args){Interlocked.Increment(ref revision);}
        internal void ConfigureCompletions(TradeCompletionInbox inbox,Func<string> recipient,Func<string> endpoint)
        {this.inbox=inbox;this.recipient=recipient;this.endpoint=endpoint;Interlocked.Increment(ref revision);}
        internal void ConfigureIntents(TradeIntentJournal journal,Func<bool> canQuery,Action<string> query)
        {intents=journal;this.canQuery=canQuery;this.query=query;Interlocked.Increment(ref revision);}
        public bool CanQueryOperation {get {Main();return canQuery?.Invoke()==true;}}
        public void QueryOperation(string operationId) {Main();if(!CanQueryOperation)throw new InvalidOperationException("TradeOperationQueryUnavailable");query(operationId);}
        public long Revision
        {
            get
            {
                Main();var current=game();string user=recipient?.Invoke(),server=endpoint?.Invoke(),save=read?.GetStatus()?.SaveIdentity;long ir=inbox?.Revision??-1;
                long ij=intents?.Revision??-1;
                if(!ReferenceEquals(current,observedGame)||user!=observedRecipient||server!=observedEndpoint||save!=observedSave||ir!=observedInbox||ij!=observedIntents)
                {observedGame=current;observedRecipient=user;observedEndpoint=server;observedSave=save;observedInbox=ir;observedIntents=ij;Interlocked.Increment(ref revision);}
                return Interlocked.Read(ref revision);
            }
        }
        public IReadOnlyList<TradeInventoryRecoveryRecord> Capture()
        {
            long ticket=Revision;if(shown==ticket)return snapshot;
            var rows=new List<TradeInventoryRecoveryRecord>();
            var status=read?.GetStatus();
            if(observedGame!=null&&reservations!=null&&status!=null&&status.Availability!=InventoryAvailability.Inactive&&status.Availability!=InventoryAvailability.UnsavedGame)
                foreach(var item in reservations.GetReservations())
                {
                    if(item==null||item.OwnerExtensionId!="builtin.trade"||item.Purpose!="trade.offer"||
                        (item.State!=InventoryReservationState.Reserved&&item.State!=InventoryReservationState.Uncertain))continue;
                    string tradeId=null,token=null;
                    // Reader for the existing operation key, not a protocol mode or result proof.
                    string id=item.OperationId??"";const string prefix="trade-offer:";int separator=id.LastIndexOf(':');
                    if(id.StartsWith(prefix,StringComparison.Ordinal)&&separator>prefix.Length&&Guid.TryParse(id.Substring(separator+1),out _))
                    {tradeId=id.Substring(prefix.Length,separator-prefix.Length);token=id.Substring(separator+1);}
                    rows.Add(new TradeInventoryRecoveryRecord(item.ReservationId,item.OperationId,item.State.ToString(),item.Evidence,tradeId,token,
                        (item.Items??Array.Empty<InventoryReservedItem>()).Where(line=>line!=null).Select(line=>new TradeInventoryRecoveryLine(line.EntryId,line.Quantity))));
                }
            if(inbox!=null && !string.IsNullOrEmpty(observedRecipient) && !string.IsNullOrEmpty(observedEndpoint))
                foreach(var receipt in inbox.ReadAll().Where(r=>r.Recipient==observedRecipient && r.Endpoint==observedEndpoint))
                    rows.Add(new TradeInventoryRecoveryRecord("completion:"+receipt.Fingerprint,null,
                        inbox.DeliveryState(receipt.Endpoint,receipt.Recipient,receipt.TradeId,receipt.Completed),inbox.DeliveryObservation(receipt),receipt.TradeId,null,
                        Array.Empty<TradeInventoryRecoveryLine>(),"Completion",
                        inbox.SaveBinding(receipt.Endpoint,receipt.Recipient,receipt.TradeId,receipt.Completed,observedGame==null?null:observedSave)));
            if(intents!=null && !string.IsNullOrEmpty(observedRecipient) && !string.IsNullOrEmpty(observedEndpoint))
                foreach(var intent in intents.Read(observedEndpoint,observedRecipient))
                    rows.Add(new TradeInventoryRecoveryRecord("intent:"+intent.OperationId,intent.OperationId,intents.State(intent),intents.Observation(intent),
                        TradeIntentJournal.TradeId(intent),intent.CorrelationId,Array.Empty<TradeInventoryRecoveryLine>(),"Intent",null));
            snapshot=rows.AsReadOnly();shown=ticket;return snapshot;
        }
        public void Dispose()
        {
            var old=read;read=null;reservations=null;snapshot=Array.Empty<TradeInventoryRecoveryRecord>();shown=-1;Interlocked.Increment(ref revision);
            inbox=null;recipient=null;endpoint=null;
            intents=null;canQuery=null;query=null;
            if(old!=null){var errors=new List<Exception>();TradeLifetimeCleanup.Try(()=>old.InventoryChanged-=Changed,errors);TradeLifetimeCleanup.Try(()=>old.AvailabilityChanged-=Changed,errors);TradeLifetimeCleanup.ThrowIfFailed(errors);}
        }
    }
}
