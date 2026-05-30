using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace Phinix.TradeExtension.Client
{
    internal sealed class ClientTradeUiHostContext : ITradeUiHostContext
    {
        private readonly IClientTradeService tradeService;
        private readonly IClientSettingsContext settingsContext;
        private readonly IClientUserEventStream userEvents;
        private readonly Func<IEnumerable<Thing>, LookTargets> dropPods;
        private readonly Action<LogEventArgs> log;

        public ClientTradeUiHostContext(
            IClientTradeService tradeService,
            IClientSettingsContext settingsContext,
            IClientUserEventStream userEvents,
            Func<IEnumerable<Thing>, LookTargets> dropPods,
            Action<LogEventArgs> log)
        {
            this.tradeService = tradeService;
            this.settingsContext = settingsContext;
            this.userEvents = userEvents;
            this.dropPods = dropPods;
            this.log = log;
        }

        public IClientTradeService TradeService => tradeService;

        public bool AllItemsTradable => settingsContext.Get<bool>("trade.allItemsTradable", false);

        public event EventHandler OnDisconnect
        {
            add => userEvents.Disconnected += value;
            remove => userEvents.Disconnected -= value;
        }

        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged
        {
            add => userEvents.UserDisplayNameChanged += value;
            remove => userEvents.UserDisplayNameChanged -= value;
        }

        public LookTargets DropPods(IEnumerable<Thing> verseThings) => dropPods?.Invoke(verseThings) ?? LookTargets.Invalid;

        public void OpenTradeWindow(ClientTradeSnapshot trade) => Find.WindowStack.Add(new TradeWindow(trade, this));

        public void Log(LogEventArgs args) => log?.Invoke(args);
    }
}
