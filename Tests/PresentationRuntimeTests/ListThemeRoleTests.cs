using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
internal static partial class Program
{
    private static void CheckListThemeRoles()
    {
        var service=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});
        using(var session=service.Open(new Module(),"colors"))
        {
            var list=new ClientPresentationList("items",new[]{new ClientPresentationRow("one","One")},"","one").WithColors("plugin.hover","plugin.selected");
            session.Publish(new ClientPresentationPage("colors","",1,new[]{new ClientPresentationSection("items","","",list:list)}),null,()=>true);
            GUI.SurfaceColors.Clear();Event.current.type=EventType.Repaint;session.Draw(new Rect(0,0,600,300));
            Check(GUI.SurfaceColors.Any(c=>c.r==.123f)&&GUI.SurfaceColors.Any(c=>c.r==.456f),"Actual Host renderer consumes owner list hover and selection roles");
            session.Publish(new ClientPresentationPage("colors","",2,new[]{new ClientPresentationSection("items","","",list:list.WithReadOnly())}),null,()=>true);
            GUI.SurfaceColors.Clear();session.Draw(new Rect(0,0,600,300));
            Check(!GUI.SurfaceColors.Any(c=>c.r==.123f||c.r==.456f),"Readonly list does not display an interactive selection despite custom colors");
            Event.current.type=EventType.Layout;
        }
    }
}
