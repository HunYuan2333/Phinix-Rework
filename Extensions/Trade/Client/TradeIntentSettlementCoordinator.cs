using System;
using System.Linq;
using System.IO;
using Phinix.InventoryExtension;
using PhinixClient.Framework;
using Utils.Framework;
namespace Phinix.TradeExtension.Client
{
    internal sealed class TradeIntentSettlement : IDisposable
    {
        private readonly TradeIntentJournal journal;private readonly IInventoryReadApi read;private readonly IInventoryReservationApi reservations;
        private readonly TradeRecoveryPump pump;private readonly Func<string> endpoint,principal;private readonly TradeOfferOperations live;private readonly Action<string> log;
        private bool active=true;
        internal TradeIntentSettlement(TradeIntentJournal journal,IInventoryReadApi read,IInventoryReservationApi reservations,IClientTaskScope tasks,
            Func<string> endpoint,Func<string> principal,TradeOfferOperations live,Action<string> log)
        {this.journal=journal;this.read=read;this.reservations=reservations;this.endpoint=endpoint;this.principal=principal;this.live=live;this.log=log;
            pump=new TradeRecoveryPump(tasks,()=>Run(null),log);
            try {journal.Changed+=Schedule;read.AvailabilityChanged+=Changed;Schedule();}catch{Dispose();throw;}}
        private void Changed(object sender,EventArgs args)=>Schedule();
        internal void Start()=>pump.Start();
        private void Schedule(){if(active)pump.Request();}
        private void Run(string after,TradeIntentRecord[] records=null)
        {
            var status=read.GetStatus();if(!status.CanWrite||endpoint()==null||principal()==null)return;
            records=records??journal.Read(endpoint(),principal()).ToArray();
            var page=records.Where(i=>after==null||StringComparer.Ordinal.Compare(i.OperationId,after)>0).OrderBy(i=>i.OperationId,StringComparer.Ordinal).Take(8).ToArray();
            foreach(var intent in page)
            {
                if(intent.Endpoint!=endpoint()||intent.Principal!=principal())continue;
                try
                {
                    var result=journal.DurableResult(intent);if(result?.Status!="Committed"||!result.Success.HasValue)continue;
                    string trade=TradeIntentJournal.TradeId(intent);live?.ApplyDurableResult(trade,intent.CorrelationId,result.Success.Value);
                    var b=journal.ReservationBinding(intent);if(b==null||b.SaveIdentity!=status.SaveIdentity||b.JournalIdentity!=status.JournalIdentity)continue;
                    var reservation=reservations.GetReservations().SingleOrDefault(r=>r.ReservationId==b.ReservationId);
                    if(reservation==null||reservation.OwnerExtensionId!="builtin.trade"||reservation.Purpose!="trade.offer"||reservation.OperationId!=b.ReservationOperation ||
                        FrameworkSerialization.SerializePayload(reservation.Items)!=b.ItemsJson)continue;
                    var target=result.Success.Value?InventoryReservationState.Committed:InventoryReservationState.Restored;
                    if(reservation.State==target)continue;
                    if(reservation.State!=InventoryReservationState.Reserved&&reservation.State!=InventoryReservationState.Uncertain)throw new InvalidDataException("Reservation outcome conflicts with authoritative result.");
                    var resolved=reservations.ResolveReservation(reservation.ReservationId,result.Success.Value?InventoryReservationResolution.Commit:InventoryReservationResolution.Restore,
                        "trade-durable-result:"+intent.OperationId+":"+intent.RequestFingerprint);
                    if(!resolved.Succeeded)log?.Invoke("Reservation settlement remains pending.");
                }
                catch(Exception error){log?.Invoke("Reservation reconciliation retained: "+error.GetType().Name);}
            }
            if(page.Length==8){string cursor=page[page.Length-1].OperationId;pump.Continue(()=>Run(cursor,records));}
        }
        public void Dispose(){if(!active)return;active=false;var failures=new System.Collections.Generic.List<Exception>();
            TradeLifetimeCleanup.Try(()=>pump.Dispose(),failures);
            TradeLifetimeCleanup.Try(()=>journal.Changed-=Schedule,failures);TradeLifetimeCleanup.Try(()=>read.AvailabilityChanged-=Changed,failures);TradeLifetimeCleanup.ThrowIfFailed(failures);}
    }
}
