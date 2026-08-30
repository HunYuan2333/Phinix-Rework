using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using Verse;
using static PhinixClient.Client;

namespace PhinixClient
{
    /// <summary>
    /// 主窗口。响应式布局（设计哲学 §8.3 鲁棒）：
    /// - 初始尺寸按屏幕分辨率缩放，clamp 到安全区间；用户手动调整过的尺寸持久化（Settings.ServerTabWidth/Height），
    ///   下次打开优先恢复。
    /// - 允许拖动调整大小，拖动期间每帧 clamp 到最小尺寸，防止布局塌陷。
    /// - Tab 条用 RimWorld 原生 <see cref="TabDrawer"/>，最大宽度 =（可用宽度 - 侧栏宽度）/ tab 数，
    ///   窗口变窄或 tab 增多时自动缩小，避免固定宽度导致重叠/越界。
    /// - 侧栏宽度响应式：按插件偏好宽度但不超过窗口 38%，且保证主内容区不小于保底宽度。
    /// </summary>
    public class ServerTab : MainTabWindow
    {
        private const float DEFAULT_SPACING = 10f;
        private const float SIDEBAR_TAB_HEIGHT = 30f;

        // 窗口尺寸（响应式上下限 + 屏幕分辨率比例）
        private const float MIN_WINDOW_WIDTH = 640f;
        private const float MIN_WINDOW_HEIGHT = 480f;
        private const float MAX_WINDOW_WIDTH = 1280f;
        private const float MAX_WINDOW_HEIGHT = 800f;
        private const float WINDOW_WIDTH_RATIO = 0.72f;
        private const float WINDOW_HEIGHT_RATIO = 0.78f;

        // Tab 条参数：上限改小（200 → 150），下限保证可读
        private const float MIN_TAB_WIDTH = 80f;
        private const float MAX_TAB_WIDTH = 150f;

        // 侧栏响应式约束
        private const float SIDEBAR_MAX_RATIO = 0.38f;
        private const float SIDEBAR_MIN_WIDTH = 120f;
        private const float MAIN_MIN_WIDTH = 480f;

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
            this.closeOnCancel = false;
            this.resizeable = true;

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

        /// <summary>
        /// 响应式初始尺寸：优先恢复用户上次调整的尺寸，否则按屏幕分辨率计算并 clamp 到安全区间。
        /// </summary>
        public override Vector2 InitialSize
        {
            get
            {
                Settings settings = Instance?.Settings;
                float savedWidth = settings?.ServerTabWidth ?? 0f;
                float savedHeight = settings?.ServerTabHeight ?? 0f;
                if (savedWidth >= MIN_WINDOW_WIDTH && savedHeight >= MIN_WINDOW_HEIGHT)
                {
                    return new Vector2(savedWidth, savedHeight);
                }

                return new Vector2(
                    Mathf.Clamp(Screen.width * WINDOW_WIDTH_RATIO, MIN_WINDOW_WIDTH, MAX_WINDOW_WIDTH),
                    Mathf.Clamp(Screen.height * WINDOW_HEIGHT_RATIO, MIN_WINDOW_HEIGHT, MAX_WINDOW_HEIGHT));
            }
        }

        public override void PreClose()
        {
            // 记忆窗口尺寸（拖动结束后最后一次 windowRect 即最终尺寸）
            Settings settings = Instance?.Settings;
            if (settings != null)
            {
                settings.ServerTabWidth = Mathf.Max(MIN_WINDOW_WIDTH, windowRect.width);
                settings.ServerTabHeight = Mathf.Max(MIN_WINDOW_HEIGHT, windowRect.height);
                settings.AcceptChanges();
            }
            base.PreClose();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 拖动过程中强制最小尺寸（WindowResizer 默认允许拖到很小），防止布局塌陷
            Rect clamped = windowRect;
            clamped.width = Mathf.Max(MIN_WINDOW_WIDTH, clamped.width);
            clamped.height = Mathf.Max(MIN_WINDOW_HEIGHT, clamped.height);
            windowRect = clamped;

            // 实时同步记忆尺寸，关闭时由 PreClose 落盘
            Settings settings = Instance?.Settings;
            if (settings != null)
            {
                settings.ServerTabWidth = windowRect.width;
                settings.ServerTabHeight = windowRect.height;
            }

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
            float sidebarWidth = 0f;
            if (sidebarProviders.Count > 0)
            {
                // 手动计算 Maximum，避免 Draw 路径 LINQ 分配
                float desiredWidth = 0f;
                for (int i = 0; i < sidebarProviders.Count; i++)
                {
                    float w = sidebarProviders[i].PreferredWidth;
                    if (w > desiredWidth) desiredWidth = w;
                }

                // 响应式约束：不超过窗口 38%，且主内容区保底 MAIN_MIN_WIDTH
                float maxByRatio = usableRect.width * SIDEBAR_MAX_RATIO;
                float maxByMain = usableRect.width - MAIN_MIN_WIDTH - DEFAULT_SPACING;
                float minSidebar = Mathf.Min(SIDEBAR_MIN_WIDTH, maxByMain);
                sidebarWidth = Mathf.Clamp(desiredWidth, minSidebar, Mathf.Min(maxByRatio, maxByMain));

                rightColumnRect = usableRect.RightPartPixels(sidebarWidth);
                mainRect = usableRect.LeftPartPixels(usableRect.width - (sidebarWidth + DEFAULT_SPACING));
            }

            // 主 Tab 条：最大宽度 =（可用宽度 - 侧栏）/ tab 数，保证 n × maxTabWidth ≤ 可用宽度
            float tabAreaWidth = usableRect.width - sidebarWidth - (sidebarWidth > 0f ? DEFAULT_SPACING : 0f);
            float maxTabWidth = ComputeTabMaxWidth(tabAreaWidth, tabList.Count);
            TabDrawer.DrawTabs(usableRect, tabList, maxTabWidth);

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

                float sidebarTabMaxWidth = Mathf.Max(
                    MIN_TAB_WIDTH, rightColumnRect.width / Mathf.Max(1, sidebarTabList.Count));
                TabDrawer.DrawTabs(sidebarTabRect, sidebarTabList, sidebarTabMaxWidth);

                if (activeSidebarTab >= 0 && activeSidebarTab < sidebarProviders.Count)
                {
                    sidebarProviders[activeSidebarTab].Draw(sidebarContentRect);
                }
            }

        }

        /// <summary>
        /// 响应式 tab 最大宽度：可用宽度均分给每个 tab，clamp 到 [MIN_TAB_WIDTH, MAX_TAB_WIDTH]。
        /// </summary>
        private static float ComputeTabMaxWidth(float availableWidth, int tabCount)
        {
            if (tabCount <= 0)
            {
                return MAX_TAB_WIDTH;
            }
            float perTab = availableWidth / tabCount;
            return Mathf.Clamp(perTab, MIN_TAB_WIDTH, MAX_TAB_WIDTH);
        }
    }
}
