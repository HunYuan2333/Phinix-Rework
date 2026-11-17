using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using PhinixClient.Storage;
using Phinix.InventoryExtension;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Trade;
using PhinixClient.Framework;
using Utils.Framework;

internal static partial class Program
{
    private sealed class InventoryBoundary : IInventoryReadApi,IInventoryReservationApi
    {
        public event EventHandler InventoryChanged {add{} remove{}}
        public event EventHandler AvailabilityChanged {add{} remove{}}
        internal InventoryStatus Status=new InventoryStatus{Availability=InventoryAvailability.Ready,SaveIdentity=new string('a',64),JournalIdentity=Guid.NewGuid().ToString("N")};
        internal InventoryReservation Reservation;
        internal int Resolutions;
        public InventoryStatus GetStatus()=>Status;
        public InventoryCapabilities GetCapabilities()=>null;
        public IReadOnlyList<InventoryEntry> GetSnapshot()=>Array.Empty<InventoryEntry>();
        public IReadOnlyList<InventoryEntry> GetAvailableSnapshot()=>Array.Empty<InventoryEntry>();
        public IReadOnlyList<InventoryReservation> GetReservations()=>new[]{Reservation};
        public InventoryReservationResult TryReserve(InventoryReservationRequest request)=>throw new NotSupportedException();
        public InventoryMaterializationResult CreatePreview(string entryId)=>throw new NotSupportedException();
        public InventoryMaterializationResult MaterializeReservation(string id)=>throw new NotSupportedException();
        public InventoryReservationResolutionResult ResolveReservation(string id,InventoryReservationResolution resolution,string evidence)
        {
            Resolutions++;
            Reservation.State=resolution==InventoryReservationResolution.Commit?InventoryReservationState.Committed:InventoryReservationState.Restored;
            return new InventoryReservationResolutionResult{Succeeded=true};
        }
    }
    private sealed class UnusedTaskScope : IClientTaskScope
    {
        public IClientTaskHandle Submit(Action action,CancellationToken cancellation=default)=>throw new InvalidOperationException("This test directly invokes the main-thread reconciliation unit, never starts its timer.");
        public void Dispose(){}
    }
    private static void ReconciliationBoundaries(IClientDatabaseService service,string root)
    {
        var inventory=new InventoryBoundary();var logs=new List<string>();
        using(var journal=new TradeIntentJournal(Path.Combine(root,"settlement-intents"),service))
        {
            Action<string,bool> response=(id,success)=>{
                var packet=new FrameworkPacket{MessageType=FrameworkTradeProtocol.OfferUpdateResponseType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateResponse{Success=success,TradeId="synthetic-trade"})};
                packet.SetCorrelationId(id);journal.RecordResponse("settlement-server","settlement-player",packet);
            };
            Action<TradeIntentSettlement> run=coordinator=>typeof(TradeIntentSettlement).GetMethod("Run",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(coordinator,new object[]{null,null});
            var facade=DispatchProxy.Create<IClientTradeService,TradeProxy>();
            using(var live=new TradeOfferOperations(facade,work=>work(),null,()=>true))
            {
                live.Start();int commits=0;
                live.Settled=token=>journal.MarkSettled("settlement-server","settlement-player",token);
                string token=live.Begin("synthetic-trade",false,()=>{commits++;return TradeOfferSettlement.Done;},()=>throw new Exception("Known success must not refund"),()=>{});
                var intent=journal.Prepare("settlement-server","settlement-player",Offer(token),Array.Empty<FrameworkPacket>());
                journal.BeginAttempt(intent);live.Submit(token,_=>{});response(token,true);
                inventory.Status.Availability=InventoryAvailability.ReadOnlyFault;
                using(var coordinator=new TradeIntentSettlement(journal,inventory,inventory,new UnusedTaskScope(),()=>"settlement-server",()=>"settlement-player",live,logs.Add))run(coordinator);
                Check(commits==1&&live.GetPending().Count==0&&inventory.Resolutions==0,"Durable success resolves live operation even while unrelated inventory is read-only");
                Check(journal.IsSettled(intent)&&logs.Count==0,"Live settlement archives evidence without spurious missing-binding reconciliation errors");
            }

            inventory.Status.Availability=InventoryAvailability.Ready;
            inventory.Reservation=new InventoryReservation{ReservationId="background-reservation",OperationId="background-operation",OwnerExtensionId="builtin.trade",Purpose="trade.offer",State=InventoryReservationState.Uncertain,Items=Array.Empty<InventoryReservedItem>()};
            var background=journal.Prepare("settlement-server","settlement-player",Offer("background-result"),Array.Empty<FrameworkPacket>());
            journal.BindReservation(background,inventory.Status,inventory.Reservation);journal.BeginAttempt(background);response("background-result",false);
            using(var coordinator=new TradeIntentSettlement(journal,inventory,inventory,new UnusedTaskScope(),()=>"settlement-server",()=>"settlement-player",null,logs.Add))
            {
                inventory.Status.SaveIdentity=new string('b',64);run(coordinator);
                Check(inventory.Resolutions==0&&!journal.IsSettled(background),"Durable result cannot mutate a reservation belonging to a different inventory identity");
                inventory.Status.SaveIdentity=new string('a',64);run(coordinator);
                Check(inventory.Resolutions==1&&inventory.Reservation.State==InventoryReservationState.Restored&&journal.IsSettled(background),"Matching authoritative rejection restores the bound reservation and compacts its evidence");
                run(coordinator);Check(inventory.Resolutions==1,"Repeating reconciliation does not restore the reservation twice");
            }
            var unknown=journal.Prepare("settlement-server","settlement-player",Offer("unknown-result"),Array.Empty<FrameworkPacket>());
            journal.BeginAttempt(unknown);
            using(var coordinator=new TradeIntentSettlement(journal,inventory,inventory,new UnusedTaskScope(),()=>"settlement-server",()=>"settlement-player",null,logs.Add))run(coordinator);
            Check(inventory.Resolutions==1&&!journal.IsSettled(unknown),"Absence of an authoritative result is never interpreted as rejection or refund");
        }
    }
}
