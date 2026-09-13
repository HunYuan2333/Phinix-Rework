using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using PhinixClient;
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
        private const float OFFER_MINIMUM_WIDTH = 260f;
        private const float OFFER_ARROW_COLUMN_WIDTH = 48f;
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

        private const float MINIMUM_WINDOW_WIDTH = 640f;
        private const float MINIMUM_WINDOW_HEIGHT = 480f;
        private const float PREFERRED_WINDOW_WIDTH = 1000f;
        private const float PREFERRED_WINDOW_HEIGHT = 750f;
        private const float MINIMUM_OFFER_AREA_HEIGHT = 120f;
        private const float MINIMUM_AVAILABLE_ITEMS_HEIGHT = 100f;

        public override Vector2 InitialSize
        {
            get
            {
                Rect safeRect = UiScreenSafeArea.Current;
                return new Vector2(
                    Mathf.Min(PREFERRED_WINDOW_WIDTH, safeRect.width),
                    Mathf.Min(PREFERRED_WINDOW_HEIGHT, safeRect.height));
            }
        }

        private static readonly Regex itemCountInputRegex = new Regex("^\\d*$", RegexOptions.Compiled);
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
        private int compactOfferTab;
        private readonly float[] actionDesiredWidths = new float[3];
        private readonly Rect[] actionRects = new Rect[3];
        private object cachedUiLanguage;
        private string cachedSearchLabel;
        private string cachedUpdateLabel;
        private string cachedResetLabel;
        private string cachedCancelLabel;
        private string cachedOurOfferLabel;
        private string cachedTheirOfferLabel;
        private float cachedSearchAreaWidth;
        private float cachedSearchLabelWidth;

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
            this.resizeable = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ClampToSafeArea();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ClampToSafeArea();
        }

        private void ClampToSafeArea()
        {
            windowRect = UiScreenSafeArea.ClampWindow(
                windowRect,
                new Vector2(MINIMUM_WINDOW_WIDTH, MINIMUM_WINDOW_HEIGHT));
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
            EnsureUiTextCache();

            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);
            Rect titleRect = inRect.TopPartPixels(Mathf.Min(TITLE_HEIGHT, inRect.height));
            float toolbarHeight = GetToolbarHeight(inRect.width);
            float fixedHeight = titleRect.height + DEFAULT_SPACING * 3f + toolbarHeight + MINIMUM_OFFER_AREA_HEIGHT;
            float availableRoom = Mathf.Max(0f, inRect.height - fixedHeight);
            float desiredAvailableHeight = Mathf.Max(MINIMUM_AVAILABLE_ITEMS_HEIGHT, inRect.height * 0.3f);
            float availableItemsHeight = Mathf.Min(desiredAvailableHeight, availableRoom);
            Rect availableItemsRect = new Rect(inRect.xMin, inRect.yMax - availableItemsHeight, inRect.width, availableItemsHeight);
            Rect toolbarRect = new Rect(
                inRect.xMin,
                Mathf.Max(titleRect.yMax, availableItemsRect.yMin - DEFAULT_SPACING - toolbarHeight),
                inRect.width,
                toolbarHeight);
            Rect offerAreaRect = new Rect(
                inRect.xMin,
                titleRect.yMax + DEFAULT_SPACING,
                inRect.width,
                Mathf.Max(0f, toolbarRect.yMin - titleRect.yMax - DEFAULT_SPACING * 2f));

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            string tradeTitle = "Phinix_trade_tradeTitle".Translate(TextHelper.StripRichText(trade.OtherPartyDisplayName));
            Widgets.LabelFit(titleRect, tradeTitle);
            TooltipHandler.TipRegion(titleRect, tradeTitle);

            Text.Font = previousFont;
            Text.Anchor = previousAnchor;

            bool? pendingAcceptedValue = getPendingAccepted();
            bool ourOfferAccepted = pendingAcceptedValue ?? trade.Accepted;
            bool theirOfferAccepted = trade.OtherPartyAccepted;
            DrawOfferArea(offerAreaRect, ref ourOfferAccepted, ref theirOfferAccepted);
            if (ourOfferAccepted != (pendingAcceptedValue ?? trade.Accepted))
            {
                sendTradeStatusUpdate(ourOfferAccepted);
            }

            DrawToolbar(toolbarRect);

            if (filteredAvailableItems.Count == 0)
            {
                Widgets.DrawMenuSection(availableItemsRect);
                Widgets.NoneLabelCenteredVertically(availableItemsRect, ("Phinix_trade_noItemsAvailable" + (availableItems.Count > 0 ? "WithSearch" : "")).Translate());
            }
            else
            {
                drawItemStackList(availableItemsRect, filteredAvailableItems, ref availableItemsScrollPos, true);
            }
        }

        private void DrawOfferArea(Rect inRect, ref bool ourOfferAccepted, ref bool theirOfferAccepted)
        {
            bool horizontal = inRect.width >= OFFER_MINIMUM_WIDTH * 2f + OFFER_ARROW_COLUMN_WIDTH + DEFAULT_SPACING * 2f;
            string ourAcceptedLabel = ("Phinix_trade_confirmOurTradeCheckbox" + (ourOfferAccepted ? "Checked" : "Unchecked")).Translate();
            string theirAcceptedLabel = ("Phinix_trade_confirmTheirTradeCheckbox" + (trade.OtherPartyAccepted ? "Checked" : "Unchecked")).Translate(TextHelper.StripRichText(trade.OtherPartyDisplayName));
            if (horizontal)
            {
                float panelWidth = Mathf.Max(0f, (inRect.width - OFFER_ARROW_COLUMN_WIDTH - DEFAULT_SPACING * 2f) / 2f);
                Rect ourRect = new Rect(inRect.xMin, inRect.yMin, panelWidth, inRect.height);
                Rect arrowRect = new Rect(ourRect.xMax + DEFAULT_SPACING, inRect.yMin, OFFER_ARROW_COLUMN_WIDTH, inRect.height);
                Rect theirRect = new Rect(arrowRect.xMax + DEFAULT_SPACING, inRect.yMin, panelWidth, inRect.height);
                Rect textureRect = new Rect(arrowRect.xMin, arrowRect.yMin + arrowRect.height * 0.25f, arrowRect.width, arrowRect.height * 0.5f);
                Widgets.DrawTextureFitted(textureRect, tradeArrows, 1f);
                drawOffer(ourRect, cachedOurOfferLabel, ourOfferCache, ref ourOfferScrollPos, ref ourOfferAccepted, ourAcceptedLabel, true, TradeTheme.OurOfferAccent, TradeTheme.OurOfferBg);
                drawOffer(theirRect, cachedTheirOfferLabel, theirOfferCache, ref theirOfferScrollPos, ref theirOfferAccepted, theirAcceptedLabel, false, TradeTheme.TheirOfferAccent, TradeTheme.TheirOfferBg);
                return;
            }

            float tabHeight = Mathf.Min(32f, inRect.height);
            float tabWidth = inRect.width / 2f;
            Rect ourTabRect = new Rect(inRect.xMin, inRect.yMin, tabWidth, tabHeight);
            Rect theirTabRect = new Rect(ourTabRect.xMax, inRect.yMin, tabWidth, tabHeight);
            Color previousColor = UnityEngine.GUI.color;
            if (compactOfferTab == 0) UnityEngine.GUI.color = TradeTheme.AcceptedBadge;
            if (Widgets.ButtonText(ourTabRect, cachedOurOfferLabel)) compactOfferTab = 0;
            UnityEngine.GUI.color = previousColor;
            if (compactOfferTab == 1) UnityEngine.GUI.color = TradeTheme.AcceptedBadge;
            if (Widgets.ButtonText(theirTabRect, cachedTheirOfferLabel)) compactOfferTab = 1;
            UnityEngine.GUI.color = previousColor;

            Rect panelRect = new Rect(inRect.xMin, inRect.yMin + tabHeight + 4f, inRect.width, Mathf.Max(0f, inRect.height - tabHeight - 4f));
            if (compactOfferTab == 0)
                drawOffer(panelRect, cachedOurOfferLabel, ourOfferCache, ref ourOfferScrollPos, ref ourOfferAccepted, ourAcceptedLabel, true, TradeTheme.OurOfferAccent, TradeTheme.OurOfferBg);
            else
                drawOffer(panelRect, cachedTheirOfferLabel, theirOfferCache, ref theirOfferScrollPos, ref theirOfferAccepted, theirAcceptedLabel, false, TradeTheme.TheirOfferAccent, TradeTheme.TheirOfferBg);
        }

        private float GetToolbarHeight(float width)
        {
            float actionWidth = actionDesiredWidths[0] + actionDesiredWidths[1] + actionDesiredWidths[2] + DEFAULT_SPACING * 2f;
            return width >= cachedSearchAreaWidth + actionWidth + DEFAULT_SPACING ? ITEM_ROW_HEIGHT : ITEM_ROW_HEIGHT * 2f + DEFAULT_SPACING;
        }

        private void DrawToolbar(Rect inRect)
        {
            bool stacked = inRect.height > ITEM_ROW_HEIGHT + 1f;
            Rect searchRect;
            Rect actionsRect;
            if (stacked)
            {
                searchRect = new Rect(inRect.xMin, inRect.yMin, inRect.width, ITEM_ROW_HEIGHT);
                actionsRect = new Rect(inRect.xMin, searchRect.yMax + DEFAULT_SPACING, inRect.width, ITEM_ROW_HEIGHT);
            }
            else
            {
                float actionsWidth = Mathf.Min(inRect.width, actionDesiredWidths[0] + actionDesiredWidths[1] + actionDesiredWidths[2] + DEFAULT_SPACING * 2f);
                actionsRect = new Rect(inRect.xMax - actionsWidth, inRect.yMin, actionsWidth, inRect.height);
                searchRect = new Rect(inRect.xMin, inRect.yMin, Mathf.Max(0f, actionsRect.xMin - inRect.xMin - DEFAULT_SPACING), inRect.height);
            }

            float searchLabelWidth = Mathf.Min(searchRect.width * 0.4f, cachedSearchLabelWidth);
            Rect searchLabelRect = new Rect(searchRect.xMin, searchRect.yMin, searchLabelWidth, searchRect.height);
            Rect searchFieldRect = new Rect(searchLabelRect.xMax + DEFAULT_SPACING, searchRect.yMin, Mathf.Max(0f, searchRect.xMax - searchLabelRect.xMax - DEFAULT_SPACING), searchRect.height);
            GUIUtils.SaveTextFormat();
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(searchLabelRect, cachedSearchLabel);
            GUIUtils.RestoreTextFormat();
            string oldSearchText = searchText;
            if (searchFieldRect.width > 0f) searchText = Widgets.TextField(searchFieldRect, searchText);
            if (searchText != oldSearchText) RebuildAvailableItemFilter();

            ResponsiveToolbarResult result = ResponsiveToolbarLayout.Calculate(actionsRect, actionDesiredWidths, 3, 2, ITEM_ROW_HEIGHT, DEFAULT_SPACING, 1, 40f, actionRects);
            for (int i = 0; i < result.VisibleActionCount; i++) DrawToolbarAction(i, actionRects[i]);
            if (result.HasOverflow && Widgets.ButtonText(result.OverflowButtonRect, "⋯"))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                for (int i = result.VisibleActionCount; i < 3; i++) AddToolbarOverflowOption(options, i);
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void DrawToolbarAction(int actionIndex, Rect rect)
        {
            Color previousColor = UnityEngine.GUI.color;
            if (actionIndex == 1) UnityEngine.GUI.color = TradeTheme.CancelButton;
            string label = actionIndex == 0 ? cachedUpdateLabel : actionIndex == 1 ? cachedCancelLabel : cachedResetLabel;
            if (Widgets.ButtonText(rect, label)) ExecuteToolbarAction(actionIndex);
            UnityEngine.GUI.color = previousColor;
        }

        private void AddToolbarOverflowOption(List<FloatMenuOption> options, int actionIndex)
        {
            string label = actionIndex == 0 ? cachedUpdateLabel : actionIndex == 1 ? cachedCancelLabel : cachedResetLabel;
            int capturedIndex = actionIndex;
            options.Add(new FloatMenuOption(label, () => ExecuteToolbarAction(capturedIndex)));
        }

        private void ExecuteToolbarAction(int actionIndex)
        {
            if (actionIndex == 0) UpdateTradeItems();
            else if (actionIndex == 1) sendCancelTradeRequest();
            else ResetTradeItems();
        }

        private void UpdateTradeItems()
        {
            string token = null;
            List<PoppedThing> selectedThings = new List<PoppedThing>();
            try
            {
                token = Guid.NewGuid().ToString();
                foreach (StackedThings itemStack in availableItems) selectedThings.AddRange(itemStack.PopSelectedWithOrigins());
                foreach (PoppedThing selectedThing in selectedThings) selectedThing.DeSpawn();
                lock (pendingItemStacksLock)
                {
                    pendingItemStacks.Add(token, new PendingThings { Things = selectedThings.ToArray(), Timestamp = DateTime.UtcNow });
                }
                hostContext.Log(new LogEventArgs($"Added {selectedThings.Count} item stack(s) to pending", LogLevel.DEBUG));
                IEnumerable<TradeItemSnapshot> actualOffer = trade.ItemsOnOffer.Concat(selectedThings.Select(selectedThing => TradeItemConverter.ConvertThingFromVerse(selectedThing.Thing)));
                tradeService.UpdateTradeItems(trade.TradeId, actualOffer, token);
                hostContext.Log(new LogEventArgs("Sent update", LogLevel.DEBUG));
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(token))
                {
                    lock (pendingItemStacksLock) pendingItemStacks.Remove(token);
                }
                restorePoppedThings(selectedThings, "TradeWindow.UpdateTradeItems");
                refreshAvailableItems();
                hostContext.Log(new LogEventArgs($"Failed to update trade items: {ex}", LogLevel.ERROR));
            }
        }

        private void ResetTradeItems()
        {
            hostContext.DropPods(trade.ItemsOnOffer.Select(TradeItemConverter.ConvertThingFromSnapshot));
            foreach (StackedThings stack in availableItems) stack.Selected = 0;
            refreshAvailableItems();
            tradeService.UpdateTradeItems(trade.TradeId, Array.Empty<TradeItemSnapshot>());
        }

        private void EnsureUiTextCache()
        {
            object language = LanguageDatabase.activeLanguage;
            if (ReferenceEquals(language, cachedUiLanguage) && cachedUpdateLabel != null) return;
            cachedUiLanguage = language;
            cachedSearchLabel = "Phinix_trade_searchLabel".Translate();
            cachedUpdateLabel = "Phinix_trade_updateButton".Translate();
            cachedResetLabel = "Phinix_trade_resetButton".Translate();
            cachedCancelLabel = "Phinix_trade_cancelButton".Translate();
            cachedOurOfferLabel = "Phinix_trade_ourOfferLabel".Translate();
            cachedTheirOfferLabel = "Phinix_trade_theirOfferLabel".Translate();
            cachedSearchLabelWidth = Text.CalcSize(cachedSearchLabel).x + 4f;
            cachedSearchAreaWidth = Mathf.Max(180f, cachedSearchLabelWidth + SEARCH_TEXT_FIELD_WIDTH + DEFAULT_SPACING);
            actionDesiredWidths[0] = Mathf.Max(BUTTON_WIDTH, Text.CalcSize(cachedUpdateLabel).x + 20f);
            actionDesiredWidths[1] = Mathf.Max(BUTTON_WIDTH, Text.CalcSize(cachedCancelLabel).x + 20f);
            actionDesiredWidths[2] = Mathf.Max(BUTTON_WIDTH, Text.CalcSize(cachedResetLabel).x + 20f);
        }

        private void RebuildAvailableItemFilter()
        {
            filteredAvailableItems.Clear();
            for (int i = 0; i < availableItems.Count; i++)
            {
                StackedThings stack = availableItems[i];
                if (stack.Count > 0 && stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    filteredAvailableItems.Add(stack);
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
            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);
            if (inRect.width <= 0f || inRect.height <= 0f) return;
            Rect accentRect = new Rect(inRect.xMin, inRect.yMin, OFFER_ACCENT_WIDTH, inRect.height);
            Widgets.DrawBoxSolid(accentRect, accentColor);

            Rect bgRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH, inRect.yMin, Mathf.Max(0f, inRect.width - OFFER_ACCENT_WIDTH), inRect.height);
            Widgets.DrawBoxSolid(bgRect, bgColor);

            float contentWidth = Mathf.Max(0f, inRect.width - OFFER_ACCENT_WIDTH - 12f);
            float measuredBadgeHeight = interactive
                ? BADGE_HEIGHT
                : Mathf.Clamp(Text.CalcHeight(acceptedLabel, Mathf.Max(1f, contentWidth)) + 4f, BADGE_HEIGHT, 54f);
            Rect titleRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH + 6f, inRect.yMin + 2f, contentWidth, Mathf.Min(OFFER_TITLE_HEIGHT, inRect.height));
            Rect badgeRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH + 6f, Mathf.Max(titleRect.yMax, inRect.yMax - measuredBadgeHeight - 4f), contentWidth, Mathf.Min(measuredBadgeHeight, inRect.height));
            Rect itemListRect = new Rect(inRect.xMin + OFFER_ACCENT_WIDTH + 4f, titleRect.yMax + 4f, Mathf.Max(0f, inRect.width - OFFER_ACCENT_WIDTH - 8f), Mathf.Max(0f, badgeRect.yMin - DEFAULT_SPACING - titleRect.yMax - 4f));

            SaveTextFormat();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.LabelFit(titleRect, title);
            TooltipHandler.TipRegion(titleRect, title);
            RestoreTextFormat();

            if (interactive)
            {
                float btnWidth = 120f;
                btnWidth = Mathf.Min(btnWidth, badgeRect.width);
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
                TooltipHandler.TipRegion(badgeRect, TextHelper.StripRichText(acceptedLabel));
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
            Rect contentRect = new Rect(inRect.xMin, inRect.yMin, scrollbarsPresent ? Mathf.Max(0f, inRect.width - SCROLLBAR_WIDTH) : inRect.width, ITEM_ROW_HEIGHT * stacks.Count);
            bool scrollRequired = contentRect.height > inRect.height;
            if (scrollRequired) Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);

            VirtualListRange visibleRange = VirtualListLayout.GetFixedRange(stacks.Count, ITEM_ROW_HEIGHT, scrollRequired ? scrollPos.y : 0f, inRect.height, 2);
            for (int stackIndex = visibleRange.FirstIndex; stackIndex < visibleRange.EndIndexExclusive; stackIndex++)
            {
                StackedThings stack = stacks[stackIndex];
                if (stack.Things.Count == 0) continue;

                float currentY = contentRect.yMin + stackIndex * ITEM_ROW_HEIGHT;
                Rect rowRect = new Rect(contentRect.xMin, currentY, contentRect.width, ITEM_ROW_HEIGHT);

                if ((stackIndex & 1) != 0) Widgets.DrawHighlight(rowRect);
                else if (Mouse.IsOver(rowRect)) Widgets.DrawBoxSolid(rowRect, TradeTheme.RowHoverBg);

                Rect iconRect = rowRect.LeftPartPixels(ICON_WIDTH);
                Widgets.ThingIcon(iconRect, stack.ThingDef, stack.StuffDef, stack.StyleDef, 0.9f);

                Rect itemNameRect;
                if (interactive)
                {
                    bool showTenButtons = rowRect.width >= 430f;
                    bool showOneButtons = rowRect.width >= 350f;
                    int visibleButtonCount = showTenButtons ? 4 : showOneButtons ? 2 : 0;
                    float buttonAreaWidth = visibleButtonCount * ITEM_BUTTON_WIDTH + ITEM_QUANTITY_FIELD_WIDTH + ITEM_COUNT_WIDTH + DEFAULT_SPACING * (visibleButtonCount + 1);
                    Rect buttonAreaRect = new Rect(rowRect.xMax - RIGHT_PADDING - buttonAreaWidth, rowRect.yMin, buttonAreaWidth, rowRect.height);
                    float controlsX = buttonAreaRect.xMin;
                    Rect btnMinus10 = new Rect(controlsX, buttonAreaRect.yMin, showTenButtons ? ITEM_BUTTON_WIDTH : 0f, buttonAreaRect.height);
                    if (showTenButtons) controlsX = btnMinus10.xMax + DEFAULT_SPACING;
                    Rect btnMinus1 = new Rect(controlsX, buttonAreaRect.yMin, showOneButtons ? ITEM_BUTTON_WIDTH : 0f, buttonAreaRect.height);
                    if (showOneButtons) controlsX = btnMinus1.xMax + DEFAULT_SPACING;
                    Rect quantityFieldRect = new Rect(controlsX, buttonAreaRect.yMin, ITEM_QUANTITY_FIELD_WIDTH, buttonAreaRect.height);
                    Rect availableCountRect = new Rect(quantityFieldRect.xMax + DEFAULT_SPACING, buttonAreaRect.yMin, ITEM_COUNT_WIDTH, buttonAreaRect.height);
                    controlsX = availableCountRect.xMax + DEFAULT_SPACING;
                    Rect btnPlus1 = new Rect(controlsX, buttonAreaRect.yMin, showOneButtons ? ITEM_BUTTON_WIDTH : 0f, buttonAreaRect.height);
                    if (showOneButtons) controlsX = btnPlus1.xMax + DEFAULT_SPACING;
                    Rect btnPlus10 = new Rect(controlsX, buttonAreaRect.yMin, showTenButtons ? ITEM_BUTTON_WIDTH : 0f, buttonAreaRect.height);

                    itemNameRect = new Rect(iconRect.xMax + DEFAULT_SPACING, rowRect.yMin, Mathf.Max(0f, buttonAreaRect.xMin - iconRect.xMax - DEFAULT_SPACING * 2), rowRect.height);

                    if (showTenButtons && Widgets.ButtonText(btnMinus10, "-10")) stack.Selected = Clamp(stack.Selected - 10, 0, stack.Count);
                    if (showOneButtons && Widgets.ButtonText(btnMinus1, "-1")) stack.Selected = Clamp(stack.Selected - 1, 0, stack.Count);

                    string buf = stack.Selected == 0 ? "" : stack.Selected.ToString();
                    buf = Widgets.TextField(quantityFieldRect, buf, 100, itemCountInputRegex);
                    int parsedCount;
                    stack.Selected = string.IsNullOrEmpty(buf) ? 0 : int.TryParse(buf, out parsedCount) ? Clamp(parsedCount, 0, stack.Count) : stack.Count;

                    SaveTextFormat();
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(availableCountRect, $"/ {stack.Count}");
                    RestoreTextFormat();

                    if (showOneButtons && Widgets.ButtonText(btnPlus1, "+1")) stack.Selected = Clamp(stack.Selected + 1, 0, stack.Count);
                    if (showTenButtons && Widgets.ButtonText(btnPlus10, "+10")) stack.Selected = Clamp(stack.Selected + 10, 0, stack.Count);

                    if (Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(rowRect))
                    {
                        DrawItemContextMenu(stack);
                        Event.current.Use();
                    }
                }
                else
                {
                    Rect itemCountRect = new Rect(rowRect.xMax - ITEM_COUNT_WIDTH - RIGHT_PADDING, rowRect.yMin, ITEM_COUNT_WIDTH, rowRect.height);
                    itemNameRect = new Rect(iconRect.xMax + DEFAULT_SPACING, rowRect.yMin, Mathf.Max(0f, itemCountRect.xMin - iconRect.xMax - DEFAULT_SPACING), rowRect.height);

                    SaveTextFormat();
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(itemCountRect, stack.Count.ToStringSI());
                    RestoreTextFormat();
                }

                SaveTextFormat();
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.LabelFit(itemNameRect, stack.Label);
                TooltipHandler.TipRegion(itemNameRect, stack.Label);
                RestoreTextFormat();
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
                new FloatMenuOption("+10", () => stack.Selected = Clamp(stack.Selected + 10, 0, stack.Count)),
                new FloatMenuOption("-10", () => stack.Selected = Clamp(stack.Selected - 10, 0, stack.Count)),
                new FloatMenuOption("+1", () => stack.Selected = Clamp(stack.Selected + 1, 0, stack.Count)),
                new FloatMenuOption("-1", () => stack.Selected = Clamp(stack.Selected - 1, 0, stack.Count)),
            };
            Find.WindowStack.Add(new FloatMenu(items));
        }
    }
}
