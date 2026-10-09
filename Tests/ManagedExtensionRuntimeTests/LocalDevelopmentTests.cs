using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static ManagedExtensionZip LocalZip(Dictionary<string,byte[]> files,string manifest=null)
    {
        var archive=new Dictionary<string,byte[]>(files) {{"manifest.json",Utf8(manifest??Manifest(files))}};
        return ManagedExtensionZip.Read(new MemoryStream(ManagedZip(archive)),CancellationToken.None);
    }
    private static void LocalDevelopmentRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-local-"+Guid.NewGuid().ToString("N"));
        var zip=LocalZip(files); string source=ManagedExtensionInstallPackage.LocalDevelopmentSourceId;
        try
        {
            Failure("ManagedInstallProvenanceMismatch",()=>new ManagedExtensionInstallPackage(source,new string('a',64),new string('b',40),new string('c',64),zip.Sha256,zip.CopyManifestBytes(),files));
            var revised=LocalZip(files,Manifest(files).Replace("Metadata test","Revised metadata test"));
            foreach(string point in new[]{"none","journal-written","transition-written","commit-decided","replacement-original-moved","package-moved","receipt-committed","state-committed","work-deleted"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,point));
                using(var runtime=EmptyRuntime(paths,Facts(files),new List<ManagedExtensionRuntimeAudit>()))
                {
                    Assert(runtime.Install(new ManagedExtensionInstallRequest(new[]{zip.LocalInstallation()}),new string[0],CancellationToken.None).Succeeded,"Install local ZIP "+point);
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    Assert(old.IsLocalDevelopment && old.RepositoryIdentitySha256==null && old.CatalogSnapshotId==null && old.CatalogSha256==null,"Local receipts carry no fabricated remote approval");
                    string receipt=File.ReadAllText(paths.GetInstalledRecordPath(source,old.PackageId));
                    Assert(receipt.Contains("\"schemaVersion\":2") && receipt.Contains("\"sourceKind\":\"local-development\"") && !receipt.Contains("catalog"),"Distinct local receipt schema");
                    Failure("ManagedReplacementIdentityInvalid",()=>zip.LocalInstallation(old));
                    Failure("ManagedReplacementIdentityInvalid",()=>LocalZip(files,Manifest(files).Replace("\"version\":\"1.0.0\"","\"version\":\"0.9.0\"")).LocalInstallation(old));
                    if(point!="none") runtime.InstallationFault=p=>{if(p==point) throw new Crash();};
                    var result=runtime.Install(new ManagedExtensionInstallRequest(new[]{revised.LocalInstallation(old)}),new string[0],CancellationToken.None);
                    Assert(result.Succeeded==(point=="none"),"Same-version local revision respects transaction outcome "+point);
                }
                using(var lease=ManagedExtensionLease.Acquire(paths))
                {
                    Assert(ManagedExtensionInstallationRecovery.Recover(paths,(c,r)=>{},CancellationToken.None).Count==0,"Local revision recovery "+point);
                    var row=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages.Single();
                    bool before=point=="journal-written" || point=="transition-written";
                    Assert(row.DiagnosticCode==null && row.IsLocalDevelopment && row.Version=="1.0.0" && row.ArtifactSha256==(before?zip:revised).Sha256,"Recovery preserves the complete chosen local build "+point);
                    Assert(!Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Local recovery leaves no transaction "+point);
                }
            }
            var controllerPaths=new ManagedExtensionPaths(Path.Combine(root,"controller"));
            var environment=new ClientEnvironmentSnapshot(new ClientEnvironmentPaths(Path.Combine(root,"Mods"),controllerPaths.SaveDataRoot),Path.Combine(root,"Mods","host"),"1.6","0.9.7","1.9.0",null,null,null,null);
            string selected=Path.Combine(root,"input.zip");
            File.WriteAllBytes(selected,ManagedZip(new Dictionary<string,byte[]>(files) {{"manifest.json",zip.CopyManifestBytes()}}));
            bool developer=true;
            using(var runtime=EmptyRuntime(controllerPaths,Facts(files),new List<ManagedExtensionRuntimeAudit>()))
            using(var controller=new ManagedStoreController(runtime,runtime,captureDeveloperMode:t=>Task.FromResult(developer)))
            {
                StoreReject("DeveloperModeRequired",()=>controller.PrepareLocal(selected,environment,false));
                controller.PrepareLocal(selected,environment,true).GetAwaiter().GetResult();
                var prepared=controller.Snapshot;
                Assert(prepared.LocalPackage!=null && prepared.Code=="LocalPayloadVerified","Local preparation works without a repository/catalog");
                Assert(controller.ClaimLocalConfirmation(prepared) && !controller.ClaimLocalConfirmation(prepared),"Shared store views claim one confirmation per local input");
                controller.DiscardLocal(prepared);
                Assert(controller.Snapshot.LocalPackage==null,"Canceling confirmation releases frozen package content");
                StoreReject("ManagedStateChanged",()=>controller.InstallLocal(prepared,environment,true));
                controller.PrepareLocal(selected,environment,true).GetAwaiter().GetResult(); prepared=controller.Snapshot;
                File.WriteAllText(selected,"changed after selection");
                StoreReject("DeveloperModeRequired",()=>controller.InstallLocal(prepared,environment,false));
                developer=false;
                controller.InstallLocal(prepared,environment,true).GetAwaiter().GetResult();
                Assert(controller.Snapshot.Code=="DeveloperModeRequired" && runtime.Refresh(null,CancellationToken.None).Packages.Count==0,"Execution rechecks developer mode before writing");
                developer=true;
                File.WriteAllBytes(selected,ManagedZip(new Dictionary<string,byte[]>(files) {{"manifest.json",zip.CopyManifestBytes()}}));
                controller.PrepareLocal(selected,environment,true).GetAwaiter().GetResult(); prepared=controller.Snapshot;
                runtime.InstallationFault=p=>{if(p=="journal-written") developer=false;};
                controller.InstallLocal(prepared,environment,true).GetAwaiter().GetResult();
                Assert(controller.Snapshot.State==ManagedStoreState.Canceled && runtime.Refresh(null,CancellationToken.None).Packages.Count==0,"Mode revoked during staging cancels before durable commit");
                Assert(!Directory.EnumerateFileSystemEntries(controllerPaths.TransactionsDirectory).Any(),"Mode revocation rolls preparation back immediately");
                developer=true; runtime.InstallationFault=null;
                controller.PrepareLocal(selected,environment,true).GetAwaiter().GetResult(); prepared=controller.Snapshot;
                File.WriteAllText(selected,"changed after validation");
                controller.InstallLocal(prepared,environment,true).GetAwaiter().GetResult();
                Assert(controller.Snapshot.Code=="LocalInstallSaved" && runtime.Refresh(null,CancellationToken.None).Packages.Single().Package.ArtifactSha256==prepared.LocalPackage.Sha256,"Install uses frozen selected bytes despite source mutation");
                var inventory=runtime.Refresh(null,CancellationToken.None);
                var controls=new ClientExtensionControlSnapshot(1,false,true,inventory,null,new[]{"test.managed"},null);
                string summary=ManagedExtensionDiagnosticSummary.Build(inventory.Packages.Single(),controls,null,"0.9.7","1.6");
                Assert(summary.Contains("Source: local-development") && summary.Contains(prepared.LocalPackage.Sha256) && summary.Contains("Current ZIP SHA-256: not loaded"),"Diagnostic summary distinguishes installed build and current session");
                Assert(summary.Contains("abstractions: 1.9.0") && summary.Contains("Module dependencies: builtin.host") && summary.Contains("active: True"),"Summary includes host, dependency and module state facts");
                Assert(!summary.Contains(root) && !summary.Contains("changed after validation"),"Summary never exports local paths or input content");
                Assert(!Directory.EnumerateFiles(Path.Combine(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"),"local-import")).Any(),"Temporary ZIPs are cleaned up");
                developer=false;
                var installed=runtime.Refresh(null,CancellationToken.None).Packages.Single().Package;
                Assert(runtime.ChangeDesiredState(installed,ManagedExtensionDesiredState.PendingRemoval,null,CancellationToken.None).Succeeded,"Mode-off development packages remain removable");
            }
            foreach(string rejected in new[]{"official","missing-dependency","module-conflict","assembly-conflict"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,rejected)); var facts=Facts(files);
                if(rejected=="module-conflict") facts=new ManagedExtensionHostFacts("1.6","0.9.7","1.4.0",facts.Assemblies,facts.ModuleIds.Concat(new[]{"test.managed"}),facts.ActiveModIds);
                if(rejected=="assembly-conflict") facts=new ManagedExtensionHostFacts("1.6","0.9.7","1.4.0",facts.Assemblies.Concat(new[]{zip.Inspected.Assemblies.First().Metadata.Identity}),facts.ModuleIds,facts.ActiveModIds);
                using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
                {
                    if(rejected=="official") Assert(runtime.Install(InstallationRequest(files),null,CancellationToken.None).Succeeded,"Prepare official conflict");
                    var candidate=rejected=="missing-dependency"?LocalZip(files,Manifest(files,dependencies:"[{\"packageId\":\"missing.provider\",\"versionRange\":\"1.0.0\",\"optional\":false}]")):zip;
                    var result=runtime.Install(new ManagedExtensionInstallRequest(new[]{candidate.LocalInstallation()}),null,CancellationToken.None);
                    string code=rejected=="official"?"ManagedInstallPackageConflict":rejected=="missing-dependency"?"CandidatePackageDependencyUnavailable":rejected=="module-conflict"?"CandidateModuleConflict":"CandidateAssemblyConflict";
                    Assert(!result.Succeeded && result.Code==code,"Local input reuses common conflict/dependency checks "+rejected+": "+result.Code);
                    Assert(!Directory.Exists(paths.GetPackageDirectory(source,"test.package")),"Rejected local packages leave no partial tree "+rejected);
                }
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
