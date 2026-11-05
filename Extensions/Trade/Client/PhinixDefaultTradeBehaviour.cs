using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.Trade;
using RimWorld;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace Phinix.TradeExtension.Client
{
    internal sealed class PhinixDefaultTradeBehaviour : IDisposable
    {
        private readonly IClientTradeService tradeService;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientSettingsContext settingsContext;
        private readonly IClientMainThreadDispatcher dispatcher;
        private readonly IClientGameWindowService windowService;
        private readonly ClientTradeUiHostContext tradeUiHostContext;
        private readonly TradeInventoryDelivery inventoryDelivery;
        private readonly IInventoryReadApi inventoryReadApi;
        private readonly Action<string, bool> acknowledgeCompletion;
        private readonly Action<LogEventArgs> log;
        private readonly Action<string,string> notifyDeliveryFailure;
        private readonly HashSet<string> waitingForTradeCreationWith = new HashSet<string>();
        private readonly object waitingLock = new object();
        private readonly Dictionary<string, PendingCompletion> pendingCompletions = new Dictionary<string, PendingCompletion>(StringComparer.OrdinalIgnoreCase);
        private TradeCompletionInbox completionJournal;
        private Func<string> completionEndpoint;
        private IClientGameScopeService completionGameScopes;
        private IInventoryReceiveScopeApi completionReceiveScopes;
        internal void ConfigureReceiveScopes(IInventoryReceiveScopeApi scopes)
        { if(started) throw new InvalidOperationException("Configure receive scopes before activation.");completionReceiveScopes=scopes; }
        internal void ConfigureGameScopes(IClientGameScopeService scopes)
        { if(started) throw new InvalidOperationException("Configure game scopes before activation."); completionGameScopes=scopes; }
        internal void ConfigureCompletionJournal(TradeCompletionInbox journal,Func<string> endpoint)
        { if(started) throw new InvalidOperationException("Configure delivery before activation."); completionJournal=journal;completionEndpoint=endpoint; }

        public PhinixDefaultTradeBehaviour(
            IClientTradeService tradeService,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext,
            IClientMainThreadDispatcher dispatcher,
            IClientGameWindowService windowService,
            ClientTradeUiHostContext tradeUiHostContext,
            TradeInventoryDelivery inventoryDelivery,
            IInventoryReadApi inventoryReadApi,
            Action<string, bool> acknowledgeCompletion,
            Action<LogEventArgs> log,
            Action<string,string> notifyDeliveryFailure=null)
        {
            this.tradeService = tradeService;
            this.userDirectory = userDirectory;
            this.settingsContext = settingsContext;
            this.dispatcher = dispatcher;
            this.windowService = windowService;
            this.tradeUiHostContext = tradeUiHostContext;
            this.inventoryDelivery = inventoryDelivery;
            this.inventoryReadApi = inventoryReadApi;
            this.acknowledgeCompletion = acknowledgeCompletion;
            this.log = log;
            this.notifyDeliveryFailure=notifyDeliveryFailure??((key,reason)=>Messages.Message(key.Translate(reason),MessageTypeDefOf.RejectInput,false));
        }

        private volatile bool started;
        private int generation;

        public void Start()
        {
            if (started) return;
            System.Threading.Interlocked.Increment(ref generation);
            started = true;
            tradeService.OnTradeCreationRequested += onTradeCreationRequested;
            tradeService.OnTradeCreationSuccess += onTradeCreationSuccess;
            tradeService.OnTradeCreationFailure += onTradeCreationFailure;
            tradeService.OnTradeCompleted += onTradeCompleted;
            tradeService.OnTradeCancelled += onTradeCancelled;
            tradeService.OnTradeUpdateFailure += onTradeUpdateFailure;
            inventoryReadApi.AvailabilityChanged += onInventoryAvailabilityChanged;
            settingsContext.OnSettingChanged += onSettingChanged;
        }

        public void Stop()
        {
            if (!started) return;
            started = false;
            System.Threading.Interlocked.Increment(ref generation);
            var failures = new List<Exception>();
            TradeLifetimeCleanup.Try(() => tradeService.OnTradeCreationRequested -= onTradeCreationRequested, failures);
            TradeLifetimeCleanup.Try(() => tradeService.OnTradeCreationSuccess -= onTradeCreationSuccess, failures);
            TradeLifetimeCleanup.Try(() => tradeService.OnTradeCreationFailure -= onTradeCreationFailure, failures);
            TradeLifetimeCleanup.Try(() => tradeService.OnTradeCompleted -= onTradeCompleted, failures);
            TradeLifetimeCleanup.Try(() => tradeService.OnTradeCancelled -= onTradeCancelled, failures);
            TradeLifetimeCleanup.Try(() => tradeService.OnTradeUpdateFailure -= onTradeUpdateFailure, failures);
            TradeLifetimeCleanup.Try(() => inventoryReadApi.AvailabilityChanged -= onInventoryAvailabilityChanged, failures);
            TradeLifetimeCleanup.Try(() => settingsContext.OnSettingChanged -= onSettingChanged, failures);
            pendingCompletions.Clear();
            lock (waitingLock) waitingForTradeCreationWith.Clear();
            TradeLifetimeCleanup.ThrowIfFailed(failures);
        }

        public void Dispose() { Stop(); }

        private void Enqueue(Action action)
        {
            if (!started) return;
            int expected = generation;
            dispatcher.Enqueue(() => { if (started && expected == generation) action(); });
        }

        private void onTradeCreationRequested(object sender, TradeCreationEventArgs args)
        {
            if (!started) return;
            if (string.IsNullOrEmpty(args?.OtherPartyUuid))
            {
                return;
            }

            lock (waitingLock)
            {
                waitingForTradeCreationWith.Add(args.OtherPartyUuid);
            }
        }

        private void onTradeCreationSuccess(object sender, TradeCreationEventArgs args)
        {
            if (!started) return;
            try
            {
                if (args?.Trade == null) return;
                if (!shouldDisplayTradeEvent(args.OtherPartyUuid)) return;
                if (tryQueueTradeWindow(args.OtherPartyUuid, args.Trade)) return;

                string displayName = resolveDisplayName(args.OtherPartyUuid);
                Enqueue(() =>
                {
                    LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeCreated");
                    Find.LetterStack.ReceiveLetter(
                        "Phinix_trade_tradeReceivedLetter_label".Translate(displayName),
                        "Phinix_trade_tradeReceivedLetter_description".Translate(displayName),
                        letterDef);
                });
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEventArgs(
                    $"[PhinixDefaultTradeBehaviour] onTradeCreationSuccess threw: {ex}",
                    LogLevel.ERROR));
            }
        }

        private void onTradeCreationFailure(object sender, TradeCreationEventArgs args)
        {
            if (!started) return;
            try
            {
                if (args == null) return;

                lock (waitingLock)
                {
                    waitingForTradeCreationWith.Remove(args.OtherPartyUuid);
                }

                Enqueue(() =>
                    windowService.Open(new Dialog_MessageBox(
                        title: "Phinix_error_tradeCreationFailedTitle".Translate(),
                        text: "Phinix_error_tradeCreationFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString()))));
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEventArgs(
                    $"[PhinixDefaultTradeBehaviour] onTradeCreationFailure threw: {ex}",
                    LogLevel.ERROR));
            }
        }

        private void onTradeCompleted(object sender, TradeCompletionEventArgs args)
        {
            if (!started) return;
            depositCompletion(args, true);
        }

        private void onTradeCancelled(object sender, TradeCompletionEventArgs args)
        {
            if (!started) return;
            depositCompletion(args, false);
        }

        private void depositCompletion(TradeCompletionEventArgs args, bool completed)
        {
            try
            {
                if (args == null) return;
                string displayName = resolveDisplayName(args.OtherPartyUuid);
                bool display = completed || shouldDisplayTradeEvent(args.OtherPartyUuid);
                string recipient=userDirectory.Uuid,endpoint=completionEndpoint?.Invoke();
                // Capture before queueing. Capture never accesses Verse from a network thread.
                var gameScope=args is ScopedTradeCompletionEventArgs received ? received.GameScope : completionGameScopes?.Capture();
                var saveScope=args is ScopedTradeCompletionEventArgs scoped ? scoped.SaveScope : completionReceiveScopes?.CaptureReceiveScope();
                var scope=new TradeCompletionScope(recipient,endpoint,gameScope==null?null:(Func<bool>)gameScope.IsCurrent,
                    ()=>userDirectory.Uuid,()=>completionEndpoint?.Invoke(),
                    (args.ItemPayloads?.Length??0)>0 || (args.Items?.Length??0)>0);
                Enqueue(() =>
                {
                    string key = completionKey(args.TradeId, !completed);
                    if(pendingCompletions.ContainsKey(key)) return;
                    pendingCompletions[key] = new PendingCompletion(args, displayName, completed, display)
                    {
                        Recipient=recipient,Endpoint=endpoint,
                        Scope=scope,SaveScope=saveScope
                    };
                    tryDepositPending(key, true);
                });
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEventArgs(
                    $"[PhinixDefaultTradeBehaviour] depositCompletion threw: {ex}",
                    LogLevel.ERROR));
            }
        }

        private void onInventoryAvailabilityChanged(object sender, EventArgs args)
        {
            if (!started) return;
            Enqueue(() =>
            {
                foreach (string key in pendingCompletions.Keys.ToArray())
                    tryDepositPending(key, false);
            });
        }

        private void onSettingChanged(string key, object value)
        {
            if (!started) return;
            if (key != InventoryDeliveryPreference.SettingKey) return;
            Enqueue(() =>
            {
                foreach (string pendingKey in pendingCompletions.Keys.ToArray())
                    tryDepositPending(pendingKey, false);
            });
        }

        private void tryDepositPending(string key, bool notifyFailure)
        {
            if (!pendingCompletions.TryGetValue(key, out PendingCompletion pending)) return;
            if(pending.Attempted) return;
            if(pending.ReconciliationOnly) return;
            try
            {
                if(completionJournal==null || pending.Scope==null || !pending.Scope.IsValid())
                {
                    pending.ReconciliationOnly=true;pending.HoldMessageKey="Phinix_trade_deliveryScopePending";
                    throw new InvalidOperationException("Trade delivery scope is unavailable; reconciliation required.");
                }
                bool useInventory = InventoryDeliveryPreference.UsesInventory(
                    settingsContext.Get(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.DirectDrop));
                bool nonempty=(pending.Args.ItemPayloads?.Length??0)>0 || (pending.Args.Items?.Length??0)>0;
                if(nonempty)
                {
                    var origin=pending.SaveScope;
                    if(origin==null || !origin.IsCurrent() || origin.SaveIdentity!=inventoryReadApi.GetStatus()?.SaveIdentity)
                    {
                        pending.ReconciliationOnly=true;
                        pending.HoldMessageKey=origin==null?"Phinix_trade_deliverySavePending":"Phinix_trade_deliveryScopePending";
                        throw new InvalidOperationException("Trade receipt belongs to an unavailable or changed inventory save scope; reconciliation required.");
                    }
                    string save=origin.SaveIdentity;
                    completionJournal.BindSave(pending.Endpoint,pending.Recipient,pending.Args.TradeId,pending.Completed,save);
                }
                // Publish the exact idempotent request before its attempt marker or any external deposit.
                // A crash after plan publication can recover without guessing a new save/journal or payload.
                InventoryDeposit frozenDeposit=null;
                if(useInventory)
                {
                    var receipt=completionJournal.GetReceipt(pending.Endpoint,pending.Recipient,pending.Args.TradeId,pending.Completed);
                    frozenDeposit=inventoryDelivery.CreateDeposit(pending.Args,pending.DisplayName,completionJournal.DeliveryReceipt(receipt)?.DeliveryId);
                    if(frozenDeposit!=null)completionJournal.RecordInventoryPlan(pending.Endpoint,pending.Recipient,pending.Args.TradeId,pending.Completed,inventoryReadApi.GetStatus().JournalIdentity,frozenDeposit);
                }
                if(!completionJournal.TryBeginDelivery(pending.Endpoint,pending.Recipient,pending.Args.TradeId,pending.Completed,useInventory))
                {
                    pending.Attempted=true;
                    throw new InvalidOperationException("Trade delivery was already attempted; reconciliation required.");
                }
                pending.Attempted=true;
                InventoryDepositResult inventoryResult = null;
                InventoryDeliveryResult directResult = null;
                if(useInventory)inventoryResult=frozenDeposit==null?new InventoryDepositResult {Status=InventoryDepositStatus.AlreadyCommitted}:inventoryDelivery.DepositFrozen(frozenDeposit);
                else directResult=inventoryDelivery.DeliverDirect(pending.Args,settingsContext.Get("trade.dropCurrentMap",false));

                bool succeeded = useInventory ? inventoryResult.Succeeded : directResult.Succeeded;
                completionJournal.RecordDeliveryReturn(pending.Endpoint,pending.Recipient,pending.Args.TradeId,pending.Completed,useInventory,succeeded);
                if (!succeeded)
                {
                    string reason = useInventory
                        ? (string.IsNullOrWhiteSpace(inventoryResult.Reason)
                            ? inventoryResult.FailureCode.ToString() : inventoryResult.Reason)
                        : (directResult?.Reason ?? "Direct delivery failed.");
                    log?.Invoke(new LogEventArgs(
                        $"[Trade] Delivery failed for '{pending.Args.TradeId}'; retained for reconciliation, no automatic retry: {reason}",
                        LogLevel.ERROR));
                    if (notifyFailure)
                        notifyDeliveryFailure(useInventory ? "Phinix_trade_inventoryDepositFailed" :
                            "Phinix_trade_directDeliveryFailed",reason);
                    return;
                }

                pendingCompletions.Remove(key);
                acknowledgeCompletion?.Invoke(pending.Args.TradeId, !pending.Completed);
                // Empty direct completions have no map/pod target, just like empty inventory deposits.
                if (!pending.Display || useInventory && inventoryResult.Status == InventoryDepositStatus.AlreadyCommitted ||
                    !useInventory && directResult.Map == null) return;

                LetterDef letterDef = DefDatabase<LetterDef>.GetNamed(pending.Completed ? "TradeAccepted" : "TradeCancelled");
                Find.LetterStack.ReceiveLetter(
                    pending.Completed
                        ? "Phinix_trade_tradeCompletedLetter_label".Translate()
                        : "Phinix_trade_tradeCancelled_label".Translate(),
                    pending.Completed
                        ? (useInventory ? "Phinix_trade_tradeCompletedInventoryDescription" :
                            "Phinix_trade_tradeCompletedLetter_description").Translate(pending.DisplayName)
                        : (useInventory ? "Phinix_trade_tradeCancelledInventoryDescription" :
                            "Phinix_trade_tradeCancelled_description").Translate(pending.DisplayName),
                    letterDef,
                    useInventory ? null : new LookTargets(directResult.DropSpot, directResult.Map));
            }
            catch (Exception ex)
            {
                if(pending.ReconciliationOnly)
                {
                    // This immutable receive scope cannot become valid on a later save.
                    // Keep Received/Unbound evidence and report once, without repeated retries/stacks.
                    log?.Invoke(new LogEventArgs("[Trade] Delivery retained for reconciliation: "+pending.HoldMessageKey+"; trade="+pending.Args.TradeId,LogLevel.WARNING));
                    if(notifyFailure)notifyDeliveryFailure(pending.HoldMessageKey,string.Empty);
                    return;
                }
                log?.Invoke(new LogEventArgs(
                    $"[Trade] Delivery did not complete for '{pending.Args.TradeId}'; retained for reconciliation: {ex}",
                    LogLevel.ERROR));
                if (notifyFailure)
                    notifyDeliveryFailure("Phinix_trade_inventoryDepositFailed",ex.Message);
            }
        }

        private static string completionKey(string tradeId, bool cancelled)
        {
            return (cancelled ? "cancelled:" : "completed:") + tradeId;
        }

        private sealed class PendingCompletion
        {
            public PendingCompletion(TradeCompletionEventArgs args, string displayName, bool completed, bool display)
            {
                Args = args;
                DisplayName = displayName;
                Completed = completed;
                Display = display;
            }

            public TradeCompletionEventArgs Args { get; }
            public string DisplayName { get; }
            public bool Completed { get; }
            public bool Display { get; }
            public string Recipient,Endpoint;
            public TradeCompletionScope Scope;
            public IInventoryReceiveScope SaveScope;
            public bool Attempted;
            public bool ReconciliationOnly;
            public string HoldMessageKey;
        }

        private void onTradeUpdateFailure(object sender, TradeUpdateEventArgs args)
        {
            if (!started) return;
            try
            {
                if (args == null) return;

                string displayName = "???";
                if (tradeService.TryGetOtherPartyUuid(args.Trade.TradeId, out string otherPartyUuid))
                {
                    displayName = resolveDisplayName(otherPartyUuid);
                }

                Enqueue(() =>
                    windowService.Open(new Dialog_MessageBox(
                        title: "Phinix_error_tradeUpdateFailedTitle".Translate(),
                        text: "Phinix_error_tradeUpdateFailedMessage".Translate(displayName, args.FailureMessage, args.FailureReason.ToString()))));
            }
            catch (Exception ex)
            {
                log?.Invoke(new LogEventArgs(
                    $"[PhinixDefaultTradeBehaviour] onTradeUpdateFailure threw: {ex}",
                    LogLevel.ERROR));
            }
        }

        private bool shouldDisplayTradeEvent(string otherPartyUuid)
        {
            if (settingsContext.Get<bool>("trade.showBlockedTrades", false))
            {
                return true;
            }

            return !new HashSet<string>(settingsContext.BlockedUsers ?? Enumerable.Empty<string>()).Contains(otherPartyUuid);
        }

        private bool tryQueueTradeWindow(string otherPartyUuid, ClientTradeSnapshot trade)
        {
            lock (waitingLock)
            {
                if (!waitingForTradeCreationWith.Remove(otherPartyUuid))
                {
                    return false;
                }
            }

            Enqueue(() => windowService.Open(new TradeWindow(trade, tradeUiHostContext)));
            return true;
        }

        private string resolveDisplayName(string uuid)
        {
            if (userDirectory.TryGetUser(uuid, out ImmutableUser user))
            {
                return TextHelper.StripRichText(user.DisplayName);
            }

            return "???";
        }
    }
}
