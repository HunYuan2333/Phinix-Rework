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
        private const float BANNER_HEIGHT = 32f;
        private const int MAX_ACTIVE_NOTICES = 3;

        public float CurrentHeight
        {
            get
            {
                float currentTime = Time.realtimeSinceStartup;
                lock (noticesLock)
                {
                    activeNotices.RemoveAll(n => (currentTime - n.StartTime) >= n.DurationSeconds);
                    return activeNotices.Count > 0 ? BANNER_HEIGHT * activeNotices.Count : 0f;
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
            }
        }

        public void Clear()
        {
            lock (noticesLock)
            {
                activeNotices.Clear();
            }
        }

        public void Draw(Rect inRect)
        {
            float currentTime = Time.realtimeSinceStartup;
            lock (noticesLock)
            {
                activeNotices.RemoveAll(n => (currentTime - n.StartTime) >= n.DurationSeconds);

                float cursorY = inRect.yMin;
                for (int i = activeNotices.Count - 1; i >= 0; i--)
                {
                    ActiveNotice notice = activeNotices[i];
                    Rect noticeRect = new Rect(inRect.xMin, cursorY, inRect.width, BANNER_HEIGHT);

                    Widgets.DrawBoxSolid(noticeRect, ChatTheme.NoticeBannerBg);
                    Widgets.Label(new Rect(inRect.xMin + 6f, cursorY, inRect.width - 50f, BANNER_HEIGHT), notice.Text);

                    float remaining = notice.DurationSeconds - (currentTime - notice.StartTime);
                    remaining = Mathf.Max(0f, remaining);
                    Rect progressRect = new Rect(inRect.xMin, cursorY + BANNER_HEIGHT - 3f, inRect.width * (remaining / notice.DurationSeconds), 3f);
                    Widgets.DrawBoxSolid(progressRect, ChatTheme.NoticeProgress);

                    Rect closeRect = new Rect(inRect.xMax - 44f, cursorY, 44f, BANNER_HEIGHT);
                    if (Widgets.ButtonText(closeRect, "×"))
                    {
                        notice.StartTime = 0f;
                    }

                    cursorY += BANNER_HEIGHT;
                }
            }
        }

        private sealed class ActiveNotice
        {
            public string Text;
            public int DurationSeconds;
            public float StartTime;
        }
    }
}
