using System;
using System.Linq;
using PhinixClient.Trade;

namespace Phinix.TradeExtension.Client
{
    // Original Phinix confirms its current quote without a wire revision.
    // This adapter is used only after the connection has been identified as Legacy.
    internal static class LegacyTradeConsentAdapter
    {
        internal static long? Observe(ClientTradeSnapshot displayed, IFrameworkTradeClientApi service)
        {
            var current = service.GetRepositoryTrades().SingleOrDefault(trade => trade.TradeId == displayed?.TradeId);
            if (!TradeSnapshotObservation.MatchesSnapshot(displayed, current))
                throw new InvalidOperationException("Refresh the trade quote before confirming.");

            return null;
        }
    }
}
