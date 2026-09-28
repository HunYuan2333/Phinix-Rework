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
    internal sealed class PhinixDefaultTradeBehaviour
    {
        private readonly IClientTradeService tradeService;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientSettingsContext settingsContext;
        private readonly IClientMainThreadDispatcher dispatcher;
        private readonly IClientWindowService windowService;
        private readonly ClientTradeUiHostContext tradeUiHostContext;
        private readonly TradeInventoryDelivery inventoryDelivery;
        private readonly IInventoryReadApi inventoryReadApi;
        private readonly Action<string, bool> acknowledgeCompletion;
        private readonly Action<LogEventArgs> log;
        private readonly HashSet<string> waitingForTradeCreationWith = new HashSet<string>();
        private readonly object waitingLock = new object();
        private readonly Dictionary<string, PendingCompletion> pendingCompletions = new Dictionary<string, PendingCompletion>(StringComparer.OrdinalIgnoreCase);

        public PhinixDefaultTradeBehaviour(
            IClientTradeService tradeService,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext,
            IClientMainThreadDispatcher dispatcher,
            IClientWindowService windowService,
            ClientTradeUiHostContext tradeUiHostContext,
            TradeInventoryDelivery inventoryDelivery,
            IInventoryReadApi inventoryReadApi,
            Action<string, bool> acknowledgeCompletion,
            Action<LogEventArgs> log)
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
        }

        public void Start()
        {
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
            tradeService.OnTradeCreationRequested -= onTradeCreationRequested;
            tradeService.OnTradeCreationSuccess -= onTradeCreationSuccess;
            tradeService.OnTradeCreationFailure -= onTradeCreationFailure;
            tradeService.OnTradeCompleted -= onTradeCompleted;
            tradeService.OnTradeCancelled -= onTradeCancelled;
            tradeService.OnTradeUpdateFailure -= onTradeUpdateFailure;
            inventoryReadApi.AvailabilityChanged -= onInventoryAvailabilityChanged;
            settingsContext.OnSettingChanged -= onSettingChanged;
            pendingCompletions.Clear();
        }

        private void onTradeCreationRequested(object sender, TradeCreationEventArgs args)
        {
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
            try
            {
                if (args?.Trade == null) return;
                if (!shouldDisplayTradeEvent(args.OtherPartyUuid)) return;
                if (tryQueueTradeWindow(args.OtherPartyUuid, args.Trade)) return;

                string displayName = resolveDisplayName(args.OtherPartyUuid);
                dispatcher.Enqueue(() =>
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
            try
            {
                if (args == null) return;

                lock (waitingLock)
                {
                    waitingForTradeCreationWith.Remove(args.OtherPartyUuid);
                }

                dispatcher.Enqueue(() =>
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
            depositCompletion(args, true);
        }

        private void onTradeCancelled(object sender, TradeCompletionEventArgs args)
        {
            depositCompletion(args, false);
        }

        private void depositCompletion(TradeCompletionEventArgs args, bool completed)
        {
            try
            {
                if (args == null) return;
                string displayName = resolveDisplayName(args.OtherPartyUuid);
                bool display = completed || shouldDisplayTradeEvent(args.OtherPartyUuid);
                dispatcher.Enqueue(() =>
                {
                    string key = completionKey(args.TradeId, !completed);
                    pendingCompletions[key] = new PendingCompletion(args, displayName, completed, display);
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
            dispatcher.Enqueue(() =>
            {
                foreach (string key in pendingCompletions.Keys.ToArray())
                    tryDepositPending(key, false);
            });
        }

        private void onSettingChanged(string key, object value)
        {
            if (key != InventoryDeliveryPreference.SettingKey) return;
            dispatcher.Enqueue(() =>
            {
                foreach (string pendingKey in pendingCompletions.Keys.ToArray())
                    tryDepositPending(pendingKey, false);
            });
        }

        private void tryDepositPending(string key, bool notifyFailure)
        {
            if (!pendingCompletions.TryGetValue(key, out PendingCompletion pending)) return;
            try
            {
                bool useInventory = InventoryDeliveryPreference.UsesInventory(
                    settingsContext.Get(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.DirectDrop));
                InventoryDepositResult inventoryResult = null;
                InventoryDeliveryResult directResult = null;
                if (useInventory)
                    inventoryResult = inventoryDelivery.Deposit(pending.Args, pending.DisplayName);
                else
                    directResult = inventoryDelivery.DeliverDirect(pending.Args,
                        settingsContext.Get("trade.dropCurrentMap", false));

                bool succeeded = useInventory ? inventoryResult.Succeeded : directResult.Succeeded;
                if (!succeeded)
                {
                    string reason = useInventory
                        ? (string.IsNullOrWhiteSpace(inventoryResult.Reason)
                            ? inventoryResult.FailureCode.ToString() : inventoryResult.Reason)
                        : (directResult?.Reason ?? "Direct delivery failed.");
                    log?.Invoke(new LogEventArgs(
                        $"[Trade] Delivery failed for '{pending.Args.TradeId}'; completion remains pending: {reason}",
                        LogLevel.ERROR));
                    if (notifyFailure)
                        Messages.Message((useInventory ? "Phinix_trade_inventoryDepositFailed" :
                            "Phinix_trade_directDeliveryFailed").Translate(reason), MessageTypeDefOf.RejectInput, false);
                    return;
                }

                pendingCompletions.Remove(key);
                acknowledgeCompletion?.Invoke(pending.Args.TradeId, !pending.Completed);
                if (!pending.Display || useInventory && inventoryResult.Status == InventoryDepositStatus.AlreadyCommitted) return;

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
                log?.Invoke(new LogEventArgs(
                    $"[Trade] Inventory delivery threw for '{pending.Args.TradeId}'; completion remains pending: {ex}",
                    LogLevel.ERROR));
                if (notifyFailure)
                    Messages.Message("Phinix_trade_inventoryDepositFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput, false);
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
        }

        private void onTradeUpdateFailure(object sender, TradeUpdateEventArgs args)
        {
            try
            {
                if (args == null) return;

                string displayName = "???";
                if (tradeService.TryGetOtherPartyUuid(args.Trade.TradeId, out string otherPartyUuid))
                {
                    displayName = resolveDisplayName(otherPartyUuid);
                }

                dispatcher.Enqueue(() =>
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

            dispatcher.Enqueue(() => windowService.Open(new TradeWindow(trade, tradeUiHostContext)));
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
