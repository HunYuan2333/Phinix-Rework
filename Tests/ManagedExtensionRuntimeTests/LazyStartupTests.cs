using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static int LazyStartupProbe(string fixture,bool positive)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-lazy-startup-"+Guid.NewGuid().ToString("N"));
        try
        {
            var files=new Dictionary<string,byte[]>{{"Assemblies/Fixture.Managed.Provider.dll",File.ReadAllBytes(fixture)}};
            var metadata=ManagedExtensionMetadataReader.Read(files.Values.Single());
            Assert(metadata.References.Any(r=>r.Name=="System.Net.WebHeaderCollection"),"Real fixture references the log dependency");
            string json=Manifest(files,"test.provider");
            json=json.Substring(0,json.IndexOf("\"modules\":[",StringComparison.Ordinal))+"\"modules\":[{\"id\":\"test.provider\",\"assemblyName\":\"Fixture.Managed.Provider\",\"entryType\":\"Fixture.Managed.Provider\",\"dependsOn\":[]}]}";
            json=json.Replace("net472","net10.0").Replace("[\"1.6\"]","[\"1.7\"]");
            var paths=new ManagedExtensionPaths(root);Install(paths,"test.source",json,files,"enabled");
            var facts=new ManagedExtensionHostFacts("1.7","0.9.7","1.5.0",ClientRuntimeAssemblyFacts.AvailableReferences().Select(ManagedAssemblyIdentity.FromAssemblyName),new string[0],new string[0]);
            var logs=new List<ManagedExtensionRuntimeAudit>();
            using(var runtime=new ManagedExtensionRuntime(paths,logs.Add,null,positive?(Func<ManagedAssemblyIdentity,Assembly>)(identity=>ClientRuntimeAssemblyFacts.ResolveFrameworkReference(new AssemblyName(identity.FullName))):null))
            {
                runtime.Start(facts,new string[0],new string[0],CancellationToken.None);
                var row=runtime.Snapshot.Packages.Single();
                if(positive)
                {
                    Assert(row.DiagnosticCode==null && row.AssembliesLoaded,"Framework materialization permits actual byte loading");
                    Assert(logs.Any(e=>e.Code=="ManagedRuntimeFrameworkLoaded") && logs.Any(e=>e.Code=="ManagedEntriesReady"),"Runtime binding and entry discovery audited");
                    Assert(PhinixExtensionRegistry.ScanCandidateModuleTypes(runtime).Any(t=>t.FullName=="Fixture.Managed.Provider"),"Actual registry sees approved module");
                    Assert(ClientRuntimeAssemblyFacts.ResolveFrameworkReference(new AssemblyName("PhinixClient, Version=0.10.1.0"))==null,"Framework authority cannot materialize arbitrary mod");
                }
                else
                {
                    Assert(row.DiagnosticCode=="ManagedHostAssemblyUnavailable" && !row.AssembliesLoaded,"Old loaded-only startup rejects before plugin execution");
                    Assert(logs.Any(e=>e.Code=="ManagedHostAssemblyUnavailable"),"Preload rejection is visible in audit");
                }
            }
            Console.WriteLine("Lazy framework startup "+(positive?"positive":"negative")+": "+assertions+" assertions passed.");return 0;
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
