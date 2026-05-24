using System;
using System.Collections.Generic;
using Trading;
using Utils;

namespace PhinixClient.Framework
{
    public sealed class LegacyClientTradeServiceAdapter : IClientTradeService
    {
        private readonly ClientTrading trading;

        public LegacyClientTradeServiceAdapter(ClientTrading trading)
        {
            this.trading = trading;
        }

        public event EventHandler<LogEventArgs> OnLogEntry
        {
            add => trading.OnLogEntry += value;
            remove => trading.OnLogEntry -= value;
        }

        public event EventHandler<CreateTradeEventArgs> OnTradeCreationSuccess
        {
            add => trading.OnTradeCreationSuccess += value;
            remove => trading.OnTradeCreationSuccess -= value;
        }

        public event EventHandler<CreateTradeEventArgs> OnTradeCreationFailure
        {
            add => trading.OnTradeCreationFailure += value;
            remove => trading.OnTradeCreationFailure -= value;
        }

        public event EventHandler<CompleteTradeEventArgs> OnTradeCompleted
        {
            add => trading.OnTradeCompleted += value;
            remove => trading.OnTradeCompleted -= value;
        }

        public event EventHandler<CompleteTradeEventArgs> OnTradeCancelled
        {
            add => trading.OnTradeCancelled += value;
            remove => trading.OnTradeCancelled -= value;
        }

        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess
        {
            add => trading.OnTradeUpdateSuccess += value;
            remove => trading.OnTradeUpdateSuccess -= value;
        }

        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure
        {
            add => trading.OnTradeUpdateFailure += value;
            remove => trading.OnTradeUpdateFailure -= value;
        }

        public event EventHandler<TradesSyncedEventArgs> OnTradesSynced
        {
            add => trading.OnTradesSynced += value;
            remove => trading.OnTradesSynced -= value;
        }

        public void CreateTrade(string uuid) => trading.CreateTrade(uuid);

        public void CancelTrade(string tradeId) => trading.CancelTrade(tradeId);

        public string[] GetTradeIds() => trading.GetTradeIds();

        public ImmutableTrade[] GetTrades() => trading.GetTrades();

        public ImmutableTrade[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids) => trading.GetTradesExceptWith(otherPartyUuids);

        public bool TryGetTrade(string tradeId, out ImmutableTrade trade) => trading.TryGetTrade(tradeId, out trade);

        public bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid) => trading.TryGetOtherPartyUuid(tradeId, out otherPartyUuid);

        public bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted) => trading.TryGetOtherPartyAccepted(tradeId, out otherPartyAccepted);

        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted) => trading.TryGetPartyAccepted(tradeId, partyUuid, out accepted);

        public bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<ProtoThing> items) => trading.TryGetItemsOnOffer(tradeId, uuid, out items);

        public void UpdateTradeItems(string tradeId, IEnumerable<ProtoThing> items, string token = "") => trading.UpdateItems(tradeId, items, token);

        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null) => trading.UpdateStatus(tradeId, accepted, cancelled);

        public void RequestInitialSync()
        {
        }
    }
}
