using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Phinix.InventoryExtension;
using Phinix.TradeExtension.Client;
using Utils.Framework;
partial class Program
{
 static void CheckDeliveryProtocol()
 {
  string dir=Path.Combine(Path.GetTempPath(),"phinix-delivery-contract-"+Guid.NewGuid().ToString("N"));
  try
  {
   var inbox=new TradeCompletionInbox(dir);var items=Items(31);string id=Guid.NewGuid().ToString("N"),save=new string('a',64),journal=Guid.NewGuid().ToString("N");
   var body=new Phinix.TradeExtension.FrameworkTradeCompletionEvent {TradeId="delivery",OtherPartyUuid="b",Items=items.ToList()};
   var request=new TradeDeliveryReceiptRequest {DeliveryId=id,TradeId="delivery",Kind="InboxStored",PayloadFingerprint=TradeDeliveryProtocol.Fingerprint(id,"a",body)};
   inbox.Record("server","a","delivery",true,"b",items);inbox.BindDelivery("server","a",request);
   inbox.BindSave("server","a","delivery",true,save);
   var deposit=new InventoryDeposit {DepositId="trade-delivery-v1:"+id,Source="builtin.trade",OriginId="delivery",OriginKind="trade-completed",Items=new List<InventoryItem> {new InventoryItem {CodecId="trade.framework-item-v1",CodecVersion=1,Quantity=1,Payload=Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(items[0]))}}};
   inbox.RecordInventoryPlan("server","a","delivery",true,journal,deposit);
   var reopened=new TradeCompletionInbox(dir);var raw=reopened.ReadAll().Single();Check(reopened.InventoryPlan(raw).JournalIdentity==journal,"Restart lost frozen journal binding");
   Check(reopened.DeliveryState("server","a","delivery",true)=="Received","Plan created an external attempt");
   Check(reopened.TryBeginDelivery("server","a","delivery",true,true),"Frozen plan could not enter its first attempt");
   Check(!reopened.TryBeginDelivery("server","a","delivery",true,true),"Replay authorized fresh delivery");
   deposit.Items[0].Quantity=2;Throws<InvalidDataException>(()=>reopened.RecordInventoryPlan("server","a","delivery",true,journal,deposit));
   request.Kind="SavedInventory";request.SaveIdentity=save;request.CheckpointFingerprint=new string('B',64);reopened.BindDelivery("server","a",request);
   Check(new TradeCompletionInbox(dir).DeliveryReceipt(raw).Kind=="SavedInventory","Saved checkpoint lost on restart");
   var sent=new List<FrameworkPacket>();var client=new TradeDeliveryReceiptClient(reopened,()=>"server",()=>"a",()=>"session",()=>true,p=>{sent.Add(p);return true;},_=>{});
   client.Send(request);var response=new FrameworkPacket {MessageType=TradeDeliveryProtocol.Response,SenderUuid=FrameworkProtocol.SystemSenderUuid,SessionId="stale",PayloadJson=FrameworkSerialization.SerializePayload(new TradeDeliveryReceiptResponse {DeliveryId=id,Accepted=true,State="Confirmed"})};response.SetCorrelationId(sent.Single().GetCorrelationId());
   Throws<UnauthorizedAccessException>(()=>client.Handle(response));response.SessionId="session";client.Handle(response);Check(reopened.DeliveryObservation(raw).Contains("Confirmed"),"Valid response not observed");
   request.CheckpointFingerprint=new string('z',64);Throws<InvalidDataException>(()=>reopened.BindDelivery("server","a",request));
   File.Delete(Directory.GetFiles(dir,"*.delivery").Single());Throws<FileNotFoundException>(()=>new TradeCompletionInbox(dir));
  }
  finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}
