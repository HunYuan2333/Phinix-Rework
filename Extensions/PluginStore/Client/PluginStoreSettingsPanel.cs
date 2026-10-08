using System;
using PhinixClient.Framework;
using Utils;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class PluginStoreSettingsPanel : IClientSettingsPanelProvider, System.IDisposable
    {
        public PluginStoreSettingsPanel() { }
        public void Dispose() { Stop(); }

        private IClientWindowService windows;
        private IClientMainThreadDispatcher dispatcher;
        private object language;
        private string openLabel;
        private PluginStoreWindow window;
        private Func<ManagedPluginStoreView> createView;

        public string SectionId => "plugin-store.browser";
        public float Order => 180f;
        public bool IsVisible(IClientSettingsContext settings) => createView != null;

        public void Initialize(IClientWindowService windows,IClientMainThreadDispatcher dispatcher,Func<ManagedPluginStoreView> createView)
        { this.windows=windows; this.dispatcher=dispatcher; this.createView=createView; }

        public void Stop()
        {
            PluginStoreWindow closing = window;
            IClientMainThreadDispatcher closingDispatcher=dispatcher;
            window = null;
            windows = null;
            dispatcher = null;
            createView=null;
            if (closing != null) closingDispatcher?.Enqueue(() => closing.Close());
        }

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            if (!ReferenceEquals(language, LanguageDatabase.activeLanguage) || openLabel == null)
            { language = LanguageDatabase.activeLanguage; openLabel = "Phinix_store_open".Translate(); }
            if (createView == null || !listing.ButtonText(openLabel)) return;
            Open();
        }
        internal void Open()
        {
            if(createView==null) return;
            if (window != null && window.IsOpen) return;
            window = new PluginStoreWindow(createView());
            windows.Open(window);
        }
    }
}
