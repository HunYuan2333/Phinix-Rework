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
    private static void LocalLoadFolderRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-local-boundary-"+Guid.NewGuid().ToString("N"));
        var paths=new ManagedExtensionPaths(Path.Combine(root,"data")); var facts=Facts(files); var baseline=FlowEnvironment(paths,facts);
        string mod=Path.Combine(root,"third-party"),load=Path.Combine(mod,"LoadFolders.xml"); Directory.CreateDirectory(mod);
        string manifest=Manifest(files); byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,zip))),"test.source");
        var endpoint=new RepositoryEndpoint("https://gateway.test","test.source");
        try
        {
            using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                var inventory=runtime.Refresh(new string[0],CancellationToken.None);
                bool ownershipRejected=false;
                try { new ManagedStorePlanner(catalog,baseline,new ManagedExtensionManagementSnapshot(new ManagedExtensionManagementPackage[0],new[]{"ReceiptInvalid"}),endpoint).Plan(catalog.Packages.Single(),CancellationToken.None); }
                catch(StoreValidationException ex)
                {
                    var failure=StoreFailureInfo.FromException(ex);
                    ownershipRejected=ex.Code=="ManagedInventoryUncertain" && failure.Scope==StoreFailureScope.Environment && failure.ContextReasons.Contains("ReceiptInvalid");
                }
                Assert(ownershipRejected,"Uncertain owned records remain blocked and report the actual ownership cause, not generic environment readiness.");
                foreach(bool enabled in new[]{false,true})
                foreach(string xml in new[]{"<broken","<loadFolders><v1.1><li>../outside</li></v1.1></loadFolders>","<!DOCTYPE x SYSTEM 'file:///not-read'><x/>","<loadFolders><v1.6><li IfModActive='missing.mod'>Custom</li></v1.6></loadFolders>"})
                {
                    File.WriteAllText(load,xml);
                    string dll=Path.Combine(mod,"1.1","Assemblies","unrelated.dll"); Directory.CreateDirectory(Path.GetDirectoryName(dll)); File.WriteAllBytes(dll,new byte[]{1,2,3});
                    var environment=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,"1.6","0.9.7","1.5.0",
                        baseline.InstalledMods.Concat(new[]{new ClientInstalledModSnapshot("third.party",mod,enabled)}),baseline.LoadedAssemblies,baseline.Modules,new string[0]);
                    Assert(new ManagedStorePlanner(catalog,environment,inventory,endpoint).Plan(catalog.Packages.Single(),CancellationToken.None)!=null,"Ordinary mod files are outside plugin planning: enabled="+enabled);
                    Assert(File.ReadAllText(load)==xml && File.ReadAllBytes(dll).SequenceEqual(new byte[]{1,2,3}),"Planning leaves unrelated mod files untouched");
                }
                var loaded=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,"1.6","0.9.7","1.5.0",baseline.InstalledMods,
                    baseline.LoadedAssemblies.Concat(new[]{new ClientLoadedAssemblySnapshot("Fixture.Managed.Plugin","1.2.3.4",mod)}),baseline.Modules,new string[0]);
                StoreReject("CandidateAssemblyConflict",()=>new ManagedStorePlanner(catalog,loaded,inventory,endpoint).Plan(catalog.Packages.Single(),CancellationToken.None));
                var incomplete=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,"1.6","0.9.7","1.5.0",baseline.InstalledMods,baseline.LoadedAssemblies,baseline.Modules,new[]{"AssemblyIdentityUnavailable"});
                StoreReject("IncompleteEnvironment",()=>new ManagedStorePlanner(catalog,incomplete,inventory,endpoint).Plan(catalog.Packages.Single(),CancellationToken.None));
                using(var controller=new ManagedStoreController(runtime,runtime))
                {
                    var environment=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,"1.6","0.9.7","1.5.0",
                        baseline.InstalledMods.Concat(new[]{new ClientInstalledModSnapshot("third.party",mod,true)}),baseline.LoadedAssemblies,baseline.Modules,new string[0]);
                    var handler=new ManagedFlowHandler {Catalog=Utf8(ManagedCatalog(ManagedListing(manifest,zip))),Payload=zip}; handler.SetMetadata();
                    controller.Refresh(endpoint,environment,false,new RepositoryTransport(handler)).GetAwaiter().GetResult();
                    var current=controller.Snapshot.Catalog;
                    controller.Plan(current.Packages.Single(),environment,current).GetAwaiter().GetResult();
                    Assert(controller.Snapshot.State==ManagedStoreState.PlanReady,"Controller plans alongside uninspectable unrelated mod files");
                    controller.Download(controller.Snapshot.Plan,environment,true,new RepositoryTransport(handler)).GetAwaiter().GetResult();
                    Assert(controller.Snapshot.State==ManagedStoreState.Installed && runtime.Refresh(new string[0],CancellationToken.None).Packages.Count==1,"Validated transactional installation still succeeds");
                }
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
