using PhinixClient;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public class TalentTradeTab : IMainTabProvider, IResponsiveMainTabProvider
    {
        public static readonly TalentTradeTab Instance = new TalentTradeTab();
        private static readonly UiLayoutHints Hints = new UiLayoutHints(new Vector2(320f, 260f), new Vector2(900f, 600f), true);
        public string TabLabel => "Phinix_legacyTalentTrade_tab".Translate();
        public float TabOrder => 1200f;
        public UiLayoutHints LayoutHints => Hints;
        private readonly TalentTabs tabs = new TalentTabs(
            "Phinix_legacyTalentTrade_subTabDirectTrade", "Phinix_legacyTalentTrade_subTabMarket", "Phinix_legacyTalentTrade_subTabRental") { Selected = 1 };
        private readonly MarketPanel marketPanel = new MarketPanel();
        private readonly RentalPanel rentalPanel = new RentalPanel();
        private readonly DirectTradePanel directTradePanel = new DirectTradePanel();

        internal void BindUserEvents(PhinixClient.Framework.IClientUserEventStream events)
        {
            directTradePanel.BindUserEvents(events);
        }

        public void Draw(Rect inRect)
        {
            if (inRect.width <= 0f || inRect.height <= 0f) return;
            if (!LegacyTalentTradeRuntime.IsOnline)
            {
                Widgets.DrawMenuSection(inRect);
                Widgets.NoneLabelCenteredVertically(inRect, "Phinix_legacyTalentTrade_pleaseLogIn".Translate());
                return;
            }
            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = true;
            try
            {
                Rect content = TalentTradeUi.Inset(tabs.Draw(inRect), 4f);
                if (content.width <= 0f || content.height <= 0f) return;
                switch (tabs.Selected)
                {
                    case 0: directTradePanel.Draw(content); break;
                    case 1: marketPanel.Draw(content); break;
                    case 2: rentalPanel.Draw(content); break;
                }
            }
            finally { Text.Font = font; Text.WordWrap = wrap; }
        }
    }
}
