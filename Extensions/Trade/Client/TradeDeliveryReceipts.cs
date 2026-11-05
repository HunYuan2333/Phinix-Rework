using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    internal static class TradeDeliveryProtocol
    {
        internal const string Request="trade.delivery.receipt.request",Response="trade.delivery.receipt.response",Contract="DurableInboxV1";
        internal const string IdKey="trade.delivery.id",HashKey="trade.delivery.hash",ContractKey="trade.delivery.contract";
        internal static string Fingerprint(string id,string recipient,FrameworkTradeCompletionEvent body)
        {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("phinix.delivery.v1\n"+id+"\n"+recipient.ToUpperInvariant()+"\n"+FrameworkSerialization.SerializePayload(body)))).Replace("-","");}
    }
    [DataContract] internal sealed class TradeDeliveryReceiptRequest
    {
        [DataMember(Order=0)] public string DeliveryId;
        [DataMember(Order=1)] public string TradeId;
        [DataMember(Order=2)] public bool Cancelled;
        [DataMember(Order=3)] public string PayloadFingerprint;
        [DataMember(Order=4)] public string Kind;
        [DataMember(Order=5)] public string SaveIdentity;
        [DataMember(Order=6)] public string CheckpointFingerprint;
    }
    [DataContract] internal sealed class TradeDeliveryReceiptResponse
    {
        [DataMember(Order=0)] public string DeliveryId;
        [DataMember(Order=1)] public bool Accepted;
        [DataMember(Order=2)] public string State;
        [DataMember(Order=3)] public string[] DeliveryStates;
    }
    [DataContract] internal sealed class TradeDeliveryBinding
    {
        [DataMember(Order=0)] public int Schema=1;
        [DataMember(Order=1)] public string ReceiptFingerprint;
        [DataMember(Order=2)] public TradeDeliveryReceiptRequest Request;
        [DataMember(Order=3)] public string Fingerprint;
    }
    [DataContract] internal sealed class TradeInventoryPlan
    {
        [DataMember(Order=0)] public int Schema=1;
        [DataMember(Order=1)] public string ReceiptFingerprint;
        [DataMember(Order=2)] public string SaveIdentity;
        [DataMember(Order=3)] public string JournalIdentity;
        [DataMember(Order=4)] public string DepositJson;
        [DataMember(Order=5)] public string Fingerprint;
    }
    internal sealed partial class TradeCompletionInbox
    {
        private string EvidencePath(TradeCompletionReceipt r,string extension)=>Path.Combine(directory,Key(r)+extension);
        private static string DeliveryHash(TradeDeliveryBinding b)=>Hash(FrameworkSerialization.SerializePayload(new TradeDeliveryBinding {ReceiptFingerprint=b.ReceiptFingerprint,Request=b.Request}));
        private TradeDeliveryBinding ReadDelivery(string path,TradeCompletionReceipt r)
        {
            Private(path,384);if(new FileInfo(path).Length>8192)throw new InvalidDataException("Delivery binding exceeds budget.");
            var b=FrameworkSerialization.DeserializePayload<TradeDeliveryBinding>(File.ReadAllText(path,Encoding.UTF8));var q=b?.Request;
            ValidateDelivery(b,r);
            return b;
        }
        private static void ValidateDelivery(TradeDeliveryBinding b,TradeCompletionReceipt r)
        {
            var q=b?.Request;
            if(b?.Schema!=1 || b.ReceiptFingerprint!=r.Fingerprint || b.Fingerprint!=DeliveryHash(b) || q==null || !Guid.TryParseExact(q.DeliveryId,"N",out _) ||
                q.TradeId!=r.TradeId || q.Cancelled==r.Completed || q.PayloadFingerprint!=TradeDeliveryProtocol.Fingerprint(q.DeliveryId,r.Recipient,new FrameworkTradeCompletionEvent {TradeId=r.TradeId,OtherPartyUuid=r.OtherParty,Items=r.Items.ToList(),Cancelled=!r.Completed}) ||
                (q.Kind!="InboxStored" && q.Kind!="SavedInventory") || (q.Kind=="InboxStored"&&(q.SaveIdentity!=null||q.CheckpointFingerprint!=null)) ||
                (q.Kind=="SavedInventory"&&(!IsSaveIdentity(q.SaveIdentity)||!IsSaveIdentity(q.CheckpointFingerprint?.ToLowerInvariant()))))throw new InvalidDataException("Invalid durable delivery binding.");
        }
        internal void BindDelivery(string endpoint,string recipient,TradeDeliveryReceiptRequest request)
        {
            lock(gate)
            {
                var r=FindReceipt(endpoint,recipient,request.TradeId,!request.Cancelled);var b=new TradeDeliveryBinding {ReceiptFingerprint=r.Fingerprint,Request=request};b.Fingerprint=DeliveryHash(b);
                ValidateDelivery(b,r);
                string path=EvidencePath(r,request.Kind=="SavedInventory"?".checkpoint":".delivery");
                if(request.Kind=="SavedInventory")
                {var original=ReadDelivery(EvidencePath(r,".delivery"),r);if(original.Request.DeliveryId!=request.DeliveryId || SaveBinding(endpoint,recipient,r.TradeId,r.Completed,request.SaveIdentity)!="Current")throw new InvalidDataException("Checkpoint scope mismatch.");}
                if(File.Exists(path)){if(FrameworkSerialization.SerializePayload(ReadDelivery(path,r))!=FrameworkSerialization.SerializePayload(b))throw new InvalidDataException("Delivery evidence conflicts.");return;}
                // Validate bytes with the same full receipt constraints before publication.
                if(request.PayloadFingerprint!=TradeDeliveryProtocol.Fingerprint(request.DeliveryId,recipient,new FrameworkTradeCompletionEvent {TradeId=r.TradeId,OtherPartyUuid=r.OtherParty,Items=r.Items.ToList(),Cancelled=!r.Completed}))throw new InvalidDataException("Delivery fingerprint mismatch.");
                PublishBytes(path,Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(b)));
            }
        }
        internal TradeDeliveryReceiptRequest DeliveryReceipt(TradeCompletionReceipt r)
        {lock(gate){string path=EvidencePath(r,".checkpoint");if(!File.Exists(path))path=EvidencePath(r,".delivery");return File.Exists(path)?ReadDelivery(path,r).Request:null;}}
        internal void RecordServerObservation(string endpoint,string principal,TradeDeliveryReceiptRequest request,TradeDeliveryReceiptResponse response)
        {
            lock(gate)
            {
                var r=FindReceipt(endpoint,principal,request.TradeId,!request.Cancelled);
                if(DeliveryReceipt(r)?.DeliveryId!=request.DeliveryId)throw new InvalidDataException("Unknown delivery observation.");
                string state=request.Kind=="SavedInventory"?"SavedInventory; ":"InboxStored; ";
                // Server status is displayed separately from the immutable local success/attempt evidence.
                observations[r.Fingerprint]=state+response.State+(response.DeliveryStates==null?"":"; "+string.Join(", ",response.DeliveryStates));
                System.Threading.Interlocked.Increment(ref revision);
            }
        }
        private readonly Dictionary<string,string> observations=new Dictionary<string,string>(StringComparer.Ordinal);
        internal string DeliveryObservation(TradeCompletionReceipt r)
        {
            lock(gate)
            {
                if(observations.TryGetValue(r.Fingerprint,out var state))return state;
                var q=DeliveryReceipt(r);return q==null?null:q.Kind=="SavedInventory"?"SavedInventory; server confirmation pending":"InboxStored; save confirmation pending";
            }
        }
        private static string PlanHash(TradeInventoryPlan p)=>Hash(FrameworkSerialization.SerializePayload(new TradeInventoryPlan {ReceiptFingerprint=p.ReceiptFingerprint,SaveIdentity=p.SaveIdentity,JournalIdentity=p.JournalIdentity,DepositJson=p.DepositJson}));
        internal void RecordInventoryPlan(string endpoint,string recipient,string trade,bool completed,string journalIdentity,Phinix.InventoryExtension.InventoryDeposit deposit)
        {
            lock(gate)
            {
                var r=FindReceipt(endpoint,recipient,trade,completed);var binding=ReadBinding(EvidencePath(r,".binding"),r);
                if(File.Exists(EvidencePath(r,".attempt")))ReadAttempt(EvidencePath(r,".attempt"),r,"Inventory","Attempted");
                if(!Guid.TryParseExact(journalIdentity,"N",out _)||deposit==null)throw new InvalidDataException("Durable inventory journal required.");
                var p=new TradeInventoryPlan {ReceiptFingerprint=r.Fingerprint,SaveIdentity=binding.SaveIdentity,JournalIdentity=journalIdentity,DepositJson=FrameworkSerialization.SerializePayload(deposit)};p.Fingerprint=PlanHash(p);
                ValidatePlan(p,r);
                byte[] bytes=Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(p));if(bytes.Length>MaxRecord)throw new InvalidDataException("Inventory plan exceeds budget.");
                string path=EvidencePath(r,".plan");if(File.Exists(path)){if(File.ReadAllText(path,Encoding.UTF8)!=Encoding.UTF8.GetString(bytes))throw new InvalidDataException("Inventory plan conflicts.");return;}
                PublishBytes(path,bytes);
            }
        }
        private void ValidatePlan(TradeInventoryPlan p,TradeCompletionReceipt r)
        {
            var d=FrameworkSerialization.DeserializePayload<Phinix.InventoryExtension.InventoryDeposit>(p.DepositJson);
            var delivery=DeliveryReceipt(r);
            string expected=delivery==null?"trade-delivery:"+(r.Completed?"completed":"cancelled")+":"+r.TradeId+":"+r.Recipient:"trade-delivery-v1:"+delivery.DeliveryId;
            if(d==null||d.DepositId!=expected||d.Source!=FrameworkTradeProtocol.Capability||d.OriginId!=r.TradeId||d.OriginKind!=(r.Completed?"trade-completed":"trade-return")||d.Items==null||d.Items.Count!=r.Items.Length)throw new InvalidDataException("Frozen deposit differs from original receipt.");
            for(int i=0;i<r.Items.Length;i++)
            {var item=d.Items[i];var bytes=Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(r.Items[i]));
                if(item==null||item.CodecId!="trade.framework-item-v1"||item.CodecVersion!=1||item.Quantity!=1||item.Payload==null||!item.Payload.SequenceEqual(bytes))throw new InvalidDataException("Frozen item differs from original receipt.");}
        }
        internal TradeInventoryPlan InventoryPlan(TradeCompletionReceipt r)
        {
            lock(gate)
            {
                string path=EvidencePath(r,".plan");if(!File.Exists(path))return null;Private(path,384);if(new FileInfo(path).Length>MaxRecord)throw new InvalidDataException("Inventory plan exceeds budget.");
                var p=FrameworkSerialization.DeserializePayload<TradeInventoryPlan>(File.ReadAllText(path,Encoding.UTF8));
                if(p?.Schema!=1||p.ReceiptFingerprint!=r.Fingerprint||p.Fingerprint!=PlanHash(p)||!Guid.TryParseExact(p.JournalIdentity,"N",out _)||
                    ReadBinding(EvidencePath(r,".binding"),r).SaveIdentity!=p.SaveIdentity)throw new InvalidDataException("Invalid frozen inventory plan.");
                if(File.Exists(EvidencePath(r,".attempt")))ReadAttempt(EvidencePath(r,".attempt"),r,"Inventory","Attempted");ValidatePlan(p,r);return p;
            }
        }
        private void ValidateDeliveryEvidence()
        {
            foreach(var path in Directory.GetFiles(directory,"*.delivery").Concat(Directory.GetFiles(directory,"*.checkpoint")).Concat(Directory.GetFiles(directory,"*.plan")))
            {var r=Read(Path.ChangeExtension(path,".receipt"));if(path.EndsWith(".plan",StringComparison.Ordinal))InventoryPlan(r);else {var binding=ReadDelivery(path,r);if(path.EndsWith(".checkpoint",StringComparison.Ordinal))
                {var original=ReadDelivery(EvidencePath(r,".delivery"),r);if(original.Request.DeliveryId!=binding.Request.DeliveryId||SaveBinding(r.Endpoint,r.Recipient,r.TradeId,r.Completed,binding.Request.SaveIdentity)!="Current")throw new InvalidDataException("Orphan checkpoint.");}}}
        }
    }
    internal sealed class TradeDeliveryReceiptClient
    {
        private readonly TradeCompletionInbox inbox;
        private readonly Func<string> endpoint,principal,session;
        private readonly Func<bool> available;
        private readonly Func<FrameworkPacket,bool> send;
        private readonly Action<string> log;
        private sealed class Pending {internal string Session,Endpoint,Principal;internal TradeDeliveryReceiptRequest Request;}
        private readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>(StringComparer.Ordinal);
        private readonly object gate=new object();
        internal void Handle(FrameworkPacket packet)
        {
            lock(gate)
            {
                string correlation=packet?.GetCorrelationId();if(correlation==null||!pending.TryGetValue(correlation,out var p)||!available())return;
                if(packet.SenderUuid!=FrameworkProtocol.SystemSenderUuid||packet.SessionId!=p.Session||session()!=p.Session||endpoint()!=p.Endpoint||principal()!=p.Principal)throw new UnauthorizedAccessException("Delivery receipt response scope differs.");
                if(packet.PayloadJson==null||packet.PayloadJson.Length>4096)throw new InvalidDataException("Receipt response exceeds budget.");
                var r=FrameworkSerialization.DeserializePayload<TradeDeliveryReceiptResponse>(packet.PayloadJson);
                if(r?.DeliveryId!=p.Request.DeliveryId||r.Accepted&&(r.State!="Confirmed"&&r.State!="OutcomeUnknown"&&r.State!="RecipientStored"))throw new InvalidDataException("Receipt response differs.");
                if(r.Accepted)inbox.RecordServerObservation(p.Endpoint,p.Principal,p.Request,r);
                pending.Remove(correlation);
            }
        }
        internal TradeDeliveryReceiptClient(TradeCompletionInbox inbox,Func<string> endpoint,Func<string> principal,Func<string> session,Func<bool> available,Func<FrameworkPacket,bool> send,Action<string> log)
        {this.inbox=inbox;this.endpoint=endpoint;this.principal=principal;this.session=session;this.available=available;this.send=send;this.log=log;}
        internal Action Prepare(FrameworkPacket packet,FrameworkTradeCompletionEvent body)
        {
            bool marked=packet.Metadata?.Any(m=>m?.Key==TradeDeliveryProtocol.ContractKey||m?.Key==TradeDeliveryProtocol.IdKey||m?.Key==TradeDeliveryProtocol.HashKey)==true;
            if(!marked){if(available())throw new InvalidDataException("Negotiated delivery contract missing.");return null;}
            if(!available()||packet.SessionId!=session()||packet.SenderUuid!=FrameworkProtocol.SystemSenderUuid)throw new InvalidDataException("Untrusted delivery session.");
            string Value(string key){var entries=packet.Metadata.Where(m=>m?.Key==key).ToArray();if(entries.Length!=1)throw new InvalidDataException("Ambiguous delivery metadata.");return entries[0].Value;}
            string id=Value(TradeDeliveryProtocol.IdKey);if(Value(TradeDeliveryProtocol.ContractKey)!=TradeDeliveryProtocol.Contract||!Guid.TryParseExact(id,"N",out _)||
                Value(TradeDeliveryProtocol.HashKey)!=TradeDeliveryProtocol.Fingerprint(id,principal(),body))throw new InvalidDataException("Invalid delivery contract.");
            if(body.Cancelled!=(packet.MessageType==FrameworkTradeProtocol.CancelledEventType))throw new InvalidDataException("Delivery outcome differs from envelope.");
            string origin=endpoint(),owner=principal();var q=new TradeDeliveryReceiptRequest {DeliveryId=id,TradeId=body.TradeId,Cancelled=body.Cancelled,PayloadFingerprint=Value(TradeDeliveryProtocol.HashKey),Kind="InboxStored"};
            return ()=>{inbox.BindDelivery(origin,owner,q);var r=inbox.GetReceipt(origin,owner,body.TradeId,!body.Cancelled);Send(inbox.DeliveryReceipt(r));};
        }
        internal void Send(TradeDeliveryReceiptRequest request)
        {
            if(request==null||!available())return;
            try
            {string id=Guid.NewGuid().ToString("N");var p=new FrameworkPacket {Flow=global::Phinix.Framework.FrameworkFlow.Command,CommandKind=global::Phinix.Framework.FrameworkCommandKind.Request,MessageType=TradeDeliveryProtocol.Request,MessageId=id,
                SessionId=session(),SenderUuid=principal(),PayloadJson=FrameworkSerialization.SerializePayload(request)};p.SetCorrelationId(id);
                lock(gate)
                {
                    foreach(var stale in pending.Where(x=>x.Value.Session!=p.SessionId||x.Value.Request.DeliveryId==request.DeliveryId).Select(x=>x.Key).ToArray())pending.Remove(stale);
                    if(pending.Count>=64)return;
                    pending.Add(id,new Pending {Session=p.SessionId,Endpoint=endpoint(),Principal=p.SenderUuid,Request=request});
                }
                if(!send(p))lock(gate)pending.Remove(id);}
            catch(Exception error){log?.Invoke("Delivery receipt remains durable; transport="+error.GetType().Name);}
        }
    }
}
