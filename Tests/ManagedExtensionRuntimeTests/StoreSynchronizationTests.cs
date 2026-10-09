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
    private sealed class SynchronizationBackend : IManagedExtensionManagementService
    {
        internal IManagedExtensionManagementService Inner;
        internal bool FailRefresh, FailAfterChange, BlockFirstRead;
        internal int Reads;
        internal readonly ManualResetEventSlim FirstRead=new ManualResetEventSlim(), SecondRead=new ManualResetEventSlim(), ReleaseRead=new ManualResetEventSlim();
        public ManagedExtensionRuntimeSnapshot Snapshot => Inner.Snapshot;
        public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> disabled,CancellationToken token)
        {
            int read=Interlocked.Increment(ref Reads);
            if(BlockFirstRead && read==1) { FirstRead.Set(); ReleaseRead.Wait(token); }
            if(BlockFirstRead && read==2) SecondRead.Set();
            if(FailRefresh) throw new IOException("private fixture path");
            return Inner.Refresh(disabled,token);
        }
        public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState desired,IEnumerable<string> disabled,CancellationToken token)
        {
            var result=Inner.ChangeDesiredState(expected,desired,disabled,token);
            if(result.Succeeded && FailAfterChange) FailRefresh=true;
            return result;
        }
    }
    private sealed class UnavailableStoreAccess : IManagedRepositoryAccess
    {
        internal int Reads;
        public Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh)
        { Reads++; throw new IOException("network unavailable"); }
        public Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null)
        { throw new Exception("Installed-only entries must not download."); }
        public void Dispose() { }
    }
    private static void StoreSynchronizationRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-store-sync-"+Guid.NewGuid().ToString("N"));
        var zip=LocalZip(files); var facts=Facts(files);
        var endpoint=new RepositoryEndpoint("https://store.test","test.source");
        var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(Manifest(files),ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",zip.CopyManifestBytes())}))))),"test.source");
        try
        {
            var paths=new ManagedExtensionPaths(root);
            using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                Assert(runtime.Install(new ManagedExtensionInstallRequest(new[]{zip.LocalInstallation()}),null,CancellationToken.None).Succeeded,"Sync fixture installs a real local package");
                var backend=new SynchronizationBackend {Inner=runtime};
                var disabled=new HashSet<string>(); int settingsVersion=0;
                int mainThread=Thread.CurrentThread.ManagedThreadId;
                using(var controls=new ClientExtensionControlService(backend,runtime,()=>Thread.CurrentThread.ManagedThreadId==mainThread,
                    ()=>settingsVersion,()=>disabled,(id,value)=>{ if(value) disabled.Add(id); else disabled.Remove(id); settingsVersion++; },
                    ()=>new[]{"test.managed"},()=>new string[0]))
                using(var store=new ManagedStoreController(controls,controls))
                using(var manager=new ManagedExtensionManagerController())
                {
                    var env=FlowEnvironment(paths,facts);
                    store.RefreshInventory(env).GetAwaiter().GetResult();
                    var inventory=controls.Capture().Inventory;
                    var offline=ManagedStoreListing.BuildEntries(null,inventory,null,"","en");
                    var local=offline.Single().Preferred;
                    Assert(local.IsLocalDevelopment && local.IsInstalledOnly && local.CatalogRecord==null && local.Installed.Package.RecordKey!=null,"Local listing uses owned inventory without fabricating catalog approval");
                    Assert(ManagedStoreListing.BuildEntries(null,inventory,null,"local-development","en").Length==1,"Developer source is searchable offline");
                    var merged=ManagedStoreListing.BuildEntries(catalog,inventory,endpoint,"","en");
                    Assert(merged.Length==2 && merged.Count(g=>g.Preferred.Installed!=null)==1,"Same package ID from a catalog and local source remains two distinct entries");
                    Assert(merged.Single(g=>!g.Preferred.IsInstalledOnly).Preferred.Installed==null,"Remote same-ID listing cannot claim a local installation");
                    Assert(catalog.Packages.Count==1 && catalog.Packages.Single().Artifact!=null,"Merging does not mutate the authorized catalog");
                    Assert(ManagedStoreUpdates.Find(catalog,inventory,endpoint,env).Length==0,"Local packages do not enter official update notices");
                    StoreReject("ManagedInstalledIdentityChanged",()=>new ManagedStorePlanner(catalog,env,inventory,endpoint).Plan(catalog.Packages.Single(),CancellationToken.None));
                    long beforeRevision=controls.Capture().Revision;
                    var revisionZip=LocalZip(files,Manifest(files).Replace("Metadata test","Sync revised metadata"));
                    Assert(controls.Install(new ManagedExtensionInstallRequest(new[]{revisionZip.LocalInstallation(local.Installed.Package)}),disabled,CancellationToken.None).Succeeded,"Same-version local revision uses the existing installation service");
                    store.RefreshInventory(env).GetAwaiter().GetResult();
                    var revisionRow=ManagedStoreListing.RestoreEntry(ManagedStoreListing.BuildEntries(null,controls.Capture().Inventory,null,"","en"),local);
                    Assert(revisionRow.Installed.Package.ArtifactSha256==revisionZip.Sha256 && revisionRow.Version==local.Version && controls.Capture().Revision>beforeRevision,"Same-version new ZIP rebinds selection and invalidates both views");
                    store.Change(local.Installed.Package,ManagedExtensionDesiredState.Disabled,env).GetAwaiter().GetResult();
                    Assert(store.Snapshot.Code=="ManagedStateChanged","Old package action cannot overwrite a same-version revision");
                    store.RefreshInventory(env).GetAwaiter().GetResult(); local=revisionRow;
                    store.Change(local.Installed.Package,ManagedExtensionDesiredState.Disabled,env).GetAwaiter().GetResult();
                    Assert(controls.Capture().InventoryKnown && controls.Capture().Inventory.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.Disabled,"Store package disable publishes shared verified state without any catalog");
                    var restored=ManagedStoreListing.RestoreEntry(ManagedStoreListing.BuildEntries(null,controls.Capture().Inventory,null,"","en"),local);
                    Assert(restored!=local && restored.Installed.Package.DesiredState==ManagedExtensionDesiredState.Disabled,"Selected local row is rebound to refreshed state");
                    manager.Refresh(controls,disabled); WaitController(manager);
                    Assert(manager.Snapshot.Packages.Single().Package.StateOperationId==restored.Installed.Package.StateOperationId,"Manager sees the store's exact operation identity");
                    manager.Change(controls,restored.Installed.Package,ManagedExtensionDesiredState.Enabled,disabled); WaitController(manager);
                    Assert(controls.Capture().Inventory.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.Enabled,"Manager change publishes state for store consumers");
                    Assert(controls.SetModuleEnabled("test.managed",false,controls.Capture().Revision).Succeeded,"Shared module choice remains independent of package intent");
                    var current=controls.Capture();
                    Assert(new ClientExtensionPackageState(current.Inventory.Packages.Single(),current).Next==ClientPackageNextState.PartiallyDisabled,"Both consumers use the same module/package projection");
                    var moduleEnv=FlowEnvironment(paths,facts,disabled);
                    store.Change(current.Inventory.Packages.Single().Package,ManagedExtensionDesiredState.PendingRemoval,moduleEnv).GetAwaiter().GetResult();
                    var removal=controls.Capture().Inventory.Packages.Single().Package;
                    Assert(removal.DesiredState==ManagedExtensionDesiredState.PendingRemoval,"Local uninstall saves next-start intent with developer mode off");
                    store.Change(removal,ManagedExtensionDesiredState.Disabled,moduleEnv).GetAwaiter().GetResult();
                    Assert(controls.Capture().Inventory.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.Disabled && disabled.Contains("test.managed"),"Cancel uninstall keeps package disabled and preserves module settings");
                    store.Change(removal,ManagedExtensionDesiredState.PendingRemoval,moduleEnv).GetAwaiter().GetResult();
                    Assert(store.Snapshot.Code=="ManagedStateChanged","A stale removal confirmation cannot replay over newer state");
                    backend.FailAfterChange=true;
                    store.Change(controls.Capture().Inventory.Packages.Single().Package,ManagedExtensionDesiredState.PendingRemoval,moduleEnv).GetAwaiter().GetResult();
                    Assert(store.Snapshot.State==ManagedStoreState.Ready && store.Snapshot.Code=="ManagedStateSavedRefreshFailed" && !store.InventoryKnown && !controls.Capture().InventoryKnown,"Committed removal survives a failed refresh without claiming verified state");
                    Assert(runtime.Refresh(disabled,CancellationToken.None).Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.PendingRemoval,"Failed post-commit refresh never rolls back durable removal intent");
                    backend.FailRefresh=false; backend.FailAfterChange=false;
                    store.RefreshInventory(moduleEnv).GetAwaiter().GetResult();
                    Assert(store.InventoryKnown && controls.Capture().InventoryKnown,"Explicit refresh recovers both consumers after an uncertain read");
                    var access=new UnavailableStoreAccess();
                    store.Refresh(endpoint,moduleEnv,false,access).GetAwaiter().GetResult();
                    Assert(access.Reads==1 && store.Snapshot.State==ManagedStoreState.Failed && store.InventoryKnown && store.Snapshot.Inventory.Packages.Count==1,"Network failure preserves independently verified installed inventory");
                    string remoteFailure=store.Snapshot.Code;
                    store.RefreshInventory(moduleEnv).GetAwaiter().GetResult();
                    Assert(store.Snapshot.State==ManagedStoreState.Failed && store.Snapshot.Code==remoteFailure && store.InventoryKnown,"Local state synchronization does not erase an index failure message");
                    store.Refresh(endpoint,moduleEnv,true).GetAwaiter().GetResult();
                    Assert(store.Snapshot.Code=="CacheUnavailable" && store.InventoryKnown,"Missing remote cache does not erase local package facts");
                    store.Change(controls.Capture().Inventory.Packages.Single().Package,ManagedExtensionDesiredState.Disabled,moduleEnv).GetAwaiter().GetResult();
                    Assert(store.Snapshot.Code=="ManagedStateSaved","Local management remains usable after both network and cache failure");
                    long stable=controls.Capture().Revision;
                    backend.BlockFirstRead=true; backend.Reads=0;
                    var first=Task.Run(()=>controls.Refresh(disabled.ToArray(),CancellationToken.None));
                    Assert(backend.FirstRead.Wait(5000),"Concurrent refresh fixture enters first read");
                    var secondStarted=new ManualResetEventSlim();
                    var second=Task.Run(()=>{ secondStarted.Set(); return controls.Refresh(disabled.ToArray(),CancellationToken.None); });
                    try
                    {
                        Assert(secondStarted.Wait(5000) && !backend.SecondRead.Wait(100),"Second consumer cannot publish before the first refresh completes");
                    }
                    finally { backend.ReleaseRead.Set(); }
                    Assert(Task.WaitAll(new Task[]{first,second},5000) && backend.Reads==1,"Concurrent consumers share one completed read without false stale-state failures");
                    Assert(controls.Capture().Revision==stable,"Equivalent refreshes do not produce an endless revision loop");
                    secondStarted.Dispose(); backend.BlockFirstRead=false;
                    backend.FailRefresh=true;
                    manager.Refresh(controls,disabled); WaitController(manager);
                    Assert(!manager.InventoryKnown && !controls.Capture().InventoryKnown && manager.Snapshot!=null,"Read failure retains presentation but revokes manager write availability");
                    backend.FailRefresh=false;
                }
                backend.FirstRead.Dispose(); backend.SecondRead.Dispose(); backend.ReleaseRead.Dispose();
            }
            var failedInstallPaths=new ManagedExtensionPaths(Path.Combine(root,"install-refresh-failure"));
            using(var runtime=EmptyRuntime(failedInstallPaths,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                var backend=new SynchronizationBackend {Inner=runtime};
                using(var store=new ManagedStoreController(backend,runtime,captureDeveloperMode:t=>Task.FromResult(true)))
                {
                    string input=Path.Combine(root,"install.zip");
                    File.WriteAllBytes(input,ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",zip.CopyManifestBytes())})));
                    var env=FlowEnvironment(failedInstallPaths,facts);
                    store.PrepareLocal(input,env,true).GetAwaiter().GetResult(); var prepared=store.Snapshot;
                    backend.FailRefresh=true;
                    store.InstallLocal(prepared,env,true).GetAwaiter().GetResult();
                    Assert(store.Snapshot.State==ManagedStoreState.Installed && store.Snapshot.Code=="ManagedInstallSavedRefreshFailed" && !store.InventoryKnown,"Local install commit remains successful when follow-up inventory read fails");
                    Assert(runtime.Refresh(null,CancellationToken.None).Packages.Single().Package.ArtifactSha256==zip.Sha256,"Post-commit read failure does not erase the installed local build");
                }
                backend.FirstRead.Dispose(); backend.SecondRead.Dispose(); backend.ReleaseRead.Dispose();
            }
            var legacyPaths=new ManagedExtensionPaths(Path.Combine(root,"legacy"));
            Install(legacyPaths,"test.source",Manifest(files),files,"disabled");
            string receipt=File.ReadAllText(legacyPaths.GetInstalledRecordPath("test.source",zip.Manifest.PackageId));
            File.WriteAllText(legacyPaths.GetInstalledRecordPath("test.source",zip.Manifest.PackageId),receipt.Replace(new string('a',64),endpoint.IdentityKey));
            using(var runtime=EmptyRuntime(legacyPaths,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                var inventory=runtime.Refresh(null,CancellationToken.None);
                Assert(inventory.Packages.Single().Package.DiagnosticCode==null && !inventory.Packages.Single().Package.IsLocalDevelopment,"Existing schema-1 repository receipt remains valid");
                var bound=ManagedStoreListing.BuildEntries(catalog,inventory,endpoint,"","en").Single().Preferred;
                Assert(!bound.IsInstalledOnly && bound.Installed!=null,"Verified older official installation merges with its real catalog");
                var removed=ManagedStoreListing.BuildEntries(null,inventory,null,"","en");
                Assert(removed.Single().Preferred.IsInstalledOnly && !removed.Single().Preferred.IsLocalDevelopment,"Delisted official package stays manageable without being mislabeled local development");
                Assert(ManagedStoreListing.RestoreEntry(removed,bound)?.Installed!=null,"Catalog disappearance preserves owned selection");
                Assert(ManagedStoreListing.RestoreEntry(ManagedStoreListing.BuildEntries(catalog,inventory,endpoint,"","en"),removed.Single().Preferred)?.CatalogRecord!=null,"Catalog return rebinds the same installed identity");
                var other=ManagedStoreListing.BuildEntries(catalog,inventory,new RepositoryEndpoint("https://other.test","test.source"),"","en");
                Assert(other.Length==2 && other.Single(g=>!g.Preferred.IsInstalledOnly).Preferred.Installed==null,"Changed repository identity cannot acquire old installations");
                Assert(File.ReadAllText(legacyPaths.GetInstalledRecordPath("test.source",zip.Manifest.PackageId)).Contains("\"schemaVersion\":1"),"Listing and refresh do not migrate legacy records");
                File.Delete(Path.Combine(legacyPaths.GetPackageDirectory("test.source",zip.Manifest.PackageId),"manifest.json"));
                var invalid=ManagedStoreListing.BuildEntries(null,runtime.Refresh(null,CancellationToken.None),null,"","en").Single().Preferred;
                Assert(invalid.Manifest==null && invalid.DisplayName("en")!=null && invalid.Installed.RemovalBlockCode!=null,"Damaged installation remains visible with blocked actions instead of crashing or disappearing");
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
