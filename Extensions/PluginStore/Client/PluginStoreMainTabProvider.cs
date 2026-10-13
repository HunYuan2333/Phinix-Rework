using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class PluginStoreMainTabProvider : IMainTabProvider, IResponsiveMainTabProvider, IClientViewLifecycle, IClientNavigationMetadata, System.IDisposable
    {
        public PluginStoreMainTabProvider() { }
        public void Dispose() { Stop(); }

        private static readonly UiLayoutHints Hints = new UiLayoutHints(
            new Vector2(320f, 260f), new Vector2(820f, 580f), true);
        private ManagedPluginStoreView view;

        public string TabLabel => "Phinix_store_tab".Translate();
        public float TabOrder => 999f;
        public UiLayoutHints LayoutHints => Hints;

        public void Initialize(ManagedPluginStoreView view) { this.view = view; }
        public void Stop() { view?.Stop(); view = null; }
        public string NavigationId => "store";
        public void ViewOpened() { view?.OpenPresentation(); }
        public void ViewClosed() { view?.ClosePresentation(); }
        public void ViewUpdate() { view?.UpdatePresentation(); }

        public void Draw(Rect inRect)
        {
            view?.Draw(inRect);
        }
    }
}
