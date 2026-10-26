using System;
using System.IO;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
internal static class Program
{
 [Utils.Framework.PhinixExtension("test.theme.editor")]
 private sealed class Module : Utils.Framework.IPhinixExtensionModule {public string ExtensionId=>"test.theme.editor";public void Register(Utils.Framework.IExtensionBuilder builder){}}
 private static int checks;
 private static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
 private static ClientPresentationRow[] Rows(ThemeSettingsWindow w)=>w.Page.Sections.Where(s=>s.List!=null).SelectMany(s=>s.List.Rows).ToArray();
 public static void Main()
 {
  string root=Path.Combine(Path.GetTempPath(),"theme-editor-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   var theme=new UiTheme(Path.Combine(root,"settings.xml"),root,()=>new ThemeSource[0],_=>{});
   theme.SetModuleTypes(new[]{typeof(Module)});var module=new Module();theme.OnActivating(module);var registration=theme.RegisterModule(module,"sample","Sample plugin");
   for(int i=0;i<25;i++)registration.RegisterColor("sample.color"+i,new ClientRgba(.2f,.3f,.4f),label:"Plugin color "+i,group:"Area "+i,description:"A searchable description "+i);
   registration.Commit();
   var services=new ThemePresentationServices(null,null,null);var w=new ThemeSettingsWindow(services,theme,theme,null,null);w.Render();
   Check(Rows(w).Length==10,"Beginner view contains ten essentials, not the full catalog");
   Check(Rows(w)[0].Id==w.Page.Sections.First(s=>s.List!=null).List.SelectedId,"Main background is selected on opening");
   Check(Rows(w).All(r=>r.Label.Contains("Phinix_theme_commonMark")),"Essentials marked in lists");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Count==2&&w.Page.Sections.Single(s=>s.Id=="values").Fields[0].ColorPicker!=null,"Default editing shows inline picker and opacity, not RGB fields");
   Check(w.Page.Sections.Single(s=>s.Id=="selected").Swatches.Count==1,"Current color stays visible in the fixed selected-item header");
   Check(!w.Page.Sections.SelectMany(s=>s.Fields).Any(f=>f.Id=="export-path"),"Export path stays out of ordinary editing");
   var tools=w.Page.Sections.Single(s=>s.Id=="tools");
   Check(tools.ActionMenus.Single().ActionIds.Contains("builtin.host.export"),"Export remains accessible through the normal action menu");
   Check(tools.ActionMenus.Single().ActionIds.Contains("builtin.host.reset"),"Destructive draft reset is separated from routine editing");
   Check(!tools.ActionMenus.Single().ActionIds.Contains("builtin.host.apply"),"Apply stays directly visible");
   w.Act("advanced");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Any(f=>f.Id=="color-hex"),"Advanced mode exposes hex input");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Count(f=>f.Slider!=null)==4,"Advanced mode retains all RGB and opacity sliders");
   w.Edit("value.0","0.61");w.Edit("value.1","-");w.Render();
   Check(w.Definitions.Single(a=>a.Id=="apply").DisabledReason!=null,"Incomplete color prevents save");
   w.Act("all-items");Check(Rows(w).Length==theme.Colors.Count+theme.Parameters.Count,"All plugin items survive category overflow");
   Check(w.Page.Sections.Count<=16,"Many plugin groups fit the public page section limit");
   Check(w.Page.Sections.Count(s=>s.List!=null)>1,"Full view shows distinct category sections");
   w.Edit("item-search","searchable description 24");w.Render();Check(Rows(w).Length==1&&Rows(w)[0].Label.Contains("Plugin color 24"),"Search includes plugin descriptions");
   w.Act("common");w.Edit("item-search","Plugin color 24");w.Render();Check(Rows(w).Length==1,"Search reaches plugins from common view too");
   w.Act("common");Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Single(f=>f.Id=="value.1").Value=="-","Browse and search preserve unfinished color drafts");
   w.Edit("value.1","0.42");w.Render();Check(w.Definitions.Single(a=>a.Id=="apply").DisabledReason==null,"Correcting the draft enables save");
   float old=theme.Background.Red;Check(Math.Abs(old-.61f)>.001,"Draft sliders do not change live palette before apply");
   w.Act("apply");Check(Math.Abs(theme.Background.Red-.61f)<.001,"Apply saves edited background through the catalog");
   var reloaded=new UiTheme(Path.Combine(root,"settings.xml"),root,()=>new ThemeSource[0],_=>{});Check(Math.Abs(reloaded.Background.Red-.61f)<.001,"Color persists with unchanged theme keys");
   w.Edit("item-search","no such color");w.Render();Check(Rows(w).Length==0,"No-match search publishes safely");
   w.Edit("color-hex","#12345678");w.Render();
   Check(w.Page.Sections.Single(s=>s.Id=="tools").Status.Text!="Phinix_theme_applied","Editing after Apply clears the old applied-success message");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Single(f=>f.Id=="color-hex").Value=="#12345678","Typed valid hex text is retained without rewriting the input");
   Check(Math.Abs(w.Page.Sections.Single(s=>s.Id=="values").Fields[0].ColorPicker.Value.Alpha-120/255f)<.0001,"Eight-digit hex synchronizes alpha with the picker");
   w.Edit("color-hex","#1234567");w.Render();
   Check(w.Definitions.Single(a=>a.Id=="apply").DisabledReason!=null,"Incomplete hex prevents save");
   w.Act("advanced");Check(w.Definitions.Single(a=>a.Id=="apply").DisabledReason!=null,"Collapsing advanced controls never discards or saves an invalid hex draft");w.Act("advanced");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Single(f=>f.Id=="color-hex").Value=="#1234567","Incomplete hex survives mode changes");
   w.Edit("color-picker","0.2,0.3,0.4,0.5");w.Render();
   Check(w.Definitions.Single(a=>a.Id=="apply").DisabledReason==null,"Choosing from the palette replaces incomplete hex safely");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Single(f=>f.Id=="color-hex").Value=="#334D6680","Picker updates advanced hexadecimal display");
   w.Edit("color-hex","ff0000");w.Render();
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields[0].ColorPicker.Value.Alpha==.5f,"Six-digit hex preserves current transparency");
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields.Single(f=>f.Id=="color-hex").Value=="ff0000","Six-digit input is not auto-expanded, so an alpha suffix can be typed");
   w.Edit("value.0","-");w.Render();w.Edit("color-picker","0.1,0.2,0.3,0.4");w.Render();
   Check(w.Definitions.Single(a=>a.Id=="apply").DisabledReason==null,"Palette also replaces unfinished RGB drafts");
   w.Edit("color-picker","2,0,0,1");w.Render();
   Check(w.Page.Sections.Single(s=>s.Id=="values").Fields[0].ColorPicker.Value.Red==.1f,"Invalid picker payload does not alter the draft");
   Check(!ThemeDraftValues.TryHex("#GG0000",1,out _)&&!ThemeDraftValues.TryHex("#12345",1,out _),"Hex rejects non-hex and wrong-length inputs");
   Check(!ThemeDraftValues.TryPicker("NaN,0,0,1",out _)&&!ThemeDraftValues.TryPicker("0,0,1",out _),"Picker parsing rejects non-finite and truncated input");
   Check(ThemeDraftValues.TryHex("  #abcdef  ",.25f,out var parsed)&&parsed.Alpha==.25f,"Hex accepts whitespace, mixed case, and keeps opacity");
   Check(ThemeDraftValues.Hex(new ClientRgba(0,1,.5f,.25f))=="#00FF8040","Hex formatting includes all four rounded byte channels");
   registration.Dispose();Console.WriteLine("Theme editor usability: "+checks+" checks passed (production projection/catalog; stub window boundary).");
  }
  finally {Directory.Delete(root,true);}
 }
}
