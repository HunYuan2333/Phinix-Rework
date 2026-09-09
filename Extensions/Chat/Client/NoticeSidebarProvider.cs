using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.GUI;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class NoticeSidebarProvider : IServerSidebarProvider, IResponsiveSidebarProvider
    {
        private readonly List<NoticeEntry> notices = new List<NoticeEntry>();
        private readonly object noticesLock = new object();
        private const int MAX_NOTICES = 100;
        private const float ScrollbarWidth = 16f;
        private const float MinimumRowHeight = 46f;
        private const int VirtualOverscan = 1;
        private Vector2 scrollPos;
        private bool unreadDirty = true;
        private int cachedUnreadCount;
        private readonly EventHandler disconnectHandler;
        private readonly IChatUiHostContext hostContext;

        private DateTime cachedNow;
        private float lastNowCacheTime;
        private const float NOW_CACHE_INTERVAL = 5f;
        private readonly float[] prefixOffsets = new float[MAX_NOTICES + 1];
        private bool layoutDirty = true;
        private float cachedLayoutWidth = -1f;
        private float cachedContentHeight;
        private object cachedLayoutLanguage;
        private object cachedHeaderLanguage;
        private string cachedMarkAllLabel;
        private float cachedMarkAllWidth;
        private int cachedTitleUnread = -1;
        private string cachedTitle;

        public float Order => 10f;

        public float PreferredWidth => 210f;
        public float MinimumWidth => 160f;
        public bool CanCollapse => true;

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
                layoutDirty = true;
            }
        }

        public void Clear()
        {
            lock (noticesLock)
            {
                notices.Clear();
                cachedUnreadCount = 0;
                unreadDirty = true;
                layoutDirty = true;
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

            EnsureHeaderCache(currentUnread);
            bool stackHeader = currentUnread > 0 && inRect.width < cachedMarkAllWidth + 88f;
            float headerHeight = stackHeader ? 60f : 28f;
            Rect headerRect = inRect.TopPartPixels(Mathf.Min(headerHeight, inRect.height));
            Rect titleRect = stackHeader
                ? new Rect(headerRect.x, headerRect.y, headerRect.width, Mathf.Min(28f, headerRect.height))
                : new Rect(headerRect.x, headerRect.y, Mathf.Max(0f, headerRect.width - (currentUnread > 0 ? cachedMarkAllWidth + 4f : 0f)), headerRect.height);
            Rect markAllRect = stackHeader
                ? new Rect(headerRect.x, titleRect.yMax + 4f, headerRect.width, Mathf.Max(0f, headerRect.yMax - titleRect.yMax - 4f))
                : new Rect(headerRect.xMax - cachedMarkAllWidth, headerRect.y, currentUnread > 0 ? cachedMarkAllWidth : 0f, headerRect.height);

            Widgets.Label(titleRect, cachedTitle);

            if (currentUnread > 0 && markAllRect.width > 0f && markAllRect.height > 0f && Widgets.ButtonText(markAllRect, cachedMarkAllLabel))
            {
                MarkAllRead();
            }

            Widgets.DrawBoxSolid(new Rect(inRect.x, headerRect.yMax, inRect.width, 1f), ChatTheme.GroupIndentLine);

            Rect listRect = new Rect(inRect.x, headerRect.yMax + 4f, inRect.width, Mathf.Max(0f, inRect.yMax - (headerRect.yMax + 4f)));
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

                EnsureNoticeLayout(inRect.width);
                bool scrollRequired = cachedContentHeight > inRect.height;
                float contentWidth = Mathf.Max(0f, inRect.width - (scrollRequired ? ScrollbarWidth : 0f));
                if (!Mathf.Approximately(contentWidth, cachedLayoutWidth))
                {
                    EnsureNoticeLayout(contentWidth);
                    scrollRequired = cachedContentHeight > inRect.height;
                }
                Rect contentRect = new Rect(inRect.x, inRect.y, contentWidth, cachedContentHeight);

                if (scrollRequired)
                    Widgets.BeginScrollView(inRect, ref scrollPos, contentRect);

                VirtualListRange visibleRange = VirtualListLayout.GetDynamicRange(
                    prefixOffsets,
                    notices.Count,
                    scrollRequired ? scrollPos.y : 0f,
                    inRect.height,
                    VirtualOverscan);
                for (int index = visibleRange.FirstIndex; index < visibleRange.EndIndexExclusive; index++)
                {
                    NoticeEntry entry = notices[index];
                    float rowHeight = prefixOffsets[index + 1] - prefixOffsets[index];
                    Rect rowRect = new Rect(contentRect.x, contentRect.y + prefixOffsets[index], contentRect.width, rowHeight);

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
                    Widgets.Label(timeRect, GetRelativeTime(entry));
                    GUIUtils.RestoreTextFormat();

                    Rect textRect = new Rect(rowRect.x + 8f, rowRect.y + 16f, rowRect.width - 16f, rowHeight - 18f);
                    Widgets.Label(textRect, entry.Text);

                    if (entry.IsUnread && Widgets.ButtonInvisible(rowRect))
                    {
                        entry.IsUnread = false;
                        unreadDirty = true;
                    }

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
                    cachedUnreadCount = 0;
                    for (int i = 0; i < notices.Count; i++)
                    {
                        if (notices[i].IsUnread) cachedUnreadCount++;
                    }
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

        private void EnsureHeaderCache(int unreadCount)
        {
            object language = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(language, cachedHeaderLanguage))
            {
                cachedHeaderLanguage = language;
                cachedMarkAllLabel = "Phinix_notice_markAllRead".Translate();
                cachedMarkAllWidth = Mathf.Max(80f, Text.CalcSize(cachedMarkAllLabel).x + 20f);
                cachedTitleUnread = -1;
            }

            if (cachedTitleUnread != unreadCount)
            {
                cachedTitle = unreadCount > 0
                    ? "Phinix_sidebar_noticesWithCount".Translate(unreadCount).ToString()
                    : TabLabel;
                cachedTitleUnread = unreadCount;
            }
        }

        private void EnsureNoticeLayout(float width)
        {
            width = Mathf.Max(0f, width);
            object language = LanguageDatabase.activeLanguage;
            if (!layoutDirty && Mathf.Approximately(width, cachedLayoutWidth) && ReferenceEquals(language, cachedLayoutLanguage))
            {
                return;
            }

            GUIUtils.SaveTextFormat();
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            prefixOffsets[0] = 0f;
            float textWidth = Mathf.Max(1f, width - 16f);
            for (int i = 0; i < notices.Count; i++)
            {
                float textHeight = Text.CalcHeight(notices[i].Text ?? string.Empty, textWidth);
                float rowHeight = Mathf.Max(MinimumRowHeight, 20f + textHeight + 4f);
                prefixOffsets[i + 1] = prefixOffsets[i] + rowHeight;
            }
            GUIUtils.RestoreTextFormat();

            cachedContentHeight = prefixOffsets[notices.Count];
            cachedLayoutWidth = width;
            cachedLayoutLanguage = language;
            layoutDirty = false;
        }

        private string GetRelativeTime(NoticeEntry entry)
        {
            float now = Time.realtimeSinceStartup;
            if ((now - lastNowCacheTime) >= NOW_CACHE_INTERVAL)
            {
                cachedNow = DateTime.UtcNow;
                lastNowCacheTime = now;
            }

            TimeSpan delta = cachedNow - entry.Timestamp;
            int bucket;
            string translationKey;
            if (delta.TotalMinutes < 1)
            {
                bucket = 0;
                translationKey = "Phinix_notice_justNow";
            }
            else if (delta.TotalMinutes < 60)
            {
                bucket = 1000 + (int)delta.TotalMinutes;
                translationKey = "Phinix_notice_minutesAgo";
            }
            else if (delta.TotalHours < 24)
            {
                bucket = 2000 + (int)delta.TotalHours;
                translationKey = "Phinix_notice_hoursAgo";
            }
            else
            {
                bucket = 3000 + (int)delta.TotalDays;
                translationKey = "Phinix_notice_daysAgo";
            }

            object language = LanguageDatabase.activeLanguage;
            if (entry.RelativeTimeBucket != bucket || !ReferenceEquals(entry.RelativeTimeLanguage, language))
            {
                string relativeTime = bucket == 0
                    ? translationKey.Translate()
                    : translationKey.Translate(bucket % 1000);
                entry.RelativeTimeText = relativeTime.Colorize(ChatTheme.ReplyQuoteText);
                entry.RelativeTimeBucket = bucket;
                entry.RelativeTimeLanguage = language;
            }
            return entry.RelativeTimeText;
        }

        private sealed class NoticeEntry
        {
            public string MessageId;
            public string Text;
            public DateTime Timestamp;
            public bool IsUnread;
            public int RelativeTimeBucket = -1;
            public object RelativeTimeLanguage;
            public string RelativeTimeText;
        }
    }
}
