using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class PluginStoreUpdateBanner : INoticeBannerProvider, System.IDisposable
    {
        public PluginStoreUpdateBanner() { }
        public void Dispose() { Stop(); }

        private ManagedStoreController controller;
        private Action open;
        private bool dismissed;
        internal void Initialize(ManagedStoreController controller,Action open)
        { this.controller=controller; this.open=open; dismissed=false; }
        internal void Stop() { controller=null; open=null; dismissed=false; }
        public float CurrentHeight => !dismissed && controller?.Updates.Count>0?32:0;
        public void Draw(Rect rect)
        {
            if(CurrentHeight==0 || rect.width<=0 || rect.height<=0) return;
            var font=Text.Font; var anchor=Text.Anchor; bool wrap=Text.WordWrap,enabled=GUI.enabled; var color=GUI.color;
            try
            {
                Text.Font=GameFont.Small; Text.Anchor=TextAnchor.MiddleLeft; Text.WordWrap=false; GUI.color=Color.white;
                string message=string.Format("Phinix_store2_updateNotice".Translate(),controller.Updates.Count);
                float close=Mathf.Min(32,rect.width);
                var body=new Rect(rect.x,rect.y,Mathf.Max(0,rect.width-close-4),Mathf.Min(32,rect.height));
                Widgets.DrawBoxSolid(body,new Color(.24f,.28f,.17f)); Widgets.Label(body,message); TooltipHandler.TipRegion(body,message);
                if(Widgets.ButtonInvisible(body)) open?.Invoke();
                if(Widgets.ButtonText(new Rect(rect.xMax-close,rect.y,close,Mathf.Min(32,rect.height)),"×")) dismissed=true;
            }
            finally { Text.Font=font; Text.Anchor=anchor; Text.WordWrap=wrap; GUI.color=color; GUI.enabled=enabled; }
        }
    }
}
