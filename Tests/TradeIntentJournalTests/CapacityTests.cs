using System;
using System.IO;
using System.Linq;
using Phinix.InventoryExtension;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using Utils.Framework;

static partial class Program
{
 const long MiB=1024*1024;
 static void Pad(string directory,long bytes)
 {
  // Sparse synthetic files exercise logical byte accounting without filling the disk.
  using(var file=new FileStream(Path.Combine(directory,"synthetic-padding"),FileMode.Create,FileAccess.Write))file.SetLength(bytes);
 }
 static void CheckCapacity()
 {
  string root=Path.Combine(Path.GetTempPath(),"phinix-intent-capacity-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try
  {
   string near=Path.Combine(root,"near");var journal=new TradeIntentJournal(near);Pad(near,64*MiB-65536);
   Throws<IOException>(()=>journal.Prepare("127.0.0.1:16200","a",Packet("no-headroom"),Array.Empty<FrameworkPacket>()));
   Check(!Directory.GetFiles(near,"*.intent").Any(),"Rejected admission persisted a new intent");

   string recover=Path.Combine(root,"recover");journal=new TradeIntentJournal(recover);Pad(recover,61*MiB);
   var first=journal.Prepare("127.0.0.1:16200","a",Packet("first"),Array.Empty<FrameworkPacket>());
   Throws<IOException>(()=>journal.Prepare("127.0.0.1:16200","a",Packet("second"),Array.Empty<FrameworkPacket>()));
   Check(journal.Prepare("127.0.0.1:16200","a",Packet("first"),Array.Empty<FrameworkPacket>()).Checksum==first.Checksum,"Capacity prevented exact intent replay lookup");
   journal.BeginAttempt(first);
   Check(new TradeIntentJournal(recover).State(first)=="OutcomeUnknown","Admission refusal discarded unresolved attempt");
   var response=new FrameworkPacket{MessageType=FrameworkTradeProtocol.CreateResponseType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateResponse{Success=true,TradeId="trade",OtherPartyUuid="b"})};response.SetCorrelationId("first");
   Check(journal.RecordResponse("127.0.0.1:16200","a",response),"Original result could not use reserved headroom");
   journal.RecordQuery(first,new TradeOperationQueryResponse{OperationId="first",Status="Committed",Success=true,Failure=FrameworkTradeFailureReason.None,TradeId="trade",SnapshotVersion=1});
   var second=journal.Prepare("127.0.0.1:16200","a",Packet("second"),Array.Empty<FrameworkPacket>());
   var reopened=new TradeIntentJournal(recover);
   Check(reopened.State(first)=="CommittedSuccess"&&reopened.State(second)=="Prepared","Actual evidence size did not release unused reservation after result");
   Check(!reopened.RecordResponse("127.0.0.1:16200","a",response),"Original result deduplication changed near capacity");

   string large=Path.Combine(root,"large-result");var largeJournal=new TradeIntentJournal(large);Pad(large,61*MiB);
   var largeIntent=largeJournal.Prepare("127.0.0.1:16200","a",Packet("large-result"),Array.Empty<FrameworkPacket>());
   largeJournal.BeginAttempt(largeIntent);
   var largeResponse=new FrameworkPacket{MessageType=FrameworkTradeProtocol.CreateResponseType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateResponse{Success=true,TradeId="trade",OtherPartyUuid="b",FailureMessage=new string('x',(int)(2*MiB-2048))})};largeResponse.SetCorrelationId("large-result");
   Check(largeJournal.RecordResponse("127.0.0.1:16200","a",largeResponse),"Reserved capacity did not admit a near-limit result");
   largeJournal.RecordQuery(largeIntent,new TradeOperationQueryResponse{OperationId="large-result",Status="Committed",Success=true,Failure=FrameworkTradeFailureReason.None,TradeId="trade",SnapshotVersion=1});
   Check(new TradeIntentJournal(large).State(largeIntent)=="CommittedSuccess","Near-limit result/query failed independent reopen");

   // Simulate an old journal admitted without headroom, or external space use.
   string legacy=Path.Combine(root,"legacy");var legacyJournal=new TradeIntentJournal(legacy);
   var legacyIntent=legacyJournal.Prepare("127.0.0.1:16200","a",Packet("legacy"),Array.Empty<FrameworkPacket>());
   Pad(legacy,64*MiB-65536-Directory.GetFiles(legacy).Sum(f=>new FileInfo(f).Length));
   legacyJournal=new TradeIntentJournal(legacy);legacyJournal.BeginAttempt(legacyIntent);
   response.SetCorrelationId("legacy");
   Check(legacyJournal.RecordResponse("127.0.0.1:16200","a",response),"Admission reservation was wrongly imposed on an existing original result");
   Check(new TradeIntentJournal(legacy).State(legacyIntent)=="ResponseSuccess","Legacy original result did not survive reopen");

   string offerDir=Path.Combine(root,"offer");var offers=new TradeIntentJournal(offerDir);Pad(offerDir,61*MiB);
   var offer=new FrameworkPacket{MessageId="offer",MessageType=FrameworkTradeProtocol.OfferUpdateRequestType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateRequest{TradeId="trade"})};
   Throws<IOException>(()=>offers.Prepare("127.0.0.1:16200","a",offer,Array.Empty<FrameworkPacket>()));
   Check(!Directory.GetFiles(offerDir,"*.intent").Any(),"Offer admission failed to reserve binding plus result evidence");

   string bindings=Path.Combine(root,"binding");journal=new TradeIntentJournal(bindings);
   var bound=journal.Prepare("127.0.0.1:16200","a",offer,Array.Empty<FrameworkPacket>());
   var status=new InventoryStatus{Availability=InventoryAvailability.Ready,SaveIdentity=new string('a',64),JournalIdentity=Guid.NewGuid().ToString("N")};
   var reservation=new InventoryReservation{OwnerExtensionId="builtin.trade",Purpose="trade.offer",ReservationId="reservation",OperationId="reserved-operation"};
   journal.BindReservation(bound,status,reservation);
   string binding=Directory.GetFiles(bindings,"*.reservation").Single();byte[] original=File.ReadAllBytes(binding);
   reservation.ReservationId=new string('x',(int)(2*MiB));
   // A separate intent tests writer/reader size symmetry, rather than a conflicting overwrite.
   var nextOffer=new FrameworkPacket{MessageId="large-binding",MessageType=offer.MessageType,PayloadJson=offer.PayloadJson};
   var next=journal.Prepare("127.0.0.1:16200","a",nextOffer,Array.Empty<FrameworkPacket>());
   Throws<IOException>(()=>journal.BindReservation(next,status,reservation));
   Check(Directory.GetFiles(bindings,"*.reservation").Length==1,"Unreadable oversized binding was committed");
   Check(File.ReadAllBytes(binding).SequenceEqual(original),"Rejected binding changed existing evidence");
   long current=Directory.GetFiles(bindings).Sum(f=>new FileInfo(f).Length);
   Pad(bindings,64*MiB-current+1);
   Throws<InvalidDataException>(()=>new TradeIntentJournal(bindings));
   Check(File.ReadAllBytes(binding).SequenceEqual(original),"Over-budget startup deleted reservation evidence");
   File.Delete(Path.Combine(bindings,"synthetic-padding"));
   Check(new TradeIntentJournal(bindings).ReservationBinding(bound)!=null,"Capacity failure prevented recovery after space restored");

   var missing=new TradeIntentRecord{Endpoint="127.0.0.1:16200",Principal="a",OperationId="not-persisted",Checksum="untrusted"};
   reservation.ReservationId="reservation";
   Throws<FileNotFoundException>(()=>journal.BindReservation(missing,status,reservation));
   Check(Directory.GetFiles(bindings,"*.reservation").Length==1,"Orphan binding was published without a committed intent");
  }
  finally{Directory.Delete(root,true);}
 }
}
