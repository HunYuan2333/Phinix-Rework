namespace Phinix.TradeExtension
{
    // Projection facts only. Row IDs are ephemeral view identities, never item ownership or mutation ports.
    public sealed class TradeOfferPresentationActionContext
    {
        public const string PointId="builtin.trade.offer.actions";
        public TradeOfferPresentationActionContext(string pageId,long revision,string tradeId,string selectedRowId,int availableUnits,int draftUnits,bool busy,bool accepted,bool otherPartyAccepted)
        {PageId=pageId;Revision=revision;TradeId=tradeId;SelectedRowId=selectedRowId;AvailableUnits=availableUnits;DraftUnits=draftUnits;Busy=busy;Accepted=accepted;OtherPartyAccepted=otherPartyAccepted;}
        public string PageId {get;} public long Revision {get;} public string TradeId {get;}
        public string SelectedRowId {get;} public int AvailableUnits {get;} public int DraftUnits {get;}
        public bool Busy {get;} public bool Accepted {get;} public bool OtherPartyAccepted {get;}
    }
}
