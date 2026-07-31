using PhinixClient;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public class TalentTradeTab : PhinixClient.IMainTabProvider
    {
        public static readonly TalentTradeTab Instance = new TalentTradeTab();

        public string TabLabel => "Phinix_legacyTalentTrade_tab".Translate();

        public float TabOrder => 1200f;

        private const float SUB_TAB_HEIGHT = 30f;
        private const float SPACING = 6f;

        private enum SubTab
        {
            DirectTrade,
            Market,
            Rental
        }

        private SubTab activeSubTab = SubTab.Market;
        private readonly MarketPanel marketPanel = new MarketPanel();
        private readonly RentalPanel rentalPanel = new RentalPanel();
        private readonly DirectTradePanel directTradePanel = new DirectTradePanel();

        public void Draw(Rect inRect)
        {
            if (!LegacyTalentTradeRuntime.IsOnline)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_legacyTalentTrade_pleaseLogIn".Translate());
                return;
            }

            // Sub-tab bar (drawn above the content box)
            Rect tabBarRect = new Rect(inRect.x, inRect.y + 4f, inRect.width, SUB_TAB_HEIGHT);
            DrawSubTabs(tabBarRect);

            // Content area (below sub-tabs, with its own box)
            float contentY = inRect.y + SUB_TAB_HEIGHT + SPACING;
            Rect contentRect = new Rect(inRect.x, contentY, inRect.width, inRect.height - SUB_TAB_HEIGHT - SPACING);
            Widgets.DrawMenuSection(contentRect);
            Rect innerContent = contentRect.ContractedBy(4f);

            switch (activeSubTab)
            {
                case SubTab.DirectTrade:
                    DrawDirectTradePanel(innerContent);
                    break;
                case SubTab.Market:
                    DrawMarketPanel(innerContent);
                    break;
                case SubTab.Rental:
                    DrawRentalPanel(innerContent);
                    break;
            }
        }

        private void DrawSubTabs(Rect rect)
        {
            float tabWidth = (rect.width - 40f) / 3f;
            float totalWidth = tabWidth * 3f;
            float startX = rect.x + (rect.width - totalWidth) / 2f;

            Rect tab1 = new Rect(startX, rect.y, tabWidth, rect.height);
            Rect tab2 = new Rect(startX + tabWidth, rect.y, tabWidth, rect.height);
            Rect tab3 = new Rect(startX + tabWidth * 2f, rect.y, tabWidth, rect.height);

            if (DrawSubTabButton(tab1, "Phinix_legacyTalentTrade_subTabDirectTrade".Translate(), activeSubTab == SubTab.DirectTrade))
            {
                activeSubTab = SubTab.DirectTrade;
            }
            if (DrawSubTabButton(tab2, "Phinix_legacyTalentTrade_subTabMarket".Translate(), activeSubTab == SubTab.Market))
            {
                activeSubTab = SubTab.Market;
            }
            if (DrawSubTabButton(tab3, "Phinix_legacyTalentTrade_subTabRental".Translate(), activeSubTab == SubTab.Rental))
            {
                activeSubTab = SubTab.Rental;
            }
        }

        private bool DrawSubTabButton(Rect rect, string label, bool active)
        {
            if (active)
            {
                Widgets.DrawHighlight(rect);
            }

            bool clicked = Widgets.ButtonText(rect, label, drawBackground: !active, doMouseoverSound: true, active: !active);
            return clicked;
        }

        private void DrawDirectTradePanel(Rect rect)
        {
            directTradePanel.Draw(rect);
        }

        private void DrawMarketPanel(Rect rect)
        {
            marketPanel.Draw(rect);
        }

        private void DrawRentalPanel(Rect rect)
        {
            rentalPanel.Draw(rect);
        }
    }
}
