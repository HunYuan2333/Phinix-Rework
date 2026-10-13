namespace Phinix.TradeExtension
{
    // Read-only projection facts; no item escrow, mutation port or mutable snapshot authority.
    public class TradePresentationActionContext
    {
        public const string PointId = "builtin.trade.list.actions";
        public TradePresentationActionContext(string pageId,long revision,string tradeId,bool accepted,bool otherPartyAccepted,int ourStacks,int theirStacks,string operationToken=null,string operationState=null,bool canRetrySettlement=false,bool tradeAvailable=true,string inventoryReservationId=null,string inventoryOperationId=null)
        {PageId=pageId;Revision=revision;TradeId=tradeId;Accepted=accepted;OtherPartyAccepted=otherPartyAccepted;OurStacks=ourStacks;TheirStacks=theirStacks;OperationToken=operationToken;OperationState=operationState;CanRetrySettlement=canRetrySettlement;TradeAvailable=tradeAvailable;InventoryReservationId=inventoryReservationId;InventoryOperationId=inventoryOperationId;}
        public string InventoryReservationId {get;} public string InventoryOperationId {get;}
        public string PageId {get;} public long Revision {get;} public string TradeId {get;}
        public string OperationToken {get;} public string OperationState {get;} public bool CanRetrySettlement {get;} public bool TradeAvailable {get;}
        public bool Accepted {get;} public bool OtherPartyAccepted {get;} public int OurStacks {get;} public int TheirStacks {get;}
    }
}
