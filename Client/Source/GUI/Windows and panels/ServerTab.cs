using System;
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
        private const float MIN_WINDOW_WIDTH = 640f, MIN_WINDOW_HEIGHT = 480f;
        private const float MAX_WINDOW_WIDTH = 1280f, MAX_WINDOW_HEIGHT = 800f;
        private const float WINDOW_WIDTH_RATIO = .72f, WINDOW_HEIGHT_RATIO = .78f;
        private readonly List<Entry<IMainTabProvider>> tabs = new List<Entry<IMainTabProvider>>();
        private readonly List<Entry<IServerSidebarProvider>> sidebars = new List<Entry<IServerSidebarProvider>>();
        private readonly List<Entry<INoticeBannerProvider>> banners = new List<Entry<INoticeBannerProvider>>();
        private readonly HashSet<string> reported = new HashSet<string>();
        private string selectedTab, selectedSidebar;
        private bool drawerOpen, bannersCollapsed, hadInputLastDraw = true;
        private readonly List<float> bannerHeights = new List<float>();
        private readonly Dictionary<string, float> navigationWidths = new Dictionary<string, float>();
        private object navigationLanguage;
        private float utilityMeasuredWidth = -1; private string utilityCaption; private Vector2 utilitySize;
        private Vector2 bannerScroll;
        private long identityThemeVersion = -1;
        private string identityRaw, identityPrefix, identityMarkup;
        private int sidebarDragControl;
        private readonly SidebarResizeCursor sidebarCursor = new SidebarResizeCursor();
        private float sidebarDragOffset;
        private int refreshFrames;
        private object refreshLanguage;
        private IUiAcceptKeyHandler activeAcceptKeyHandler;

        private sealed class Entry<T> where T : class
        {
            internal IClientApiProvider<T> Handle;
            internal T Api;
            internal string Id, Label;
            internal float Order;
            internal bool Failed, Opened;
            internal bool Current => Handle.TryGet(out var api) && ReferenceEquals(api, Api);
        }
        protected override float Margin => 0f;

        public ServerTab()
        {
            // The shell paints its own rounded background; a native rectangle leaks at the corners.
            doWindowBackground = false;
            closeOnAccept = false; closeOnCancel = false; resizeable = true; draggable = true;
            RefreshProviders();
        }
        private static string L(string key) => ("Phinix_shell_" + key).Translate();
        private void Report(string id, Exception error)
        {
            if (reported.Count < 64 && reported.Add(id)) Log.Warning("[Phinix] UI provider " + id + " failed: " + error);
        }
        private void Refresh<T>(List<Entry<T>> target, Func<T, string> label, Func<T, float> order) where T : class
        {
            var directory = Instance.UiApiDirectory;
            if (directory == null) { foreach (var entry in target) CloseView(entry); target.Clear(); return; }
            var next = new List<Entry<T>>();
            foreach (var handle in directory.Capture<T>())
            {
                if (!handle.TryGet(out var api)) continue;
                string fallback = handle.OwnerId + "/registration." + handle.RegistrationId;
                try
                {
                    var metadata = api as IClientNavigationMetadata;
                    string local = metadata?.NavigationId;
                    if (local != null)
                    {
                        try { new ClientPresentationPage(local, "", 0, null); }
                        catch (Exception error) { Report(fallback, error); local = null; }
                    }
                    string id = local == null ? fallback : handle.OwnerId + "/" + local;
                    if (next.Any(e => e.Id == id)) { Report(fallback, new ArgumentException("DuplicateNavigationId")); id = fallback; }
                    var old = target.Find(e => e.Handle.RegistrationId == handle.RegistrationId && ReferenceEquals(e.Api, api));
                    var entry = old ?? new Entry<T>();
                    entry.Handle = handle; entry.Api = api; entry.Id = id; entry.Label = label(api) ?? ""; entry.Order = order(api); next.Add(entry);
                }
                catch (Exception error)
                {
                    Report(fallback, error);
                    next.Add(new Entry<T> { Handle = handle, Api = api, Id = fallback, Label = handle.OwnerId, Failed = true });
                }
            }
            next.Sort((a, b) => { int c = a.Order.CompareTo(b.Order); if (c == 0) c = string.CompareOrdinal(a.Id, b.Id); return c; });
            foreach (var removed in target) if (!next.Contains(removed)) CloseView(removed);
            target.Clear(); target.AddRange(next);
        }
        private void RefreshProviders()
        {
            Refresh(tabs, p => p.TabLabel, p => p.TabOrder);
            Refresh(sidebars, p => p.TabLabel, p => p.Order);
            Refresh(banners, p => "", p => 0);
            if (!tabs.Any(e => e.Id == selectedTab)) selectedTab = tabs.FirstOrDefault()?.Id;
            if (!sidebars.Any(e => e.Id == selectedSidebar)) selectedSidebar = sidebars.FirstOrDefault()?.Id;
            if (sidebars.Count == 0) drawerOpen = false;
            activeAcceptKeyHandler = null;
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
                    Vector2 providerPreferredSize = GetInitialProviderPreferredSize();
                    preferredWidth = Mathf.Min(
                        Mathf.Max(UI.screenWidth * WINDOW_WIDTH_RATIO, providerPreferredSize.x),
                        MAX_WINDOW_WIDTH);
                    preferredHeight = Mathf.Min(
                        Mathf.Max(UI.screenHeight * WINDOW_HEIGHT_RATIO, providerPreferredSize.y),
                        MAX_WINDOW_HEIGHT);
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

        public override void PostOpen()
        {
            base.PostOpen();
            Instance?.NotifyMainWindowOpened();
            UpdateViews();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!Find.WindowStack.GetsInput(this) || !Application.isFocused) { ReleaseSidebarDrag(); sidebarCursor.Release(); }
            ClampWindowToScreenSafeArea();
            if (++refreshFrames >= 30 || !ReferenceEquals(refreshLanguage, LanguageDatabase.activeLanguage))
            { refreshFrames = 0; refreshLanguage = LanguageDatabase.activeLanguage; RefreshProviders(); }

            UpdateViews();
            Settings settings = Instance?.Settings;
            if (settings != null)
            {
                settings.ServerTabWidth = windowRect.width;
                settings.ServerTabHeight = windowRect.height;
            }
        }

        public override void PreClose()
        {
            ReleaseSidebarDrag();
            sidebarCursor.Release();
            foreach (var entry in tabs) CloseView(entry);
            foreach (var entry in sidebars) CloseView(entry);
            foreach (var entry in banners) CloseView(entry);
            activeAcceptKeyHandler = null;
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

        private void CloseView<T>(Entry<T> entry) where T : class
        {
            if (!entry.Opened) return; entry.Opened = false;
            try { (entry.Api as IClientViewLifecycle)?.ViewClosed(); }
            catch (Exception error) { Report(entry.Id, error); }
        }
        private void UpdateView<T>(Entry<T> entry) where T : class
        {
            if (!entry.Current) { CloseView(entry); return; }
            if (entry.Failed) return;
            try
            {
                var view = entry.Api as IClientViewLifecycle;
                if (view == null) return;
                if (!entry.Opened) { entry.Opened = true; view.ViewOpened(); }
                view.ViewUpdate();
            }
            catch (Exception error) { entry.Failed = true; CloseView(entry); Report(entry.Id, error); }
        }
        private void UpdateViews()
        {
            foreach (var entry in tabs) UpdateView(entry);
            foreach (var entry in sidebars) UpdateView(entry);
            foreach (var entry in banners) UpdateView(entry);
        }
        private Vector2 GetInitialProviderPreferredSize()
        {
            try
            {
                var responsive = tabs.FirstOrDefault()?.Api as IResponsiveMainTabProvider;
                var hints = responsive?.LayoutHints ?? UiLayoutHints.Default;
                return hints.PreferredContentSize + new Vector2(36f, 76f);
            }
            catch (Exception error) { Report("layout-hints", error); return UiLayoutHints.Default.PreferredContentSize + new Vector2(36f, 76f); }
        }
        public override void OnAcceptKeyPressed()
        {
            if (AcceptHandlerCurrent() && CanHandleAcceptKey(activeAcceptKeyHandler)) InvokeAcceptKey();
            closeOnAccept = false;
        }
        private bool AcceptHandlerCurrent()
        {
            var tab = tabs.Find(entry => entry.Id == selectedTab);
            if (tab != null && tab.Current && !tab.Failed && ReferenceEquals(activeAcceptKeyHandler, tab.Api)) return true;
            var sidebar = sidebars.Find(entry => entry.Id == selectedSidebar);
            return sidebar != null && sidebar.Current && !sidebar.Failed && ReferenceEquals(activeAcceptKeyHandler, sidebar.Api);
        }
        private void InvokeAcceptKey()
        {
            try
            {
                if (activeAcceptKeyHandler?.TryHandleAcceptKey() == true && Event.current != null && Event.current.type != EventType.Used) Event.current.Use();
            }
            catch (Exception error) { Report("accept-key", error); activeAcceptKeyHandler = null; }
        }
        private static bool CanHandleAcceptKey(IUiAcceptKeyHandler handler)
        { return handler != null && handler.WantsAcceptKey && string.IsNullOrEmpty(Input.compositionString); }
        private void DrawProvider<T>(Entry<T> entry, Rect rect, Action<T, Rect> draw) where T : class
        {
            if (entry == null || rect.width <= 0 || rect.height <= 0) return;
            if (!entry.Current || entry.Failed) { ClientWidgets.Label(rect, L("providerFailed"), HostThemeDrawing.Current, true); return; }
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.BeginGroup(rect);
                try { draw(entry.Api, new Rect(0, 0, rect.width, rect.height)); }
                catch (Exception error) { entry.Failed = true; Report(entry.Id, error); }
                finally { UnityEngine.GUI.EndGroup(); }
            }
        }
        private Vector2 SettingsButtonSize(float width)
        {
            string label = L("settings");
            if (utilityMeasuredWidth != width || utilityCaption != label)
            { utilityMeasuredWidth = width; utilityCaption = label; utilitySize = ClientWidgets.MeasureButton(label, Mathf.Max(1, width * .35f), minimumWidth: 80); }
            return utilitySize;
        }
        private float NavigationWidth(string label)
        {
            if (!ReferenceEquals(navigationLanguage, LanguageDatabase.activeLanguage)) { navigationWidths.Clear(); navigationLanguage = LanguageDatabase.activeLanguage; }
            if (navigationWidths.TryGetValue(label, out var natural)) return natural;
            using (new ClientGuiScope(true)) { Text.Font = GameFont.Small; Text.WordWrap = false; natural = Mathf.Max(80, Text.CalcSize(label).x + 24); }
            navigationWidths[label] = natural; return natural;
        }
        private void Navigation<T>(Rect rect, List<Entry<T>> entries, string selected, Action<string> select, bool fill = false) where T : class
        {
            if (entries.Count == 0 || rect.width <= 0) return;
            if (!ReferenceEquals(navigationLanguage, LanguageDatabase.activeLanguage)) { navigationWidths.Clear(); navigationLanguage = LanguageDatabase.activeLanguage; }
            if (navigationWidths.Count >= 256) navigationWidths.Clear();
            // Balance short captions as before; long captions retain their measured width.
            // Overflow is based on readable widths, never on compressed text.
            float share = (rect.width - 4 * (entries.Count - 1)) / entries.Count;
            float preferred = fill ? Mathf.Max(80, share) : Mathf.Clamp(share, 80, 140);
            float total = -4;
            foreach (var entry in entries) total += Mathf.Max(preferred, NavigationWidth(entry.Label)) + 4;
            bool overflow = total > rect.width;
            float available = Mathf.Max(0, rect.width - (overflow ? 40 : 0)), x = rect.x;
            foreach (var entry in entries)
            {
                if (!navigationWidths.TryGetValue(entry.Label, out float width)) continue;
                width = Mathf.Max(preferred, width);
                if (x + width > rect.x + available) break;
                if (ClientWidgets.NavigationPill(new Rect(x, rect.y, width, rect.height), entry.Label, entry.Id == selected, HostThemeDrawing.Current) && entry.Current) select(entry.Id);
                x += width + 4;
            }
            if (overflow && ClientWidgets.Button(new Rect(rect.xMax - Mathf.Min(40, rect.width), rect.y, Mathf.Min(40, rect.width), rect.height), "…", HostThemeDrawing.Current))
            {
                // All entries, including an overflowed selection, stay discoverable. Menu callbacks revalidate exact registration.
                var options = new List<FloatMenuOption>();
                foreach (var item in entries)
                {
                    var entry = item;
                    options.Add(new FloatMenuOption(entry.Label, () => { if (IsOpen && entry.Current) select(entry.Id); }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        public override void DoWindowContents(Rect rect)
        {
            using (HostThemeDrawing.BeginDraw())
            using (new ClientGuiScope(true))
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                // Margin=0 exposes the full window to this draw group. Content padding is explicit.
                ClientWidgets.RoundedSurface(rect, HostThemeDrawing.Current.Background.ToUnityColor(), 16);
                ClientWidgets.RoundedBorder(rect, HostThemeDrawing.Current.Separator.ToUnityColor(), 16, 2);
                rect = rect.ContractedBy(12);
                var settingsSize = SettingsButtonSize(rect.width);
                float navHeight = Mathf.Min(Mathf.Max(ClientWidgets.ControlHeight, settingsSize.y), rect.height);
                float utilityWidth = settingsSize.x;
                float y = rect.y + navHeight + 8f;
                y += DrawBanners(new Rect(rect.x, y, rect.width, Mathf.Max(0, rect.yMax - y)));
                var content = new Rect(rect.x, y, rect.width, Mathf.Max(0, rect.yMax - y));
                var sidebar = sidebars.Find(e => e.Id == selectedSidebar);
                float sideWidth = 0f, sidePreferred = 270f, sideMinimum = 120f;
                if (sidebar != null && sidebar.Current)
                {
                    if (!sidebar.Failed) try
                    {
                        float preferred = sidebar.Api.PreferredWidth;
                        if (float.IsNaN(preferred) || float.IsInfinity(preferred) || preferred < 0) throw new ArgumentException("SidebarWidthInvalid");
                        var responsive = sidebar.Api as IResponsiveSidebarProvider;
                        float minimum = responsive?.MinimumWidth ?? 120f;
                        if (float.IsNaN(minimum) || float.IsInfinity(minimum) || minimum < 0) throw new ArgumentException("SidebarMinimumInvalid");
                        sideMinimum = minimum; sidePreferred = Mathf.Max(preferred, minimum);
                    }
                    catch (Exception error) { sidebar.Failed = true; Report(sidebar.Id, error); }
                }
                bool collapsed = sidebar != null && !SidebarSplitLayout.TryWidth(content.width, sidePreferred, sideMinimum, Instance.Settings.ServerSidebarRatio, out sideWidth);
                int splitControl = GUIUtility.GetControlID(FocusType.Passive);
                if (sidebar != null && !collapsed)
                    sideWidth = DrawSidebarDivider(content, sideWidth, sideMinimum, splitControl);
                else { ReleaseSidebarDrag(); sidebarCursor.Release(); }
                if (!collapsed) drawerOpen = false;
                float drawerWidth = collapsed ? 36f : 0f;
                var utility = new Rect(rect.xMax - utilityWidth, rect.y, utilityWidth, navHeight);
                if (ClientWidgets.Button(utility, L("settings"), HostThemeDrawing.Current, wrap: true)) OpenUtilities();
                if (collapsed && ClientWidgets.Button(new Rect(utility.x - drawerWidth - 4, rect.y, drawerWidth, navHeight), "☰", HostThemeDrawing.Current)) drawerOpen = !drawerOpen;
                Navigation(new Rect(rect.x, rect.y, Mathf.Max(0, utility.x - rect.x - drawerWidth - 12f), navHeight), tabs, selectedTab, id => { selectedTab = id; UnityEngine.GUI.FocusControl(null); });
                var main = new Rect(content.x, content.y, collapsed ? content.width : Mathf.Max(0, content.width - (sideWidth > 0 ? sideWidth + 8 : 0)), content.height);
                var tab = tabs.Find(e => e.Id == selectedTab);
                // Drawer owns input; do not draw interactive main controls beneath it.
                if (!(collapsed && drawerOpen)) DrawProvider(tab, main, (api, area) => api.Draw(area));
                if (tab == null) ClientWidgets.Label(main, L("noPages"), HostThemeDrawing.Current, true);
                if (sidebar != null && (!collapsed || drawerOpen))
                {
                    float width = collapsed ? Mathf.Min(content.width, Mathf.Max(280f, sidePreferred)) : sideWidth;
                    var panel = new Rect(content.xMax - width, content.y, width, content.height);
                    // The shell frames navigation; providers own their themed content cards.
                    ClientWidgets.RoundedSurface(panel, HostThemeDrawing.Current.Background.ToUnityColor());
                    ClientWidgets.RoundedBorder(panel, HostThemeDrawing.Current.Separator.ToUnityColor());
                    float inset = Mathf.Min(6, Mathf.Min(panel.width, panel.height) / 2);
                    panel = panel.ContractedBy(inset);
                    float selfHeight = Mathf.Min(ClientWidgets.ControlHeight + 8, panel.height);
                    DrawSelfIdentity(new Rect(panel.x, panel.y, panel.width, selfHeight));
                    float selfSpace = Mathf.Min(panel.height, selfHeight + 4);
                    panel = new Rect(panel.x, panel.y + selfSpace, panel.width, Mathf.Max(0, panel.height - selfSpace));
                    float headerHeight = sidebars.Count > 1 ? Mathf.Min(32, panel.height) : 0;
                    if (headerHeight > 0) Navigation(new Rect(panel.x, panel.y, panel.width, headerHeight), sidebars, selectedSidebar, id => { selectedSidebar = id; UnityEngine.GUI.FocusControl(null); }, fill: true);
                    float contentTop = Mathf.Min(panel.height, headerHeight + 8);
                    if (panel.height > headerHeight + 4)
                        ClientWidgets.Fill(new Rect(panel.x + 4, panel.y + headerHeight + 4, Mathf.Max(0, panel.width - 8), 1), HostThemeDrawing.Current.Separator.ToUnityColor());
                    DrawProvider(sidebars.Find(e => e.Id == selectedSidebar), new Rect(panel.x, panel.y + contentTop, panel.width, Mathf.Max(0, panel.height - contentTop)), (api, area) => api.Draw(area));
                }
                activeAcceptKeyHandler = tab != null && tab.Current && !tab.Failed && !(collapsed && drawerOpen) ? tab.Api as IUiAcceptKeyHandler : null;
                if (activeAcceptKeyHandler == null && sidebar != null && sidebar.Current && !sidebar.Failed && (!collapsed || drawerOpen)) activeAcceptKeyHandler = sidebar.Api as IUiAcceptKeyHandler;
                var current = Event.current;
                if (hadInputLastDraw && current != null && current.type == EventType.Used && current.rawType == EventType.KeyDown &&
                    (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && Find.WindowStack.GetsInput(this) && AcceptHandlerCurrent() && CanHandleAcceptKey(activeAcceptKeyHandler)) InvokeAcceptKey();
                closeOnAccept = CanHandleAcceptKey(activeAcceptKeyHandler);
                hadInputLastDraw = Find.WindowStack.GetsInput(this);
            }
        }
        private float DrawBanners(Rect rect)
        {
            if (navigationWidths.Count >= 256) navigationWidths.Clear();
            float total = 0;
            var heights = bannerHeights; heights.Clear();
            foreach (var entry in banners)
            {
                float height = 0;
                try { if (entry.Current) height = entry.Failed ? 32 : entry.Api.CurrentHeight; }
                catch (Exception error) { entry.Failed = true; height = 32; Report(entry.Id, error); }
                if (float.IsNaN(height) || float.IsInfinity(height)) { entry.Failed = true; Report(entry.Id, new ArgumentException("BannerHeightInvalid")); height = 32; }
                if (height > 4096) { entry.Failed = true; Report(entry.Id, new ArgumentOutOfRangeException("BannerHeight")); height = 32; }
                height = Mathf.Max(0, height); heights.Add(height); total += height;
            }
            if (total <= 0 || rect.height < 40) return 0;
            if (ClientWidgets.Button(new Rect(rect.x, rect.y, rect.width, 28), L(bannersCollapsed ? "showNotices" : "hideNotices"), HostThemeDrawing.Current)) bannersCollapsed = !bannersCollapsed;
            if (bannersCollapsed) return 36;
            float heightAvailable = Mathf.Min(total, Mathf.Max(0, rect.height * .3f));
            var viewport = new Rect(rect.x, rect.y + 32, rect.width, heightAvailable);
            float width = Mathf.Max(1, viewport.width - 16);
            bannerScroll.y = Mathf.Clamp(bannerScroll.y, 0, Mathf.Max(0, total - heightAvailable));
            Widgets.BeginScrollView(viewport, ref bannerScroll, new Rect(0, 0, width, total));
            try
            {
                float y = 0;
                for (int i = 0; i < banners.Count; i++)
                {
                    if (heights[i] > 0 && y + heights[i] >= bannerScroll.y && y <= bannerScroll.y + heightAvailable)
                        DrawProvider(banners[i], new Rect(0, y, width, heights[i]), (api, area) => api.Draw(area));
                    y += heights[i];
                }
            }
            finally { Widgets.EndScrollView(); }
            ClientWidgets.ScrollbarDecoration(viewport,bannerScroll,new Rect(0,0,width,total),HostThemeDrawing.Current);
            return heightAvailable + 40;
        }
        private void ReleaseSidebarDrag()
        {
            if (sidebarDragControl != 0 && GUIUtility.hotControl == sidebarDragControl) GUIUtility.hotControl = 0;
            sidebarDragControl = 0;
        }
        private float DrawSidebarDivider(Rect content, float width, float minimum, int control)
        {
            var ev = Event.current;
            float center = content.xMax - width - SidebarSplitLayout.Gap / 2;
            var hit = new Rect(center - SidebarSplitLayout.Gap / 2, content.y, SidebarSplitLayout.Gap, content.height);
            bool hover = hit.Contains(ev.mousePosition);
            if (sidebarDragControl != 0 && (sidebarDragControl != control || GUIUtility.hotControl != control || !Find.WindowStack.GetsInput(this))) ReleaseSidebarDrag();
            if (ev.type == EventType.MouseDown && ev.button == 0 && hover && GUIUtility.hotControl == 0 && Find.WindowStack.GetsInput(this))
            {
                sidebarDragOffset = ev.mousePosition.x - center;
                sidebarDragControl = control; GUIUtility.hotControl = control;
                UnityEngine.GUI.FocusControl(null); ev.Use();
            }
            if (sidebarDragControl == control && GUIUtility.hotControl == control)
            {
                if (ev.type == EventType.MouseDrag && ev.button == 0)
                {
                    float desired = content.xMax - ev.mousePosition.x + sidebarDragOffset - SidebarSplitLayout.Gap / 2;
                    float ratio = SidebarSplitLayout.RatioForWidth(content.width, minimum, desired);
                    Instance.Settings.ServerSidebarRatio = ratio;
                    SidebarSplitLayout.TryWidth(content.width, width, minimum, ratio, out width);
                    center = content.xMax - width - SidebarSplitLayout.Gap / 2;
                    ev.Use();
                }
                else if (ev.rawType == EventType.MouseUp && ev.button == 0) { ReleaseSidebarDrag(); ev.Use(); }
            }
            bool active = (hover || sidebarDragControl == control) && Find.WindowStack.GetsInput(this) && Application.isFocused;
            sidebarCursor.Update(active);
            var theme = HostThemeDrawing.Current;
            if (active) ClientWidgets.RoundedSurface(new Rect(center - SidebarSplitLayout.Gap / 2, content.y, SidebarSplitLayout.Gap, content.height), theme.HoverHighlight.ToUnityColor(), 3);
            float gripWidth = active ? 4 : 2, gripHeight = active ? 64 : 48;
            var grip = new Rect(center - gripWidth / 2, content.y + Mathf.Max(0, (content.height - gripHeight) / 2), gripWidth, Mathf.Min(gripHeight, content.height));
            ClientWidgets.RoundedSurface(grip, active ? ClientWidgets.Role(theme, "ui.accent", "ui.input.focus") : theme.Separator.ToUnityColor(), 1);
            TooltipHandler.TipRegion(hit, L("resizeSidebar"));
            return width;
        }

        private void DrawSelfIdentity(Rect rect)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            var theme = HostThemeDrawing.Current;
            ClientWidgets.RoundedSurface(rect, theme.Surface.ToUnityColor(), 8);
            ClientWidgets.RoundedBorder(rect, theme.Separator.ToUnityColor(), 8);
            ClientWidgets.Fill(new Rect(rect.x + 4, rect.y + 8, 2, Mathf.Max(0, rect.height - 16)), ClientWidgets.Role(theme, "ui.accent", "ui.input.focus"));
            var facts = Instance.CaptureNicknameFacts();
            var size = ClientWidgets.MeasureButton("Phinix_nickname_edit".Translate(), Mathf.Max(1, rect.width * .4f), minimumWidth: 56);
            float buttonWidth = size.x;
            var name = new Rect(rect.x + 12, rect.y, Mathf.Max(0, rect.width - buttonWidth - 24), rect.height);
            using (new ClientGuiScope(true))
            {
                Text.Font = GameFont.Small; Text.WordWrap = false;
                string prefix = "Phinix_nickname_self".Translate();
                if (identityMarkup == null || identityRaw != facts.DisplayName || identityPrefix != prefix || identityThemeVersion != theme.Version)
                {
                    identityRaw = facts.DisplayName; identityPrefix = prefix; identityThemeVersion = theme.Version;
                    identityMarkup = NicknameDisplayMarkup.Identity(prefix, facts.DisplayName, theme.PrimaryText, theme.GetColor("ui.accent"));
                }
                // White is a neutral GUI tint: explicit nested name colors must not be multiplied by the theme text color.
                ClientWidgets.ClippedLabel(name, identityMarkup, Color.white, TextAnchor.MiddleLeft);
                TooltipHandler.TipRegion(name, (facts.Online ? "Phinix_nickname_online" : "Phinix_nickname_offline").Translate() + "\n" + Utils.TextHelper.StripRichText(facts.DisplayName));
            }
            if (ClientWidgets.Button(new Rect(rect.xMax - buttonWidth - 4, rect.y + Mathf.Min(4, rect.height / 2), buttonWidth, Mathf.Max(0, rect.height - 8)),
                "Phinix_nickname_edit".Translate(), HostThemeDrawing.Current, enabled: Instance.CanEditNickname, wrap: true)) Instance.OpenNicknameEditor();
        }

        private void OpenUtilities()
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                new FloatMenuOption("Phinix_nickname_title".Translate(), () => { if (IsOpen) Instance.OpenNicknameEditor(); }),
                new FloatMenuOption(L("connection"), () => { if (IsOpen) Find.WindowStack.Add(new SettingsWindow()); }),
                new FloatMenuOption(L("fullSettings"), () => { if (IsOpen) Find.WindowStack.Add(new FullClientSettingsWindow()); }),
                new FloatMenuOption(L("themes"), () => { if (IsOpen) Instance.OpenThemeEditor(); }),
                new FloatMenuOption(L("plugins"), () => { if (IsOpen) Instance.OpenPluginManager(); })
            }));
        }
        private void ClampWindowToScreenSafeArea()
        {
            windowRect = UiScreenSafeArea.ClampWindow(
                windowRect,
                new Vector2(MIN_WINDOW_WIDTH, MIN_WINDOW_HEIGHT));
        }

        private static Rect GetScreenSafeRect()
        {
            return UiScreenSafeArea.Current;
        }

        private static float ClampLength(float value, float minimum, float maximum)
        {
            float nonNegativeMaximum = Mathf.Max(0f, maximum);
            float effectiveMinimum = Mathf.Min(Mathf.Max(0f, minimum), nonNegativeMaximum);
            return Mathf.Clamp(value, effectiveMinimum, nonNegativeMaximum);
        }
    }
}
