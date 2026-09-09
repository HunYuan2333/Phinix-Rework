using System;
using System.Collections.Generic;
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
        private Texture2D CollapseIcon => blockedSpacerCollapseIcon ?? (blockedSpacerCollapseIcon = ContentFinder<Texture2D>.Get("collapse", false));

        private readonly IChatUiHostContext hostContext;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientSettingsContext settingsContext;

        private readonly List<ImmutableUser> onlineUsers = new List<ImmutableUser>();
        private readonly List<ImmutableUser> blockedUsers = new List<ImmutableUser>();
        private readonly List<ImmutableUser> filteredOnlineUsers = new List<ImmutableUser>();
        private readonly List<ImmutableUser> filteredBlockedUsers = new List<ImmutableUser>();
        private readonly object userListsLock = new object();
        private readonly Dictionary<ImmutableUser, string> formattedDisplayNames = new Dictionary<ImmutableUser, string>();

        private bool onlineUsersChanged;
        private bool blockedUsersChanged;
        private float[] onlineNormalOffsets = new float[1];
        private float[] onlineScrollbarOffsets = new float[1];
        private float[] blockedNormalOffsets = new float[1];
        private float[] blockedScrollbarOffsets = new float[1];
        private float cachedWidth = -1f;
        private object cachedLanguage;
        private string cachedBlockedUsersLabel;
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

        public int OnlineCount
        {
            get
            {
                lock (userListsLock)
                {
                    return onlineUsers.Count;
                }
            }
        }

        public void Draw(Rect inRect)
        {
            if (inRect.width <= 0f || inRect.height <= 0f)
            {
                return;
            }

            object language = LanguageDatabase.activeLanguage;
            if (onlineUsersChanged || blockedUsersChanged || !Mathf.Approximately(cachedWidth, inRect.width) || !ReferenceEquals(language, cachedLanguage))
            {
                if (Monitor.TryEnter(userListsLock))
                {
                    try
                    {
                        RebuildLayout(inRect.width, language);
                    }
                    finally
                    {
                        Monitor.Exit(userListsLock);
                    }
                }
            }

            bool hasBlockedUsers = filteredBlockedUsers.Count > 0;
            float totalHeight = onlineNormalOffsets[filteredOnlineUsers.Count];
            if (hasBlockedUsers)
            {
                totalHeight += blockedSpacerHeight;
                if (!settingsContext.CollapseBlockedUsers)
                {
                    totalHeight += blockedNormalOffsets[filteredBlockedUsers.Count];
                }
            }

            Rect contentRect = new Rect(inRect.xMin, inRect.yMin, inRect.width, totalHeight);
            if (contentRect.height > inRect.height)
            {
                totalHeight = onlineScrollbarOffsets[filteredOnlineUsers.Count];
                if (hasBlockedUsers)
                {
                    totalHeight += blockedSpacerHeight;
                    if (!settingsContext.CollapseBlockedUsers)
                    {
                        totalHeight += blockedScrollbarOffsets[filteredBlockedUsers.Count];
                    }
                }

                contentRect.width = Mathf.Max(0f, inRect.width - ScrollbarWidth);
                contentRect.height = totalHeight;
            }

            Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);

            bool useScrollbarLayout = contentRect.height > inRect.height;
            float[] onlineOffsets = useScrollbarLayout ? onlineScrollbarOffsets : onlineNormalOffsets;
            float[] blockedOffsets = useScrollbarLayout ? blockedScrollbarOffsets : blockedNormalOffsets;
            VirtualListRange onlineRange = VirtualListLayout.GetDynamicRange(onlineOffsets, filteredOnlineUsers.Count, scrollPos.y, inRect.height, 1);
            for (int index = onlineRange.FirstIndex; index < onlineRange.EndIndexExclusive; index++)
            {
                float height = onlineOffsets[index + 1] - onlineOffsets[index];
                drawUser(new Rect(contentRect.xMin, contentRect.yMin + onlineOffsets[index], contentRect.width, height), filteredOnlineUsers[index], false);
            }

            float blockedHeaderY = contentRect.yMin + onlineOffsets[filteredOnlineUsers.Count];
            if (hasBlockedUsers)
            {
                Rect paddedRect = new Rect(
                    contentRect.xMin,
                    blockedHeaderY + BlockedSpacerPaddingTop,
                    contentRect.width,
                    blockedSpacerHeight - BlockedSpacerPaddingTop - BlockedSpacerPaddingBottom);
                TextAnchor oldTextAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(paddedRect, cachedBlockedUsersLabel);
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
                if (CollapseIcon != null)
                {
                    Widgets.DrawTextureFitted(
                        collapseIconRect,
                        CollapseIcon,
                        0.4f,
                        new Vector2(CollapseIcon.width, CollapseIcon.height),
                        new Rect(0f, 0f, 1f, 1f),
                        settingsContext.CollapseBlockedUsers ? 0 : 90);
                }
                else
                {
                    TextAnchor oldCollapseAnchor = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(collapseIconRect, settingsContext.CollapseBlockedUsers ? ">" : "v");
                    Text.Anchor = oldCollapseAnchor;
                }

                if (!settingsContext.CollapseBlockedUsers)
                {
                    float blockedStartY = blockedHeaderY + blockedSpacerHeight;
                    float blockedOffset = blockedStartY - contentRect.yMin;
                    if (scrollPos.y + inRect.height >= blockedOffset)
                    {
                        float blockedScrollY = Mathf.Max(0f, scrollPos.y - blockedOffset);
                        VirtualListRange blockedRange = VirtualListLayout.GetDynamicRange(blockedOffsets, filteredBlockedUsers.Count, blockedScrollY, inRect.height, 1);
                        for (int index = blockedRange.FirstIndex; index < blockedRange.EndIndexExclusive; index++)
                        {
                            float height = blockedOffsets[index + 1] - blockedOffsets[index];
                            drawUser(new Rect(contentRect.xMin, blockedStartY + blockedOffsets[index], contentRect.width, height), filteredBlockedUsers[index], true);
                        }
                    }
                }
            }

            Widgets.EndScrollView();
        }

        private void RebuildLayout(float width, object language)
        {
            filteredOnlineUsers.Clear();
            for (int i = 0; i < onlineUsers.Count; i++)
            {
                ImmutableUser user = onlineUsers[i];
                if (user.DisplayName.StripTags().IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    filteredOnlineUsers.Add(user);
                }
            }

            filteredBlockedUsers.Clear();
            for (int i = 0; i < blockedUsers.Count; i++)
            {
                ImmutableUser user = blockedUsers[i];
                if (user.DisplayName.StripTags().IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    filteredBlockedUsers.Add(user);
                }
            }

            onlineNormalOffsets = EnsureCapacity(onlineNormalOffsets, filteredOnlineUsers.Count + 1);
            onlineScrollbarOffsets = EnsureCapacity(onlineScrollbarOffsets, filteredOnlineUsers.Count + 1);
            blockedNormalOffsets = EnsureCapacity(blockedNormalOffsets, filteredBlockedUsers.Count + 1);
            blockedScrollbarOffsets = EnsureCapacity(blockedScrollbarOffsets, filteredBlockedUsers.Count + 1);
            formattedDisplayNames.Clear();
            BuildOffsets(filteredOnlineUsers, onlineNormalOffsets, onlineScrollbarOffsets, width);
            BuildOffsets(filteredBlockedUsers, blockedNormalOffsets, blockedScrollbarOffsets, width);

            onlineUsersChanged = false;
            blockedUsersChanged = false;
            cachedWidth = width;
            cachedLanguage = language;
            cachedBlockedUsersLabel = "Phinix_chat_blockedUsers".Translate();
        }

        private void BuildOffsets(List<ImmutableUser> users, float[] normalOffsets, float[] scrollbarOffsets, float width)
        {
            normalOffsets[0] = 0f;
            scrollbarOffsets[0] = 0f;
            for (int i = 0; i < users.Count; i++)
            {
                ImmutableUser user = users[i];
                string formatted = formatDisplayName(user.DisplayName, user);
                formattedDisplayNames[user] = formatted;
                float normalHeight = Text.CalcHeight(formatted, Mathf.Max(1f, width)) + UserButtonPaddingVertical * 2f;
                float scrollbarHeight = Text.CalcHeight(formatted, Mathf.Max(1f, width - ScrollbarWidth)) + UserButtonPaddingVertical * 2f;
                normalOffsets[i + 1] = normalOffsets[i] + normalHeight;
                scrollbarOffsets[i + 1] = scrollbarOffsets[i] + scrollbarHeight;
            }
        }

        private static float[] EnsureCapacity(float[] values, int required)
        {
            return values.Length >= required ? values : new float[required];
        }

        public void Filter(string searchText)
        {
            this.searchText = searchText ?? string.Empty;
            onlineUsersChanged = true;
            blockedUsersChanged = true;
        }

        public void Refresh()
        {
            refreshUserLists();
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
                ImmutableUser[] users = userDirectory.GetUsers(true);
                for (int i = 0; i < users.Length; i++)
                {
                    if (!blockedUsers.Contains(users[i]))
                    {
                        onlineUsers.Add(users[i]);
                    }
                }
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
            string formattedDisplayName;
            if (!formattedDisplayNames.TryGetValue(user, out formattedDisplayName))
            {
                formattedDisplayName = formatDisplayName(user.DisplayName, user);
            }
            if (blocked)
            {
                Widgets.DrawRectFast(inRect, ChatTheme.BlockedBg);
            }

            Rect paddedRect = inRect.ContractedBy(UserButtonPaddingHorizontal, UserButtonPaddingVertical);
            if (Mouse.IsOver(inRect))
            {
                string stripped = TextHelper.StripRichText(formattedDisplayName);
                Widgets.Label(paddedRect, stripped.Colorize(Widgets.MouseoverOptionColor));
            }
            else
            {
                Widgets.Label(paddedRect, formattedDisplayName);
            }

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

        private string formatDisplayName(string displayName, ImmutableUser user)
        {
            if (hostContext.BlockedUsers.Contains(user.Uuid))
            {
                return TextHelper.StripRichText(displayName).Colorize(ChatTheme.BlockedName);
            }

            if (!hostContext.ShowNameFormatting)
            {
                return TextHelper.StripRichText(displayName);
            }

            return ChatTheme.FormatDisplayName(displayName, user.Uuid, ChatTheme.GetNameColor(user.Uuid));
        }
    }
}
