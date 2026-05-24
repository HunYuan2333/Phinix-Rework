using System;
using System.Collections.Generic;
using Trading;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace PhinixClient.Framework
{
    public sealed class ClientTradeUiFacade : ITradeUiFacade
    {
        private readonly Client client;

        public ClientTradeUiFacade(Client client)
        {
            this.client = client;
        }

        public event EventHandler OnDisconnect
        {
            add => client.OnDisconnect += value;
            remove => client.OnDisconnect -= value;
        }

        public event EventHandler<UICreateTradeEventArgs> OnTradeCreationSuccess
        {
            add => client.OnTradeCreationSuccess += value;
            remove => client.OnTradeCreationSuccess -= value;
        }

        public event EventHandler<UICompleteTradeEventArgs> OnTradeCompleted
        {
            add => client.OnTradeCompleted += value;
            remove => client.OnTradeCompleted -= value;
        }

        public event EventHandler<UICompleteTradeEventArgs> OnTradeCancelled
        {
            add => client.OnTradeCancelled += value;
            remove => client.OnTradeCancelled -= value;
        }

        public event EventHandler<UITradeUpdateEventArgs> OnTradeUpdateSuccess
        {
            add => client.OnTradeUpdateSuccess += value;
            remove => client.OnTradeUpdateSuccess -= value;
        }

        public event EventHandler<UITradeUpdateEventArgs> OnTradeUpdateFailure
        {
            add => client.OnTradeUpdateFailure += value;
            remove => client.OnTradeUpdateFailure -= value;
        }

        public event EventHandler<UITradesSyncedEventArgs> OnTradesSynced
        {
            add => client.OnTradesSynced += value;
            remove => client.OnTradesSynced -= value;
        }

        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged
        {
            add => client.OnUserDisplayNameChanged += value;
            remove => client.OnUserDisplayNameChanged -= value;
        }

        public ImmutableTrade[] GetTrades() => client.GetTrades();

        public void CancelTrade(string tradeId) => client.CancelTrade(tradeId);

        public void UpdateTradeItems(string tradeId, IEnumerable<ProtoThing> items, string token = "") => client.UpdateTradeItems(tradeId, items, token);

        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null) => client.UpdateTradeStatus(tradeId, accepted, cancelled);

        public LookTargets DropPods(IEnumerable<Thing> verseThings) => client.DropPods(verseThings);

        public void Log(LogEventArgs args) => client.Log(args);
    }
}
