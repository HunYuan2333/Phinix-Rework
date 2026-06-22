using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.GUI;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class NoticeSidebarProvider : IServerSidebarProvider
    {
        private readonly List<NoticeEntry> notices = new List<NoticeEntry>();
        private readonly object noticesLock = new object();
        private const int MAX_NOTICES = 100;
        private Vector2 scrollPos;
        private bool unreadDirty = true;
        private int cachedUnreadCount;
        private readonly EventHandler disconnectHandler;
        private readonly IChatUiHostContext hostContext;

        private DateTime cachedNow;
        private float lastNowCacheTime;
        private const float NOW_CACHE_INTERVAL = 5f;

        public float Order => 10f;

        public float PreferredWidth => 210f;

        public string TabLabel => "Phinix_sidebar_notices".Translate();

        public NoticeSidebarProvider(IChatUiHostContext hostContext)
        {
            this.hostContext = hostContext;
            disconnectHandler = (_, __) => Clear();
            hostContext.OnDisconnect += disconnectHandler;
        }

        public void Add(UIChatMessage notice)
        {
            if (notice == null || !notice.IsNotice) return;
            lock (noticesLock)
            {
                if (notices.Count >= MAX_NOTICES)
                    notices.RemoveAt(0);
                notices.Add(new NoticeEntry
                {
                    MessageId = notice.MessageId,
                    Text = notice.Message,
                    Timestamp = notice.Timestamp,
                    IsUnread = true
                });
                unreadDirty = true;
            }
        }

        public void Clear()
        {
            lock (noticesLock)
            {
                notices.Clear();
                cachedUnreadCount = 0;
                unreadDirty = true;
            }
        }

        public void Shutdown()
        {
            if (hostContext != null && disconnectHandler != null)
            {
                hostContext.OnDisconnect -= disconnectHandler;
            }
        }

        public void Draw(Rect inRect)
        {
            int currentUnread = GetUnreadCount();

            Rect headerRect = inRect.TopPartPixels(28f);
            Rect markAllRect = headerRect.RightPartPixels(80f);
            Rect titleRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 84f, headerRect.height);

            string title;
            if (currentUnread > 0)
                title = "Phinix_sidebar_noticesWithCount".Translate(currentUnread) + "";
            else
                title = TabLabel;
            Widgets.Label(titleRect, title);

            if (currentUnread > 0 && Widgets.ButtonText(markAllRect, "Phinix_notice_markAllRead".Translate()))
            {
                MarkAllRead();
            }

            Widgets.DrawBoxSolid(new Rect(inRect.x, headerRect.yMax, inRect.width, 1f), ChatTheme.GroupIndentLine);

            Rect listRect = new Rect(inRect.x, headerRect.yMax + 4f, inRect.width, inRect.yMax - (headerRect.yMax + 4f));
            DrawNoticeList(listRect);
        }

        private void DrawNoticeList(Rect inRect)
        {
            lock (noticesLock)
            {
                if (notices.Count == 0)
                {
                    Widgets.NoneLabelCenteredVertically(inRect, TabLabel);
                    return;
                }

                float rowHeight = 46f;
                float contentHeight = rowHeight * notices.Count;
                bool scrollRequired = contentHeight > inRect.height;
                Rect contentRect = new Rect(inRect.x, inRect.y, scrollRequired ? inRect.width - 16f : inRect.width, contentHeight);

                if (scrollRequired)
                    Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);

                float currentY = contentRect.y;
                foreach (var entry in notices)
                {
                    Rect rowRect = new Rect(contentRect.x, currentY, contentRect.width, rowHeight);

                    if (entry.IsUnread)
                    {
                        Widgets.DrawBoxSolid(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), ChatTheme.NoticeAccent);
                        Widgets.DrawBoxSolid(rowRect, ChatTheme.NoticeBg);
                    }
                    else if (Mouse.IsOver(rowRect))
                    {
                        Widgets.DrawBoxSolid(rowRect, ChatTheme.RowHoverBg);
                    }

                    Rect timeRect = new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, 14f);

                    GUIUtils.SaveTextFormat();
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Widgets.Label(timeRect, GetRelativeTime(entry.Timestamp).Colorize(ChatTheme.ReplyQuoteText));
                    GUIUtils.RestoreTextFormat();

                    Rect textRect = new Rect(rowRect.x + 8f, rowRect.y + 16f, rowRect.width - 16f, rowHeight - 18f);
                    Widgets.Label(textRect, entry.Text);

                    if (entry.IsUnread && Widgets.ButtonInvisible(rowRect))
                    {
                        entry.IsUnread = false;
                        unreadDirty = true;
                    }

                    currentY += rowHeight;
                }

                if (scrollRequired)
                    Widgets.EndScrollView();
            }
        }

        private int GetUnreadCount()
        {
            if (unreadDirty)
            {
                lock (noticesLock)
                {
                    cachedUnreadCount = notices.Count(n => n.IsUnread);
                    unreadDirty = false;
                }
            }
            return cachedUnreadCount;
        }

        private void MarkAllRead()
        {
            lock (noticesLock)
            {
                foreach (var entry in notices)
                    entry.IsUnread = false;
                unreadDirty = true;
            }
        }

        private string GetRelativeTime(DateTime timestamp)
        {
            float now = Time.realtimeSinceStartup;
            if ((now - lastNowCacheTime) >= NOW_CACHE_INTERVAL)
            {
                cachedNow = DateTime.UtcNow;
                lastNowCacheTime = now;
            }

            TimeSpan delta = cachedNow - timestamp;
            if (delta.TotalMinutes < 1)
                return "Phinix_notice_justNow".Translate();
            if (delta.TotalMinutes < 60)
                return "Phinix_notice_minutesAgo".Translate((int)delta.TotalMinutes);
            if (delta.TotalHours < 24)
                return "Phinix_notice_hoursAgo".Translate((int)delta.TotalHours);
            return "Phinix_notice_daysAgo".Translate((int)delta.TotalDays);
        }

        private sealed class NoticeEntry
        {
            public string MessageId;
            public string Text;
            public DateTime Timestamp;
            public bool IsUnread;
        }
    }
}