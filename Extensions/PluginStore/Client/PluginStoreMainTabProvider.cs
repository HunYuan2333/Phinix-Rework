using PhinixClient;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class PluginStoreMainTabProvider : IMainTabProvider, IResponsiveMainTabProvider
    {
        private static readonly UiLayoutHints Hints = new UiLayoutHints(
            new Vector2(320f, 260f), new Vector2(820f, 580f), true);
        private ManagedPluginStoreView view;

        public string TabLabel => "Phinix_store_tab".Translate();
        public float TabOrder => 999f;
        public UiLayoutHints LayoutHints => Hints;

        public void Initialize(ManagedPluginStoreView view) { this.view = view; }
        public void Stop() { view = null; }

        public void Draw(Rect inRect)
        {
            view?.Draw(inRect);
        }
    }
}
