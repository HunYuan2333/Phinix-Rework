using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClientTrade = PhinixClient.Trade;
using TradeLegacyConversions = PhinixClient.Trade.TradeLegacyConversions;
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
            trading.OnTradeCreationSuccess += onLegacyTradeCreationSuccess;
            trading.OnTradeCreationFailure += onLegacyTradeCreationFailure;
            trading.OnTradeCompleted += onLegacyTradeCompleted;
            trading.OnTradeCancelled += onLegacyTradeCancelled;
            trading.OnTradeUpdateSuccess += onLegacyTradeUpdateSuccess;
            trading.OnTradeUpdateFailure += onLegacyTradeUpdateFailure;
            trading.OnTradesSynced += onLegacyTradesSynced;
        }

        public event EventHandler<LogEventArgs> OnLogEntry
        {
            add => trading.OnLogEntry += value;
            remove => trading.OnLogEntry -= value;
        }

        public event EventHandler<PhinixClientTrade.TradeCreationEventArgs> OnTradeCreationSuccess;

        public event EventHandler<PhinixClientTrade.TradeCreationEventArgs> OnTradeCreationFailure;

        public event EventHandler<PhinixClientTrade.TradeCompletionEventArgs> OnTradeCompleted;

        public event EventHandler<PhinixClientTrade.TradeCompletionEventArgs> OnTradeCancelled;

        public event EventHandler<PhinixClientTrade.TradeUpdateEventArgs> OnTradeUpdateSuccess;

        public event EventHandler<PhinixClientTrade.TradeUpdateEventArgs> OnTradeUpdateFailure;

        public event EventHandler<PhinixClientTrade.TradesSyncedEventArgs> OnTradesSynced;

        public void CreateTrade(string uuid) => trading.CreateTrade(uuid);

        public void CancelTrade(string tradeId) => trading.CancelTrade(tradeId);

        public string[] GetTradeIds() => trading.GetTradeIds();

        public PhinixClientTrade.ClientTradeSnapshot[] GetTrades() => trading.GetTrades().Select(TradeLegacyConversions.ToClientTrade).ToArray();

        public PhinixClientTrade.ClientTradeSnapshot[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids) => trading.GetTradesExceptWith(otherPartyUuids).Select(TradeLegacyConversions.ToClientTrade).ToArray();

        public bool TryGetTrade(string tradeId, out PhinixClientTrade.ClientTradeSnapshot trade)
        {
            if (!trading.TryGetTrade(tradeId, out ImmutableTrade legacyTrade))
            {
                trade = null;
                return false;
            }

            trade = TradeLegacyConversions.ToClientTrade(legacyTrade);
            return true;
        }

        public bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid) => trading.TryGetOtherPartyUuid(tradeId, out otherPartyUuid);

        public bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted) => trading.TryGetOtherPartyAccepted(tradeId, out otherPartyAccepted);

        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted) => trading.TryGetPartyAccepted(tradeId, partyUuid, out accepted);

        public bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<PhinixClientTrade.TradeItemSnapshot> items)
        {
            items = Array.Empty<PhinixClientTrade.TradeItemSnapshot>();
            if (!trading.TryGetItemsOnOffer(tradeId, uuid, out IEnumerable<ProtoThing> legacyItems))
            {
                return false;
            }

            items = legacyItems.Select(TradeLegacyConversions.ToTradeItemSnapshot).ToArray();
            return true;
        }

        public void UpdateTradeItems(string tradeId, IEnumerable<PhinixClientTrade.TradeItemSnapshot> items, string token = "")
        {
            trading.UpdateItems(tradeId, (items ?? Enumerable.Empty<PhinixClientTrade.TradeItemSnapshot>()).Select(TradeLegacyConversions.ToLegacyProtoThing), token);
        }

        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null) => trading.UpdateStatus(tradeId, accepted, cancelled);

        public void RequestInitialSync()
        {
        }

        private void onLegacyTradeCreationSuccess(object sender, Trading.CreateTradeEventArgs args)
        {
            if (trading.TryGetTrade(args.TradeId, out ImmutableTrade trade))
            {
                OnTradeCreationSuccess?.Invoke(this, new PhinixClientTrade.TradeCreationEventArgs(TradeLegacyConversions.ToClientTrade(trade)));
                return;
            }

            OnTradeCreationSuccess?.Invoke(this, new PhinixClientTrade.TradeCreationEventArgs(new PhinixClientTrade.ClientTradeSnapshot(args.TradeId, new UserManagement.ImmutableUser(args.OtherPartyUuid))));
        }

        private void onLegacyTradeCreationFailure(object sender, Trading.CreateTradeEventArgs args)
        {
            OnTradeCreationFailure?.Invoke(this, new PhinixClientTrade.TradeCreationEventArgs(args.OtherPartyUuid, TradeLegacyConversions.ToClientFailureReason(args.FailureReason), args.FailureMessage));
        }

        private void onLegacyTradeCompleted(object sender, Trading.CompleteTradeEventArgs args)
        {
            OnTradeCompleted?.Invoke(this, new PhinixClientTrade.TradeCompletionEventArgs(args.TradeId, true, args.OtherPartyUuid, args.Items.Select(TradeLegacyConversions.ToTradeItemSnapshot)));
        }

        private void onLegacyTradeCancelled(object sender, Trading.CompleteTradeEventArgs args)
        {
            OnTradeCancelled?.Invoke(this, new PhinixClientTrade.TradeCompletionEventArgs(args.TradeId, false, args.OtherPartyUuid, args.Items.Select(TradeLegacyConversions.ToTradeItemSnapshot)));
        }

        private void onLegacyTradeUpdateSuccess(object sender, Trading.TradeUpdateEventArgs args)
        {
            PhinixClientTrade.ClientTradeSnapshot trade = TryGetTrade(args.TradeId, out PhinixClientTrade.ClientTradeSnapshot existingTrade)
                ? existingTrade
                : new PhinixClientTrade.ClientTradeSnapshot(args.TradeId, new UserManagement.ImmutableUser());
            OnTradeUpdateSuccess?.Invoke(this, new PhinixClientTrade.TradeUpdateEventArgs(trade, args.Token));
        }

        private void onLegacyTradeUpdateFailure(object sender, Trading.TradeUpdateEventArgs args)
        {
            PhinixClientTrade.ClientTradeSnapshot trade = TryGetTrade(args.TradeId, out PhinixClientTrade.ClientTradeSnapshot existingTrade)
                ? existingTrade
                : new PhinixClientTrade.ClientTradeSnapshot(args.TradeId, new UserManagement.ImmutableUser());
            OnTradeUpdateFailure?.Invoke(this, new PhinixClientTrade.TradeUpdateEventArgs(trade, TradeLegacyConversions.ToClientFailureReason(args.FailureReason), args.FailureMessage, args.Token));
        }

        private void onLegacyTradesSynced(object sender, Trading.TradesSyncedEventArgs args)
        {
            List<PhinixClientTrade.ClientTradeSnapshot> trades = new List<PhinixClientTrade.ClientTradeSnapshot>();
            foreach (string tradeId in args.TradeIds)
            {
                if (TryGetTrade(tradeId, out PhinixClientTrade.ClientTradeSnapshot trade))
                {
                    trades.Add(trade);
                }
            }

            OnTradesSynced?.Invoke(this, new PhinixClientTrade.TradesSyncedEventArgs(trades));
        }
    }
}
