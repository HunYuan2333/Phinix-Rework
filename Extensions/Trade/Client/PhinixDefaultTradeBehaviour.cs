using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Action<LogEventArgs> log;
        private readonly HashSet<string> waitingForTradeCreationWith = new HashSet<string>();
        private readonly object waitingLock = new object();

        public PhinixDefaultTradeBehaviour(
            IClientTradeService tradeService,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext,
            IClientMainThreadDispatcher dispatcher,
            IClientWindowService windowService,
            ClientTradeUiHostContext tradeUiHostContext,
            Action<LogEventArgs> log)
        {
            this.tradeService = tradeService;
            this.userDirectory = userDirectory;
            this.settingsContext = settingsContext;
            this.dispatcher = dispatcher;
            this.windowService = windowService;
            this.tradeUiHostContext = tradeUiHostContext;
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
        }

        public void Stop()
        {
            tradeService.OnTradeCreationRequested -= onTradeCreationRequested;
            tradeService.OnTradeCreationSuccess -= onTradeCreationSuccess;
            tradeService.OnTradeCreationFailure -= onTradeCreationFailure;
            tradeService.OnTradeCompleted -= onTradeCompleted;
            tradeService.OnTradeCancelled -= onTradeCancelled;
            tradeService.OnTradeUpdateFailure -= onTradeUpdateFailure;
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
            if (args?.Trade == null)
            {
                return;
            }

            if (!shouldDisplayTradeEvent(args.OtherPartyUuid))
            {
                return;
            }

            if (tryQueueTradeWindow(args.OtherPartyUuid, args.Trade))
            {
                return;
            }

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

        private void onTradeCreationFailure(object sender, TradeCreationEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            lock (waitingLock)
            {
                waitingForTradeCreationWith.Remove(args.OtherPartyUuid);
            }

            dispatcher.Enqueue(() =>
                windowService.Open(new Dialog_MessageBox(
                    title: "Phinix_error_tradeCreationFailedTitle".Translate(),
                    text: "Phinix_error_tradeCreationFailedMessage".Translate(args.FailureMessage, args.FailureReason.ToString()))));
        }

        private void onTradeCompleted(object sender, TradeCompletionEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            string displayName = resolveDisplayName(args.OtherPartyUuid);
            Thing[] verseItems = args.Items
                .Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown)
                .Where(thing => thing != null && thing.def.defName != "UnknownItem")
                .ToArray();

            dispatcher.Enqueue(() =>
            {
                LookTargets dropSpotLookTarget = tradeUiHostContext.DropPods(verseItems);
                LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeAccepted");
                Find.LetterStack.ReceiveLetter(
                    "Phinix_trade_tradeCompletedLetter_label".Translate(),
                    "Phinix_trade_tradeCompletedLetter_description".Translate(displayName),
                    letterDef,
                    dropSpotLookTarget);
            });
        }

        private void onTradeCancelled(object sender, TradeCompletionEventArgs args)
        {
            if (args == null || !shouldDisplayTradeEvent(args.OtherPartyUuid))
            {
                return;
            }

            string displayName = resolveDisplayName(args.OtherPartyUuid);
            Thing[] verseItems = args.Items
                .Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown)
                .Where(thing => thing != null && thing.def.defName != "UnknownItem")
                .ToArray();

            dispatcher.Enqueue(() =>
            {
                LookTargets dropSpotLookTarget = tradeUiHostContext.DropPods(verseItems);
                LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeCancelled");
                Find.LetterStack.ReceiveLetter(
                    "Phinix_trade_tradeCancelled_label".Translate(),
                    "Phinix_trade_tradeCancelled_description".Translate(displayName),
                    letterDef,
                    dropSpotLookTarget);
            });
        }

        private void onTradeUpdateFailure(object sender, TradeUpdateEventArgs args)
        {
            if (args == null)
            {
                return;
            }

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

        private bool shouldDisplayTradeEvent(string otherPartyUuid)
        {
            if (settingsContext.ShowBlockedTrades)
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
