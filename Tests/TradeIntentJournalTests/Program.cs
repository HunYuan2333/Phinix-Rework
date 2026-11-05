using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using Utils.Framework;
static partial class Program
{
 static int count;
 static void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
 static void Throws<T>(Action a) where T:Exception{count++;try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
 static FrameworkPacket Packet(string id,string other="b")=>new(){MessageId=id,MessageType=FrameworkTradeProtocol.CreateRequestType,SessionId="synthetic-session-secret",SenderUuid="a",PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateRequest{OtherPartyUuid=other})};
 static void Main(string[] args)
 {
  if(args.Length==1&&args[0]=="--capacity-only") {CheckCapacity();Console.WriteLine("CAPACITY RESULT passed; checks="+count);return;}
  if(args.Length==1&&args[0]=="--filesystem-only") {CheckFilesystemIsolation();Console.WriteLine("Filesystem isolation passed.");return;}
  if(args.Length==2&&args[0]=="--child")
  {
   var child=new TradeIntentJournal(args[1]);var intent=child.Prepare("127.0.0.1:16200","a",Packet("kill"),Array.Empty<FrameworkPacket>());
   child.BeginAttempt(intent);File.WriteAllText(Path.Combine(args[1],"barrier"),"intent.attempt-before-transport");Thread.Sleep(Timeout.Infinite);return;
  }
  string dir=Path.Combine(Path.GetTempPath(),"phinix-intents-"+Guid.NewGuid().ToString("N"));
  try
  {
   CheckFilesystemIsolation();
   CheckCapacity();
   var journal=new TradeIntentJournal(dir);var p=Packet("op1");
   var record=journal.Prepare("127.0.0.1:16200","a",p,Array.Empty<FrameworkPacket>());
   string expected=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("create\n{\"OtherPartyUuid\":\"b\"}")));
   Check(record.RequestFingerprint==expected,"Canonical request fingerprint differs from independent literal");
   Check(journal.State(record)=="Prepared","Prepared request is already attempted");
   Check(!Directory.GetFiles(dir).Any(f=>File.ReadAllText(f).Contains("synthetic-session-secret")),"Ephemeral session credential persisted");
   Check(journal.Read("127.0.0.1:16200","a").Single().OperationId=="op1","Intent not durable before transport");
   Check(new TradeIntentJournal(dir).Prepare("127.0.0.1:16200","a",p,Array.Empty<FrameworkPacket>()).Checksum==record.Checksum,"Exact request is not idempotent");
   Throws<InvalidDataException>(()=>journal.Prepare("127.0.0.1:16200","a",Packet("op1","changed"),Array.Empty<FrameworkPacket>()));
   Check(journal.Read("127.0.0.1:16201","a").Length==0&&journal.Read("127.0.0.1:16200","b").Length==0,"Intent scope crossed server/principal");
   journal.BeginAttempt(record);Check(new TradeIntentJournal(dir).State(record)=="OutcomeUnknown","Attempt not durable across reopen");
   Throws<InvalidOperationException>(()=>journal.BeginAttempt(record));
   var response=new FrameworkPacket{MessageType=FrameworkTradeProtocol.CreateResponseType,SessionId="synthetic-session-secret",PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateResponse{Success=true,TradeId="created",OtherPartyUuid="b"})};response.SetCorrelationId("op1");
   Check(journal.RecordResponse("127.0.0.1:16200","a",response),"First exact result not published");
   Check(new TradeIntentJournal(dir).State(record)=="ResponseSuccess","Original response not durable before business callback");
   Check(!journal.RecordResponse("127.0.0.1:16200","a",response),"Duplicate result allows repeated business callback");
   response.PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateResponse{Success=false,OtherPartyUuid="b"});
   Throws<InvalidDataException>(()=>journal.RecordResponse("127.0.0.1:16200","a",response));
   var sameToken=Packet("different-op");sameToken.SetCorrelationId("op1");
   Throws<InvalidDataException>(()=>journal.Prepare("127.0.0.1:16200","a",sameToken,Array.Empty<FrameworkPacket>()));
   var item=new FrameworkPacket{MessageId="item1",SessionId="synthetic-session-secret",PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkItemPayload{CodecId="synthetic.opaque",PayloadBytes=new byte[]{17,0,255}})};
   var offer=new FrameworkPacket{MessageId="offer1",MessageType=FrameworkTradeProtocol.OfferUpdateRequestType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateRequest{TradeId="trade1",ItemPacketRefs=new(){"item1"}})};
   Throws<InvalidDataException>(()=>journal.Prepare("127.0.0.1:16200","a",offer,Array.Empty<FrameworkPacket>()));
   var offered=journal.Prepare("127.0.0.1:16200","a",offer,new[]{item});
   Check(offered.Items.Single().MessageId=="item1"&&FrameworkSerialization.DeserializePayload<FrameworkItemPayload>(offered.Items.Single().PayloadJson).PayloadBytes.SequenceEqual(new byte[]{17,0,255}),"Offer lost staged opaque batch/IDs");
   FrameworkPacket queryPacket=null;string session="current-session",principal="a";
   var queries=new TradeOperationQueryClient(journal,()=>"127.0.0.1:16200",()=>principal,()=>session,()=>true,packet=>{queryPacket=packet;return true;});
   queries.Query("offer1");var query=FrameworkSerialization.DeserializePayload<TradeOperationQueryRequest>(queryPacket.PayloadJson);
   Check(query.OperationId=="offer1"&&query.RequestFingerprint==offered.RequestFingerprint&&queryPacket.MessageId!="offer1","Query failed to use the original durable ID/fingerprint");
   Check(!queryPacket.PayloadJson.Contains("Principal")&&!queryPacket.PayloadJson.Contains("Items"),"Query supplies identity/asset claims");
   var lookup=new FrameworkPacket{MessageType=TradeOperationQueryProtocol.Response,SessionId=session,SenderUuid=FrameworkProtocol.SystemSenderUuid,
    PayloadJson=FrameworkSerialization.SerializePayload(new TradeOperationQueryResponse{OperationId="offer1",Status="NotFound"})};lookup.SetCorrelationId(queryPacket.GetCorrelationId());
   session="different-session";Throws<UnauthorizedAccessException>(()=>queries.Handle(lookup));session="current-session";
   principal="b";Throws<UnauthorizedAccessException>(()=>queries.Handle(lookup));principal="a";
   queries.Handle(lookup);Check(journal.State(offered)=="NotFoundObserved","NotFound did not remain a nonfinal observation");
   queries.Query("offer1");lookup.SetCorrelationId(queryPacket.GetCorrelationId());
   lookup.PayloadJson=FrameworkSerialization.SerializePayload(new TradeOperationQueryResponse{OperationId="offer1",Status="Committed",Success=true,Failure=FrameworkTradeFailureReason.None,TradeId="trade1",SnapshotVersion=2});
   queries.Handle(lookup);Check(new TradeIntentJournal(dir).State(offered)=="CommittedSuccess","Queried original result was not persisted");
   Throws<InvalidDataException>(()=>journal.RecordQuery(offered,new TradeOperationQueryResponse{OperationId="offer1",Status="Committed",Success=true,Failure=FrameworkTradeFailureReason.None,TradeId="other-trade",SnapshotVersion=2}));
   Throws<InvalidDataException>(()=>journal.RecordQuery(offered,new TradeOperationQueryResponse{OperationId="offer1",Status="Committed",Success=true,Failure=FrameworkTradeFailureReason.None,TradeId="trade1",SnapshotVersion=3}));
   journal.RecordQuery(offered,new TradeOperationQueryResponse{OperationId="offer1",Status="NotFound"});Check(journal.State(offered)=="CommittedSuccess","Later missing lookup erased a known original result");
   Throws<InvalidDataException>(()=>journal.RecordQuery(record,new TradeOperationQueryResponse{OperationId="op1",Status="Committed",Success=false,Failure=FrameworkTradeFailureReason.None}));
   var consent=new FrameworkPacket{MessageId="consent",MessageType=FrameworkTradeProtocol.StatusUpdateRequestType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeStatusUpdateRequest{TradeId="trade1",Accepted=true})};consent.SetSnapshotVersion(2);
   Check(journal.Prepare("127.0.0.1:16200","a",consent,Array.Empty<FrameworkPacket>()).Kind=="status@2","Consent version absent from canonical intent");
   string f=Directory.GetFiles(dir,"*.intent").First();byte[] bytes=File.ReadAllBytes(f);File.WriteAllText(f,File.ReadAllText(f).Replace("127.0.0.1","127.0.0.9"));
   Throws<InvalidDataException>(()=>new TradeIntentJournal(dir));File.WriteAllBytes(f,bytes);
   string orphan=Path.Combine(dir,new string('f',64)+".attempt");File.WriteAllText(orphan,"orphan");Throws<InvalidDataException>(()=>new TradeIntentJournal(dir));File.Delete(orphan);
   string killed=Path.Combine(dir,"kill");Directory.CreateDirectory(killed);
   var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(string a in new[]{Assembly.GetExecutingAssembly().Location,"--child",killed})start.ArgumentList.Add(a);
   using(var child=Process.Start(start))
   {
    try{Check(SpinWait.SpinUntil(()=>File.Exists(Path.Combine(killed,"barrier"))||child.HasExited,10000)&&!child.HasExited,"Intent child barrier");child.Kill(true);Check(child.WaitForExit(5000),"Intent child kill deadline");}
    finally{if(!child.HasExited){child.Kill(true);child.WaitForExit(5000);}}
   }
   var recovered=new TradeIntentJournal(killed);Check(recovered.State(recovered.Read("127.0.0.1:16200","a").Single())=="OutcomeUnknown","Kill before transport became unattempted/refundable");
   Console.WriteLine("INTENT RESULT passed; checks="+count+"; real files/independent fingerprint/scope/whole-batch/attempt-before-send; no game receipt");
  }
  finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
}
