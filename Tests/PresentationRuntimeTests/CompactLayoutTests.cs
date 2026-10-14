using System;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
internal static partial class Program
{
    private static void CheckCompactPresentationLayout()
    {
        var rows=Enumerable.Range(0,40).Select(i=>new ClientPresentationRow("resource."+i,"resource."+i,"Available 50 · Map").WithControls(null,ClientPresentationTextField.Integer("units."+i,"","0"))).ToArray();
        var regular=new ClientPresentationList("resources",rows,"");var compact=regular.WithCompactRows();
        Check(compact.WithColors("plugin.hover",null).WithReadOnly().CompactRows&&!regular.CompactRows,"Density is opt-in and survives theme/read-only copies without changing other plugins");
        var service=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});var session=service.Open(new Module(),"density");
        Func<long,ClientPresentationList,ClientPresentationPage> page=(revision,list)=>new ClientPresentationPage("density","",revision,new[]{new ClientPresentationSection("items","","",list:list).WithPlainLayout()});
        Event.current.type=EventType.Repaint;Widgets.FieldValue=null;
        session.Publish(page(1,regular),null,()=>true);Widgets.Labels.Clear();session.Draw(new ClientPresentationBounds(0,0,1000,500));
        int normal=Widgets.Labels.Count(l=>l.Text.StartsWith("resource."));
        session.Publish(page(2,compact),null,()=>true);Widgets.Labels.Clear();session.Draw(new ClientPresentationBounds(0,0,1000,500));
        int dense=Widgets.Labels.Count(l=>l.Text.StartsWith("resource."));
        Check(dense>=normal+3,"Same viewport presents more editable resources with compact layout: "+normal+" -> "+dense);
        Check(Widgets.Labels.Any(l=>l.Text.Contains("Map")),"Compact rows retain source and availability facts");
        var toolbar=new ClientPresentationSection("toolbar","Trade", "",fields:new[]{new ClientPresentationTextField("search","","",placeholder:"Search")}).WithInlineToolbar().WithPlainLayout();
        Check(toolbar.WithWrappedActions().WithFieldColumns().InlineToolbar,"Toolbar hint survives all ordinary section copies");
        session.Publish(new ClientPresentationPage("density","",3,new[]{toolbar}),null,()=>true);Widgets.Labels.Clear();session.Draw(new ClientPresentationBounds(0,0,1000,500));
        var title=Widgets.Labels.Single(l=>l.Text=="<b>Trade</b>");
        Check(Math.Abs(title.Rect.y-Widgets.LastFieldRect.y)<1,"Toolbar search occupies title row instead of a separate block");
        Text.MeasureWrapping=true;
        var longRow=new ClientPresentationRow("long",new string('N',120),new string('D',100)).WithControls(null,ClientPresentationTextField.Integer("invalid","","999","Out of range: enter 0 to 50"));
        session.Publish(page(4,new ClientPresentationList("resources",new[]{longRow},"").WithCompactRows()),null,()=>true);
        foreach(int width in new[]{1000,360}){Widgets.Labels.Clear();session.Draw(new ClientPresentationBounds(0,0,width,1200));Check(session.FailureCode==null&&Widgets.Labels.Any(l=>l.Text.Contains("Out of range")),"Long labels and invalid quantity remain visible at width "+width);}
        Text.MeasureWrapping=false;session.Dispose();
    }
}
