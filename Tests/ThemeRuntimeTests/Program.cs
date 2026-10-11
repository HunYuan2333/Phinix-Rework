using System;
using System.IO;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
using Utils.Framework;

internal static class Program
{
    private static int checks;
    private static void Check(bool v,string message){checks++;if(!v)throw new Exception(message);}
    private static void Equal(float a,float b,string m)=>Check(Math.Abs(a-b)<.0001,m);
    private static void Reject(Action action,string m){bool failed=false;try{action();}catch(Exception){failed=true;}Check(failed,m);}
    public static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-themes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(root,"theme.xml"),settings=Path.Combine(root,"settings.xml"),user=Path.Combine(root,"user");
            File.WriteAllText(file,"<PhinixTheme version='1'><color key='primaryText' r='.2' g='.3' b='.4'/><color key='chat.sample' r='.7' g='.2' b='.1'/></PhinixTheme>");
            var input=ThemeInputAdapter.Read(file,"test", "local");Check(input.Colors.ContainsKey("ui.text.primary")&&!input.Colors.ContainsKey("primaryText"),"Aliases are normalized at input.");
            var sources=new List<ThemeSource>{new ThemeSource{Id="test",Path=file,Source="local"}};
            var service=new UiTheme(settings,user,()=>sources,_=>{});
            var registration=Register(service,"chat");registration.RegisterColor("chat.sample",new Color(.1f,.1f,.1f));registration.Commit();
            Check(service.SelectedThemeId=="builtin.default","New install does not auto-select arbitrary files.");
            var draft=service.Edit("test");draft.Colors["ui.text.primary"]=new Color(.8f,.8f,.8f);
            Equal(service.PrimaryText.r,1,"Editing has no global side effects.");
            service.Apply(draft);Equal(service.PrimaryText.r,.8f,"Apply uses draft.");Equal(service.GetColor("chat.sample").r,.7f,"Late registration receives theme values.");
            draft.Colors["ui.text.primary"]=new Color(.9f,.9f,.9f);Equal(service.PrimaryText.r,.8f,"Apply copies caller state.");
            var reloaded=new UiTheme(settings,user,()=>sources,_=>{});Equal(reloaded.PrimaryText.r,.8f,"Selection/overrides persist.");
            long version=service.Version;var broken=service.Edit("test");broken.Colors["ui.text.primary"]=new Color(float.NaN,0,0);Reject(()=>service.Apply(broken),"Nonfinite draft rejected.");Check(service.Version==version,"Rejected draft does not commit.");
            File.WriteAllText(file,"<PhinixTheme version='2'><color key='ui.text.primary' r='.9' g='.1' b='NaN' a='1'/></PhinixTheme>");service.Refresh();Equal(service.GetColor("chat.sample").r,.7f,"Invalid replacement keeps verified active snapshot.");Check(service.Diagnostics.Count>0,"Invalid theme has diagnostics.");
            File.Delete(file);service.Refresh();Equal(service.PrimaryText.r,1,"Removed selected theme falls back including overridden platform values.");Check(service.SelectedThemeId=="test","Selection intent retained.");
            registration.Dispose();Check(!service.Colors.ExistsKey("chat.sample"),"Scoped definitions revoked.");
            var owner=Register(service,"a");owner.RegisterColor("a.color",new Color(1,0,0));owner.Commit();using(var collision=Register(service,"b"))Reject(()=>collision.RegisterColor("a.color",new Color(0,1,0)),"Duplicate owner cannot replace default.");owner.Dispose();owner.Dispose();
            var eventService=new UiTheme(Path.Combine(root,"new-settings.xml"),user,()=>new ThemeSource[0],_=>{});int callbacks=0;eventService.Changed+=()=>throw new Exception("observer");eventService.Changed+=()=>callbacks++;eventService.Apply(eventService.Edit("builtin.default"));Check(callbacks==1,"Observer exception isolated.");
            string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);var fail=new UiTheme(blocked,user,()=>new ThemeSource[0],_=>{});var edit=fail.Edit("builtin.default");edit.Colors["ui.text.primary"]=new Color(.4f,.4f,.4f);Reject(()=>fail.Apply(edit),"Failed persistence rejects commit.");Equal(fail.PrimaryText.r,1,"Persistence failure leaves valid snapshot.");
            string malicious=Path.Combine(root,"evil.xml");File.WriteAllText(malicious,"<!DOCTYPE PhinixTheme [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><PhinixTheme version='1'>&x;</PhinixTheme>");Reject(()=>ThemeInputAdapter.Read(malicious,"evil","local"),"External entities rejected.");
            File.WriteAllText(malicious,"<PhinixTheme version='2'><color key='x' r='1' g='0' b='0' a='1'/><color key='x' r='0' g='0' b='0' a='1'/></PhinixTheme>");Reject(()=>ThemeInputAdapter.Read(malicious,"evil","local"),"Duplicate values rejected atomically.");
            string migration=Path.Combine(root,"prior.xml");File.WriteAllText(migration,"<PhinixTheme version='1'><color key='primaryText' r='.3' g='.3' b='.3'/></PhinixTheme>");var migrated=new UiTheme(Path.Combine(root,"migration-settings.xml"),Path.Combine(root,"migrated"),()=>new[]{new ThemeSource{Id="prior",Path=migration,Source="user",Migrate=true}},_=>{});Equal(migrated.PrimaryText.r,.3f,"Old composition imported as regular theme.");Check(migrated.SelectedThemeId=="user/migrated.xml","No compatibility mode in selector.");
            GUI.color=new Color(.25f,.5f,.75f);GUI.enabled=false;Text.Font=GameFont.Tiny;Text.WordWrap=false;
            using(new ClientGuiScope(true)){GUI.color=Color.white;using(new ClientGuiScope(true)){GUI.color=Color.clear;}Equal(GUI.color.r,1,"Nested scope returns to immediate caller.");}
            Equal(GUI.color.r,.25f,"Outer scope restores tint.");Check(!GUI.enabled&&Text.Font==GameFont.Tiny&&!Text.WordWrap,"Scope restores interaction and text.");
            Widgets.Throw=true;Reject(()=>ClientWidgets.Button(new Rect(),"test",eventService),"Throwing control unwinds.");Equal(GUI.color.r,.25f,"Exception restores tint.");Widgets.Throw=false;
            Check(!ClientWidgets.Button(new Rect(),"disabled",eventService),"Disabled caller remains disabled.");Check(Widgets.Clicks==1,"Control consumes one interaction.");
            Console.WriteLine("Theme runtime: "+checks+" assertions passed.");
        }
        finally{Directory.Delete(root,true);}
    }
    private static IClientThemeRegistration Register(UiTheme service, string keyNamespace)
    {
        service.SetModuleTypes(new[] { typeof(ThemeModule) });
        var module = new ThemeModule(); service.OnActivating(module);
        return service.RegisterModule(module, keyNamespace);
    }
    [PhinixExtension("theme.fixture")]
    private sealed class ThemeModule : IPhinixExtensionModule
    {
        public string ExtensionId => "theme.fixture";
        public void Register(IExtensionBuilder builder) { }
    }
    private static bool ExistsKey(this System.Collections.Generic.IReadOnlyList<ClientThemeColor> colors,string key){foreach(var c in colors)if(c.Key==key)return true;return false;}
}
