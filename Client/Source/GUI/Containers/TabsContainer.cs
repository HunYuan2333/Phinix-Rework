// Original file provided by Longwelwind (https://github.com/Longwelwind)
// as a part of the RimWorld mod Phi (https://github.com/Longwelwind/Phi)

using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PhinixClient.GUI
{
    [System.Obsolete("Use TabDrawer overflow navigation and direct content drawing instead.")]
    public class TabsContainer : Displayable
    {
        /// <inheritdoc />
        public override bool IsFluidHeight => false;

        /// <summary>
        /// Collection of tabs that will be drawn.
        /// </summary>
        private List<TabContainerEntry> tabs;
        private List<TabRecord> tabRecords;

        /// <summary>
        /// Callback invoked when a different tab is selected.
        /// </summary>
        private Action<int> onTabChange;

        /// <summary>
        /// Index of the currently-selected tab.
        /// </summary>
        private int selectedTab;
        private float cachedTabWidth = -1f;
        private float cachedTabHeight;
        private object cachedLanguage;

        public TabsContainer(Action<int> onTabChange = null, int selectedTab = 0)
        {
            this.onTabChange = onTabChange;
            this.selectedTab = selectedTab;

            this.tabs = new List<TabContainerEntry>();
            this.tabRecords = new List<TabRecord>();
        }

        /// <summary>
        /// Adds a tab to the container.
        /// </summary>
        /// <param name="label">Label shown on the tab itself</param>
        /// <param name="displayable">Contents of the tab</param>
        public void AddTab(string label, Displayable displayable)
        {
            // Set the current index to where this new tab will be
            int index = tabs.Count;

            // Create a tab record
            TabRecord tab = new TabRecord(
                label: label,
                clickedAction: () => { selectedTab = index; onTabChange?.Invoke(index); },
                selected: () => selectedTab == index
            );

            // Add the tab to the tab list
            tabs.Add(new TabContainerEntry { tab = tab, displayable = displayable });
            tabRecords.Add(tab);
            cachedTabWidth = -1f;
        }

        /// <inheritdoc />
        public override void Draw(Rect inRect)
        {
            // Do nothing if there's no tabs
            if (tabs.Count == 0) return;

            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);
            object language = LanguageDatabase.activeLanguage;
            if (!ReferenceEquals(cachedLanguage, language))
            {
                cachedLanguage = language;
                cachedTabWidth = -1f;
            }

            float tabHeight = GetTabHeight(inRect.width);
            tabHeight = Mathf.Min(tabHeight, inRect.height);
            Rect contentRect = new Rect(inRect.xMin, inRect.yMin + tabHeight, inRect.width, Mathf.Max(0f, inRect.height - tabHeight));
            if (inRect.width > 0f)
            {
                TabDrawer.DrawTabsOverflow(inRect, tabRecords, 80f, 200f);
            }

            // We draw the selected tab
            selectedTab = Mathf.Clamp(selectedTab, 0, tabs.Count - 1);
            Displayable selectedDisplayable = tabs[selectedTab].displayable;
            selectedDisplayable.Draw(contentRect);
        }

        /// <inheritdoc />
        public override void Update()
        {
            foreach (TabContainerEntry tab in tabs)
            {
                tab.displayable.Update();
            }
        }

        /// <inheritdoc />
        public override float CalcHeight(float width)
        {
            return GetTabHeight(Mathf.Max(0f, width));
        }

        private float GetTabHeight(float width)
        {
            if (width <= 0f || tabRecords.Count == 0)
            {
                return 0f;
            }
            if (!Mathf.Approximately(cachedTabWidth, width))
            {
                cachedTabWidth = width;
                cachedTabHeight = TabDrawer.GetOverflowTabHeight(new Rect(0f, 0f, width, 0f), tabRecords, 80f, 200f);
            }
            return Mathf.Max(0f, cachedTabHeight);
        }
    }
}
