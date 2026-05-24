using System;
using System.Collections.Generic;
using Trading;
using Utils;

namespace PhinixClient.Framework
{
    public interface IClientTradeService
    {
        event EventHandler<LogEventArgs> OnLogEntry;
        event EventHandler<CreateTradeEventArgs> OnTradeCreationSuccess;
        event EventHandler<CreateTradeEventArgs> OnTradeCreationFailure;
        event EventHandler<CompleteTradeEventArgs> OnTradeCompleted;
        event EventHandler<CompleteTradeEventArgs> OnTradeCancelled;
        event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess;
        event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure;
        event EventHandler<TradesSyncedEventArgs> OnTradesSynced;

        void CreateTrade(string uuid);

        void CancelTrade(string tradeId);

        string[] GetTradeIds();

        ImmutableTrade[] GetTrades();

        ImmutableTrade[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids);

        bool TryGetTrade(string tradeId, out ImmutableTrade trade);

        bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid);

        bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted);

        bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted);

        bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<ProtoThing> items);

        void UpdateTradeItems(string tradeId, IEnumerable<ProtoThing> items, string token = "");

        void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null);

        void RequestInitialSync();
    }
}
