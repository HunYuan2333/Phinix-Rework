using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Trading;
using UnityEngine;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;
using Thing = Verse.Thing;

namespace PhinixClient.Framework
{
    public sealed class PhinixDefaultTradeBehaviour
    {
        private readonly IClientTradeFacade tradeFacade;
        private readonly ClientUserManager userManager;
        private readonly PhinixClientItemPipeline itemPipeline;
        private readonly Func<IEnumerable<Thing>, LookTargets> dropPods;
        private readonly Action<LogEventArgs> log;

        public PhinixDefaultTradeBehaviour(IClientTradeFacade tradeFacade, ClientUserManager userManager, PhinixClientItemPipeline itemPipeline, Func<IEnumerable<Thing>, LookTargets> dropPods, Action<LogEventArgs> log)
        {
            this.tradeFacade = tradeFacade;
            this.userManager = userManager;
            this.itemPipeline = itemPipeline;
            this.dropPods = dropPods;
            this.log = log;
        }

        public bool ShouldDisplayTradeEvent(string otherPartyUuid, bool showBlockedTrades, IEnumerable<string> blockedUserUuids)
        {
            if (showBlockedTrades) return true;

            HashSet<string> blockedUsers = new HashSet<string>(blockedUserUuids ?? Enumerable.Empty<string>());
            return !blockedUsers.Contains(otherPartyUuid);
        }

        public bool TryQueueTradeWindow(string otherPartyUuid, string tradeId, HashSet<string> waitingForTradeCreationWith, object waitingForTradeCreationWithLock, List<ImmutableTrade> tradeWindowQueue, object tradeWindowQueueLock)
        {
            lock (waitingForTradeCreationWithLock)
            {
                if (!waitingForTradeCreationWith.Remove(otherPartyUuid))
                {
                    return false;
                }
            }

            if (!tradeFacade.TryGetTrade(tradeId, out ImmutableTrade trade))
            {
                log?.Invoke(new LogEventArgs(string.Format("Failed to get newly created trade {0} when attempting to open immediately", tradeId), LogLevel.WARNING));
                return false;
            }

            lock (tradeWindowQueueLock)
            {
                tradeWindowQueue.Add(trade);
            }

            return true;
        }

        public void HandleTradeCreationSuccess(CreateTradeEventArgs args, bool showBlockedTrades, IEnumerable<string> blockedUserUuids, HashSet<string> waitingForTradeCreationWith, object waitingForTradeCreationWithLock, List<ImmutableTrade> tradeWindowQueue, object tradeWindowQueueLock)
        {
            if (!ShouldDisplayTradeEvent(args.OtherPartyUuid, showBlockedTrades, blockedUserUuids)) return;
            if (TryQueueTradeWindow(args.OtherPartyUuid, args.TradeId, waitingForTradeCreationWith, waitingForTradeCreationWithLock, tradeWindowQueue, tradeWindowQueueLock)) return;

            string displayName = resolveDisplayName(args.OtherPartyUuid);
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeCreated");
            Find.LetterStack.ReceiveLetter(
                label: "Phinix_trade_tradeReceivedLetter_label".Translate(displayName),
                text: "Phinix_trade_tradeReceivedLetter_description".Translate(displayName),
                textLetterDef: letterDef
            );
        }

        public void HandleTradeCompleted(CompleteTradeEventArgs args)
        {
            HandleTradeCompleted(new TradeCompletionContext
            {
                TradeId = args?.TradeId,
                OtherPartyUuid = args?.OtherPartyUuid,
                Items = itemPipeline.EncodeTradeItems(args?.Items),
                Log = (message, level) => log?.Invoke(new LogEventArgs(message, level))
            });
        }

        public void HandleTradeCompleted(TradeCompletionContext context)
        {
            if (context == null) return;

            string displayName = resolveDisplayName(context.OtherPartyUuid);
            LookTargets dropSpotLookTarget = deliverItems(context.Items);
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeAccepted");
            Find.LetterStack.ReceiveLetter(
                "Phinix_trade_tradeCompletedLetter_label".Translate(),
                "Phinix_trade_tradeCompletedLetter_description".Translate(displayName),
                letterDef,
                dropSpotLookTarget);
        }

        public void HandleTradeCancelled(CompleteTradeEventArgs args, bool showBlockedTrades, IEnumerable<string> blockedUserUuids)
        {
            if (!ShouldDisplayTradeEvent(args.OtherPartyUuid, showBlockedTrades, blockedUserUuids)) return;

            string displayName = resolveDisplayName(args.OtherPartyUuid);
            LookTargets dropSpotLookTarget = deliverItems(args.Items);
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("TradeCancelled");
            Find.LetterStack.ReceiveLetter(
                "Phinix_trade_tradeCancelled_label".Translate(),
                "Phinix_trade_tradeCancelled_description".Translate(displayName),
                letterDef,
                dropSpotLookTarget);
        }

        public void HandleTradeUpdateFailure(TradeUpdateEventArgs args)
        {
            string displayName = "???";
            if (tradeFacade.TryGetOtherPartyUuid(args.TradeId, out string otherPartyUuid) &&
                userManager.TryGetDisplayName(otherPartyUuid, out string resolvedDisplayName))
            {
                displayName = TextHelper.StripRichText(resolvedDisplayName);
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                title: "Phinix_error_tradeUpdateFailedTitle".Translate(),
                text: "Phinix_error_tradeUpdateFailedMessage".Translate(displayName, args.FailureMessage, args.FailureReason.ToString())));
        }

        private string resolveDisplayName(string uuid)
        {
            if (userManager.TryGetDisplayName(uuid, out string displayName))
            {
                return TextHelper.StripRichText(displayName);
            }

            return "???";
        }

        private LookTargets deliverItems(IEnumerable<ProtoThing> items)
        {
            Thing[] verseItems = itemPipeline.DecodeTradeItems(items)
                .Where(thing => thing.def.defName != "UnknownItem")
                .ToArray();

            return dropPods(verseItems);
        }

        private LookTargets deliverItems(IEnumerable<FrameworkItemPayload> items)
        {
            Thing[] verseItems = itemPipeline.DecodeItems(items)
                .Where(thing => thing.def.defName != "UnknownItem")
                .ToArray();

            return dropPods(verseItems);
        }
    }
}
