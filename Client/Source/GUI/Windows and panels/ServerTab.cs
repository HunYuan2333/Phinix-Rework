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
    /// - 初始尺寸按 RimWorld UI 坐标缩放，clamp 到屏幕安全区；用户手动调整过的尺寸持久化（Settings.ServerTabWidth/Height），
    ///   下次打开优先恢复。
    /// - 允许拖动调整大小，拖动期间每帧 clamp 到最小尺寸，防止布局塌陷。
    /// - 主/侧栏 Tab 使用 RimWorld 原生溢出布局，Tab 增多时自动换行且内容区同步让出实际高度。
    /// - 侧栏宽度响应式：按插件偏好宽度但不超过窗口 38%，且保证主内容区不小于保底宽度。
    /// </summary>
    public class ServerTab : MainTabWindow
    {
        private const float DEFAULT_SPACING = 10f;

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
        private IUiAcceptKeyHandler activeAcceptKeyHandler;
        private bool hadInputLastDraw = true;
        private float cachedMainTabWidth = -1f;
        private float cachedMainTabHeight;
        private float cachedSidebarTabWidth = -1f;
        private float cachedSidebarTabHeight;
        private object cachedTabLanguage;

        public ServerTab()
        {
            this.closeOnAccept = false;
            this.closeOnCancel = false;
            this.resizeable = true;
            this.draggable = true;

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
                float preferredWidth;
                float preferredHeight;
                if (savedWidth > 0f && savedHeight > 0f)
                {
                    preferredWidth = savedWidth;
                    preferredHeight = savedHeight;
                }
                else
                {
                    preferredWidth = Mathf.Min(UI.screenWidth * WINDOW_WIDTH_RATIO, MAX_WINDOW_WIDTH);
                    preferredHeight = Mathf.Min(UI.screenHeight * WINDOW_HEIGHT_RATIO, MAX_WINDOW_HEIGHT);
                }

                Rect safeRect = GetScreenSafeRect();
                return new Vector2(
                    ClampLength(preferredWidth, MIN_WINDOW_WIDTH, safeRect.width),
                    ClampLength(preferredHeight, MIN_WINDOW_HEIGHT, safeRect.height));
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ClampWindowToScreenSafeArea();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ClampWindowToScreenSafeArea();

            Settings settings = Instance?.Settings;
            if (settings != null)
            {
                settings.ServerTabWidth = windowRect.width;
                settings.ServerTabHeight = windowRect.height;
            }
        }

        public override void PreClose()
        {
            // 记忆窗口尺寸（拖动结束后最后一次 windowRect 即最终尺寸）
            Settings settings = Instance?.Settings;
            if (settings != null)
            {
                Rect safeRect = GetScreenSafeRect();
                settings.ServerTabWidth = ClampLength(windowRect.width, MIN_WINDOW_WIDTH, safeRect.width);
                settings.ServerTabHeight = ClampLength(windowRect.height, MIN_WINDOW_HEIGHT, safeRect.height);
                settings.AcceptChanges();
            }
            base.PreClose();
        }

        public override void OnAcceptKeyPressed()
        {
            IUiAcceptKeyHandler handler = activeAcceptKeyHandler;
            if (!CanHandleAcceptKey(handler) || !handler.TryHandleAcceptKey())
            {
                closeOnAccept = false;
                return;
            }

            closeOnAccept = false;
            Event current = Event.current;
            if (current != null && current.type != EventType.Used)
            {
                current.Use();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            object activeLanguage = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(cachedTabLanguage, activeLanguage))
            {
                cachedTabLanguage = activeLanguage;
                cachedMainTabWidth = -1f;
                cachedSidebarTabWidth = -1f;
            }

            float mainColumnWidth;
            float sidebarWidth;
            ComputeColumnWidths(inRect.width, out mainColumnWidth, out sidebarWidth);

            float mainTabHeight = Mathf.Min(GetMainTabHeight(mainColumnWidth, inRect), Mathf.Max(0f, inRect.height));
            float maxBannerHeight = Mathf.Max(0f, inRect.height - mainTabHeight);
            float bannerHeight = ComputeBannerHeight(maxBannerHeight);
            DrawBanners(inRect, bannerHeight);

            float contentHeight = Mathf.Max(0f, inRect.height - bannerHeight - mainTabHeight);
            Rect contentRect = new Rect(inRect.xMin, inRect.yMin + bannerHeight + mainTabHeight, inRect.width, contentHeight);
            Rect mainRect = new Rect(contentRect.xMin, contentRect.yMin, mainColumnWidth, contentRect.height);
            Rect mainTabRect = new Rect(
                inRect.xMin,
                inRect.yMin + bannerHeight,
                mainColumnWidth,
                Mathf.Max(0f, inRect.height - bannerHeight));
            Rect rightColumnRect = default;
            if (sidebarWidth > 0f)
            {
                rightColumnRect = new Rect(contentRect.xMax - sidebarWidth, contentRect.yMin, sidebarWidth, contentRect.height);
            }

            TabDrawer.DrawTabsOverflow(mainTabRect, tabList, MIN_TAB_WIDTH, MAX_TAB_WIDTH);

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
                float sidebarTabHeight = Mathf.Min(
                    GetSidebarTabHeight(rightColumnRect.width, rightColumnRect),
                    Mathf.Max(0f, rightColumnRect.height));
                Rect sidebarContentRect = new Rect(
                    rightColumnRect.xMin,
                    rightColumnRect.yMin + sidebarTabHeight,
                    rightColumnRect.width,
                    Mathf.Max(0f, rightColumnRect.height - sidebarTabHeight));
                TabDrawer.DrawTabsOverflow(rightColumnRect, sidebarTabList, MIN_TAB_WIDTH, MAX_TAB_WIDTH);

                if (activeSidebarTab >= 0 && activeSidebarTab < sidebarProviders.Count)
                {
                    sidebarProviders[activeSidebarTab].Draw(sidebarContentRect);
                }
            }

            RefreshAcceptKeyHandler();
            TryHandleConsumedAcceptKey();
            closeOnAccept = CanHandleAcceptKey(activeAcceptKeyHandler);
            hadInputLastDraw = Find.WindowStack.GetsInput(this);
        }

        private void RefreshAcceptKeyHandler()
        {
            activeAcceptKeyHandler = null;

            if (activeTab >= 0 && activeTab < tabProviders.Count)
            {
                activeAcceptKeyHandler = tabProviders[activeTab] as IUiAcceptKeyHandler;
            }

            if (activeAcceptKeyHandler == null &&
                activeSidebarTab >= 0 && activeSidebarTab < sidebarProviders.Count)
            {
                activeAcceptKeyHandler = sidebarProviders[activeSidebarTab] as IUiAcceptKeyHandler;
            }
        }

        private void TryHandleConsumedAcceptKey()
        {
            Event current = Event.current;
            if (!hadInputLastDraw ||
                current == null ||
                current.type != EventType.Used ||
                current.rawType != EventType.KeyDown ||
                !IsEnterKey(current.keyCode) ||
                !Find.WindowStack.GetsInput(this) ||
                !CanHandleAcceptKey(activeAcceptKeyHandler))
            {
                return;
            }

            activeAcceptKeyHandler.TryHandleAcceptKey();
        }

        private static bool CanHandleAcceptKey(IUiAcceptKeyHandler handler)
        {
            return handler != null &&
                handler.WantsAcceptKey &&
                string.IsNullOrEmpty(Input.compositionString);
        }

        private static bool IsEnterKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter;
        }

        private void ComputeColumnWidths(float availableWidth, out float mainWidth, out float sidebarWidth)
        {
            mainWidth = Mathf.Max(0f, availableWidth);
            sidebarWidth = 0f;
            if (sidebarProviders.Count == 0 || availableWidth <= DEFAULT_SPACING)
            {
                return;
            }

            float desiredWidth = 0f;
            for (int i = 0; i < sidebarProviders.Count; i++)
            {
                desiredWidth = Mathf.Max(desiredWidth, sidebarProviders[i].PreferredWidth);
            }

            float maxByRatio = Mathf.Max(0f, availableWidth * SIDEBAR_MAX_RATIO);
            float maxByMain = Mathf.Max(0f, availableWidth - MAIN_MIN_WIDTH - DEFAULT_SPACING);
            float maxSidebar = Mathf.Min(maxByRatio, maxByMain);
            if (maxSidebar <= 0f)
            {
                return;
            }

            float minSidebar = Mathf.Min(SIDEBAR_MIN_WIDTH, maxSidebar);
            sidebarWidth = Mathf.Clamp(desiredWidth, minSidebar, maxSidebar);
            mainWidth = Mathf.Max(0f, availableWidth - sidebarWidth - DEFAULT_SPACING);
        }

        private float ComputeBannerHeight(float maximumHeight)
        {
            float total = 0f;
            for (int i = 0; i < bannerProviders.Count && total < maximumHeight; i++)
            {
                total += Mathf.Max(0f, bannerProviders[i].CurrentHeight);
            }
            return Mathf.Min(total, maximumHeight);
        }

        private void DrawBanners(Rect inRect, float availableHeight)
        {
            float cursorY = inRect.yMin;
            float remainingHeight = availableHeight;
            for (int i = 0; i < bannerProviders.Count && remainingHeight > 0f; i++)
            {
                float height = Mathf.Min(Mathf.Max(0f, bannerProviders[i].CurrentHeight), remainingHeight);
                if (height <= 0f)
                {
                    continue;
                }

                bannerProviders[i].Draw(new Rect(inRect.xMin, cursorY, inRect.width, height));
                cursorY += height;
                remainingHeight -= height;
            }
        }

        private float GetMainTabHeight(float width, Rect referenceRect)
        {
            if (!Mathf.Approximately(cachedMainTabWidth, width))
            {
                cachedMainTabWidth = width;
                Rect tabBaseRect = new Rect(referenceRect.xMin, referenceRect.yMin, width, referenceRect.height);
                cachedMainTabHeight = TabDrawer.GetOverflowTabHeight(tabBaseRect, tabList, MIN_TAB_WIDTH, MAX_TAB_WIDTH);
            }
            return Mathf.Max(0f, cachedMainTabHeight);
        }

        private float GetSidebarTabHeight(float width, Rect referenceRect)
        {
            if (!Mathf.Approximately(cachedSidebarTabWidth, width))
            {
                cachedSidebarTabWidth = width;
                Rect tabBaseRect = new Rect(referenceRect.xMin, referenceRect.yMin, width, referenceRect.height);
                cachedSidebarTabHeight = TabDrawer.GetOverflowTabHeight(tabBaseRect, sidebarTabList, MIN_TAB_WIDTH, MAX_TAB_WIDTH);
            }
            return Mathf.Max(0f, cachedSidebarTabHeight);
        }

        private void ClampWindowToScreenSafeArea()
        {
            Rect safeRect = GetScreenSafeRect();
            Rect clamped = windowRect;
            clamped.width = ClampLength(clamped.width, MIN_WINDOW_WIDTH, safeRect.width);
            clamped.height = ClampLength(clamped.height, MIN_WINDOW_HEIGHT, safeRect.height);
            clamped.x = Mathf.Clamp(clamped.x, safeRect.xMin, safeRect.xMax - clamped.width);
            clamped.y = Mathf.Clamp(clamped.y, safeRect.yMin, safeRect.yMax - clamped.height);
            windowRect = clamped;
        }

        private static Rect GetScreenSafeRect()
        {
            float width = Mathf.Max(0f, UI.screenWidth);
            float height = Mathf.Max(0f, UI.screenHeight);
            return new Rect(0f, 0f, width, height);
        }

        private static float ClampLength(float value, float minimum, float maximum)
        {
            float nonNegativeMaximum = Mathf.Max(0f, maximum);
            float effectiveMinimum = Mathf.Min(Mathf.Max(0f, minimum), nonNegativeMaximum);
            return Mathf.Clamp(value, effectiveMinimum, nonNegativeMaximum);
        }
    }
}
