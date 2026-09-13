using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class NoticeBannerProvider : INoticeBannerProvider
    {
        private readonly List<ActiveNotice> activeNotices = new List<ActiveNotice>();
        private readonly object noticesLock = new object();
        private const float MinimumBannerHeight = 32f;
        private const float MaximumBannerHeight = 120f;
        private const float CloseButtonWidth = 44f;
        private const int MAX_ACTIVE_NOTICES = 3;
        private float cachedLayoutWidth = 480f;
        private float cachedTotalHeight;
        private bool layoutDirty = true;
        private object cachedLayoutLanguage;

        public float CurrentHeight
        {
            get
            {
                float currentTime = Time.realtimeSinceStartup;
                lock (noticesLock)
                {
                    RemoveExpired(currentTime);
                    EnsureLayout(cachedLayoutWidth);
                    return cachedTotalHeight;
                }
            }
        }

        public void Enqueue(UIChatMessage notice)
        {
            lock (noticesLock)
            {
                if (activeNotices.Count >= MAX_ACTIVE_NOTICES)
                {
                    activeNotices.RemoveAt(0);
                }

                activeNotices.Add(new ActiveNotice
                {
                    Text = notice.Message,
                    DurationSeconds = notice.NoticeDurationSeconds > 0 ? notice.NoticeDurationSeconds : 10,
                    StartTime = Time.realtimeSinceStartup
                });
                layoutDirty = true;
            }
        }

        public void Clear()
        {
            lock (noticesLock)
            {
                activeNotices.Clear();
                cachedTotalHeight = 0f;
                layoutDirty = true;
            }
        }

        public void Draw(Rect inRect)
        {
            float currentTime = Time.realtimeSinceStartup;
            lock (noticesLock)
            {
                RemoveExpired(currentTime);
                EnsureLayout(inRect.width);

                float cursorY = inRect.yMin;
                for (int i = activeNotices.Count - 1; i >= 0; i--)
                {
                    ActiveNotice notice = activeNotices[i];
                    float visibleHeight = Mathf.Min(notice.CachedHeight, Mathf.Max(0f, inRect.yMax - cursorY));
                    if (visibleHeight <= 0f) break;
                    Rect noticeRect = new Rect(inRect.xMin, cursorY, inRect.width, visibleHeight);

                    Widgets.DrawBoxSolid(noticeRect, ChatTheme.NoticeBannerBg);
                    Rect labelRect = new Rect(inRect.xMin + 6f, cursorY + 2f, Mathf.Max(0f, inRect.width - CloseButtonWidth - 12f), Mathf.Max(0f, visibleHeight - 5f));
                    Widgets.Label(labelRect, notice.Text);
                    TooltipHandler.TipRegion(labelRect, notice.Text);

                    float remaining = notice.DurationSeconds - (currentTime - notice.StartTime);
                    remaining = Mathf.Max(0f, remaining);
                    Rect progressRect = new Rect(inRect.xMin, noticeRect.yMax - 3f, inRect.width * (remaining / notice.DurationSeconds), 3f);
                    Widgets.DrawBoxSolid(progressRect, ChatTheme.NoticeProgress);

                    Rect closeRect = new Rect(inRect.xMax - Mathf.Min(CloseButtonWidth, inRect.width), cursorY, Mathf.Min(CloseButtonWidth, inRect.width), visibleHeight);
                    if (Widgets.ButtonText(closeRect, "×"))
                    {
                        activeNotices.RemoveAt(i);
                        layoutDirty = true;
                    }

                    cursorY += notice.CachedHeight;
                }
            }
        }

        private void RemoveExpired(float currentTime)
        {
            for (int i = activeNotices.Count - 1; i >= 0; i--)
            {
                ActiveNotice notice = activeNotices[i];
                if ((currentTime - notice.StartTime) >= notice.DurationSeconds)
                {
                    activeNotices.RemoveAt(i);
                    layoutDirty = true;
                }
            }
        }

        private void EnsureLayout(float width)
        {
            width = Mathf.Max(0f, width);
            object language = LanguageDatabase.activeLanguage;
            if (!layoutDirty && Mathf.Approximately(width, cachedLayoutWidth) && ReferenceEquals(language, cachedLayoutLanguage))
            {
                return;
            }

            float textWidth = Mathf.Max(1f, width - CloseButtonWidth - 12f);
            float totalHeight = 0f;
            for (int i = 0; i < activeNotices.Count; i++)
            {
                ActiveNotice notice = activeNotices[i];
                notice.CachedHeight = Mathf.Clamp(Text.CalcHeight(notice.Text ?? string.Empty, textWidth) + 7f, MinimumBannerHeight, MaximumBannerHeight);
                totalHeight += notice.CachedHeight;
            }

            cachedLayoutWidth = width;
            cachedLayoutLanguage = language;
            cachedTotalHeight = totalHeight;
            layoutDirty = false;
        }

        private sealed class ActiveNotice
        {
            public string Text;
            public int DurationSeconds;
            public float StartTime;
            public float CachedHeight;
        }
    }
}
