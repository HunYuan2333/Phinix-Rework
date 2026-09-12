using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.GUI;
using UnityEngine;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public class TradeMainTabProvider : IMainTabProvider, IResponsiveMainTabProvider
    {
        private static readonly UiLayoutHints CachedLayoutHints = new UiLayoutHints(
            new Vector2(360f, 260f),
            new Vector2(760f, 540f),
            true);
        private readonly ITradeUiHostContext hostContext;
        private readonly TradeList tradeList;

        public string TabLabel => "Phinix_tabs_trades".Translate();
        public float TabOrder => 1;
        public UiLayoutHints LayoutHints => CachedLayoutHints;

        public TradeMainTabProvider(ITradeUiHostContext hostContext)
        {
            this.hostContext = hostContext;
            tradeList = new TradeList(hostContext);
        }

        public void Draw(Rect inRect)
        {
            tradeList.Draw(inRect);
        }
    }
}
