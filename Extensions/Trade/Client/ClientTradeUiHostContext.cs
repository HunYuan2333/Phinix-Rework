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
        private IClientSettingsContext settingsContext;
        private IClientUserEventStream userEvents;
        private IClientMainThreadDispatcher dispatcher;
        private IClientWindowService windowService;
        private Action<LogEventArgs> log;
        private bool started;
        private event EventHandler disconnected;
        private event EventHandler<UserDisplayNameChangedEventArgs> userDisplayNameChanged;

        public ClientTradeUiHostContext(IClientTradeService tradeService)
        {
            this.tradeService = tradeService;
        }

        internal void Initialize(
            IClientSettingsContext settingsContext,
            IClientUserEventStream userEvents,
            IClientMainThreadDispatcher dispatcher,
            IClientWindowService windowService,
            Action<LogEventArgs> log)
        {
            this.settingsContext = settingsContext;
            this.userEvents = userEvents;
            this.dispatcher = dispatcher;
            this.windowService = windowService;
            this.log = log;
        }

        public IClientTradeService TradeService => tradeService;

        internal void Start()
        {
            if (started) return;
            userEvents.Disconnected += onDisconnected;
            userEvents.UserDisplayNameChanged += onUserDisplayNameChanged;
            started = true;
        }

        internal void Stop()
        {
            if (!started) return;
            userEvents.Disconnected -= onDisconnected;
            userEvents.UserDisplayNameChanged -= onUserDisplayNameChanged;
            started = false;
        }

        public bool AllItemsTradable => settingsContext.Get<bool>("trade.allItemsTradable", false);

        public event EventHandler OnDisconnect
        {
            add => disconnected += value;
            remove => disconnected -= value;
        }

        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged
        {
            add => userDisplayNameChanged += value;
            remove => userDisplayNameChanged -= value;
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

        private void onDisconnected(object sender, EventArgs args) => disconnected?.Invoke(sender, args);

        private void onUserDisplayNameChanged(object sender, UserDisplayNameChangedEventArgs args) =>
            userDisplayNameChanged?.Invoke(sender, args);
    }
}
