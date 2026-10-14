using PhinixClient.Framework;
using PhinixClient;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static bool ImageTexture(IClientPresentationImage image,Texture expected)
    { using(var lease=image.Acquire()) return ((IClientTextureImageLease)lease).TryGetTexture(out var texture)&&ReferenceEquals(texture,expected); }
    private static void CheckReadOnlyList()
    {
        var staticImage=ClientTextureImage.FromTexture(Texture2D.whiteTexture);
        var original=new ClientPresentationList("facts",new[]{new ClientPresentationRow("one","Actual fact").WithImage(staticImage).WithBadges(new[]{new ClientPresentationBadge("fact","Fact")})},"","one");
        var facts=original.WithReadOnly();
        Check(ReferenceEquals(facts.Rows[0].Image,staticImage),"Row icons survive badge and read-only immutable copies");
        Check(!original.IsReadOnly&&facts.IsReadOnly&&facts.SelectedId==null,"Read-only list creates independent projection without stale selection");
        int resolutions=0;var borrowed=new Texture();
        var image=ClientTextureImage.FromSource(()=>{resolutions++;return borrowed;},()=>true,null,default(ClientPresentationImageSize));
        var dynamicRow=new ClientPresentationRow("dynamic","Borrowed portrait").WithImage(image).WithBadges(new[]{new ClientPresentationBadge("source","Owner")});
        Check(dynamicRow.HasIcon&&ReferenceEquals(dynamicRow.Image,image),"Public row accepts borrowed base Texture without Texture2D conversion");
        Check(ReferenceEquals(dynamicRow.WithImage(staticImage).Image,staticImage),"Explicit static icon replaces dynamic source");
        var dynamicService=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});
        using(var session=dynamicService.Open(new Module(),"dynamic"))
        {
            session.Publish(new ClientPresentationPage("dynamic","",1,new[]{new ClientPresentationSection("dynamic","","",list:new ClientPresentationList("dynamic",new[]{dynamicRow},"").WithReadOnly())}),null,()=>true);
            Check(resolutions==0,"Publication and measurement do not resolve borrowed textures");
            Event.current.type=EventType.Layout;session.Draw(new ClientPresentationBounds(0,0,600,400));
            Check(resolutions==0,"Layout pass performs no portrait rendering");
            Event.current.type=EventType.Repaint;int before=Widgets.FittedIcons;session.Draw(new ClientPresentationBounds(0,0,600,400));
            Check(resolutions==1&&Widgets.FittedIcons==before+1,"Visible repaint resolves and draws the borrowed texture exactly once");
        }
        Check(resolutions==1,"Session disposal never resolves or disposes owner texture");
        var known=new Texture2D();var fallback=new Texture2D();
        var quotes=Phinix.TradeExtension.Client.TradeQuoteRows.Build(new[]{
            new PhinixClient.Trade.TradeItemSnapshot("known",3),new PhinixClient.Trade.TradeItemSnapshot("missing",2),
            new PhinixClient.Trade.TradeItemSnapshot("MinifiedThing",1,innerItem:new PhinixClient.Trade.TradeItemSnapshot("known",1)),null
        },id=>id=="known"?"Known":null,key=>key,id=>id=="known"?known:fallback);
        Check(ImageTexture(quotes[0].Image,known)&&ImageTexture(quotes[1].Image,fallback)&&ImageTexture(quotes[2].Image,known)&&ImageTexture(quotes[3].Image,fallback),"Snapshot rows carry known, unknown and inner-item icons without materialization");
        var lifetime=new Lifetime();var service=new ClientPresentationService(lifetime,new Theme(),()=>true,_=>{});
        using(var session=service.Open(new Module(),"facts"))
        {
            session.Publish(new ClientPresentationPage("facts","",1,new[]{new ClientPresentationSection("facts","","",list:facts)}),null,()=>true);
            Event.current.type=EventType.MouseDown;Widgets.InvisibleClicks=1;
            int iconCount=Widgets.FittedIcons;session.Draw(new ClientPresentationBounds(0,0,600,400));
            Check(Widgets.FittedIcons==iconCount,"Mouse input pass does not draw or resolve a fact image");
            Check(Widgets.InvisibleClicks==1&&!session.TryDequeueInput(out var ignored),"Host read-only list consumes no selection hit target and emits no input");
            Widgets.InvisibleClicks=0;Event.current.type=EventType.Layout;
        }
    }
}
