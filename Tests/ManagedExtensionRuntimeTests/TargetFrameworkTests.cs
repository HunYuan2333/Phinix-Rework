using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Phinix.PluginStore;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    // The second fixture is compiled for net10 from the maintained Provider
    // source. No PE/string patch substitutes for a real target-framework DLL.
    private static int TargetFrameworkProbe(string net10Fixture)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-managed-tfm-"+Guid.NewGuid().ToString("N"));
        string oldFixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Fixtures","Fixture.Managed.Provider.dll");
        try
        {
            foreach(string framework in new[]{"net472","net10.0"})
            {
                var files=new Dictionary<string,byte[]> {{"Assemblies/Fixture.Managed.Provider.dll",File.ReadAllBytes(framework=="net472"?oldFixture:net10Fixture)}};
                string json=Manifest(files,"test.provider");
                json=json.Substring(0,json.IndexOf("\"modules\":[",StringComparison.Ordinal))+"\"modules\":[{\"id\":\"test.provider\",\"assemblyName\":\"Fixture.Managed.Provider\",\"entryType\":\"Fixture.Managed.Provider\",\"dependsOn\":[]}]}";
                // A declaration claiming both games still cannot bypass the
                // runtime gate by including the target game's version string.
                json=json.Replace("\"net472\"",Q(framework)).Replace("[\"1.6\"]","[\"1.6\",\"1.7\"]");
                var manifest=ManagedExtensionManifestReader.Read(Utf8(json));
                var inspected=ManagedExtensionPayloadInspector.Inspect(manifest,files,CancellationToken.None);
                Assert(inspected.Assemblies.Single().Metadata.TargetFramework==ManagedExtensionTargetFramework.MetadataName(framework),"Actual DLL TFM matches declaration: "+framework);
                string other=framework=="net472"?"net10.0":"net472";
                Failure("AssemblyTargetFrameworkMismatch",()=>ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(json.Replace(Q(framework),Q(other)))),files,CancellationToken.None));
                var paths=new ManagedExtensionPaths(Path.Combine(root,framework));
                Install(paths,"test.source",json,files,"enabled");
                var inventory=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None);
                var row=inventory.Packages.Single();
                // Planner requires the exact manifest object read for this row.
                inspected=ManagedExtensionPayloadInspector.Inspect(row.Manifest,files,CancellationToken.None);
                var inputs=new Dictionary<string,ManagedExtensionInspectedPayload>{{row.RecordKey,inspected}};
                foreach(string game in new[]{"1.6","1.7"})
                {
                    var host=new ManagedExtensionHostFacts(game,"0.9.7","1.4.0",inspected.Assemblies.Single().Metadata.References,new string[0],new string[0]);
                    var result=ManagedExtensionCandidatePlanner.Plan(inventory,inputs,host,new string('9',32),CancellationToken.None).Single();
                    bool expected=framework=="net472"?game=="1.6":game=="1.7";
                    Assert(result.DiagnosticCode==(expected?null:"CandidateHostIncompatible"),"Actual payload preflight: "+framework+" -> "+game);
                    Assert((result.Payload!=null)==expected,"Rejected TFM exposes no executable payload");
                    var storePaths=new ManagedExtensionPaths(Path.Combine(root,framework+"-store-"+game));
                    var baseline=FlowEnvironment(storePaths,host);
                    var environment=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,game,"0.9.7","1.4.0",baseline.InstalledMods,baseline.LoadedAssemblies,baseline.Modules,new string[0]);
                    byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(json))}));
                    string listing=ManagedListing(json,zip).Replace(Q("id")+":"+Q("test.package"),Q("id")+":"+Q("test.provider"));
                    var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing)),ManagedSource);
                    using(var runtime=EmptyRuntime(storePaths,host,new List<ManagedExtensionRuntimeAudit>()))
                    {
                        var planner=new ManagedStorePlanner(catalog,environment,runtime.Refresh(new string[0],CancellationToken.None),new RepositoryEndpoint("https://store.test",ManagedSource));
                        if(expected) Assert(planner.Plan(catalog.Packages.Single(),CancellationToken.None)!=null,"Store permits matching game adapter profile");
                        else StoreReject("CandidateHostIncompatible",()=>planner.Plan(catalog.Packages.Single(),CancellationToken.None));
                    }
                }
                Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Managed.Provider"),"Static preflight never loads fixture code");
            }
            Console.WriteLine("Managed target framework: "+assertions+" assertions passed; real net472/net10 DLLs, no plugin execution.");
            return 0;
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
