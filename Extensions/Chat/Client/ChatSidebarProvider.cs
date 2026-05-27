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

        private readonly IChatUiHostContext hostContext;
        private readonly IClientSessionContext sessionContext;
        private readonly Action openSettingsWindow;
        private readonly UserList userList;
        private string userSearch = string.Empty;

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

        public void Draw(Rect inRect)
        {
            Rect settingsButtonRect = inRect.TopPartPixels(SettingsButtonHeight);
            Rect userSearchRect = new Rect(inRect.x, settingsButtonRect.yMax + DefaultSpacing, inRect.width, UserSearchHeight);
            Rect userListRect = new Rect(inRect.x, userSearchRect.yMax + DefaultSpacing, inRect.width, inRect.yMax - (userSearchRect.yMax + DefaultSpacing));

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

            if (sessionContext.Authenticated && sessionContext.LoggedIn)
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
