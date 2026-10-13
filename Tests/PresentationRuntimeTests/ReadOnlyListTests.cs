using PhinixClient.Framework;
using PhinixClient;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static void CheckReadOnlyList()
    {
        var original=new ClientPresentationList("facts",new[]{new ClientPresentationRow("one","Actual fact").WithIcon(Texture2D.whiteTexture).WithBadges(new[]{new ClientPresentationBadge("fact","Fact")})},"","one");
        var facts=original.WithReadOnly();
        Check(ReferenceEquals(facts.Rows[0].Icon,Texture2D.whiteTexture),"Row icons survive badge and read-only immutable copies");
        Check(!original.IsReadOnly&&facts.IsReadOnly&&facts.SelectedId==null,"Read-only list creates independent projection without stale selection");
        int resolutions=0;var borrowed=new Texture();
        var dynamicRow=new ClientPresentationRow("dynamic","Borrowed portrait").WithIconSource(()=>{resolutions++;return borrowed;}).WithBadges(new[]{new ClientPresentationBadge("source","Owner")});
        Check(dynamicRow.HasIcon&&dynamicRow.Icon==null&&dynamicRow.IconSource!=null,"Public row accepts borrowed base Texture without Texture2D conversion");
        Check(dynamicRow.WithIcon(Texture2D.whiteTexture).IconSource==null,"Explicit static icon replaces dynamic source");
        var dynamicService=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});
        using(var session=dynamicService.Open(new Module(),"dynamic"))
        {
            session.Publish(new ClientPresentationPage("dynamic","",1,new[]{new ClientPresentationSection("dynamic","","",list:new ClientPresentationList("dynamic",new[]{dynamicRow},"").WithReadOnly())}),null,()=>true);
            Check(resolutions==0,"Publication and measurement do not resolve borrowed textures");
            Event.current.type=EventType.Layout;session.Draw(new Rect(0,0,600,400));
            Check(resolutions==0,"Layout pass performs no portrait rendering");
            Event.current.type=EventType.Repaint;int before=Widgets.FittedIcons;session.Draw(new Rect(0,0,600,400));
            Check(resolutions==1&&Widgets.FittedIcons==before+1,"Visible repaint resolves and draws the borrowed texture exactly once");
        }
        Check(resolutions==1,"Session disposal never resolves or disposes owner texture");
        var known=new Texture2D();var fallback=new Texture2D();
        var quotes=Phinix.TradeExtension.Client.TradeQuoteRows.Build(new[]{
            new PhinixClient.Trade.TradeItemSnapshot("known",3),new PhinixClient.Trade.TradeItemSnapshot("missing",2),
            new PhinixClient.Trade.TradeItemSnapshot("MinifiedThing",1,innerItem:new PhinixClient.Trade.TradeItemSnapshot("known",1)),null
        },id=>id=="known"?"Known":null,key=>key,id=>id=="known"?known:fallback);
        Check(ReferenceEquals(quotes[0].Icon,known)&&ReferenceEquals(quotes[1].Icon,fallback)&&ReferenceEquals(quotes[2].Icon,known)&&ReferenceEquals(quotes[3].Icon,fallback),"Snapshot rows carry known, unknown and inner-item icons without materialization");
        var lifetime=new Lifetime();var service=new ClientPresentationService(lifetime,new Theme(),()=>true,_=>{});
        using(var session=service.Open(new Module(),"facts"))
        {
            session.Publish(new ClientPresentationPage("facts","",1,new[]{new ClientPresentationSection("facts","","",list:facts)}),null,()=>true);
            Event.current.type=EventType.MouseDown;Widgets.InvisibleClicks=1;
            int iconCount=Widgets.FittedIcons;session.Draw(new Rect(0,0,600,400));
            Check(Widgets.FittedIcons==iconCount+1,"The public renderer draws the icon of a read-only fact row");
            Check(Widgets.InvisibleClicks==1&&!session.TryDequeueInput(out var ignored),"Host read-only list consumes no selection hit target and emits no input");
            Widgets.InvisibleClicks=0;Event.current.type=EventType.Layout;
        }
    }
}
