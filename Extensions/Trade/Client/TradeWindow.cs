using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PhinixClient.GUI;
using PhinixClient.Framework;
using PhinixClient.Trade;
using RimWorld;
using UnityEngine;
using Utils;
using Verse;
using static PhinixClient.GUI.GUIUtils;

namespace Phinix.TradeExtension.Client
{
    public class TradeWindow : Window
    {
        private const float SCROLLBAR_WIDTH = 16f;
        private const float DEFAULT_SPACING = 10f;
        private const float OFFER_WINDOW_WIDTH = 400f;
        private const float OFFER_TITLE_HEIGHT = 20f;
        private const float OFFER_ROW_HEIGHT = 28f;
        private const float BADGE_HEIGHT = 22f;
        private const float OFFER_ACCENT_WIDTH = 3f;
        private const float SEARCH_TEXT_FIELD_WIDTH = 135f;
        private const float BUTTON_WIDTH = 80f;
        private const float ICON_WIDTH = 30f;
        private const float ITEM_ROW_HEIGHT = 28f;
        private const float ITEM_BUTTON_WIDTH = 30f;
        private const float ITEM_QUANTITY_FIELD_WIDTH = 55f;
        private const float ITEM_COUNT_WIDTH = 50f;
        private const float TITLE_HEIGHT = 30f;

        public override Vector2 InitialSize => new Vector2(1000f, 750f);

        private static readonly Regex itemCountInputRegex = new Regex("\\d*", RegexOptions.Compiled);
        private readonly Texture2D tradeArrows = ContentFinder<Texture2D>.Get("tradeArrows");
        private readonly ITradeUiHostContext hostContext;
        private readonly IClientTradeService tradeService;

        private Vector2 ourOfferScrollPos = Vector2.zero;
        private Vector2 theirOfferScrollPos = Vector2.zero;
        private Vector2 availableItemsScrollPos = Vector2.zero;

        private List<StackedThings> ourOfferCache = new List<StackedThings>();
        private List<StackedThings> theirOfferCache = new List<StackedThings>();

        private ClientTradeSnapshot trade;
        private ClientTradeSnapshot updatedTrade;
        private bool tradeUpdated = false;
        private object updatedTradeLock = new object();

        private List<StackedThings> availableItems = new List<StackedThings>();
        private List<StackedThings> filteredAvailableItems = new List<StackedThings>();
        private string searchText = string.Empty;

        private Dictionary<string, PendingThings> pendingItemStacks = new Dictionary<string, PendingThings>();
        private object pendingItemStacksLock = new object();

        private volatile bool shouldClose;
        private readonly object pendingAcceptedLock = new object();
        private bool? pendingAccepted;

        public TradeWindow(ClientTradeSnapshot trade, ITradeUiHostContext hostContext)
        {
            this.trade = trade;
            this.hostContext = hostContext;
            this.tradeService = hostContext.TradeService;

            this.doCloseX = true;
            this.closeOnAccept = false;
            this.closeOnCancel = false;
            this.closeOnClickedOutside = false;
            this.forcePause = true;
            this.draggable = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();

            tradeService.OnTradeCompleted += OnTradeFinished;
            tradeService.OnTradeCancelled += OnTradeFinished;
            tradeService.OnTradeUpdateSuccess += OnTradeUpdated;
            tradeService.OnTradeUpdateFailure += OnTradeUpdated;

            refreshAvailableItems();

            ourOfferCache = StackedThings.GroupThings(
                trade.ItemsOnOffer.Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown),
                logMessage);
            theirOfferCache = StackedThings.GroupThings(
                trade.OtherPartyItemsOnOffer.Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown),
                logMessage);
        }

        public override void Close(bool doCloseSound = true)
        {
            base.Close(doCloseSound);

            tradeService.OnTradeCompleted -= OnTradeFinished;
            tradeService.OnTradeCancelled -= OnTradeFinished;
            tradeService.OnTradeUpdateSuccess -= OnTradeUpdated;
            tradeService.OnTradeUpdateFailure -= OnTradeUpdated;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (shouldClose)
            {
                Close();
                return;
            }

            if (tradeUpdated)
            {
                if (Monitor.TryEnter(updatedTradeLock))
                {
                    trade = updatedTrade;
                    ourOfferCache = StackedThings.GroupThings(
                        trade.ItemsOnOffer.Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown),
                        logMessage);
                    theirOfferCache = StackedThings.GroupThings(
                        trade.OtherPartyItemsOnOffer.Select(TradeItemConverter.ConvertThingFromSnapshotOrUnknown),
                        logMessage);
                    tradeUpdated = false;
                    Monitor.Exit(updatedTradeLock);
                }
            }

            Rect titleRect = inRect.TopPartPixels(TITLE_HEIGHT);
            float availableItemsHeight = Mathf.Max(150f, inRect.height * 0.3f);
            Rect availableItemsRect = new Rect(inRect.xMin, inRect.yMax - availableItemsHeight, inRect.width, availableItemsHeight);
            Rect bottomBarRect = new Rect(inRect.xMin, availableItemsRect.yMin - ITEM_ROW_HEIGHT - DEFAULT_SPACING, inRect.width, ITEM_ROW_HEIGHT);
            Rect offerAreaRect = new Rect(inRect.xMin, titleRect.yMax + DEFAULT_SPACING, inRect.width, bottomBarRect.yMin - titleRect.yMax - DEFAULT_SPACING * 2);
            Rect offerHalfRect = new Rect(offerAreaRect.xMin, offerAreaRect.yMin, inRect.width, (offerAreaRect.height - DEFAULT_SPACING) / 2);
            Rect ourOfferRect = offerHalfRect.LeftPartPixels(OFFER_WINDOW_WIDTH);
            Rect theirOfferRect = offerHalfRect.RightPartPixels(OFFER_WINDOW_WIDTH);
            Rect centreColumnRect = new Rect(ourOfferRect.xMax, offerHalfRect.yMin, theirOfferRect.xMin - ourOfferRect.xMax, offerHalfRect.height);
            Rect tradeArrowsRect = new Rect(centreColumnRect.xMin + 10f, centreColumnRect.yMin + centreColumnRect.height / 4, centreColumnRect.width - 20f, centreColumnRect.height / 2);

            Rect searchFieldRect = bottomBarRect.LeftPartPixels(SEARCH_TEXT_FIELD_WIDTH);
            Rect searchLabelRect = searchFieldRect.TranslatedBy(-(SEARCH_TEXT_FIELD_WIDTH + DEFAULT_SPACING));
            Rect cancelButtonRect = bottomBarRect.RightPartPixels(BUTTON_WIDTH);
            Rect resetButtonRect = new Rect(cancelButtonRect.xMin - BUTTON_WIDTH - DEFAULT_SPACING, bottomBarRect.yMin, BUTTON_WIDTH, bottomBarRect.height);
            Rect updateButtonRect = new Rect(resetButtonRect.xMin - BUTTON_WIDTH - DEFAULT_SPACING, bottomBarRect.yMin, BUTTON_WIDTH, bottomBarRect.height);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.LabelFit(titleRect, "Phinix_trade_tradeTitle".Translate(TextHelper.StripRichText(trade.OtherPartyDisplayName)));

            Text.Font = previousFont;
            Text.Anchor = previousAnchor;

            Widgets.DrawTextureFitted(tradeArrowsRect, tradeArrows, 1f);

            bool? pendingAcceptedValue = getPendingAccepted();
            bool ourOfferAccepted = pendingAcceptedValue ?? trade.Accepted;
            drawOffer(ourOfferRect, "Phinix_trade_ourOfferLabel".Translate(),
                ourOfferCache, ref ourOfferScrollPos, ref ourOfferAccepted,
                ("Phinix_trade_confirmOurTradeCheckbox" + (ourOfferAccepted ? "Checked" : "Unchecked")).Translate(),
                true, TradeTheme.OurOfferAccent, TradeTheme.OurOfferBg);
            if (ourOfferAccepted != (pendingAcceptedValue ?? trade.Accepted))
            {
                sendTradeStatusUpdate(ourOfferAccepted);
            }

            bool theirOfferAccepted = trade.OtherPartyAccepted;
            drawOffer(theirOfferRect, "Phinix_trade_theirOfferLabel".Translate(),
                theirOfferCache, ref theirOfferScrollPos, ref theirOfferAccepted,
                ("Phinix_trade_confirmTheirTradeCheckbox" + (trade.OtherPartyAccepted ? "Checked" : "Unchecked")).Translate(TextHelper.StripRichText(trade.OtherPartyDisplayName)),
                false, TradeTheme.TheirOfferAccent, TradeTheme.TheirOfferBg);

            if (Widgets.ButtonText(updateButtonRect, "Phinix_trade_updateButton".Translate()))
            {
                string token = null;
                List<PoppedThing> selectedThings = new List<PoppedThing>();
                try
                {
                    token = Guid.NewGuid().ToString();

                    foreach (StackedThings itemStack in availableItems)
                    {
                        selectedThings.AddRange(itemStack.PopSelectedWithOrigins());
                    }

                    foreach (PoppedThing selectedThing in selectedThings)
                    {
                        selectedThing.DeSpawn();
                    }

                    lock (pendingItemStacksLock)
                    {
                        pendingItemStacks.Add(token, new PendingThings
                        {
                            Things = selectedThings.ToArray(),
                            Timestamp = DateTime.UtcNow
                        });
                    }
                    hostContext.Log(new LogEventArgs($"Added {selectedThings.Count} item stack(s) to pending", LogLevel.DEBUG));

                    IEnumerable<TradeItemSnapshot> actualOffer = trade.ItemsOnOffer.Concat(
                        selectedThings.Select(selectedThing => TradeItemConverter.ConvertThingFromVerse(selectedThing.Thing)));
                    tradeService.UpdateTradeItems(trade.TradeId, actualOffer, token);
                    hostContext.Log(new LogEventArgs("Sent update", LogLevel.DEBUG));
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrEmpty(token))
                    {
                        lock (pendingItemStacksLock)
                        {
                            pendingItemStacks.Remove(token);
                        }
                    }

                    restorePoppedThings(selectedThings, "TradeWindow.UpdateTradeItems");
                    refreshAvailableItems();
                    hostContext.Log(new LogEventArgs($"Failed to update trade items: {ex}", LogLevel.ERROR));
                }
            }

            if (Widgets.ButtonText(resetButtonRect, "Phinix_trade_resetButton".Translate()))
            {
                hostContext.DropPods(trade.ItemsOnOffer.Select(TradeItemConverter.ConvertThingFromSnapshot));
                foreach (StackedThings stack in availableItems)
                {
                    stack.Selected = 0;
                }
                refreshAvailableItems();
                tradeService.UpdateTradeItems(trade.TradeId, Array.Empty<TradeItemSnapshot>());
            }

            Color previousColour = UnityEngine.GUI.color;
            UnityEngine.GUI.color = TradeTheme.CancelButton;
            if (Widgets.ButtonText(cancelButtonRect, "Phinix_trade_cancelButton".Translate()))
            {
                sendCancelTradeRequest();
            }
            UnityEngine.GUI.color = previousColour;

            GUIUtils.SaveTextFormat();
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(searchLabelRect, "Phinix_trade_searchLabel".Translate());
            GUIUtils.RestoreTextFormat();

            string oldSearchText = searchText;
            searchText = Widgets.TextField(searchFieldRect, searchText);
            if (searchText != oldSearchText)
            {
                filteredAvailableItems = availableItems.Where(stack => stack.Count > 0 && stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1).ToList();
            }

            if (!filteredAvailableItems.Any(stack => stack.Count > 0))
            {
                Widgets.DrawMenuSection(availableItemsRect);
                Widgets.NoneLabelCenteredVertically(availableItemsRect, ("Phinix_trade_noItemsAvailable" + (availableItems.Any() ? "WithSearch" : "")).Translate());
            }
            else
            {
                drawItemStackList(availableItemsRect, filteredAvailableItems, ref availableItemsScrollPos, true);
            }
        }

        private void OnTradeFinished(object sender, TradeCompletionEventArgs args)
        {
            hostContext.RunOnMainThread(() => shouldClose = true);
        }

        private void OnTradeUpdated(object sender, TradeUpdateEventArgs args)
        {
            hostContext.RunOnMainThread(() => applyTradeUpdated(args));
        }

        private void sendTradeStatusUpdate(bool accepted)
        {
            hostContext.Log(new LogEventArgs($"[TradeWindow] Accept toggled: tradeId={trade.TradeId}, accepted={accepted}", LogLevel.DEBUG));
            setPendingAccepted(accepted);
            try
            {
                tradeService.UpdateTradeStatus(trade.TradeId, accepted: accepted);
            }
            catch (Exception exception)
            {
                clearPendingAccepted();
                hostContext.Log(new LogEventArgs($"Failed to send trade status update for trade '{trade.TradeId}': {exception.Message}", LogLevel.ERROR));
            }
        }

        private void sendCancelTradeRequest()
        {
            hostContext.Log(new LogEventArgs($"[TradeWindow] Cancel clicked: tradeId={trade.TradeId}", LogLevel.DEBUG));
            try
            {
                tradeService.CancelTrade(trade.TradeId);
            }
            catch (Exception exception)
            {
                hostContext.Log(new LogEventArgs($"Failed to send trade cancel request for trade '{trade.TradeId}': {exception.Message}", LogLevel.ERROR));
            }
        }

        private void refreshAvailableItems()
        {
            List<Map> homeMaps = Find.Maps.Where(map => map != null && map.IsPlayerHome).ToList();
            List<Thing> rawThings = StoredThingCollector
                .Collect(homeMaps, hostContext.AllItemsTradable, logMessage)
                .Where(thing => thing.def.category == ThingCategory.Item && !thing.def.IsCorpse)
                .ToList();
            availableItems = StackedThings.GroupThings(rawThings, logMessage);
            filteredAvailableItems = availableItems
                .Where(stack => stack.Count > 0 && stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1)
                .ToList();

            hostContext.Log(new LogEventArgs($"[TradeWindow] refreshAvailableItems: homeMaps={homeMaps.Count()}, allItemsTradable={hostContext.AllItemsTradable}, rawThings={rawThings.Count}, groupedStacks={availableItems.Count}, filteredStacks={filteredAvailableItems.Count}, searchText='{searchText}'", LogLevel.DEBUG));
        }

        private void applyTradeUpdated(TradeUpdateEventArgs args)
        {
            hostContext.Log(new LogEventArgs($"[TradeWindow] OnTradeUpdated: tradeId={args.Trade?.TradeId}, failure={args.FailureReason}, token={args.Token ?? "null"}", LogLevel.DEBUG));
            bool matchingTrade = args.Trade != null &&
                string.Equals(args.Trade.TradeId, trade.TradeId, StringComparison.OrdinalIgnoreCase);
            if (matchingTrade)
            {
                bool? pendingAcceptedValue = getPendingAccepted();
                if (args.FailureReason != TradeFailureReason.None ||
                    (pendingAcceptedValue.HasValue && args.Trade.Accepted == pendingAcceptedValue.Value))
                {
                    clearPendingAccepted();
                }

                lock (updatedTradeLock)
                {
                    updatedTrade = args.Trade;
                    tradeUpdated = true;
                }
            }

            if (string.IsNullOrEmpty(args.Token))
            {
                return;
            }

            bool foundPendingThings = false;
            PendingThings pendingThings = default;
            lock (pendingItemStacksLock)
            {
                foundPendingThings = pendingItemStacks.TryGetValue(args.Token, out pendingThings);
                if (foundPendingThings)
                {
                    pendingItemStacks.Remove(args.Token);
                }
            }

            if (!foundPendingThings)
            {
                return;
            }

            if (args.FailureReason == TradeFailureReason.None)
            {
                foreach (PoppedThing pendingThing in pendingThings.Things)
                {
                    Thing thing = pendingThing.Thing;
                    if (!thing.Destroyed)
                    {
                        thing.Destroy();
                    }
                }
                return;
            }

            restorePoppedThings(pendingThings.Things, "TradeWindow.TradeUpdateFailure");
            refreshAvailableItems();
        }

        private void restorePoppedThings(IEnumerable<PoppedThing> poppedThings, string context)
        {
            List<Thing> unrestoredThings = PoppedThing.RestoreAll(poppedThings, logMessage, context);
            if (unrestoredThings.Count == 0)
            {
                return;
            }

            try
            {
                hostContext.DropPods(unrestoredThings);
                hostContext.Log(new LogEventArgs(
                    $"[{context}] Returned {unrestoredThings.Count} thing(s) by drop pod after direct restoration failed.",
                    LogLevel.WARNING));
            }
            catch (Exception exception)
            {
                hostContext.Log(new LogEventArgs(
                    $"[{context}] Failed to return {unrestoredThings.Count} thing(s) by drop pod.{Environment.NewLine}{exception}",
                    LogLevel.ERROR));
            }
        }

        private void logMessage(string message, LogLevel level)
        {
            hostContext.Log(new LogEventArgs(message, level));
        }

        private bool? getPendingAccepted()
        {
            lock (pendingAcceptedLock)
            {
                return pendingAccepted;
            }
        }

        private void setPendingAccepted(bool? value)
        {
            lock (pendingAcceptedLock)
            {
                pendingAccepted = value;
            }
        }

        private void clearPendingAccepted()
        {
            lock (pendingAcceptedLock)
            {
                pendingAccepted = null;
            }
        }

        private void drawOffer(Rect inRect, string title, List<StackedThings> itemStacks, ref Vector2 scrollPos, ref bool accepted, string acceptedLabel, bool interactive, Color accentColor, Color bgColor)
        {
            Rect accentRect = new Rect(inRect.xMin, inRect.yMin, OFFER_ACCENT_WIDTH, inRect.height);
            Widgets.DrawBoxSolid(accentRect, accentColor);

            Rect bgRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH, inRect.yMin, inRect.width - OFFER_ACCENT_WIDTH, inRect.height);
            Widgets.DrawBoxSolid(bgRect, bgColor);

            Rect titleRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH + 6f, inRect.yMin + 2f, inRect.width - OFFER_ACCENT_WIDTH - 12f, OFFER_TITLE_HEIGHT);

            Rect badgeRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH + 6f, inRect.yMax - BADGE_HEIGHT - 4f, inRect.width - OFFER_ACCENT_WIDTH - 12f, BADGE_HEIGHT);
            Rect itemListRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH + 4f, titleRect.yMax + 4f, inRect.width - OFFER_ACCENT_WIDTH - 8f, badgeRect.yMin - DEFAULT_SPACING - titleRect.yMax - 4f);

            SaveTextFormat();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.LabelFit(titleRect, title);
            RestoreTextFormat();

            if (interactive)
            {
                float btnWidth = 120f;
                Rect confirmBtnRect = new Rect(badgeRect.xMin + (badgeRect.width - btnWidth) / 2f, badgeRect.yMin, btnWidth, badgeRect.height);

                Color prevColor = UnityEngine.GUI.color;
                if (accepted)
                {
                    UnityEngine.GUI.color = TradeTheme.AcceptedBadge;
                }

                string btnLabel = accepted
                    ? "Phinix_trade_confirmAccepted".Translate()
                    : "Phinix_trade_confirmTrade".Translate();

                if (Widgets.ButtonText(confirmBtnRect, btnLabel))
                {
                    accepted = !accepted;
                }
                UnityEngine.GUI.color = prevColor;
            }
            else
            {
                string icon = accepted ? "✓" : "○";
                Color labelColor = accepted ? TradeTheme.AcceptedBadge : TradeTheme.PendingBadge;
                string badgeText = "<color=" + ColorUtility.ToHtmlStringRGB(labelColor) + ">" + icon + " " + acceptedLabel + "</color>";
                SaveTextFormat();
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(badgeRect, badgeText);
                RestoreTextFormat();
            }

            if (itemStacks.Count == 0)
            {
                SaveTextFormat();
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;
                Widgets.Label(itemListRect, "Phinix_trade_offerEmpty".Translate().Colorize(TradeTheme.PendingBadge));
                RestoreTextFormat();
                return;
            }

            drawItemStackList(itemListRect, itemStacks, ref scrollPos, false);
        }

        private void drawItemStackList(Rect inRect, List<StackedThings> stacks, ref Vector2 scrollPos, bool interactive = false)
        {
            if (inRect.height <= 0f || stacks == null || stacks.Count == 0) return;

            float RIGHT_PADDING = 5f;

            bool scrollbarsPresent = ITEM_ROW_HEIGHT * stacks.Count > inRect.height;
            int drawableStacks = stacks.Count(stack => stack.Things.Count > 0);
            Rect contentRect = new Rect(inRect.xMin, inRect.yMin, scrollbarsPresent ? inRect.width - SCROLLBAR_WIDTH : inRect.width, ITEM_ROW_HEIGHT * drawableStacks);
            bool scrollRequired = contentRect.height > inRect.height;
            if (scrollRequired) Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);

            bool alternateBackground = false;
            float currentY = contentRect.yMin;
            foreach (StackedThings stack in stacks)
            {
                if (stack.Things.Count == 0) continue;

                Rect rowRect = new Rect(contentRect.xMin, currentY, contentRect.width, ITEM_ROW_HEIGHT);

                if (alternateBackground) Widgets.DrawHighlight(rowRect);
                else if (Mouse.IsOver(rowRect)) Widgets.DrawBoxSolid(rowRect, TradeTheme.RowHoverBg);

                Rect iconRect = rowRect.LeftPartPixels(ICON_WIDTH);
                Widgets.ThingIcon(iconRect, stack.ThingDef, stack.StuffDef, stack.StyleDef, 0.9f);

                Rect itemNameRect;
                if (interactive)
                {
                    float buttonAreaWidth = ITEM_BUTTON_WIDTH * 4 + ITEM_QUANTITY_FIELD_WIDTH + ITEM_COUNT_WIDTH + DEFAULT_SPACING * 4;
                    Rect buttonAreaRect = new Rect(rowRect.xMax - RIGHT_PADDING - buttonAreaWidth, rowRect.yMin, buttonAreaWidth, rowRect.height);
                    Rect btnMinus10 = new Rect(buttonAreaRect.xMin, buttonAreaRect.yMin, ITEM_BUTTON_WIDTH, buttonAreaRect.height);
                    Rect btnMinus1 = btnMinus10.TranslatedBy(ITEM_BUTTON_WIDTH);
                    Rect quantityFieldRect = new Rect(btnMinus1.xMax + DEFAULT_SPACING, buttonAreaRect.yMin, ITEM_QUANTITY_FIELD_WIDTH, buttonAreaRect.height);
                    Rect availableCountRect = new Rect(quantityFieldRect.xMax + DEFAULT_SPACING, buttonAreaRect.yMin, ITEM_COUNT_WIDTH, buttonAreaRect.height);
                    Rect btnPlus1 = new Rect(availableCountRect.xMax + DEFAULT_SPACING, buttonAreaRect.yMin, ITEM_BUTTON_WIDTH, buttonAreaRect.height);
                    Rect btnPlus10 = btnPlus1.TranslatedBy(ITEM_BUTTON_WIDTH);

                    itemNameRect = new Rect(iconRect.xMax + DEFAULT_SPACING, rowRect.yMin, buttonAreaRect.xMin - iconRect.xMax - DEFAULT_SPACING * 2, rowRect.height);

                    if (Widgets.ButtonText(btnMinus10, "-10")) stack.Selected = Clamp(stack.Selected - 10, 0, stack.Count);
                    if (Widgets.ButtonText(btnMinus1, "-1")) stack.Selected = Clamp(stack.Selected - 1, 0, stack.Count);

                    string buf = stack.Selected == 0 ? "" : stack.Selected.ToString();
                    buf = Widgets.TextField(quantityFieldRect, buf, 100, itemCountInputRegex);
                    stack.Selected = string.IsNullOrEmpty(buf) ? 0 : Clamp(int.Parse(buf), 0, stack.Count);

                    SaveTextFormat();
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(availableCountRect, $"/ {stack.Count}");
                    RestoreTextFormat();

                    if (Widgets.ButtonText(btnPlus1, "+1")) stack.Selected = Clamp(stack.Selected + 1, 0, stack.Count);
                    if (Widgets.ButtonText(btnPlus10, "+10")) stack.Selected = Clamp(stack.Selected + 10, 0, stack.Count);

                    if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(rowRect))
                    {
                        DrawItemContextMenu(stack);
                        Event.current.Use();
                    }
                }
                else
                {
                    Rect itemCountRect = new Rect(rowRect.xMax - ITEM_COUNT_WIDTH - RIGHT_PADDING, rowRect.yMin, ITEM_COUNT_WIDTH, rowRect.height);
                    itemNameRect = new Rect(iconRect.xMax + DEFAULT_SPACING, rowRect.yMin, itemCountRect.xMin - iconRect.xMax - DEFAULT_SPACING, rowRect.height);

                    SaveTextFormat();
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(itemCountRect, stack.Count.ToStringSI());
                    RestoreTextFormat();
                }

                SaveTextFormat();
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.LabelFit(itemNameRect, stack.Label);
                RestoreTextFormat();

                alternateBackground = !alternateBackground;
                currentY += ITEM_ROW_HEIGHT;
            }

            if (scrollRequired) Widgets.EndScrollView();
        }

        private void DrawItemContextMenu(StackedThings stack)
        {
            List<FloatMenuOption> items = new List<FloatMenuOption>
            {
                new FloatMenuOption("Phinix_trade_selectAll".Translate(), () => stack.Selected = stack.Count),
                new FloatMenuOption("Phinix_trade_selectHalf".Translate(), () => stack.Selected = stack.Count / 2),
                new FloatMenuOption("Phinix_trade_selectNone".Translate(), () => stack.Selected = 0),
                new FloatMenuOption("Phinix_trade_select100".Translate(), () => stack.Selected = Clamp(100, 0, stack.Count)),
            };
            Find.WindowStack.Add(new FloatMenu(items));
        }
    }
}
