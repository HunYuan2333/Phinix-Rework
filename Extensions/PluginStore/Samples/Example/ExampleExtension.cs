using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.Example.Basic
{
    [PhinixExtension("phinix.example.basic")]
    public sealed class ExampleExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        private readonly ExampleState state = new ExampleState();
        private readonly ExampleTab tab;
        private readonly ExampleSettingsPanel panel;
        public ExampleExtension() { tab = new ExampleTab(state); panel = new ExampleSettingsPanel(state); }
        public string ExtensionId => "phinix.example.basic";
        public void Register(IExtensionBuilder builder)
        {
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.RegisterApi<IClientSettingsPanelProvider>(panel);
        }
        public void Activate(ExtensionHostContext hostContext)
        {
            state.Start(hostContext.GetRequiredService<IClientSettingsContext>(),
                hostContext.GetRequiredService<IClientLocalizationService>().ForModule(this), hostContext.Log);
        }
        public void Shutdown(ExtensionHostContext hostContext) { state.Stop(); }
    }

    // One state shared by the tab and settings provider, with package-prefixed settings keys.
    // The host calls lifecycle/UI methods on the main thread. No game/map/item API is used.
    internal sealed class ExampleState
    {
        private IClientSettingsContext settings;
        private IClientLocalizer localizer;
        private Action<string, LogLevel> log;
        private long generation;
        internal bool Active => settings != null;
        internal long Generation => generation;
        internal int Count => Math.Max(0, settings?.Get("phinix.example.basic.clicks", 0) ?? 0);
        internal bool ShowHints => settings?.Get("phinix.example.basic.showHints", true) ?? true;
        internal string Text(string key) { return localizer?.Text(key) ?? key; }
        internal string CountText => localizer?.Format("count", Count) ?? Count.ToString();
        internal void Start(IClientSettingsContext context, IClientLocalizer localization, Action<string, LogLevel> sink)
        {
            Stop(); settings = context; localizer = localization; log = sink;
            localizer.LanguageChanged += LanguageChanged;
            log?.Invoke("Example: activated; tab and settings registered through public extension APIs.", LogLevel.INFO);
        }
        private void LanguageChanged() { log?.Invoke("Example: language changed; locale=" + localizer?.Locale, LogLevel.INFO); }
        internal void Stop()
        {
            generation++;
            if (localizer != null) { localizer.LanguageChanged -= LanguageChanged; localizer.Dispose(); localizer = null; }
            if (Active) log?.Invoke("Example: shutdown; localization subscription and callbacks cleared.", LogLevel.INFO);
            settings = null; log = null;
        }
        internal void Click()
        {
            if (!Active) return;
            int count = Count;
            if (count < int.MaxValue) settings.Set("phinix.example.basic.clicks", count + 1);
            log?.Invoke("Example: click count=" + Count + ".", LogLevel.INFO);
        }
        internal void SetHints(bool value)
        {
            if (!Active || value == ShowHints) return;
            settings.Set("phinix.example.basic.showHints", value);
            log?.Invoke("Example: show hints=" + value + ".", LogLevel.INFO);
        }
        internal void ConfirmReset()
        {
            if (!Active) return;
            long ticket = generation;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(Text("confirmReset"), () =>
            {
                // A confirmation from a previous activation cannot change retained settings.
                if (!Active || ticket != generation) return;
                settings.Set("phinix.example.basic.clicks", 0);
                log?.Invoke("Example: click counter reset.", LogLevel.INFO);
            }));
        }
    }

    internal sealed class ExampleTab : IMainTabProvider, IResponsiveMainTabProvider
    {
        private readonly ExampleState state;
        private static readonly UiLayoutHints Hints = new UiLayoutHints(new Vector2(320f, 180f), new Vector2(820f, 320f), true);
        internal ExampleTab(ExampleState state) { this.state = state; }
        public string TabLabel => state.Text("tab");
        public float TabOrder => 998f;
        public UiLayoutHints LayoutHints => Hints;
        public void Draw(Rect inRect)
        {
            if (!state.Active || inRect.width <= 0 || inRect.height <= 0) return;
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor; bool wrap = Text.WordWrap, enabled = GUI.enabled; Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                float y = inRect.y;
                if (state.ShowHints)
                {
                    float height = Math.Min(Text.CalcHeight(state.Text("intro"), inRect.width), Math.Max(0, inRect.height - 72));
                    Widgets.Label(new Rect(inRect.x, y, inRect.width, height), state.Text("intro")); y += height + 8;
                }
                if (inRect.yMax - y >= 30)
                {
                    if (Widgets.ButtonText(new Rect(inRect.x, y, inRect.width, 30), state.Text("click") + " · " + state.CountText)) state.Click();
                    y += 36;
                }
                if (inRect.yMax - y >= 30)
                {
                    if (Widgets.ButtonText(new Rect(inRect.x, y, inRect.width, 30), state.Text("reset"))) state.ConfirmReset();
                    y += 36;
                }
                if (state.ShowHints && inRect.yMax > y) Widgets.Label(new Rect(inRect.x, y, inRect.width, inRect.yMax - y), state.Text("persistence"));
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; GUI.enabled = enabled; }
        }
    }

    internal sealed class ExampleSettingsPanel : IClientSettingsPanelProvider
    {
        private readonly ExampleState state;
        internal ExampleSettingsPanel(ExampleState state) { this.state = state; }
        public string SectionId => "phinix.example.basic.settings";
        public float Order => 190f;
        public bool IsVisible(IClientSettingsContext settings) => state.Active;
        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            if (!state.Active) return;
            GameFont font = Text.Font; TextAnchor anchor = Text.Anchor; bool wrap = Text.WordWrap, enabled = GUI.enabled; Color color = GUI.color;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
                listing.Label(state.Text("settingsTitle"));
                bool hints = state.ShowHints;
                Rect rect = listing.GetRect(Math.Max(30, Text.CalcHeight(state.Text("showHints"), Math.Max(1, listing.ColumnWidth - 36))));
                Widgets.CheckboxLabeled(rect, state.Text("showHints"), ref hints);
                state.SetHints(hints);
                listing.Label(state.CountText);
                if (listing.ButtonText(state.Text("reset"))) state.ConfirmReset();
            }
            finally { Text.Font = font; Text.Anchor = anchor; Text.WordWrap = wrap; GUI.color = color; GUI.enabled = enabled; }
        }
    }
}
