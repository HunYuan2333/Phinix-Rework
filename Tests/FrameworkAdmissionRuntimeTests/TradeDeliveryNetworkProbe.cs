using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Collections.Generic;
using Phinix.TradeExtension;
using Utils.Framework;

internal static partial class TradeNetworkProbe
{
    // Protocol/checkpoint fields are synthetic fixtures. This verifies receipt
    // transport and persistence, never that RimWorld has saved actual assets.
    internal static void RunDelivery(int port,string phase,string statePath,string clientDll,string inboxPath)
    {
        bool completed=phase.StartsWith("completed-",StringComparison.Ordinal);
        if(completed)phase=phase.Substring("completed-".Length);
        string eventType=completed?FrameworkTradeProtocol.CompletedEventType:FrameworkTradeProtocol.CancelledEventType;
        bool initial=phase=="initial";
        var state=initial?new State{Keys=new[]{Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N")}}:JsonSerializer.Deserialize<State>(File.ReadAllText(statePath));
        using var a=new Peer(port,state.Keys[0],receipts:true);using var b=new Peer(port,state.Keys[1],receipts:true);
        Wait(a,b,()=>a.Ready&&b.Ready);
        var product=Assembly.LoadFrom(Path.GetFullPath(clientDll));
        Check(SHA256.HashData(File.ReadAllBytes(product.Location)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(clientDll))),"Probe loaded different client DLL bytes");
        var inboxType=product.GetType("Phinix.TradeExtension.Client.TradeCompletionInbox",true);
        var receiptType=product.GetType("Phinix.TradeExtension.Client.TradeDeliveryReceiptClient",true);
        var requestType=product.GetType("Phinix.TradeExtension.Client.TradeDeliveryReceiptRequest",true);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,flags).Invoke(target,args);
        object Field(object target,string name)=>target.GetType().GetField(name).GetValue(target);
        string endpoint="127.0.0.1:"+port;
        object Inbox(string owner)=>Activator.CreateInstance(inboxType,flags,null,new object[]{Path.Combine(inboxPath,owner)},null);
        object inboxA=Inbox("a"),inboxB=Inbox("b");FrameworkPacket sentA=null,sentB=null;
        object Client(object inbox,Peer peer,Action<FrameworkPacket> observed)=>Activator.CreateInstance(receiptType,flags,null,new object[]{inbox,(Func<string>)(()=>endpoint),(Func<string>)(()=>peer.Principal),(Func<string>)(()=>peer.SessionId),(Func<bool>)(()=>peer.Ready),
            (Func<FrameworkPacket,bool>)(p=>{observed(p);p.Kind=FrameworkProtocol.KindCommand;peer.Send(p);return true;}),(Action<string>)(_=>{})},null);
        object clientA=Client(inboxA,a,p=>sentA=p),clientB=Client(inboxB,b,p=>sentB=p);
        FrameworkPacket Reply(Peer peer,FrameworkPacket request)
        {Wait(a,b,()=>peer.Find("trade.delivery.receipt.response",request.GetCorrelationId())!=null);return peer.Find("trade.delivery.receipt.response",request.GetCorrelationId());}
        void Expect(FrameworkPacket packet,bool accepted,string status=null)
        {using var json=JsonDocument.Parse(packet.PayloadJson);Check(json.RootElement.GetProperty("Accepted").GetBoolean()==accepted,"Receipt acceptance differs");if(status!=null)Check(json.RootElement.GetProperty("State").GetString()==status,"Receipt state differs");}
        void Receive(Peer peer,object inbox,object client)
        {
            var packet=peer.Packets.Last(p=>p.MessageType==eventType);
            var body=FrameworkSerialization.DeserializePayload<FrameworkTradeCompletionEvent>(packet.PayloadJson);
            Check(body.TradeId==state.Trade&&body.Cancelled==!completed,"Completion identity/outcome differs");
            Check(body.Items.Count==(peer==a?1:0),"Whole-batch recipient payload differs");
            if(peer==a)Check(body.Items[0].CodecId=="synthetic.opaque"&&body.Items[0].PayloadBytes.SequenceEqual(new byte[]{17,0,255}),"Cancellation lost exact original opaque bytes");
            string id=packet.Metadata.Single(m=>m.Key=="trade.delivery.id").Value;
            string literal="phinix.delivery.v1\n"+id+"\n"+peer.Principal.ToUpperInvariant()+"\n"+FrameworkSerialization.SerializePayload(body);
            Check(packet.Metadata.Single(m=>m.Key=="trade.delivery.hash").Value==Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(literal))),"Independent delivery fingerprint differs");
            var send=(Action)Call(client,"Prepare",packet,body);
            bool added=(bool)Call(inbox,"Record",endpoint,peer.Principal,body.TradeId,!body.Cancelled,body.OtherPartyUuid,body.Items);
            Check(added==initial,"Duplicate delivery changed durable receive identity");send();
        }
        if(initial)
        {
            state.Principals=new[]{a.Principal,b.Principal};
            a.Command(FrameworkTradeProtocol.CreateRequestType,new FrameworkTradeCreateRequest{OtherPartyUuid=b.Principal},"delivery-create");
            Wait(a,b,()=>a.Find(FrameworkTradeProtocol.CreateResponseType,"delivery-create")!=null);
            var created=FrameworkSerialization.DeserializePayload<FrameworkTradeCreateResponse>(a.Find(FrameworkTradeProtocol.CreateResponseType,"delivery-create").PayloadJson);Check(created.Success,"Creation refused");state.Trade=created.TradeId;
            var offering=completed?b:a;
            offering.Send(new FrameworkPacket{Kind=FrameworkProtocol.KindItem,MessageType=FrameworkTradeProtocol.Capability,MessageId="delivery-item",PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkItemPayload{CodecId="synthetic.opaque",PayloadBytes=new byte[]{17,0,255}})});
            offering.Command(FrameworkTradeProtocol.OfferUpdateRequestType,new FrameworkTradeOfferUpdateRequest{TradeId=state.Trade,ItemPacketRefs=new List<string>{"delivery-item"}},"delivery-offer");
            Wait(a,b,()=>offering.Find(FrameworkTradeProtocol.OfferUpdateResponseType,"delivery-offer")!=null);Check(FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateResponse>(offering.Find(FrameworkTradeProtocol.OfferUpdateResponseType,"delivery-offer").PayloadJson).Success,"Offer refused");
            if(completed)
            {
                long Latest(Peer peer)=>peer.Packets.Where(p=>p.MessageType==FrameworkTradeProtocol.SnapshotType)
                    .SelectMany(p=>FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(p.PayloadJson).Trades).Where(t=>t.TradeId==state.Trade).Select(t=>t.SnapshotVersion).DefaultIfEmpty(0).Max();
                void Consent(Peer peer,string id,long version)
                {
                    var packet=new FrameworkPacket{Kind=FrameworkProtocol.KindCommand,CommandKind=global::Phinix.Framework.FrameworkCommandKind.Request,MessageId=id,MessageType=FrameworkTradeProtocol.StatusUpdateRequestType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeStatusUpdateRequest{TradeId=state.Trade,Accepted=true})};
                    string correlation=id+"-correlation";packet.SetCorrelationId(correlation);
                    packet.SetSnapshotVersion(version);peer.Send(packet);Wait(a,b,()=>peer.Find(FrameworkTradeProtocol.StatusUpdateResponseType,correlation)!=null);
                    Check(FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateResponse>(peer.Find(FrameworkTradeProtocol.StatusUpdateResponseType,correlation).PayloadJson).Success,"Observed-version confirmation refused");
                }
                Wait(a,b,()=>Latest(a)>=2);Consent(a,"delivery-confirm-a",Latest(a));
                Wait(a,b,()=>Latest(b)>=3);Consent(b,"delivery-confirm-b",Latest(b));
            }
            else a.Command(FrameworkTradeProtocol.StatusUpdateRequestType,new FrameworkTradeStatusUpdateRequest{TradeId=state.Trade,Cancelled=true},"delivery-cancel");
            Wait(a,b,()=>a.Packets.Any(p=>p.MessageType==eventType)&&b.Packets.Any(p=>p.MessageType==eventType));
            Receive(a,inboxA,clientA);Expect(Reply(a,sentA),true,"OutcomeUnknown");
            // Fault: server committed InboxStored, but the client does not apply
            // its response before both processes end. Durable original remains.
            Receive(b,inboxB,clientB);var reply=Reply(b,sentB);Expect(reply,true,"Confirmed");Call(clientB,"Handle",reply);
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write};if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using var file=new FileStream(statePath,options);JsonSerializer.Serialize(file,state);
        }
        else
        {
            Check(a.Principal==state.Principals[0]&&b.Principal==state.Principals[1],"Restart changed stable recipients");
            object record=Call(inboxA,"GetReceipt",endpoint,a.Principal,state.Trade,completed);
            object original=Call(inboxA,"DeliveryReceipt",record);
            if(phase=="recover")
            {
                Wait(a,b,()=>a.Packets.Any(p=>p.MessageType==eventType));
                Receive(a,inboxA,clientA);var reply=Reply(a,sentA);Expect(reply,true,"OutcomeUnknown");Call(clientA,"Handle",reply);
                string foreign=sentA.PayloadJson;
                b.Send(new FrameworkPacket{Kind=FrameworkProtocol.KindCommand,CommandKind=global::Phinix.Framework.FrameworkCommandKind.Request,MessageId="foreign-original",PayloadJson=foreign,MessageType="trade.delivery.receipt.request"});
                Wait(a,b,()=>b.Find("trade.delivery.receipt.response","foreign-original")!=null);Expect(b.Find("trade.delivery.receipt.response","foreign-original"),false);
                string save=new string('a',64);Call(inboxA,"BindSave",endpoint,a.Principal,state.Trade,completed,save);
                object checkpoint=Activator.CreateInstance(requestType);
                foreach(string name in new[]{"DeliveryId","TradeId","Cancelled","PayloadFingerprint"})requestType.GetField(name).SetValue(checkpoint,Field(original,name));
                requestType.GetField("Kind").SetValue(checkpoint,"SavedInventory");requestType.GetField("SaveIdentity").SetValue(checkpoint,save);requestType.GetField("CheckpointFingerprint").SetValue(checkpoint,new string('b',64));
                Call(inboxA,"BindDelivery",endpoint,a.Principal,checkpoint);Call(clientA,"Send",checkpoint);Expect(Reply(a,sentA),true,"Confirmed");
                // Second response intentionally not applied; next process resends
                // the exact durable checkpoint rather than materializing assets.
            }
            else
            {
                Check((string)Field(original,"Kind")=="SavedInventory","Original checkpoint lost across client restart");
                Call(clientA,"Send",original);var reply=Reply(a,sentA);Expect(reply,true,"Confirmed");Call(clientA,"Handle",reply);
                Check(!a.Packets.Any(p=>p.MessageType==eventType)&&!b.Packets.Any(p=>p.MessageType==eventType),"Confirmed archived delivery replayed after restart");
            }
            Check(((Array)Call(inboxA,"ReadAll")).Length==1,"Replay created another durable delivery record");
        }
        Console.WriteLine("CLIENT DELIVERY NETWORK RESULT passed; phase="+phase+"; actual receipt/inbox DLL, synthetic checkpoint only; no inventory/game save proof");
    }
}
