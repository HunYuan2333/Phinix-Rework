using System.Collections.Generic;
using PhinixClient.Trade;

namespace Phinix.TradeExtension.Client
{
    internal sealed class FrameworkLegacyTradeClientAdapter :
        IFrameworkLegacyTradeRepositoryApi,
        IFrameworkLegacyTradeCompletionApi
    {
        private readonly PhinixFrameworkTradeClientService tradeService;

        public FrameworkLegacyTradeClientAdapter(PhinixFrameworkTradeClientService tradeService)
        {
            this.tradeService = tradeService;
        }

        public void UpsertTrade(FrameworkTradeStateSnapshot snapshot)
        {
            tradeService?.ApplyLegacyTradeSnapshot(snapshot);
        }

        public void RemoveTrade(string tradeId)
        {
            tradeService?.RemoveLegacyTrade(tradeId);
        }

        public void CompleteTrade(string tradeId, bool success, string otherPartyUuid, IEnumerable<TradeItemSnapshot> items)
        {
            tradeService?.CompleteLegacyTrade(tradeId, success, otherPartyUuid, items);
        }
    }
}
