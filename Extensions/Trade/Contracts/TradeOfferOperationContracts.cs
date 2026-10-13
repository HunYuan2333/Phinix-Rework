using System.Collections.Generic;
namespace Phinix.TradeExtension
{
    public enum TradeOfferOperationState { Preparing, AwaitingResult, Unknown, LocalSettlementPending }
    public sealed class TradeOfferOperationInfo
    {
        public TradeOfferOperationInfo(string tradeId,string token,bool reset,TradeOfferOperationState state,string message,bool canRetry)
        {TradeId=tradeId;Token=token;IsReset=reset;State=state;Message=message;CanRetry=canRetry;}
        public string TradeId {get;}public string Token {get;}public bool IsReset {get;}
        public TradeOfferOperationState State {get;}public string Message {get;}public bool CanRetry {get;}
    }
    // Trade-owned current-session operations. No Thing access or automatic rollback on view closure.
    public interface ITradeOfferOperationApi
    {
        long Revision {get;}
        IReadOnlyList<TradeOfferOperationInfo> GetPending();
        bool RetryLocalSettlement(string token);
    }
}
