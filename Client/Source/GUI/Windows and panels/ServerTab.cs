using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using Verse;
using static PhinixClient.Client;

namespace PhinixClient
{
    public class ServerTab : MainTabWindow
    {
        private const float DEFAULT_SPACING = 10f;
        private const float SIDEBAR_TAB_HEIGHT = 30f;

        public override Vector2 InitialSize => new Vector2(1000f, 680f);

        private readonly List<IMainTabProvider> tabProviders;
        private readonly List<IServerSidebarProvider> sidebarProviders;
        private readonly List<INoticeBannerProvider> bannerProviders;
        private readonly List<TabRecord> tabList = new List<TabRecord>();
        private readonly List<TabRecord> sidebarTabList = new List<TabRecord>();
        private int activeTab = 0;
        private int activeSidebarTab = 0;

        public ServerTab()
        {
            this.closeOnAccept = false;

            tabProviders = Instance.MainTabProviders
                .OrderBy(p => p.TabOrder)
                .ToList();
            sidebarProviders = Instance.SidebarProviders
                .OrderBy(p => p.Order)
                .ToList();
            bannerProviders = Instance.BannerProviders
                .ToList();

            for (int i = 0; i < tabProviders.Count; i++)
            {
                int index = i;
                tabList.Add(new TabRecord(tabProviders[i].TabLabel, () => activeTab = index, () => activeTab == index));
            }

            for (int i = 0; i < sidebarProviders.Count; i++)
            {
                int index = i;
                sidebarTabList.Add(new TabRecord(sidebarProviders[i].TabLabel, () => activeSidebarTab = index, () => activeSidebarTab == index));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float bannerHeight = 0f;
            foreach (var bannerProvider in bannerProviders)
            {
                bannerHeight += bannerProvider.CurrentHeight;
            }

            Rect usableRect;
            if (bannerHeight > 0f)
            {
                float cursorY = inRect.yMin;
                foreach (var bannerProvider in bannerProviders)
                {
                    float h = bannerProvider.CurrentHeight;
                    if (h > 0f)
                    {
                        bannerProvider.Draw(new Rect(inRect.xMin, cursorY, inRect.width, h));
                        cursorY += h;
                    }
                }

                usableRect = inRect.BottomPartPixels(inRect.height - (bannerHeight + TabDrawer.TabHeight));
            }
            else
            {
                usableRect = inRect.BottomPartPixels(inRect.height - TabDrawer.TabHeight);
            }

            Rect mainRect = usableRect;
            Rect rightColumnRect = default;
            if (sidebarProviders.Count > 0)
            {
                // 手动计算 Maximum，避免 Draw 路径 LINQ 分配
                float sidebarWidth = 0f;
                for (int i = 0; i < sidebarProviders.Count; i++)
                {
                    float w = sidebarProviders[i].PreferredWidth;
                    if (w > sidebarWidth) sidebarWidth = w;
                }
                rightColumnRect = usableRect.RightPartPixels(sidebarWidth);
                mainRect = usableRect.LeftPartPixels(usableRect.width - (sidebarWidth + DEFAULT_SPACING));
            }

            TabDrawer.DrawTabs(usableRect, tabList, 200f);

            if (activeTab >= 0 && activeTab < tabProviders.Count)
            {
                tabProviders[activeTab].Draw(mainRect);
            }
            else
            {
                Widgets.DrawMenuSection(mainRect);
            }

            if (sidebarProviders.Count == 1)
            {
                sidebarProviders[0].Draw(rightColumnRect);
            }
            else if (sidebarProviders.Count > 1)
            {
                Rect sidebarTabRect = rightColumnRect.TopPartPixels(SIDEBAR_TAB_HEIGHT);
                Rect sidebarContentRect = new Rect(
                    rightColumnRect.x, sidebarTabRect.yMax,
                    rightColumnRect.width, rightColumnRect.yMax - sidebarTabRect.yMax);

                TabDrawer.DrawTabs(sidebarTabRect, sidebarTabList, rightColumnRect.width);

                if (activeSidebarTab >= 0 && activeSidebarTab < sidebarProviders.Count)
                {
                    sidebarProviders[activeSidebarTab].Draw(sidebarContentRect);
                }
            }

            // Prevent Enter/Esc from bubbling up to RimWorld's Window layer
            // and closing this MainTabWindow. Tab content (e.g. Chat) handles
            // these keys before this point if needed.
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter ||
                 Event.current.keyCode == KeyCode.Escape))
            {
                Event.current.Use();
                Event.current.keyCode = KeyCode.None;
            }
        }
    }
}