using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.GUI;
using UnityEngine;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public class TradeMainTabProvider : IMainTabProvider
    {
        private readonly ITradeUiHostContext hostContext;
        private readonly TradeList tradeList;

        public string TabLabel => "Phinix_tabs_trades".Translate();
        public float TabOrder => 1;

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
