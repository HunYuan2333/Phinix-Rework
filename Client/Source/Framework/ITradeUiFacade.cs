using System;
using System.Collections.Generic;
using Trading;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace PhinixClient.Framework
{
    public interface ITradeUiFacade
    {
        event EventHandler OnDisconnect;
        event EventHandler<UICreateTradeEventArgs> OnTradeCreationSuccess;
        event EventHandler<UICompleteTradeEventArgs> OnTradeCompleted;
        event EventHandler<UICompleteTradeEventArgs> OnTradeCancelled;
        event EventHandler<UITradeUpdateEventArgs> OnTradeUpdateSuccess;
        event EventHandler<UITradeUpdateEventArgs> OnTradeUpdateFailure;
        event EventHandler<UITradesSyncedEventArgs> OnTradesSynced;
        event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;

        ImmutableTrade[] GetTrades();

        void CancelTrade(string tradeId);

        void UpdateTradeItems(string tradeId, IEnumerable<ProtoThing> items, string token = "");

        void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null);

        LookTargets DropPods(IEnumerable<Thing> verseThings);

        void Log(LogEventArgs args);
    }
}
