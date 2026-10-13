using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;
namespace Phinix.TradeExtension.Client
{
    public class TradeMainTabProvider : IMainTabProvider,IResponsiveMainTabProvider,IClientNavigationMetadata,IClientViewLifecycle,IDisposable
    {
        private static readonly UiLayoutHints Hints=new UiLayoutHints(new Vector2(360,260),new Vector2(760,540),true);
        private readonly TradeList list;
        public TradeMainTabProvider(ITradeUiHostContext context){list=new TradeList(context);}
        internal void Start(IPhinixExtensionModule owner,IClientPresentationService renderer,IClientExtensionPointService points,IClientTheme theme,ITradeOfferOperationApi operations,ITradeInventoryRecoveryApi recovery)=>list.Start(owner,renderer,points,theme,operations,recovery);
        public string TabLabel=>"Phinix_tabs_trades".Translate();public float TabOrder=>1;
        public string NavigationId=>"trades";public UiLayoutHints LayoutHints=>Hints;
        public void ViewOpened()=>list.ViewOpened();public void ViewClosed()=>list.ViewClosed();public void ViewUpdate()=>list.ViewUpdate();
        public void Draw(Rect rect)=>list.Draw(rect);public void Dispose()=>list.Dispose();
    }
}
