using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void AssemblyOccupancyRegression(Dictionary<string,byte[]> files)
    {
        string json=Manifest(files);
        foreach(string name in new[]{"ChatExtension","ChatExtension.Client","TradeExtension","TradeExtension.Client","InventoryExtension","InventoryExtension.Client","LegacyAdapter.Client"})
        {
            var declaration=ManagedExtensionManifestReader.Read(Utf8(json.Replace("Fixture.Managed.Helper",name)));
            Assert(declaration.Assemblies.Any(a=>a.Name==name),"Business identity is format-neutral: "+name);
        }
        foreach(string name in new[]{"Utils","Assembly-CSharp","System.Private.CoreLib","UnityEngine.CoreModule"})
            Failure("ProtectedAssembly",()=>ManagedExtensionManifestReader.Read(Utf8(json.Replace("Fixture.Managed.Helper",name))));
        Failure("ProtectedAssembly",()=>ManagedExtensionManifestReader.Read(Utf8(json.Replace("Assemblies/Fixture.Managed.Helper.dll","Assemblies/0Harmony.dll"))));

        var aliasFiles=files.ToDictionary(p=>p.Key=="Assemblies/Fixture.Managed.Helper.dll"?"Assemblies/Host.Discovered.Feature.dll":p.Key,p=>p.Value);
        string aliasJson=Manifest(aliasFiles);
        var baseFacts=Facts(files);
        var ownNames=files.Values.Select(b=>ManagedExtensionMetadataReader.Read(b).Identity.Name).ToArray();
        var references=baseFacts.Assemblies.Concat(files.Values.SelectMany(b=>ManagedExtensionMetadataReader.Read(b).References).Where(a=>!ownNames.Contains(a.Name)))
            .GroupBy(a=>a.FullName,StringComparer.Ordinal).Select(g=>g.First()).ToArray();
        var facts=new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",references,new[]{"builtin.host"},new string[0]);
        var occupied=new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",references.Concat(new[]{ManagedAssemblyIdentity.FromAssemblyName(
            new AssemblyName("host.discovered.feature, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"))}),facts.ModuleIds,facts.ActiveModIds);
        string root=Path.Combine(Path.GetTempPath(),"phinix-assembly-occupancy-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new ManagedExtensionPaths(Path.Combine(root,"candidate"));
            Install(paths,"test.source",aliasJson,aliasFiles,"enabled");
            var inventory=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None);
            var row=inventory.Packages.Single();
            var inspected=new Dictionary<string,ManagedExtensionInspectedPayload>{{row.RecordKey,ManagedExtensionPayloadInspector.Inspect(row.Manifest,aliasFiles,CancellationToken.None)}};
            Assert(ManagedExtensionCandidatePlanner.Plan(inventory,inspected,facts,new string('9',32),CancellationToken.None).Single().DiagnosticCode==null,
                "An unoccupied filename alias remains a valid ordinary package");
            Assert(ManagedExtensionCandidatePlanner.Plan(inventory,inspected,occupied,new string('9',32),CancellationToken.None).Single().DiagnosticCode=="CandidateAssemblyConflict",
                "Discovered host occupancy rejects a filename alias, case-insensitively");

            var runtimePaths=new ManagedExtensionPaths(Path.Combine(root,"install"));
            using(var runtime=EmptyRuntime(runtimePaths,occupied,new List<ManagedExtensionRuntimeAudit>()))
            {
                var result=runtime.Install(InstallationRequest(aliasFiles,aliasJson),new string[0],CancellationToken.None);
                Assert(!result.Succeeded && result.Code=="CandidateAssemblyConflict","Host alias conflict is rejected at the actual install boundary");
                Assert(!Directory.Exists(runtimePaths.PackagesDirectory) && !Directory.Exists(runtimePaths.TransactionsDirectory),"Alias conflict cannot mutate visible package files or transaction records");
                byte[] zip=ManagedZip(aliasFiles.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(aliasJson))}));
                var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(aliasJson,zip))),"test.source");
                var endpoint=new RepositoryEndpoint("https://gateway.test","test.source");
                Assert(new ManagedStorePlanner(catalog,FlowEnvironment(runtimePaths,facts),runtime.Refresh(new string[0],CancellationToken.None),endpoint).Plan(catalog.Packages.Single(),CancellationToken.None).Items.Count==1,
                    "Store planning accepts an unoccupied ordinary filename alias");
                StoreReject("ManagedDependencyConflict",()=>new ManagedStorePlanner(catalog,FlowEnvironment(runtimePaths,occupied),runtime.Refresh(new string[0],CancellationToken.None),endpoint).Plan(catalog.Packages.Single(),CancellationToken.None));
            }

            var collision=files.ToDictionary(p=>p.Key=="Assemblies/Fixture.Managed.Plugin.dll"?"Assemblies/PluginAlias.dll":"Assemblies/Fixture.Managed.Plugin.dll",p=>p.Value);
            Failure("DuplicateAssembly",()=>ManagedExtensionManifestReader.Read(Utf8(Manifest(collision))));
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Managed.Plugin"),"Occupancy inspection/install rejection never executes candidate code");
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
