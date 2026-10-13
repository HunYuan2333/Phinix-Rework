using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Phinix.InventoryExtension;

namespace Phinix.TradeExtension.Client
{
    // Read existing durable records, never materialize or resolve a reservation.
    internal sealed class TradeInventoryRecovery : ITradeInventoryRecoveryApi, IDisposable
    {
        private readonly Func<bool> mainThread;
        private readonly Func<object> game;
        private IInventoryReadApi read;
        private IInventoryReservationApi reservations;
        private object observedGame;
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
        public long Revision
        {
            get {Main();var current=game();if(!ReferenceEquals(current,observedGame)){observedGame=current;Interlocked.Increment(ref revision);}return Interlocked.Read(ref revision);}
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
            snapshot=rows.AsReadOnly();shown=ticket;return snapshot;
        }
        public void Dispose()
        {
            var old=read;read=null;reservations=null;snapshot=Array.Empty<TradeInventoryRecoveryRecord>();shown=-1;Interlocked.Increment(ref revision);
            if(old!=null){var errors=new List<Exception>();TradeLifetimeCleanup.Try(()=>old.InventoryChanged-=Changed,errors);TradeLifetimeCleanup.Try(()=>old.AvailabilityChanged-=Changed,errors);TradeLifetimeCleanup.ThrowIfFailed(errors);}
        }
    }
}
