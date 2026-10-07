using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void DisabledSettingReinstallRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-disabled-reinstall-"+Guid.NewGuid().ToString("N"));
        var paths=new ManagedExtensionPaths(root); var facts=Facts(files); var disabled=new[]{"test.managed","test.nested"};
        string manifest=Manifest(files); var zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        try
        {
            Install(paths,"test.source",manifest,files,"enabled");
            using(var runtime=new ManagedExtensionRuntime(paths))
            {
                runtime.Start(facts,disabled,new string[0],CancellationToken.None);
                var inventory=runtime.Refresh(disabled,CancellationToken.None); var row=inventory.Packages.Single();
                Assert(!row.Current.AssembliesLoaded && row.Package.DesiredState==ManagedExtensionDesiredState.Enabled,"Module disable never instantiates package; package intent remains independent");
                Assert(row.ModuleSettingsBlockCode==null,"Verified unloaded package retains manifest module recovery actions");
                var entries=ExtensionDisplayState.IncludeDisabledSettings(new ExtensionDiscoveryResult[0],disabled);
                Assert(entries.Count==2 && entries.All(e=>e.State==ExtensionModuleState.Disabled),"All disabled modules stay visible without discovering/loading their types");
                Assert(runtime.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.PendingRemoval,disabled,CancellationToken.None).Succeeded,"Disabled module package can request owned removal");
            }
            using(var runtime=new ManagedExtensionRuntime(paths))
            {
                runtime.Start(facts,disabled,new string[0],CancellationToken.None);
                var inventory=runtime.Refresh(disabled,CancellationToken.None);
                Assert(inventory.Packages.Count==0,"Restart completes physical removal before reinstall");
                var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,zip))),"test.source");
                var endpoint=new RepositoryEndpoint("https://gateway.test","test.source");
                StoreReject("ManagedAllModulesDisabled",()=>new ManagedStorePlanner(catalog,FlowEnvironment(paths,facts,disabled),inventory,endpoint).Plan(catalog.Packages.Single(),CancellationToken.None));
                Assert(ExtensionDisplayState.IncludeDisabledSettings(null,disabled).Count==2,"Uninstalled modules still have explicit settings recovery entries");
                var restored=new[]{"another.disabled.module"};
                var plan=new ManagedStorePlanner(catalog,FlowEnvironment(paths,facts,restored),runtime.Refresh(restored,CancellationToken.None),endpoint).Plan(catalog.Packages.Single(),CancellationToken.None);
                Assert(plan.Items.Count==1 && plan.Items.Single().RequiresDownload,"Explicitly restoring these module IDs permits reinstall without clearing other preferences");
                Assert(runtime.Install(InstallationRequest(files,manifest),restored,CancellationToken.None).Succeeded,"Actual install accepts recovered settings while preserving transaction validation");
                Assert(runtime.Refresh(restored,CancellationToken.None).Packages.Count==1,"Reinstall produces exactly one owned package");
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
