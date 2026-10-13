using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Trade;
using Utils.Framework;

internal static partial class Program
{
    private static FrameworkPacket OfferResponse(string id,string token)
    {
        var packet=new FrameworkPacket {PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateResponse{TradeId=id,Success=true}),MessageId=Guid.NewGuid().ToString()};
        packet.SetCorrelationId(token);return packet;
    }
    private static FrameworkPacket TradeSnapshot(string id,string token,bool full=false)
    {
        var packet=new FrameworkPacket {MessageId=Guid.NewGuid().ToString(),PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeStateCollectionSnapshot {
            Trades=new List<FrameworkTradeStateSnapshot>{new FrameworkTradeStateSnapshot{TradeId=id,Participants=new List<FrameworkTradeParticipantSnapshot>{new FrameworkTradeParticipantSnapshot{Uuid="local"},new FrameworkTradeParticipantSnapshot{Uuid="remote"}}}}
        })};
        if(token!=null)packet.SetCorrelationId(token);
        packet.SetStateKind(full?FrameworkMetadataStateKinds.Snapshot:FrameworkMetadataStateKinds.Delta);return packet;
    }
    private static void CheckOfferCorrelation()
    {
        var service=new PhinixFrameworkTradeClientService(null,new Users(),null);
        var events=new List<TradeUpdateEventArgs>();service.OnTradeUpdateSuccess+=(_,args)=>events.Add(args);
        service.HandleOfferUpdateResponse(OfferResponse("one","reset-a"));
        service.HandleSnapshot(TradeSnapshot("one","other-operation"));
        Assert(!events.Any(e=>e.Token=="reset-a")&&service.GetTrades().Length==1,"Unrelated delta updates display but cannot prove an acknowledged Reset token.");
        service.HandleSnapshot(TradeSnapshot("one","reset-a",true));
        Assert(!events.Any(e=>e.Token=="reset-a"),"Full sync is not the operation-correlated update, even when its packet correlation happens to match.");
        var missing=TradeSnapshot("one",null);missing.MessageId="reset-a";service.HandleSnapshot(missing);
        Assert(!events.Any(e=>e.Token=="reset-a"),"Missing correlation metadata cannot borrow a matching message ID to acknowledge an offer.");
        service.HandleOfferUpdateResponse(OfferResponse("one","offer-b"));
        service.HandleOfferUpdateResponse(OfferResponse("one","offer-b"));
        service.HandleSnapshot(TradeSnapshot("ONE","offer-b"));
        Assert(events.Count(e=>e.Token=="offer-b")==1&&!events.Any(e=>e.Token=="reset-a"),"Matching delta consumes only its token; duplicate responses while pending do not duplicate acknowledgements.");
        service.HandleSnapshot(TradeSnapshot("two","reset-a"));
        Assert(!events.Any(e=>e.Token=="reset-a"),"Matching token on a different trade cannot settle the first trade.");
        service.HandleSnapshot(TradeSnapshot("one","RESET-A"));
        Assert(!events.Any(e=>e.Token=="reset-a"),"Correlation IDs remain case-sensitive, independent of case-insensitive trade identity.");
        var malformed=TradeSnapshot("one","reset-a");
        var invalidPayload=FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(malformed.PayloadJson);
        invalidPayload.Trades[0].Participants.RemoveAt(0);malformed.PayloadJson=FrameworkSerialization.SerializePayload(invalidPayload);service.HandleSnapshot(malformed);
        Assert(!events.Any(e=>e.Token=="reset-a"),"Matching correlation with no local participant cannot manufacture an empty own offer for Reset.");
        invalidPayload.Trades[0].Participants.Add(new FrameworkTradeParticipantSnapshot{Uuid="remote"});malformed.PayloadJson=FrameworkSerialization.SerializePayload(invalidPayload);service.HandleSnapshot(malformed);
        Assert(!events.Any(e=>e.Token=="reset-a"),"Duplicate participants cannot supply a confirmation basis or crash delta projection.");
        service.HandleSnapshot(TradeSnapshot("one","reset-a"));
        Assert(events.Count(e=>e.Token=="reset-a")==1&&events.Single(e=>e.Token=="reset-a").Trade.ItemsOnOffer.Length==0,"The matching acknowledged delta carries its exact empty offer basis once.");
        service.HandleSnapshot(TradeSnapshot("one","reset-a"));
        Assert(events.Count(e=>e.Token=="reset-a")==1,"Repeated delta without a pending response emits no second correlated acknowledgement.");
        var noProof=new PhinixFrameworkTradeClientService(null,new Users(),null);int noProofAcks=0;noProof.OnTradeUpdateSuccess+=(_,args)=>{if(args.Token=="unproved")noProofAcks++;};
        noProof.HandleOfferUpdateResponse(new FrameworkPacket {MessageId="unproved",PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateResponse{TradeId="one",Success=true})});
        noProof.HandleSnapshot(TradeSnapshot("one","unproved"));
        Assert(noProofAcks==0,"Response message ID cannot substitute for missing offer correlation metadata.");
        var reordered=new PhinixFrameworkTradeClientService(null,new Users(),null);var later=new List<TradeUpdateEventArgs>();reordered.OnTradeUpdateSuccess+=(_,args)=>later.Add(args);
        reordered.HandleSnapshot(TradeSnapshot("one","first"));reordered.HandleOfferUpdateResponse(OfferResponse("one","first"));reordered.HandleSnapshot(TradeSnapshot("one","other"));
        Assert(!later.Any(e=>e.Token=="first"),"Delta-before-response retains uncertainty; a later unrelated delta cannot manufacture a result.");
        var status=new FrameworkPacket{PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeStatusUpdateResponse{TradeId="one",Success=true})};status.SetCorrelationId("one");
        service.HandleStatusUpdateResponse(status);service.HandleSnapshot(TradeSnapshot("one","foreign"));int before=events.Count(e=>e.Token=="");
        service.HandleSnapshot(TradeSnapshot("one","one"));
        Assert(events.Count(e=>e.Token=="")==before+1,"Existing trade-ID-correlated status acknowledgement remains supported without consuming offer tokens.");
        service.BeginTradeUpdate("legacy","adapter-token");
        Assert(service.CompleteTradeUpdate("legacy","adapter-token",TradeFailureReason.None,null),"Adapter explicit Begin/Complete result path is unchanged.");
    }
}
