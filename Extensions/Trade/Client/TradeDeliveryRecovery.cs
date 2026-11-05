using System;
using System.Linq;
using Phinix.InventoryExtension;
using PhinixClient.Framework;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    // Recovery never re-materializes Things. Only a frozen, save+journal-bound idempotent inventory deposit may repeat.
    internal sealed class TradeDeliveryRecovery : IDisposable
    {
        private readonly TradeCompletionInbox inbox;
        private readonly IInventoryReadApi read;
        private readonly IInventoryDepositApi deposit;
        private readonly IInventorySavedReceiptApi saved;
        private readonly TradeRecoveryPump pump;
        private readonly TradeDeliveryReceiptClient receipts;
        private readonly Func<string> endpoint,principal;
        private readonly Action<string> log;
        private bool active=true;
        internal TradeDeliveryRecovery(TradeCompletionInbox inbox,IInventoryReadApi read,IInventoryDepositApi deposit,IInventorySavedReceiptApi saved,
            IClientTaskScope tasks,TradeDeliveryReceiptClient receipts,Func<string> endpoint,Func<string> principal,Action<string> log)
        {
            this.inbox=inbox;this.read=read;this.deposit=deposit;this.saved=saved;this.receipts=receipts;this.endpoint=endpoint;this.principal=principal;this.log=log;
            pump=new TradeRecoveryPump(tasks,()=>Recover(null),log);
            try {read.AvailabilityChanged+=Changed;read.InventoryChanged+=Changed;if(saved!=null)saved.SavedCheckpoint+=Changed;Schedule();}
            catch {Dispose();throw;}
        }
        internal void RequestRecovery()=>Schedule();
        internal void Start()=>pump.Start();
        private void Changed(object sender,EventArgs args)=>Schedule();
        private void Schedule()
        {if(active)pump.Request();}
        private void Recover(string after,TradeCompletionReceipt[] records=null)
        {
            var status=read.GetStatus();string server=endpoint(),owner=principal();if(!status.CanWrite||status.SaveIdentity==null||status.JournalIdentity==null||server==null||owner==null)return;
            records=records??inbox.ReadAll();
            var page=records.Where(r=>r.Endpoint==server&&r.Recipient==owner&&(after==null||StringComparer.Ordinal.Compare(r.Fingerprint,after)>0)).OrderBy(r=>r.Fingerprint,StringComparer.Ordinal).Take(8).ToArray();
            foreach(var r in page)
            {
                try
                {
                    if(inbox.SaveBinding(server,owner,r.TradeId,r.Completed,status.SaveIdentity)!="Current")continue;
                    var plan=inbox.InventoryPlan(r);if(plan==null||plan.JournalIdentity!=status.JournalIdentity||plan.SaveIdentity!=status.SaveIdentity)continue;
                    var request=FrameworkSerialization.DeserializePayload<InventoryDeposit>(plan.DepositJson);var result=deposit.CheckDeposit(request);
                    string state=inbox.DeliveryState(server,owner,r.TradeId,r.Completed);
                    if(state=="Received")
                    {if(!inbox.TryBeginDelivery(server,owner,r.TradeId,r.Completed,true))continue;state="Attempted";}
                    // Once a successful return/checkpoint exists, absence is divergence, never permission to create assets again.
                    if(result.Status==InventoryDepositStatus.NotCommitted && state=="Attempted")result=deposit.TryDeposit(request);
                    if(!result.Succeeded)continue;
                    if(state=="Attempted")inbox.RecordDeliveryReturn(server,owner,r.TradeId,r.Completed,true,true);
                    var original=inbox.DeliveryReceipt(r);if(original==null)continue;
                    if(original.Kind=="SavedInventory"){receipts.Send(original);continue;}
                    var checkpoint=saved?.ReadSavedDepositReceipt(request);if(checkpoint==null||checkpoint.SaveIdentity!=plan.SaveIdentity||checkpoint.JournalIdentity!=plan.JournalIdentity)continue;
                    var reply=new TradeDeliveryReceiptRequest {DeliveryId=original.DeliveryId,TradeId=r.TradeId,Cancelled=!r.Completed,PayloadFingerprint=original.PayloadFingerprint,
                        Kind="SavedInventory",SaveIdentity=checkpoint.SaveIdentity,CheckpointFingerprint=checkpoint.CheckpointFingerprint};
                    inbox.BindDelivery(server,owner,reply);receipts.Send(reply);
                }
                catch(Exception error){log?.Invoke("Delivery recovery retained for reconciliation: "+error.GetType().Name);}
            }
            if(page.Length==8){string cursor=page[page.Length-1].Fingerprint;pump.Continue(()=>Recover(cursor,records));}
        }
        public void Dispose(){if(!active)return;active=false;var failures=new System.Collections.Generic.List<Exception>();
            TradeLifetimeCleanup.Try(()=>pump.Dispose(),failures);
            TradeLifetimeCleanup.Try(()=>read.AvailabilityChanged-=Changed,failures);TradeLifetimeCleanup.Try(()=>read.InventoryChanged-=Changed,failures);
            TradeLifetimeCleanup.Try(()=>{if(saved!=null)saved.SavedCheckpoint-=Changed;},failures);TradeLifetimeCleanup.ThrowIfFailed(failures);}
    }
}
