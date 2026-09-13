using System;
using System.Collections.Generic;
using System.Threading;
using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UnityEngine;
using UserManagement;
using Utils;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public class TradeList
    {
        private readonly ITradeUiHostContext hostContext;
        private readonly IClientTradeService tradeService;
        private const float SCROLLBAR_WIDTH = 16f;

        private const float DEFAULT_SPACING = 10f;

        private const float TRADE_TITLE_LABEL_HEIGHT = 20f;

        private const float ACCEPTED_STATE_LABEL_HEIGHT = 18f;

        private const float ROW_HEIGHT = TRADE_TITLE_LABEL_HEIGHT + ACCEPTED_STATE_LABEL_HEIGHT;

        private const float BUTTON_WIDTH = 80f;
        private const float NARROW_ROW_HEIGHT = 78f;
        private const float MINIMUM_INLINE_TITLE_WIDTH = 180f;
        private const int VIRTUAL_OVERSCAN = 2;

        /// <summary>
        /// List of active trades.
        /// </summary>
        private readonly List<ClientTradeSnapshot> trades = new List<ClientTradeSnapshot>();
        /// <summary>
        /// Filtered list of active trades.
        /// </summary>
        private readonly List<ClientTradeSnapshot> filteredTrades = new List<ClientTradeSnapshot>();
        /// <summary>
        /// Whether <see cref="trades"/> has been updated and <see cref="filteredTrades"/> should be repopulated by the
        /// UI thread.
        /// </summary>
        private bool tradesChanged = false;
        /// <summary>
        /// Lock object protecting <see cref="trades"/>.
        /// </summary>
        private readonly object tradesLock = new object();
        private Vector2 scrollPos;
        private object cachedLanguage;
        private string cachedOpenLabel;
        private string cachedCancelLabel;

        /// <summary>
        /// Creates a new <see cref="TradeList"/>.
        /// </summary>
        public TradeList(ITradeUiHostContext hostContext)
        {
            this.hostContext = hostContext;
            tradeService = hostContext.TradeService;

            // Subscribe to update events
            hostContext.OnDisconnect += onDisconnectHandler;
            tradeService.OnTradesSynced += onTradesSyncedHandler;
            tradeService.OnTradeCancelled += onTradeCompletedOrCancelledHandler;
            tradeService.OnTradeCompleted += onTradeCompletedOrCancelledHandler;
            tradeService.OnTradeCreationSuccess += onTradeCreationSuccessHandler;
            tradeService.OnTradeUpdateFailure += onTradeUpdateHandler;
            tradeService.OnTradeUpdateSuccess += onTradeUpdateHandler;
            hostContext.OnUserDisplayNameChanged += onUserDisplayNameChangedHandler;

            // Pre-fill the trade rows
            repopulateTradeRows();
        }

        public void Draw(Rect inRect)
        {
            if (inRect.width <= 0f || inRect.height <= 0f) return;
            EnsureTextCache();
            if (tradesChanged)
            {
                // Try lock the unfiltered list, otherwise wait until the next frame to refresh content
                if (Monitor.TryEnter(tradesLock))
                {
                    // Repopulate the list content
                    filteredTrades.Clear();
                    filteredTrades.AddRange(trades);

                    // Unset the changed flag and release the lock
                    tradesChanged = false;
                    Monitor.Exit(tradesLock);
                }
            }

            // Draw a placeholder if there are no trades
            if (filteredTrades == null || filteredTrades.Count == 0)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_trade_noActiveTradesPlaceholder".Translate());
                return;
            }

            bool compactRows = inRect.width < BUTTON_WIDTH * 2f + DEFAULT_SPACING * 2f + MINIMUM_INLINE_TITLE_WIDTH;
            float rowHeight = compactRows ? NARROW_ROW_HEIGHT : ROW_HEIGHT;
            Rect contentRect = new Rect(
                x: inRect.xMin,
                y: inRect.yMin,
                width: inRect.width,
                height: rowHeight * filteredTrades.Count
            );
            bool scrollRequired = contentRect.height > inRect.height;
            if (scrollRequired) contentRect.width = Mathf.Max(0f, inRect.width - SCROLLBAR_WIDTH);

            if (scrollRequired) Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);
            VirtualListRange visibleRange = VirtualListLayout.GetFixedRange(filteredTrades.Count, rowHeight, scrollRequired ? scrollPos.y : 0f, inRect.height, VIRTUAL_OVERSCAN);
            for (int i = visibleRange.FirstIndex; i < visibleRange.EndIndexExclusive; i++)
            {
                ClientTradeSnapshot trade = filteredTrades[i];
                float currentY = contentRect.yMin + i * rowHeight;

                Rect rowRect = new Rect(contentRect.xMin, currentY, contentRect.width, rowHeight);
                Rect buttonAreaRect = compactRows
                    ? new Rect(rowRect.xMin, rowRect.yMax - 30f, rowRect.width, 30f)
                    : new Rect(rowRect.xMax - (BUTTON_WIDTH * 2 + DEFAULT_SPACING), currentY, BUTTON_WIDTH * 2 + DEFAULT_SPACING, ROW_HEIGHT);
                float textWidth = compactRows ? rowRect.width : Mathf.Max(0f, rowRect.width - buttonAreaRect.width - DEFAULT_SPACING);
                Rect tradeTitleRect = new Rect(rowRect.xMin, currentY, textWidth, TRADE_TITLE_LABEL_HEIGHT);
                Rect acceptedStateRect = new Rect(rowRect.xMin, tradeTitleRect.yMax, textWidth, ACCEPTED_STATE_LABEL_HEIGHT);

                // Background highlight
                if (i % 2 != 0) Widgets.DrawHighlight(rowRect);
                else if (Mouse.IsOver(rowRect)) Widgets.DrawBoxSolid(rowRect, TradeTheme.RowHoverBg);

                // Save the current text settings
                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;

                // Trade with ... label
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                string tradeTitle = "Phinix_trade_activeTrade_tradeWithLabel".Translate(TextHelper.StripRichText(trade.OtherPartyDisplayName));
                Widgets.LabelFit(tradeTitleRect, tradeTitle);
                TooltipHandler.TipRegion(tradeTitleRect, tradeTitle);

                // Accepted state label
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.LowerLeft;
                string acceptedState = ("Phinix_trade_activeTrade_theyHave" + (!trade.OtherPartyAccepted ? "Not" : "") + "Accepted").Translate();
                Widgets.LabelFit(acceptedStateRect, acceptedState);
                TooltipHandler.TipRegion(acceptedStateRect, acceptedState);

                // Restore the text settings
                Text.Font = previousFont;
                Text.Anchor = previousAnchor;

                // Open button
                float actionWidth = Mathf.Max(0f, (buttonAreaRect.width - DEFAULT_SPACING) / 2f);
                Rect openRect = new Rect(buttonAreaRect.xMin, buttonAreaRect.yMin, actionWidth, buttonAreaRect.height);
                Rect cancelRect = new Rect(openRect.xMax + DEFAULT_SPACING, buttonAreaRect.yMin, actionWidth, buttonAreaRect.height);
                if (Widgets.ButtonText(openRect, cachedOpenLabel))
                {
                    hostContext.OpenTradeWindow(trade);
                }

                // Cancel button
                if (Widgets.ButtonText(cancelRect, cachedCancelLabel))
                {
                    tradeService.CancelTrade(trade.TradeId);
                }
            }
            if (scrollRequired) Widgets.EndScrollView();
        }

        private void EnsureTextCache()
        {
            object language = LanguageDatabase.activeLanguage;
            if (ReferenceEquals(language, cachedLanguage) && cachedOpenLabel != null) return;
            cachedLanguage = language;
            cachedOpenLabel = "Phinix_trade_activeTrade_openButton".Translate();
            cachedCancelLabel = "Phinix_trade_activeTrade_cancelButton".Translate();
        }

        /// <summary>
        /// Repopulates <see cref="trades"/> with the current active trades.
        /// </summary>
        /// <seealso cref="Client.GetTrades()"/>
        private void repopulateTradeRows()
        {
            lock (tradesLock)
            {
                // Clear and repopulate the list with active trades
                trades.Clear();
                trades.AddRange(tradeService.GetTrades());

                // Mark the rows to be updated
                tradesChanged = true;

                hostContext.Log(new LogEventArgs("Repopulated trade list", LogLevel.DEBUG));
            }
        }

        private void onDisconnectHandler(object sender, EventArgs e)
        {
            lock (tradesLock)
            {
                // Clear out the trade list
                trades.Clear();
                tradesChanged = true;
            }
        }

        private void onTradesSyncedHandler(object sender, TradesSyncedEventArgs args)
        {
            lock (tradesLock)
            {
                // Repopulate trade list with this set and mark the rows to be updated
                trades.Clear();
                trades.AddRange(args.Trades);
                tradesChanged = true;
            }
        }

        private void onTradeCompletedOrCancelledHandler(object sender, TradeCompletionEventArgs args)
        {
            lock (tradesLock)
            {
                // Remove the trade and mark the rows to be updated
                trades.RemoveAll(t => t.TradeId == args.TradeId);
                tradesChanged = true;
            }
        }

        private void onTradeCreationSuccessHandler(object sender, TradeCreationEventArgs args)
        {
            lock (tradesLock)
            {
                // Add the trade and mark the rows to be updated
                if (args.Trade != null)
                {
                    trades.Add(args.Trade);
                    tradesChanged = true;
                }
            }
        }

        private void onTradeUpdateHandler(object sender, TradeUpdateEventArgs args)
        {
            lock (tradesLock)
            {
                // Update the trade row
                int index = trades.FindIndex(t => t.TradeId == args.Trade.TradeId);
                if (index >= 0 && args.Trade != null)
                {
                    trades[index] = args.Trade;
                }

                // Mark the rows to be updated
                tradesChanged = true;
            }
        }

        private void onUserDisplayNameChangedHandler(object sender, UserDisplayNameChangedEventArgs args)
        {
            lock (tradesLock)
            {
                bool changed = false;
                int matchIndex = trades.FindIndex(0, t => t.OtherPartyUuid == args.Uuid);
                while (matchIndex >= 0)
                {
                    changed = true;
                    ImmutableUser user = trades[matchIndex].OtherParty;
                    user = new ImmutableUser(
                        uuid: user.Uuid,
                        displayName: args.NewDisplayName,
                        loggedIn: user.LoggedIn,
                        acceptingTrades: user.AcceptingTrades
                    );

                    trades[matchIndex] = new ClientTradeSnapshot(
                        tradeId: trades[matchIndex].TradeId,
                        otherParty: user,
                        ourItemsOnOffer: trades[matchIndex].ItemsOnOffer,
                        otherPartyItemsOnOffer: trades[matchIndex].OtherPartyItemsOnOffer,
                        accepted: trades[matchIndex].Accepted,
                        otherPartyAccepted: trades[matchIndex].OtherPartyAccepted
                    );

                    try
                    {
                        matchIndex = trades.FindIndex(matchIndex + 1, t => t.OtherPartyUuid == args.Uuid);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        break;
                    }
                }

                if (changed)
                {
                    tradesChanged = true;
                }
            }
        }
    }
}
