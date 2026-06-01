using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using PhinixClient.Trade;
using RimWorld;
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
        private readonly IClientMainThreadDispatcher dispatcher;
        private readonly IClientWindowService windowService;
        private readonly Action<LogEventArgs> log;

        public ClientTradeUiHostContext(
            IClientTradeService tradeService,
            IClientSettingsContext settingsContext,
            IClientUserEventStream userEvents,
            IClientMainThreadDispatcher dispatcher,
            IClientWindowService windowService,
            Action<LogEventArgs> log)
        {
            this.tradeService = tradeService;
            this.settingsContext = settingsContext;
            this.userEvents = userEvents;
            this.dispatcher = dispatcher;
            this.windowService = windowService;
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

        public LookTargets DropPods(IEnumerable<Thing> verseThings)
        {
            Map map = settingsContext.Get("trade.dropCurrentMap", false)
                ? Find.CurrentMap
                : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            DropPodUtility.DropThingsNear(dropSpot, map, verseThings, canRoofPunch: false);

            return new LookTargets(dropSpot, map);
        }

        public void RunOnMainThread(Action action)
        {
            if (dispatcher != null)
            {
                dispatcher.Enqueue(action);
                return;
            }

            action?.Invoke();
        }

        public void OpenTradeWindow(ClientTradeSnapshot trade)
        {
            RunOnMainThread(() => windowService?.Open(new TradeWindow(trade, this)));
        }

        public void Log(LogEventArgs args) => log?.Invoke(args);
    }
}
