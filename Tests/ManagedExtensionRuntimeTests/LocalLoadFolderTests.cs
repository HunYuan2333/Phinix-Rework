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
        string root=Path.Combine(Path.GetTempPath(),"phinix-loadfolders-"+Guid.NewGuid().ToString("N"));
        var paths=new ManagedExtensionPaths(Path.Combine(root,"data")); var facts=Facts(files); var baseline=FlowEnvironment(paths,facts);
        string mod=Path.Combine(root,"angel"),load=Path.Combine(mod,"LoadFolders.xml"); Directory.CreateDirectory(mod);
        var environment=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,"1.6","0.9.7","1.5.0",
            baseline.InstalledMods.Concat(new[]{new ClientInstalledModSnapshot("haiuan.angel",mod,false)}),baseline.LoadedAssemblies,baseline.Modules,new string[0]);
        string manifest=Manifest(files); byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,zip))),"test.source");
        try
        {
            foreach(string folder in new[]{"","/",".","./","\\","Common","./Common","Custom\\Nested","./Custom\\Nested/.","Custom//Nested"})
            {
                File.WriteAllText(load,"<loadFolders><v1.6><li IfModActive='unused.mod'>"+folder+"</li></v1.6></loadFolders>");
                ManagedStoreLocalGate.Check(environment,catalog.Packages,CancellationToken.None);
                Assert(true,"Safe local load folder is admitted without loading any assembly: "+folder);
            }
            string dll=Path.Combine(mod,"Custom","Nested","Assemblies","renamed.dll"); Directory.CreateDirectory(Path.GetDirectoryName(dll));
            File.WriteAllBytes(dll,files["Assemblies/Fixture.Managed.Plugin.dll"]);
            foreach(string folder in new[]{"Custom\\Nested","./Custom/Nested/.","Custom//Nested"})
            {
                File.WriteAllText(load,"<loadFolders><v1.6><li>"+folder+"</li></v1.6></loadFolders>");
                StoreReject("LegacyModAssemblyConflict",()=>ManagedStoreLocalGate.Check(environment,catalog.Packages,CancellationToken.None));
            }
            File.Delete(dll);
            foreach(string folder in new[]{"../outside","..\\outside","Custom/../Nested","C:\\outside","//server/share","\\\\server\\share"})
            {
                File.WriteAllText(load,"<loadFolders><v1.6><li>"+folder+"</li></v1.6></loadFolders>");
                try { ManagedStoreLocalGate.Check(environment,catalog.Packages,CancellationToken.None); throw new Exception("Unsafe folder accepted: "+folder); }
                catch(StoreValidationException ex)
                { Assert(ex.Code=="LocalIdentityUncertain" && ex.LocalIdentity?.Reason=="LocalLoadFolderInvalid" && ex.LocalIdentity.ModId=="haiuan.angel","Unsafe folders preserve mod-specific diagnostics: "+folder); }
            }
            File.WriteAllText(load,"<loadFolders><v1.6><li>.</li><li>Custom\\Nested</li></v1.6></loadFolders>");
            using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
            using(var controller=new ManagedStoreController(runtime,runtime))
            {
                var handler=new ManagedFlowHandler {Catalog=Utf8(ManagedCatalog(ManagedListing(manifest,zip))),Payload=zip}; handler.SetMetadata();
                var endpoint=new RepositoryEndpoint("https://gateway.test","test.source");
                controller.Refresh(endpoint,environment,false,new RepositoryTransport(handler)).GetAwaiter().GetResult();
                var current=controller.Snapshot.Catalog;
                controller.Plan(current.Packages.Single(),environment,current).GetAwaiter().GetResult();
                Assert(controller.Snapshot.State==ManagedStoreState.PlanReady,"Ordinary local load folders no longer veto controller planning");
                controller.Download(controller.Snapshot.Plan,environment,true,new RepositoryTransport(handler)).GetAwaiter().GetResult();
                Assert(controller.Snapshot.State==ManagedStoreState.Installed && runtime.Refresh(new string[0],CancellationToken.None).Packages.Count==1,"Full validated download and durable installation work alongside normalized local folders");
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
