using System;
using System.Collections.Generic;
using PhinixClient.Trade;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace PhinixClient.Framework
{
    public interface ITradeUiFacade
    {
        event EventHandler OnDisconnect;
        event EventHandler<TradeCreationEventArgs> OnTradeCreationSuccess;
        event EventHandler<TradeCompletionEventArgs> OnTradeCompleted;
        event EventHandler<TradeCompletionEventArgs> OnTradeCancelled;
        event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess;
        event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure;
        event EventHandler<TradesSyncedEventArgs> OnTradesSynced;
        event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;

        ClientTradeSnapshot[] GetTrades();

        void CancelTrade(string tradeId);

        void UpdateTradeItems(string tradeId, IEnumerable<TradeItemSnapshot> items, string token = "");

        void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null);

        LookTargets DropPods(IEnumerable<Thing> verseThings);

        void Log(LogEventArgs args);
    }
}
