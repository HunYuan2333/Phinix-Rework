using System;
using PhinixClient;
using PhinixClient.Framework;
using System.Threading;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Direct trade sub-tab: online users list + active trades list.
    /// </summary>
    public class DirectTradePanel
    {
        private const float ROW_HEIGHT = 36f;
        private const float BUTTON_WIDTH = 80f;
        private const float BUTTON_HEIGHT = 28f;
        private const float TOOLBAR_HEIGHT = 36f;
        private const float SPACING = 6f;

        private readonly TalentTabs tabs = new TalentTabs("Phinix_legacyTalentTrade_tradeOnlineUsers", "Phinix_legacyTalentTrade_tradeActiveTrades");
        private readonly List<string> others = new List<string>();
        private readonly Dictionary<string, string> names = new Dictionary<string, string>();
        private IClientUserEventStream userEvents;
        private int usersVersion;
        private int cachedUsersVersion = -1;

        internal void BindUserEvents(IClientUserEventStream events)
        {
            if (userEvents != null)
            {
                userEvents.UsersChanged -= OnUsersChanged;
                userEvents.UserDisplayNameChanged -= OnUsersChanged;
                userEvents.Disconnected -= OnUsersChanged;
            }
            userEvents = events;
            if (userEvents != null)
            {
                userEvents.UsersChanged += OnUsersChanged;
                userEvents.UserDisplayNameChanged += OnUsersChanged;
                userEvents.Disconnected += OnUsersChanged;
            }
            Interlocked.Increment(ref usersVersion);
        }

        private void OnUsersChanged(object sender, EventArgs args)
        {
            Interlocked.Increment(ref usersVersion);
        }
        private Vector2 usersScrollPos;
        private Vector2 tradesScrollPos;
        // §8.3：Draw 路径不每帧取快照——按状态版本缓存
        private int cachedStateVersion = -1;
        private DirectTrade[] cachedTrades = Array.Empty<DirectTrade>();

        // Currently open trade window
        private DirectTradeWindow openTradeWindow;

        public void Draw(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            var split = ResponsiveSplitLayout.Calculate(rect, new Vector2(240f, rect.height),
                new Vector2(320f, rect.height), new Vector2(rect.width * 0.4f, rect.height), SPACING, tabs.Selected == 0);
            if (split.Mode == ResponsiveSplitMode.Horizontal)
            {
                DrawOnlineUsers(split.FirstRect);
                DrawActiveTrades(split.SecondRect);
            }
            else
            {
                Rect content = tabs.Draw(rect);
                if (tabs.Selected == 0) DrawOnlineUsers(content);
                else DrawActiveTrades(content);
            }
        }

        private void DrawOnlineUsers(Rect rect)
        {
            if (rect.height <= TOOLBAR_HEIGHT || rect.width <= 0f) return;
            TalentTradeUi.Label(new Rect(rect.x, rect.y, rect.width, TOOLBAR_HEIGHT), "Phinix_legacyTalentTrade_tradeOnlineUsers".Translate());
            Rect listRect = TalentTradeUi.Below(rect, TOOLBAR_HEIGHT);
            // Network callbacks only invalidate; UI snapshots are rebuilt on the draw thread.
            int version = Volatile.Read(ref usersVersion);
            if (cachedUsersVersion != version)
            {
                cachedUsersVersion = version;
                string localUuid = TalentTradeManager.GetLocalUuid();
                string[] users = TalentTradeManager.GetOnlineUserUuids();
                others.Clear();
                names.Clear();
                for (int i = 0; i < users.Length; i++)
                {
                    if (users[i] == localUuid) continue;
                    others.Add(users[i]);
                    string name;
                    names[users[i]] = LegacyTalentTradeRuntime.TryGetDisplayName(users[i], out name) ? name : users[i];
                }
            }
            float width = Mathf.Max(0f, listRect.width - 16f);
            float stride = (width < 240f ? 66f : ROW_HEIGHT) + SPACING;
            usersScrollPos.y = Mathf.Clamp(usersScrollPos.y, 0f, Mathf.Max(0f, others.Count * stride - listRect.height));
            Widgets.BeginScrollView(listRect, ref usersScrollPos, new Rect(0f, 0f, width, others.Count * stride));
            try
            {
                var range = VirtualListLayout.GetFixedRange(others.Count, stride, usersScrollPos.y, listRect.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                    DrawUserRow(new Rect(0f, i * stride, width, stride - SPACING), others[i]);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawUserRow(Rect rect, string uuid)
        {
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Rect inner = TalentTradeUi.Inset(rect, 4f);
            bool compact = rect.width < 240f;
            float buttonWidth = Mathf.Min(BUTTON_WIDTH, inner.width);
            Rect button = new Rect(compact ? inner.x : inner.xMax - buttonWidth,
                compact ? inner.yMax - BUTTON_HEIGHT : inner.y, compact ? inner.width : buttonWidth, BUTTON_HEIGHT);
            string name;
            TalentTradeUi.Label(new Rect(inner.x, inner.y, compact ? inner.width : Mathf.Max(0f, inner.width - buttonWidth - SPACING), 24f),
                names.TryGetValue(uuid, out name) ? name : uuid);
            if (TalentTradeUi.Button(button, "Phinix_legacyTalentTrade_tradeWith".Translate()))
            {
                string tradeId = TalentTradeManager.InitiateDirectTrade(uuid);
                if (tradeId != null) OpenTradeWindow(tradeId);
            }
        }

        private void DrawActiveTrades(Rect rect)
        {
            if (rect.height <= TOOLBAR_HEIGHT || rect.width <= 0f) return;
            if (cachedStateVersion != TalentTradeManager.StateVersion)
            {
                cachedStateVersion = TalentTradeManager.StateVersion;
                cachedTrades = TalentTradeManager.GetActiveTradesSnapshot();
            }
            TalentTradeUi.Label(new Rect(rect.x, rect.y, rect.width, TOOLBAR_HEIGHT), "Phinix_legacyTalentTrade_tradeActiveTrades".Translate());
            Rect listRect = TalentTradeUi.Below(rect, TOOLBAR_HEIGHT);
            if (cachedTrades.Length == 0)
            {
                Widgets.NoneLabelCenteredVertically(listRect, "Phinix_legacyTalentTrade_tradeNoActive".Translate());
                return;
            }
            float width = Mathf.Max(0f, listRect.width - 16f);
            float stride = (width < 440f ? 66f : ROW_HEIGHT) + SPACING;
            tradesScrollPos.y = Mathf.Clamp(tradesScrollPos.y, 0f, Mathf.Max(0f, cachedTrades.Length * stride - listRect.height));
            Widgets.BeginScrollView(listRect, ref tradesScrollPos, new Rect(0f, 0f, width, cachedTrades.Length * stride));
            try
            {
                string uuid = TalentTradeManager.GetLocalUuid();
                var range = VirtualListLayout.GetFixedRange(cachedTrades.Length, stride, tradesScrollPos.y, listRect.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                    if (cachedTrades[i] != null)
                        DrawTradeRow(new Rect(0f, i * stride, width, stride - SPACING), cachedTrades[i], uuid);
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawTradeRow(Rect rect, DirectTrade trade, string localUuid)
        {
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            Rect inner = TalentTradeUi.Inset(rect, 4f);

            // Other party name
            bool isInitiator = trade.InitiatorUuid == localUuid;
            string otherName = isInitiator ? trade.TargetName : trade.InitiatorName;
            if (string.IsNullOrEmpty(otherName)) otherName = isInitiator ? trade.TargetUuid : trade.InitiatorUuid;

            Text.Font = GameFont.Small;
            bool compact = rect.width < 440f;
            float actionWidth = Mathf.Min(BUTTON_WIDTH, Mathf.Max(0f, (inner.width - SPACING) / 2f));
            float labelWidth = compact ? inner.width : Mathf.Max(0f, inner.width - actionWidth * 2 - SPACING * 2);
            TalentTradeUi.Label(new Rect(inner.x, inner.y, labelWidth, 24f),
                otherName + " | " + "Phinix_legacyTalentTrade_tradeStatus".Translate(trade.State.ToString()));

            float btnX = compact ? inner.x : inner.xMax - actionWidth * 2 - SPACING;
            float buttonY = compact ? inner.yMax - BUTTON_HEIGHT : inner.y;

            // Pending trades from others: Accept/Reject
            if (trade.State == DirectTradeState.Pending && trade.TargetUuid == localUuid)
            {
                Rect acceptBtn = new Rect(btnX, buttonY, actionWidth, BUTTON_HEIGHT);
                if (TalentTradeUi.Button(acceptBtn, "Phinix_legacyTalentTrade_tradeAcceptRequest".Translate()))
                {
                    TalentTradeManager.AcceptTrade(trade.Id);
                    OpenTradeWindow(trade.Id);
                }
                btnX += actionWidth + SPACING;

                Rect rejectBtn = new Rect(btnX, buttonY, actionWidth, BUTTON_HEIGHT);
                if (TalentTradeUi.Button(rejectBtn, "Phinix_legacyTalentTrade_tradeRejectRequest".Translate()))
                {
                    TalentTradeManager.RejectTrade(trade.Id);
                }
            }
            else
            {
                // Open button
                Rect openBtn = new Rect(btnX, buttonY, actionWidth, BUTTON_HEIGHT);
                if (TalentTradeUi.Button(openBtn, "Phinix_legacyTalentTrade_tradeOpen".Translate()))
                {
                    OpenTradeWindow(trade.Id);
                }
                btnX += actionWidth + SPACING;

                // Cancel button
                Rect cancelBtn = new Rect(btnX, buttonY, actionWidth, BUTTON_HEIGHT);
                if (TalentTradeUi.Button(cancelBtn, "Phinix_legacyTalentTrade_cancel".Translate()))
                {
                    TalentTradeManager.CancelTrade(trade.Id);
                }
            }
        }

        private void OpenTradeWindow(string tradeId)
        {
            DirectTrade trade = TalentTradeManager.GetTrade(tradeId);
            if (trade == null) return;

            if (openTradeWindow != null)
            {
                openTradeWindow.Close();
            }

            openTradeWindow = new DirectTradeWindow(trade);
            Find.WindowStack.Add(openTradeWindow);
        }
    }
}
