using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using static PhinixClient.Client;

namespace PhinixClient
{
    public class ServerTab : MainTabWindow
    {
        private const float DEFAULT_SPACING = 10f;

        public override Vector2 InitialSize => new Vector2(1000f, 680f);

        private readonly List<IMainTabProvider> tabProviders;
        private readonly IServerSidebarProvider sidebarProvider;
        private readonly List<TabRecord> tabList = new List<TabRecord>();
        private int activeTab = 0;

        public ServerTab()
        {
            this.closeOnAccept = false;

            tabProviders = Instance.MainTabProviders
                .OrderBy(p => p.TabOrder)
                .ToList();
            sidebarProvider = Instance.SidebarProviders
                .OrderBy(p => p.Order)
                .FirstOrDefault();

            for (int i = 0; i < tabProviders.Count; i++)
            {
                int index = i;
                tabList.Add(new TabRecord(tabProviders[i].TabLabel, () => activeTab = index, () => activeTab == index));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect usableRect = inRect.BottomPartPixels(inRect.height - TabDrawer.TabHeight);
            Rect mainRect = usableRect;
            Rect rightColumnRect = default;
            if (sidebarProvider != null)
            {
                rightColumnRect = usableRect.RightPartPixels(sidebarProvider.PreferredWidth);
                mainRect = usableRect.LeftPartPixels(usableRect.width - (rightColumnRect.width + DEFAULT_SPACING));
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

            if (sidebarProvider != null)
            {
                sidebarProvider.Draw(rightColumnRect);
            }

            // Prevent Enter/Esc from bubbling up to RimWorld's Window layer
            // and closing this MainTabWindow. Tab content (e.g. Chat) handles
            // these keys before this point if needed.
            // Event.current.Use() sets type=Used but leaves keyCode intact.
            // RimWorld's Window layer checks keyCode directly, so we must
            // clear it to prevent OnAcceptKeyPressed/OnCancelKeyPressed.
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
