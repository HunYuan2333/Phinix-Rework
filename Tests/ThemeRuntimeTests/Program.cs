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
            var registration=Register(service,"chat");registration.RegisterColor("chat.sample",new ClientRgba(.1f,.1f,.1f));registration.Commit();
            Check(service.SelectedThemeId=="builtin.default","New install does not auto-select arbitrary files.");
            var draft=service.Edit("test");draft.Colors["ui.text.primary"]=new ClientRgba(.8f,.8f,.8f);
            Equal(service.PrimaryText.Red,1,"Editing has no global side effects.");
            service.Apply(draft);Equal(service.PrimaryText.Red,.8f,"Apply uses draft.");Equal(service.GetColor("chat.sample").Red,.7f,"Late registration receives theme values.");
            draft.Colors["ui.text.primary"]=new ClientRgba(.9f,.9f,.9f);Equal(service.PrimaryText.Red,.8f,"Apply copies caller state.");
            var reloaded=new UiTheme(settings,user,()=>sources,_=>{});Equal(reloaded.PrimaryText.Red,.8f,"Selection/overrides persist.");
            long version=service.Version;var broken=service.Edit("test");broken.Colors["ui.text.primary"]=new ClientRgba(float.NaN,0,0);Reject(()=>service.Apply(broken),"Nonfinite draft rejected.");Check(service.Version==version,"Rejected draft does not commit.");
            File.WriteAllText(file,"<PhinixTheme version='2'><color key='ui.text.primary' r='.9' g='.1' b='NaN' a='1'/></PhinixTheme>");service.Refresh();Equal(service.GetColor("chat.sample").Red,.7f,"Invalid replacement keeps verified active snapshot.");Check(service.Diagnostics.Count>0,"Invalid theme has diagnostics.");
            File.Delete(file);service.Refresh();Equal(service.PrimaryText.Red,1,"Removed selected theme falls back including overridden platform values.");Check(service.SelectedThemeId=="test","Selection intent retained.");
            registration.Dispose();Check(!service.Colors.ExistsKey("chat.sample"),"Scoped definitions revoked.");
            var owner=Register(service,"a");owner.RegisterColor("a.color",new ClientRgba(1,0,0));owner.Commit();using(var collision=Register(service,"b"))Reject(()=>collision.RegisterColor("a.color",new ClientRgba(0,1,0)),"Duplicate owner cannot replace default.");owner.Dispose();owner.Dispose();
            var eventService=new UiTheme(Path.Combine(root,"new-settings.xml"),user,()=>new ThemeSource[0],_=>{});int callbacks=0;eventService.Changed+=()=>throw new Exception("observer");eventService.Changed+=()=>callbacks++;eventService.Apply(eventService.Edit("builtin.default"));Check(callbacks==1,"Observer exception isolated.");
            var controlDraft = eventService.Edit("builtin.default");
            controlDraft.Colors["ui.button.background"] = new ClientRgba(.12f,.23f,.34f);
            controlDraft.Colors["ui.accent"] = new ClientRgba(.95f,.9f,.8f);
            eventService.Apply(controlDraft);
            Equal(eventService.GetColor("ui.button.pressed").Red,.12f,"Missing pressed color follows custom button background, not bright accent.");
            controlDraft.Colors["ui.button.pressed"] = new ClientRgba(.83f,.71f,.62f);
            eventService.Apply(controlDraft);
            Equal(eventService.GetColor("ui.button.pressed").Red,.83f,"Explicit custom pressed color remains authoritative.");
            long priorRegistrationVersion = eventService.Version;
            using (var neutral = Register(eventService, "neutral"))
            {
                var rgba = new ClientRgba(.1234567f, .2345678f, .3456789f, .456789f);
                neutral.RegisterColor("neutral.raw", rgba);
                neutral.RegisterColor("neutral.inherited", new ClientRgba(1, 0, 0), fallbackKey: "neutral.raw");
                Check(eventService.Version == priorRegistrationVersion && !eventService.TryGetColor("neutral.raw", out _), "Staged neutral registration does not publish a partial palette.");
                neutral.Commit();
                var resolved = eventService.GetColor("neutral.raw");
                Check(resolved.Red == rgba.Red && resolved.Green == rgba.Green && resolved.Blue == rgba.Blue && resolved.Alpha == rgba.Alpha, "Registered RGBA reaches drawing without rounding or color-space conversion.");
                var inherited = eventService.PreviewColor(eventService.Edit("builtin.default"), "neutral.inherited");
                Check(inherited.Value.Red == rgba.Red && inherited.Value.Alpha == rgba.Alpha && inherited.Source == ClientThemeValueSource.Inherited, "Neutral definitions retain fallback resolution and provenance.");
                var exported = Path.Combine(root, "neutral-export.xml"); eventService.Export(eventService.Edit("builtin.default"), exported);
                Check(!ThemeInputAdapter.Read(exported, "default-export", "local").Colors.ContainsKey("neutral.raw"), "Export does not invent overrides from registered defaults.");
                var exportDraft = eventService.Edit("builtin.default"); exportDraft.Colors["neutral.raw"] = resolved;
                eventService.Export(exportDraft, exported);
                var readBack = ThemeInputAdapter.Read(exported, "roundtrip", "local").Colors["neutral.raw"];
                Check(readBack.Red == rgba.Red && readBack.Green == rgba.Green && readBack.Blue == rgba.Blue && readBack.Alpha == rgba.Alpha, "An explicitly exported neutral default preserves all channels through the real XML exporter/parser.");
            }
            Check(!eventService.TryGetColor("neutral.raw", out _) && !eventService.TryGetColor("neutral.inherited", out _), "Disposal revokes neutral definitions and dependents together.");
            long validPaletteVersion = eventService.Version;
            using (var invalid = Register(eventService, "invalid"))
            {
                Reject(() => invalid.RegisterColor("invalid.nan", new ClientRgba(float.NaN, 0, 0)), "Neutral registration rejects nonfinite channels.");
                Reject(() => invalid.RegisterColor("invalid.alpha", new ClientRgba(0, 0, 0, 2)), "Neutral registration rejects invalid alpha without clamping.");
            }
            Check(eventService.Version == validPaletteVersion, "Rejected neutral registrations never publish a new effective palette.");
            string blocked=Path.Combine(root,"blocked");Directory.CreateDirectory(blocked);var fail=new UiTheme(blocked,user,()=>new ThemeSource[0],_=>{});var edit=fail.Edit("builtin.default");edit.Colors["ui.text.primary"]=new ClientRgba(.4f,.4f,.4f);Reject(()=>fail.Apply(edit),"Failed persistence rejects commit.");Equal(fail.PrimaryText.Red,1,"Persistence failure leaves valid snapshot.");
            var roles=eventService.Edit("builtin.default");roles.Colors["ui.accent"]=new ClientRgba(.73f,.2f,.1f);roles.Colors["ui.hover"]=new ClientRgba(.11f,.3f,.4f);
            roles.Colors["future.unknown"]=new ClientRgba(.9f,.8f,.7f);eventService.Apply(roles);
            Equal(eventService.GetColor("ui.action.primary").Red,.73f,"New action role inherits old accent override.");
            Equal(eventService.GetColor("ui.navigation.selected").Red,.11f,"New navigation role inherits old hover override.");
            Equal(eventService.Success.Red,.3f,"Primary accent does not change success semantics.");
            var explicitRoles=eventService.Edit("builtin.default");explicitRoles.Colors["ui.action.primary"]=new ClientRgba(.42f,.5f,.6f);eventService.Apply(explicitRoles);
            Equal(eventService.GetColor("ui.action.primary").Red,.42f,"Explicit role wins over fallback.");
            explicitRoles.Colors.Remove("ui.action.primary");explicitRoles.InheritedKeys.Add("ui.action.primary");eventService.Apply(explicitRoles);
            Equal(eventService.GetColor("ui.action.primary").Red,.73f,"Explicit inherit returns to old accent role.");
            Check(eventService.Edit("builtin.default").Colors.ContainsKey("future.unknown"),"Unknown values survive role apply.");
            string packagePath = Path.Combine(root, "roundtrip-theme.zip");
            eventService.ExportPackage(eventService.Edit("builtin.default"), new ClientThemeExportInfo("probe-theme", "Probe", "Fixture", "1.0.0"), packagePath);
            Utils.Framework.Themes.ThemePackage package;
            using (var stream = File.OpenRead(packagePath)) package = Utils.Framework.Themes.ThemePackage.Read(stream, System.Threading.CancellationToken.None);
            var documentBytes = package.CopyFile("Themes/main.xml");
            Check(documentBytes.Length > 3 && documentBytes[0] == 0xef && documentBytes[1] == 0xbb && documentBytes[2] == 0xbf, "Package roundtrip exercises the actual exported UTF-8 BOM.");
            var withoutBom = new byte[documentBytes.Length - 3]; Array.Copy(documentBytes, 3, withoutBom, 0, withoutBom.Length);
            Check(ThemeInputAdapter.ReadXml(withoutBom).Root.Name == "PhinixTheme", "Byte input also accepts UTF-8 without BOM.");
            Reject(() => ThemeInputAdapter.ReadXml(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE PhinixTheme [<!ENTITY x 'external'>]><PhinixTheme version='1'>&x;</PhinixTheme>")), "Byte input shares the file reader DTD prohibition.");
            ThemeDefinition packageDefinition;
            using (var stream = new MemoryStream(documentBytes)) packageDefinition = ThemeInputAdapter.Read(System.Xml.Linq.XDocument.Load(stream), "package/probe-theme/main", "local-package", "Probe");
            Check(packageDefinition.Colors["future.unknown"].Red == .9f && packageDefinition.InheritedKeys.Contains("ui.action.primary"), "Neutral package export preserves unknown colors and inherit markers in verified theme data.");
            var packageTheme = new UiTheme(Path.Combine(root, "package-settings.xml"), Path.Combine(root, "package-user"), () => new[] { new ThemeSource { Id = "package/probe-theme/main", Document = documentBytes, Name = "Probe", Author = "Fixture", Source = "local-package" } }, _ => {});
            Check(packageTheme.Diagnostics.Count == 0, "Package source diagnostics: " + string.Join("; ", packageTheme.Diagnostics));
            packageTheme.Apply(packageTheme.Edit("package/probe-theme/main"));
            Check(packageTheme.GetColor("ui.action.primary").Red == .73f, "A locally exported package reloads through the same catalog and fallback resolver.");
            var reexported = Path.Combine(root, "package-roundtrip.xml"); packageTheme.Export(packageTheme.Edit("package/probe-theme/main"), reexported);
            Check(ThemeInputAdapter.Read(reexported, "roundtrip", "local").Colors["future.unknown"].Red == .9f, "Package-loaded unknown colors survive another ordinary export.");
            string malicious=Path.Combine(root,"evil.xml");File.WriteAllText(malicious,"<!DOCTYPE PhinixTheme [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><PhinixTheme version='1'>&x;</PhinixTheme>");Reject(()=>ThemeInputAdapter.Read(malicious,"evil","local"),"External entities rejected.");
            File.WriteAllText(malicious,"<PhinixTheme version='2'><color key='x' r='1' g='0' b='0' a='1'/><color key='x' r='0' g='0' b='0' a='1'/></PhinixTheme>");Reject(()=>ThemeInputAdapter.Read(malicious,"evil","local"),"Duplicate values rejected atomically.");
            string migration=Path.Combine(root,"prior.xml");File.WriteAllText(migration,"<PhinixTheme version='1'><color key='primaryText' r='.3' g='.3' b='.3'/></PhinixTheme>");var migrated=new UiTheme(Path.Combine(root,"migration-settings.xml"),Path.Combine(root,"migrated"),()=>new[]{new ThemeSource{Id="prior",Path=migration,Source="user",Migrate=true}},_=>{});Equal(migrated.PrimaryText.Red,.3f,"Old composition imported as regular theme.");Check(migrated.SelectedThemeId=="user/migrated.xml","No compatibility mode in selector.");
            GUI.color=new Color(.25f,.5f,.75f);GUI.enabled=false;Text.Font=GameFont.Tiny;Text.WordWrap=false;
            using(new ClientGuiScope(true)){GUI.color=Color.white;using(new ClientGuiScope(true)){GUI.color=Color.clear;}Equal(GUI.color.r,1,"Nested scope returns to immediate caller.");}
            Equal(GUI.color.r,.25f,"Outer scope restores tint.");Check(!GUI.enabled&&Text.Font==GameFont.Tiny&&!Text.WordWrap,"Scope restores interaction and text.");
            Widgets.Throw=true;Reject(()=>ClientWidgets.Button(new Rect(),"test",eventService),"Throwing control unwinds.");Equal(GUI.color.r,.25f,"Exception restores tint.");Widgets.Throw=false;
            Check(!ClientWidgets.Button(new Rect(),"disabled",eventService),"Disabled caller remains disabled.");Check(Widgets.Clicks==1,"Control consumes one interaction.");
            var rawDraft=new ClientThemeDraft("builtin.default",new Dictionary<string,ClientRgba>{{"rgba",new ClientRgba(.2f,.3f,.4f,1)},{"unknown.future",new ClientRgba(1,1,1)}},new Dictionary<string,float>{{"parameter",.25f}});
            var raw=new ThemeDraftInput {Values=new[]{"0.2","0.3","0.4","1"}};
            ThemeDraftValues.Change(rawDraft,"rgba",raw,false,0,1,0,"-");
            Check(raw.Invalid && raw.Values[0]=="-","Incomplete raw RGBA text is retained");Equal(rawDraft.Colors["rgba"].Red,.2f,"Invalid input does not replace last valid preview");
            ThemeDraftValues.Change(rawDraft,"rgba",raw,false,0,1,0,"0.7");Equal(rawDraft.Colors["rgba"].Red,.7f,"Valid RGBA updates draft only");Check(!raw.Invalid,"Repair clears draft error");
            ThemeDraftValues.Change(rawDraft,"rgba",raw,false,0,1,3,"2");Check(raw.Invalid,"RGBA rejects out-of-range alpha");Equal(rawDraft.Colors["rgba"].Alpha,1,"Rejected alpha preserves valid draft");
            var number=new ThemeDraftInput {IsFloat=true,Values=new[]{"0.25"}};
            ThemeDraftValues.Change(rawDraft,"parameter",number,true,0,1,0,"NaN");Check(number.Invalid,"Parameter rejects nonfinite input");Equal(rawDraft.Floats["parameter"],.25f,"Nonfinite draft does not reach persisted values");
            ThemeDraftValues.Change(rawDraft,"parameter",number,true,0,1,0,"0.8");Equal(rawDraft.Floats["parameter"],.8f,"Parameter uses invariant raw text within registered bounds");
            var inputs=new Dictionary<string,ThemeDraftInput>{{"rgba",raw},{"parameter",number}};
            ThemeDraftValues.Clear(rawDraft,inputs,"rgba",true);Check(rawDraft.InheritedKeys.Contains("rgba")&&!rawDraft.Colors.ContainsKey("rgba")&&!inputs.ContainsKey("rgba"),"Explicit inherit clears only selected override and invalid buffer");
            Check(rawDraft.Colors.ContainsKey("unknown.future"),"Editing or clearing registered key preserves unknown draft values");
            ThemeDraftValues.Reset(rawDraft,inputs);Check(rawDraft.Colors.Count==0&&rawDraft.Floats.Count==0&&rawDraft.InheritedKeys.Count==0&&inputs.Count==0,"Explicit current-draft reset clears values and input without applying");
            CheckPluginThemeAdapters(eventService);
            Console.WriteLine("Theme runtime: "+checks+" assertions passed.");
        }
        finally{Directory.Delete(root,true);}
    }
    private static void CheckPluginThemeAdapters(UiTheme service)
    {
        service.SetModuleTypes(new[] { typeof(ThemeModule) });
        var host = new ExtensionHostContext(); host.AddService<IClientTheme>(service); host.AddService<IClientThemeRegistry>(service);
        {
            var owner = new ThemeModule(); service.OnActivating(owner);
            var value = new Color(.1234567f, .2345678f, .3456789f, .456789f);
            var defaults = new Dictionary<string, Color> { { "red-probe.color", value } };
            var source = Phinix.LegacyRedPacketExtension.Client.PluginThemeAdapter.Create(host, owner, "red-probe", "Probe", new[] { new Phinix.LegacyRedPacketExtension.Client.PluginColor("red-probe.color", value, "Color", null) }, defaults);
            var palette = source.Capture(); var read = palette["red-probe.color"];
            Check(read.r == value.r && read.g == value.g && read.b == value.b && read.a == value.a, "red-probe adapter constructs current RGBA registration without rounding.");
            Check(ReferenceEquals(palette, source.Capture()), "red-probe adapter caches a stable snapshot revision.");
            var draft = service.Edit("builtin.default"); draft.Colors["red-probe.color"] = new ClientRgba(.8f, .7f, .6f, .5f); service.Apply(draft);
            Check(source.Capture()["red-probe.color"].r == .8f, "red-probe adapter observes hot application through the ordinary public reader.");
            source.Dispose(); source.Dispose(); service.OnStopped(owner);
            Check(!service.TryGetColor("red-probe.color", out _) && service.PrimaryText.Red == 1, "red-probe adapter disposal revokes registration while retaining borrowed Host service.");
            Reject(() => source.Capture(), "red-probe disposed adapter refuses late reads.");
        }
        {
            var owner = new ThemeModule(); service.OnActivating(owner);
            var value = new Color(.1234567f, .2345678f, .3456789f, .456789f);
            var defaults = new Dictionary<string, Color> { { "talent-probe.color", value } };
            var source = Phinix.LegacyTalentTradeExtension.Client.PluginThemeAdapter.Create(host, owner, "talent-probe", "Probe", new[] { new Phinix.LegacyTalentTradeExtension.Client.PluginColor("talent-probe.color", value, "Color", null) }, defaults);
            var palette = source.Capture(); var read = palette["talent-probe.color"];
            Check(read.r == value.r && read.g == value.g && read.b == value.b && read.a == value.a, "talent-probe adapter constructs current RGBA registration without rounding.");
            Check(ReferenceEquals(palette, source.Capture()), "talent-probe adapter caches a stable snapshot revision.");
            var draft = service.Edit("builtin.default"); draft.Colors["talent-probe.color"] = new ClientRgba(.8f, .7f, .6f, .5f); service.Apply(draft);
            Check(source.Capture()["talent-probe.color"].r == .8f, "talent-probe adapter observes hot application through the ordinary public reader.");
            source.Dispose(); source.Dispose(); service.OnStopped(owner);
            Check(!service.TryGetColor("talent-probe.color", out _) && service.PrimaryText.Red == 1, "talent-probe adapter disposal revokes registration while retaining borrowed Host service.");
            Reject(() => source.Capture(), "talent-probe disposed adapter refuses late reads.");
        }
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
