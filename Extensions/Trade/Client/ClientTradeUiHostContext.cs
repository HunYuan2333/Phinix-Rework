using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Phinix.InventoryExtension;
using RimWorld;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace Phinix.TradeExtension.Client
{
    internal sealed class ClientTradeUiHostContext : ITradeUiHostContext, IDisposable
    {
        private readonly IClientTradeService tradeService;
        private IClientSettingsContext settingsContext;
        private IClientUserEventStream userEvents;
        private IClientMainThreadDispatcher dispatcher;
        private IClientWindowService windowService;
        private Action<LogEventArgs> log;
        private IInventoryReadApi inventoryReadApi;
        private IInventoryReservationApi inventoryReservationApi;
        private volatile bool started;
        private int generation;
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
            Action<LogEventArgs> log,
            IInventoryReadApi inventoryReadApi,
            IInventoryReservationApi inventoryReservationApi)
        {
            this.settingsContext = settingsContext;
            this.userEvents = userEvents;
            this.dispatcher = dispatcher;
            this.windowService = windowService;
            this.log = log;
            this.inventoryReadApi = inventoryReadApi;
            this.inventoryReservationApi = inventoryReservationApi;
        }

        public IClientTradeService TradeService => tradeService;
        internal IInventoryReadApi InventoryReadApi => started ? inventoryReadApi : null;
        internal IInventoryReservationApi InventoryReservationApi => started ? inventoryReservationApi : null;

        internal void Start()
        {
            if (started) return;
            System.Threading.Interlocked.Increment(ref generation);
            started = true;
            userEvents.Disconnected += onDisconnected;
            userEvents.UserDisplayNameChanged += onUserDisplayNameChanged;
        }

        internal void Stop()
        {
            if (!started) return;
            started = false;
            System.Threading.Interlocked.Increment(ref generation);
            var failures = new List<Exception>();
            TradeLifetimeCleanup.Try(() => userEvents.Disconnected -= onDisconnected, failures);
            TradeLifetimeCleanup.Try(() => userEvents.UserDisplayNameChanged -= onUserDisplayNameChanged, failures);
            TradeLifetimeCleanup.ThrowIfFailed(failures);
        }

        public void Dispose()
        {
            try { Stop(); }
            finally
            {
                settingsContext = null;
                userEvents = null;
                dispatcher = null;
                windowService = null;
                inventoryReadApi = null;
                inventoryReservationApi = null;
                log = null;
            }
        }

        public bool AllItemsTradable => started && settingsContext.Get<bool>("trade.allItemsTradable", false);

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
            if (!started) throw new InvalidOperationException("Trade has stopped.");
            Map map = settingsContext.Get("trade.dropCurrentMap", false)
                ? Find.CurrentMap
                : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (!InventoryDropDelivery.TryResolveTradeDropTarget(map, out IntVec3 dropSpot, out string reason))
                throw new InvalidOperationException(reason);
            InventoryDeliveryResult result = InventoryDropDelivery.DeliverThings(verseThings, map, dropSpot);
            if (!result.Succeeded) throw new InvalidOperationException(result.Reason ?? "Drop-pod delivery failed.");

            return new LookTargets(dropSpot, map);
        }

        public void RunOnMainThread(Action action)
        {
            if (!started || action == null) return;
            int expected = generation;
            Action run = () => { if (started && expected == generation) action(); };
            if (dispatcher != null) dispatcher.Enqueue(run);
            else run();
        }

        public void OpenTradeWindow(ClientTradeSnapshot trade)
        {
            RunOnMainThread(() => windowService?.Open(new TradeWindow(trade, this)));
        }

        public void Log(LogEventArgs args) => log?.Invoke(args);

        private void onDisconnected(object sender, EventArgs args) { if (started) disconnected?.Invoke(sender, args); }

        private void onUserDisplayNameChanged(object sender, UserDisplayNameChangedEventArgs args)
            { if (started) userDisplayNameChanged?.Invoke(sender, args); }
    }
}
