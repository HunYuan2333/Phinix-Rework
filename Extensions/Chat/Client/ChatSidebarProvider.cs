using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatSidebarProvider : IServerSidebarProvider, IResponsiveSidebarProvider
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
        private object cachedLanguage;
        private bool cachedStatusOnline;
        private int cachedOnlineCount = -1;
        private string cachedStatusText;
        private string cachedSettingsLabel;

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
        public float MinimumWidth => 160f;
        public bool CanCollapse => true;

        public string TabLabel => "Phinix_sidebar_users".Translate();

        public void Draw(Rect inRect)
        {
            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);
            bool online = sessionContext.Authenticated && sessionContext.LoggedIn;
            EnsureTextCache(online, userList.OnlineCount);

            float cursorY = inRect.yMin;

            // 连接状态条：状态点 + 在线人数（或"未连接"）。
            Rect statusRect = ClipToBottom(new Rect(inRect.x, cursorY, inRect.width, StatusBarHeight), inRect.yMax);
            Rect statusDotRect = new Rect(
                statusRect.xMin,
                statusRect.yMin + Mathf.Max(0f, (statusRect.height - StatusDotSize) / 2f),
                Mathf.Min(StatusDotSize, statusRect.width),
                Mathf.Min(StatusDotSize, statusRect.height));
            if (statusDotRect.width > 0f && statusDotRect.height > 0f)
            {
                Widgets.DrawBoxSolid(statusDotRect, online ? OnlineStatusColor : OfflineStatusColor);
            }

            Rect statusTextRect = new Rect(
                statusDotRect.xMax + 4f,
                statusRect.yMin,
                Mathf.Max(0f, statusRect.xMax - statusDotRect.xMax - 4f),
                statusRect.height);
            TextAnchor oldStatusAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (statusTextRect.width > 0f && statusTextRect.height > 0f)
            {
                Widgets.Label(statusTextRect, cachedStatusText);
            }
            Text.Anchor = oldStatusAnchor;

            cursorY += StatusBarHeight + DefaultSpacing;

            Rect settingsButtonRect = ClipToBottom(new Rect(inRect.x, cursorY, inRect.width, SettingsButtonHeight), inRect.yMax);
            cursorY += SettingsButtonHeight + DefaultSpacing;

            Rect userSearchRect = ClipToBottom(new Rect(inRect.x, cursorY, inRect.width, UserSearchHeight), inRect.yMax);
            cursorY += UserSearchHeight + DefaultSpacing;

            Rect userListRect = new Rect(inRect.x, Mathf.Min(cursorY, inRect.yMax), inRect.width, Mathf.Max(0f, inRect.yMax - cursorY));

            if (settingsButtonRect.height > 0f && Widgets.ButtonText(settingsButtonRect, cachedSettingsLabel))
            {
                openSettingsWindow?.Invoke();
            }

            string userSearchOld = userSearch;
            if (userSearchRect.height > 0f)
            {
                userSearch = Widgets.TextField(userSearchRect, userSearch);
            }
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
                if (userListRect.width > 0f && userListRect.height > 0f)
                {
                    userList.Draw(userListRect);
                }
            }
            else
            {
                if (userListRect.width > 0f && userListRect.height > 0f)
                {
                    Widgets.DrawMenuSection(userListRect);
                }
            }
        }

        private void EnsureTextCache(bool online, int onlineCount)
        {
            object language = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(language, cachedLanguage))
            {
                cachedLanguage = language;
                cachedSettingsLabel = "Phinix_chat_settingsButton".Translate();
                cachedOnlineCount = -1;
            }

            if (cachedStatusOnline != online || cachedOnlineCount != onlineCount || cachedStatusText == null)
            {
                string status = online
                    ? "Phinix_chat_onlineCount".Translate(onlineCount)
                    : "Phinix_chat_notConnected".Translate();
                cachedStatusText = status.Colorize(ChatTheme.ReplyQuoteText);
                cachedStatusOnline = online;
                cachedOnlineCount = onlineCount;
            }
        }

        private static Rect ClipToBottom(Rect rect, float bottom)
        {
            rect.height = Mathf.Min(rect.height, Mathf.Max(0f, bottom - rect.yMin));
            return rect;
        }
    }
}
