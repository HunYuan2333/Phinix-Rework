using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using UserManagement;
using Utils;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class UserList
    {
        private const float ScrollbarWidth = 16f;
        private const float BlockedSpacerPaddingTop = 7f;
        private const float BlockedSpacerPaddingBottom = 3f;
        private const float UserButtonPaddingHorizontal = 3f;
        private const float UserButtonPaddingVertical = 5f;

        private readonly float blockedSpacerHeight = BlockedSpacerPaddingTop + BlockedSpacerPaddingBottom;
        private Texture2D blockedSpacerCollapseIcon;
        private Texture2D CollapseIcon => blockedSpacerCollapseIcon ?? (blockedSpacerCollapseIcon = ContentFinder<Texture2D>.Get("collapse"));
        private readonly Color blockedBackgroundColour = new Color(0f, 0f, 0f, 0.35f);
        private readonly Color blockedNameColour = new Color(0.6f, 0.6f, 0.6f);

        private readonly IChatUiHostContext hostContext;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientSettingsContext settingsContext;

        private readonly List<ImmutableUser> onlineUsers = new List<ImmutableUser>();
        private readonly List<ImmutableUser> blockedUsers = new List<ImmutableUser>();
        private readonly List<ImmutableUser> filteredOnlineUsers = new List<ImmutableUser>();
        private readonly List<ImmutableUser> filteredBlockedUsers = new List<ImmutableUser>();
        private readonly object userListsLock = new object();
        private readonly Dictionary<ImmutableUser, (float Normal, float Scrollbar)> userRectHeights = new Dictionary<ImmutableUser, (float Normal, float Scrollbar)>();
        private readonly Dictionary<ImmutableUser, (float Normal, float Scrollbar)> blockedUserRectHeights = new Dictionary<ImmutableUser, (float Normal, float Scrollbar)>();

        private bool onlineUsersChanged;
        private bool blockedUsersChanged;
        private (float Normal, float Scrollbar) userRectHeightsSum = (0f, 0f);
        private (float Normal, float Scrollbar) blockedUserRectHeightsSum = (0f, 0f);
        private string searchText = string.Empty;
        private Vector2 scrollPos;

        public UserList(IChatUiHostContext hostContext, IClientUserDirectory userDirectory, IClientSettingsContext settingsContext)
        {
            this.hostContext = hostContext;
            this.userDirectory = userDirectory;
            this.settingsContext = settingsContext;

            hostContext.OnUsersChanged += (_, __) => refreshUserLists();
            hostContext.OnBlockedUsersChanged += (_, __) => refreshUserLists();
            hostContext.OnDisconnect += (_, __) => onDisconnect();

            refreshUserLists();
        }

        public void Draw(Rect inRect)
        {
            if (onlineUsersChanged || blockedUsersChanged)
            {
                if (onlineUsersChanged && Monitor.TryEnter(userListsLock))
                {
                    filteredOnlineUsers.Clear();
                    filteredOnlineUsers.AddRange(onlineUsers.Where(u => u.DisplayName.StripTags().IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1));
                    onlineUsersChanged = false;
                    Monitor.Exit(userListsLock);
                }

                if (blockedUsersChanged && Monitor.TryEnter(userListsLock))
                {
                    filteredBlockedUsers.Clear();
                    filteredBlockedUsers.AddRange(blockedUsers.Where(u => u.DisplayName.StripTags().IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1));
                    blockedUsersChanged = false;
                    Monitor.Exit(userListsLock);
                }

                userRectHeights.Clear();
                userRectHeightsSum = (0f, 0f);
                foreach (ImmutableUser user in filteredOnlineUsers)
                {
                    float normalHeight = Text.CalcHeight(formatDisplayName(user.DisplayName, false), inRect.width) + (UserButtonPaddingVertical * 2);
                    float heightWithScrollbar = Text.CalcHeight(formatDisplayName(user.DisplayName, false), inRect.width - ScrollbarWidth) + (UserButtonPaddingVertical * 2);
                    userRectHeights.Add(user, (normalHeight, heightWithScrollbar));
                    userRectHeightsSum.Normal += normalHeight;
                    userRectHeightsSum.Scrollbar += heightWithScrollbar;
                }

                blockedUserRectHeights.Clear();
                blockedUserRectHeightsSum = (0f, 0f);
                foreach (ImmutableUser user in filteredBlockedUsers)
                {
                    float normalHeight = Text.CalcHeight(formatDisplayName(user.DisplayName, true), inRect.width) + (UserButtonPaddingVertical * 2);
                    float heightWithScrollbar = Text.CalcHeight(formatDisplayName(user.DisplayName, true), inRect.width - ScrollbarWidth) + (UserButtonPaddingVertical * 2);
                    blockedUserRectHeights.Add(user, (normalHeight, heightWithScrollbar));
                    blockedUserRectHeightsSum.Normal += normalHeight;
                    blockedUserRectHeightsSum.Scrollbar += heightWithScrollbar;
                }
            }

            float totalHeight = userRectHeightsSum.Normal;
            if (filteredBlockedUsers.Any())
            {
                totalHeight += blockedSpacerHeight;
                if (!settingsContext.CollapseBlockedUsers)
                {
                    totalHeight += blockedUserRectHeightsSum.Normal;
                }
            }

            Rect contentRect = new Rect(inRect.xMin, inRect.yMin, inRect.width, totalHeight);
            if (contentRect.height > inRect.height)
            {
                totalHeight = userRectHeightsSum.Scrollbar;
                if (filteredBlockedUsers.Any())
                {
                    totalHeight += blockedSpacerHeight;
                    if (!settingsContext.CollapseBlockedUsers)
                    {
                        totalHeight += blockedUserRectHeightsSum.Scrollbar;
                    }
                }

                contentRect.width = inRect.width - ScrollbarWidth;
                contentRect.height = totalHeight;
            }

            Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);

            float currentY = contentRect.yMin;
            foreach (ImmutableUser user in filteredOnlineUsers)
            {
                float height = contentRect.height > inRect.height ? userRectHeights[user].Scrollbar : userRectHeights[user].Normal;
                drawUser(new Rect(contentRect.xMin, currentY, contentRect.width, height), user, false);
                currentY += height;
            }

            if (filteredBlockedUsers.Any())
            {
                Rect paddedRect = new Rect(
                    contentRect.xMin,
                    currentY + BlockedSpacerPaddingTop,
                    contentRect.width,
                    blockedSpacerHeight - BlockedSpacerPaddingTop - BlockedSpacerPaddingBottom);
                TextAnchor oldTextAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(paddedRect, "Phinix_chat_blockedUsers".Translate());
                Text.Anchor = oldTextAnchor;

                if (Widgets.ButtonInvisible(paddedRect, false))
                {
                    settingsContext.CollapseBlockedUsers = !settingsContext.CollapseBlockedUsers;
                }

                Rect collapseIconRect = new Rect(
                    paddedRect.xMin + UserButtonPaddingHorizontal,
                    paddedRect.yMin - 1f,
                    paddedRect.height,
                    paddedRect.height);
                Widgets.DrawTextureFitted(
                    collapseIconRect,
                    CollapseIcon,
                    0.4f,
                    new Vector2(CollapseIcon.width, CollapseIcon.height),
                    new Rect(0f, 0f, 1f, 1f),
                    settingsContext.CollapseBlockedUsers ? 0 : 90);

                currentY += blockedSpacerHeight;

                if (!settingsContext.CollapseBlockedUsers)
                {
                    foreach (ImmutableUser user in filteredBlockedUsers)
                    {
                        float height = contentRect.height > inRect.height ? blockedUserRectHeights[user].Scrollbar : blockedUserRectHeights[user].Normal;
                        drawUser(new Rect(contentRect.xMin, currentY, contentRect.width, height), user, true);
                        currentY += height;
                    }
                }
            }

            Widgets.EndScrollView();
        }

        public void Filter(string searchText)
        {
            this.searchText = searchText ?? string.Empty;
            onlineUsersChanged = true;
            blockedUsersChanged = true;
        }

        private void refreshUserLists()
        {
            refreshBlockedUserList();
            refreshOnlineUserList();
        }

        private void refreshOnlineUserList()
        {
            lock (userListsLock)
            {
                onlineUsers.Clear();
                onlineUsers.AddRange(userDirectory.GetUsers(true).Where(u => !blockedUsers.Contains(u)));
            }

            onlineUsersChanged = true;
        }

        private void refreshBlockedUserList()
        {
            lock (userListsLock)
            {
                blockedUsers.Clear();
                foreach (string uuid in hostContext.BlockedUsers)
                {
                    if (userDirectory.TryGetUser(uuid, out ImmutableUser user))
                    {
                        blockedUsers.Add(user);
                    }
                }
            }

            blockedUsersChanged = true;
        }

        private void onDisconnect()
        {
            lock (userListsLock)
            {
                onlineUsers.Clear();
                blockedUsers.Clear();
            }

            onlineUsersChanged = true;
            blockedUsersChanged = true;
        }

        private void drawUser(Rect inRect, ImmutableUser user, bool blocked)
        {
            string formattedDisplayName = formatDisplayName(user.DisplayName, blocked);
            if (blocked)
            {
                Widgets.DrawRectFast(inRect, blockedBackgroundColour);
            }

            Rect paddedRect = inRect.ContractedBy(UserButtonPaddingHorizontal, UserButtonPaddingVertical);
            Widgets.Label(paddedRect, Mouse.IsOver(inRect) ? formattedDisplayName.Colorize(Widgets.MouseoverOptionColor) : formattedDisplayName);

            if (Widgets.ButtonInvisible(inRect, false))
            {
                drawContextMenu(user);
            }
        }

        private void drawContextMenu(ImmutableUser user)
        {
            if (user.Uuid == hostContext.Uuid)
            {
                return;
            }

            List<FloatMenuOption> items = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "Phinix_chat_contextMenu_tradeWith".Translate(TextHelper.StripRichText(user.DisplayName)),
                    () => hostContext.CreateTrade(user.Uuid))
            };
            items.Add(
                new FloatMenuOption(
                    (hostContext.BlockedUsers.Contains(user.Uuid) ? "Phinix_chat_contextMenu_unblockUser" : "Phinix_chat_contextMenu_blockUser").Translate(),
                    () =>
                    {
                        if (hostContext.BlockedUsers.Contains(user.Uuid))
                        {
                            hostContext.UnBlockUser(user.Uuid);
                        }
                        else
                        {
                            hostContext.BlockUser(user.Uuid);
                        }
                    }));

            Find.WindowStack.Add(new FloatMenu(items));
        }

        private string formatDisplayName(string displayName, bool blocked)
        {
            if (blocked)
            {
                return TextHelper.StripRichText(displayName).Colorize(blockedNameColour);
            }

            return hostContext.ShowNameFormatting ? displayName : TextHelper.StripRichText(displayName);
        }
    }
}
