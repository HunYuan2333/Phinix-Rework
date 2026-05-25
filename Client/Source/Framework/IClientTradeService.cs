using System;
using System.Collections.Generic;
using PhinixClient.Trade;
using Utils;

namespace PhinixClient.Framework
{
    public interface IClientTradeService
    {
        event EventHandler<LogEventArgs> OnLogEntry;
        event EventHandler<TradeCreationEventArgs> OnTradeCreationSuccess;
        event EventHandler<TradeCreationEventArgs> OnTradeCreationFailure;
        event EventHandler<TradeCompletionEventArgs> OnTradeCompleted;
        event EventHandler<TradeCompletionEventArgs> OnTradeCancelled;
        event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess;
        event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure;
        event EventHandler<TradesSyncedEventArgs> OnTradesSynced;

        void CreateTrade(string uuid);

        void CancelTrade(string tradeId);

        string[] GetTradeIds();

        ClientTradeSnapshot[] GetTrades();

        ClientTradeSnapshot[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids);

        bool TryGetTrade(string tradeId, out ClientTradeSnapshot trade);

        bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid);

        bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted);

        bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted);

        bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<TradeItemSnapshot> items);

        void UpdateTradeItems(string tradeId, IEnumerable<TradeItemSnapshot> items, string token = "");

        void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null);

        void RequestInitialSync();
    }
}
