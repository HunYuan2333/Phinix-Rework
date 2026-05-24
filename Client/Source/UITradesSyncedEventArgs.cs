using System;
using System.Collections.Generic;
using System.Linq;
using Trading;
using PhinixClient.Framework;

namespace PhinixClient
{
    public class UITradesSyncedEventArgs : TradesSyncedEventArgs
    {
        /// <summary>
        /// Collection of synchronised trades and their details.
        /// </summary>
        public readonly ImmutableTrade[] Trades;
        
        public UITradesSyncedEventArgs(IEnumerable<ImmutableTrade> trades) : base(trades.Select(t => t.TradeId))
        {
            this.Trades = trades.ToArray();
        }

        /// <summary>
        /// Converts a base <see cref="TradesSyncedEventArgs"/> into a <see cref="UITradesSyncedEventArgs"/> using the
        /// given <see cref="IClientTradeFacade"/> for trade details lookup.
        /// </summary>
        /// <param name="args">Base event args</param>
        /// <param name="tradeFacade"><see cref="IClientTradeFacade"/> instance for trade details lookup</param>
        /// <returns>Converted event args</returns>
        public static UITradesSyncedEventArgs FromTradesSyncedEventArgs(TradesSyncedEventArgs args, IClientTradeFacade tradeFacade)
        {
            // Convert each of the received trades
            List<ImmutableTrade> convertedTrades = new List<ImmutableTrade>();
            foreach (string tradeId in args.TradeIds)
            {
                if (tradeFacade != null && tradeFacade.TryGetTrade(tradeId, out ImmutableTrade trade))
                {
                    convertedTrades.Add(trade);
                }
            }

            return new UITradesSyncedEventArgs(convertedTrades);
        }
    }
}
