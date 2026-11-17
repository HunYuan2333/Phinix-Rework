using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhinixClient;

internal static partial class Program
{
    private static void ErrorDiagnostics(string root)
    {
        string storeRoot=Path.Combine(root,"theme-store-errors");
        var storeFailures=new List<Exception>();
        using(var store=new Utils.Framework.Themes.ManagedThemePackageStore(storeRoot,id=>{},(operation,error)=>storeFailures.Add(error))) {
            var paths=(Utils.Framework.ManagedExtensions.ManagedExtensionPaths)store.GetType().GetField("paths",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(store);
            Directory.CreateDirectory(paths.InstalledRecordsDirectory);File.WriteAllText(Path.Combine(paths.InstalledRecordsDirectory,"bad.json"),"{broken");
            var inventory=store.Refresh(System.Threading.CancellationToken.None);
            Check(inventory.Diagnostics.Count>0 && storeFailures.Count>0 && !string.IsNullOrEmpty(storeFailures[0].StackTrace),"Theme store keeps original record read failure before returning safe inventory diagnostics.");
        }
        var failures=new List<Tuple<string,Exception>>();var original=new IOException("theme secret");
        var service=new UiTheme(Path.Combine(root,"missing-settings"),Path.Combine(root,"discover"),()=>{throw original;},message=>{throw new ApplicationException("bad log observer");},null,(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        Check(failures.Any(r=>r.Item1=="ThemeDiscoveryFailed" && ReferenceEquals(r.Item2,original)) && failures.Any(r=>r.Item1=="ThemeDiagnosticObserverFailed"),"Discovery and diagnostic observer errors retain separate original causes without losing the default theme.");
        Check(service.Capture()!=null && !service.Diagnostics.Any(d=>d.Contains("theme secret")),"Friendly theme diagnostics do not expose arbitrary exception messages.");
        var normal=new UiTheme(Path.Combine(root,"observer-settings"),Path.Combine(root,"observer"),()=>new ThemeSource[0],message=>{},null,(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        int healthy=0;normal.Changed+=()=>{throw original;};normal.Changed+=()=>healthy++;
        var draft=normal.Edit("builtin.default");draft.Colors["ui.text.primary"]=new PhinixClient.Framework.ClientRgba(.1f,.2f,.3f);normal.Apply(draft);
        Check(healthy==1 && failures.Any(r=>r.Item1=="ThemeObserverFailed" && ReferenceEquals(r.Item2,original)),"Theme observer failure is saved and does not skip healthy observers or undo the applied palette.");
        string malformed=Path.Combine(root,"malformed-settings.xml");File.WriteAllText(malformed,"<ThemeSettings secret='never-copy-this'");
        new UiTheme(malformed,Path.Combine(root,"settings-error"),()=>new ThemeSource[0],message=>{},null,(operation,error)=>failures.Add(Tuple.Create(operation,error)));
        Check(failures.Any(r=>r.Item1=="ThemeSettingsRejected" && !string.IsNullOrEmpty(r.Item2.StackTrace)),"Theme settings parser original stack is retained before safe fallback.");
    }
}
