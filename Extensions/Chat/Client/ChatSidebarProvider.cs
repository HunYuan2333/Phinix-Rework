using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatSidebarProvider : IServerSidebarProvider
    {
        private const float DefaultSpacing = 10f;
        private const float SettingsButtonHeight = 30f;
        private const float UserSearchHeight = 30f;
        private const float StatusBarHeight = 22f;
        private const float StatusDotSize = 10f;
        private static readonly Color OnlineStatusColor = new Color(0.35f, 0.8f, 0.4f, 1f);
        private static readonly Color OfflineStatusColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        private readonly IChatUiHostContext hostContext;
        private readonly IClientSessionContext sessionContext;
        private readonly Action openSettingsWindow;
        private readonly UserList userList;
        private string userSearch = string.Empty;
        private bool wasOnline;

        public ChatSidebarProvider(
            IChatUiHostContext hostContext,
            IClientSessionContext sessionContext,
            IClientUserDirectory userDirectory,
            IClientSettingsContext settingsContext,
            Action openSettingsWindow)
        {
            this.hostContext = hostContext;
            this.sessionContext = sessionContext;
            this.openSettingsWindow = openSettingsWindow;
            userList = new UserList(hostContext, userDirectory, settingsContext);
        }

        public float Order => 0f;

        public float PreferredWidth => 210f;

        public string TabLabel => "Phinix_sidebar_users".Translate();

        public void Draw(Rect inRect)
        {
            bool online = sessionContext.Authenticated && sessionContext.LoggedIn;

            float cursorY = inRect.yMin;

            // 连接状态条：状态点 + 在线人数（或"未连接"）。
            Rect statusRect = new Rect(inRect.x, cursorY, inRect.width, StatusBarHeight);
            Rect statusDotRect = new Rect(
                statusRect.xMin,
                statusRect.yMin + (StatusBarHeight - StatusDotSize) / 2f,
                StatusDotSize,
                StatusDotSize);
            Widgets.DrawBoxSolid(statusDotRect, online ? OnlineStatusColor : OfflineStatusColor);

            string statusText = online
                ? "Phinix_chat_onlineCount".Translate(userList.OnlineCount)
                : "Phinix_chat_notConnected".Translate();
            Rect statusTextRect = new Rect(
                statusDotRect.xMax + 4f,
                statusRect.yMin,
                statusRect.xMax - statusDotRect.xMax - 4f,
                statusRect.height);
            TextAnchor oldStatusAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(statusTextRect, statusText.Colorize(ChatTheme.ReplyQuoteText));
            Text.Anchor = oldStatusAnchor;

            cursorY += StatusBarHeight + DefaultSpacing;

            Rect settingsButtonRect = new Rect(inRect.x, cursorY, inRect.width, SettingsButtonHeight);
            cursorY += SettingsButtonHeight + DefaultSpacing;

            Rect userSearchRect = new Rect(inRect.x, cursorY, inRect.width, UserSearchHeight);
            cursorY += UserSearchHeight + DefaultSpacing;

            Rect userListRect = new Rect(inRect.x, cursorY, inRect.width, inRect.yMax - cursorY);

            if (Widgets.ButtonText(settingsButtonRect, "Phinix_chat_settingsButton".Translate()))
            {
                openSettingsWindow?.Invoke();
            }

            string userSearchOld = userSearch;
            userSearch = Widgets.TextField(userSearchRect, userSearch);
            if (!userSearch.Equals(userSearchOld, StringComparison.Ordinal))
            {
                userList.Filter(userSearch);
            }

            if (online && !wasOnline)
            {
                userList.Refresh();
            }

            wasOnline = online;

            if (online)
            {
                userList.Draw(userListRect);
            }
            else
            {
                Widgets.DrawMenuSection(userListRect);
            }
        }
    }
}
