using System;
using System.IO;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message)
    { checks++; if(!value)throw new Exception(message); }
    private static void Main(string[] args)
    {
        string package=Path.GetFullPath(args.Single());
        foreach(int minor in new[]{6,7})
        {
            string game="1."+minor;
            var paths=ClientGameAssemblyLayout.GetProbeDirectories(package,1,minor).ToArray();
            Check(paths.Length==5,"Host, bundled root and all three independent plugin folders are probed");
            Check(paths.All(p=>p.StartsWith(Path.Combine(package,game)+Path.DirectorySeparatorChar,StringComparison.Ordinal)),"Only the selected game is probed");
            var files=paths.SelectMany(p=>Directory.GetFiles(p,"*.dll")).ToArray();
            // Infrastructure DLLs need identity-only inspection: the strict
            // managed-plugin reader intentionally supports a smaller attribute
            // surface than third-party infrastructure assemblies.
            var identities=files.Select(p=>ManagedExtensionMetadataReader.ReadIdentity(File.ReadAllBytes(p))).ToArray();
            Check(files.Length==(minor==6?36:29),"Complete local cohort DLL count");
            Check(identities.Select(m=>m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()==files.Length,"No duplicate assembly names");
            foreach(string sdk in new[]{"ClientExtensionAbstractions","ClientPresentation.Contracts"})
                Check(identities.Single(m=>m.Name==sdk).Version=="3.0.0.0","API3 identity: "+sdk);
            foreach(string owner in new[]{"phinix.legacy.redpacket","phinix.legacy.talent-trade","phinix.example.basic"})
            {
                string bundle=Path.Combine(package,game,"Extensions",owner);
                var dlls=Directory.GetFiles(bundle,"*.dll");
                Check(dlls.Length==(owner=="phinix.example.basic"?1:2),"Plugin owns only its DLLs: "+owner);
                foreach(string dll in dlls)
                {
                    var m=ManagedExtensionMetadataReader.Read(File.ReadAllBytes(dll));
                    foreach(var r in m.References.Where(r=>r.Name=="ClientExtensionAbstractions" || r.Name=="ClientPresentation.Contracts"))
                        Check(r.Version=="3.0.0.0","Consumer binds API3: "+m.Identity.Name);
                    Check(m.TargetFramework==ManagedExtensionTargetFramework.MetadataName(minor==6?"net472":"net10.0"),"Actual selected plugin framework");
                    var languages=ExtensionLocalizationCatalog.LoadCompanion(dll,m.Identity.Name,CancellationToken.None);
                    Check(languages.Languages.Count()==2,"Both language companions are valid");
                    Check(!string.IsNullOrEmpty(languages.Resolve("name","zh-CN",true)),"Localized display name is available");
                }
            }
        }
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("LegacyRedPacketExtension") || a.GetName().Name.StartsWith("LegacyTalentTradeExtension") || a.GetName().Name=="Phinix.Example.Basic"),"Inspection never loads business plugin code");
        Console.WriteLine("Local joint cohort: "+checks+" checks passed; actual loader paths, API3 references and language companions. No game execution.");
    }
}
