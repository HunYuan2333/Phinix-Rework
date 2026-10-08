using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    internal sealed class ManagedStoreSnapshot
    {
        internal ManagedStoreSnapshot(long revision,ManagedStoreState state,ManagedStoreCatalogSnapshot catalog,RepositoryBrowseInfo repository,ManagedStorePlan plan,
            ManagedExtensionManagementSnapshot inventory,string code,string requestId=null,LocalIdentityDiagnostic localIdentity=null,ManagedExtensionAssemblyReferenceFailure referenceFailure=null,ManagedStoreProgress progress=null,IEnumerable<string> contextReasons=null)
        { Revision=revision; State=state; Catalog=catalog; Repository=repository; Plan=plan; Inventory=inventory; Code=code; RequestId=requestId; LocalIdentity=localIdentity; ReferenceFailure=referenceFailure; Progress=progress; ContextReasons=StoreCollections.Freeze(contextReasons); }
        internal System.Collections.ObjectModel.ReadOnlyCollection<string> ContextReasons { get; }
        internal long Revision { get; }
        internal ManagedStoreState State { get; }
        internal ManagedStoreCatalogSnapshot Catalog { get; }
        internal RepositoryBrowseInfo Repository { get; }
        internal ManagedStorePlan Plan { get; }
        internal ManagedExtensionManagementSnapshot Inventory { get; }
        internal string Code { get; }
        internal string RequestId { get; }
        internal LocalIdentityDiagnostic LocalIdentity { get; }
        internal ManagedExtensionAssemblyReferenceFailure ReferenceFailure { get; }
        internal ManagedStoreProgress Progress { get; }
        internal bool Busy => ManagedStoreOperation.IsBusy(State);
    }
    internal sealed class ManagedStoreController : IDisposable
    {
        private readonly object gate=new object();
        private readonly IManagedExtensionManagementService management;
        private readonly IManagedExtensionInstallationService installation;
        private readonly Action<string> log;
        private readonly Func<CancellationToken,Task<ClientEnvironmentSnapshot>> captureEnvironment;
        private ManagedStoreSnapshot snapshot=new ManagedStoreSnapshot(0,ManagedStoreState.Idle,null,null,null,null,null);
        private readonly ManagedStoreOperation operation=new ManagedStoreOperation();
        private CancellationTokenSource running;
        private bool disposed;
        private long lastProgressTicks;
        private ClientEnvironmentSnapshot updateEnvironment,updatesEnvironment;
        private ManagedStoreCatalogSnapshot updatesCatalog;
        private ManagedExtensionManagementSnapshot updatesInventory;
        private ManagedStoreRecord[] updates=new ManagedStoreRecord[0];
        private System.Collections.ObjectModel.ReadOnlyCollection<ManagedStoreRecord> updatesView=Array.AsReadOnly(new ManagedStoreRecord[0]);
        internal ManagedStoreController(IManagedExtensionManagementService management,IManagedExtensionInstallationService installation,Action<string> log=null,Func<CancellationToken,Task<ClientEnvironmentSnapshot>> captureEnvironment=null)
        { this.management=management; this.installation=installation; this.log=log; this.captureEnvironment=captureEnvironment; }
        internal ManagedStoreSnapshot Snapshot { get { lock(gate) return snapshot; } }
        internal System.Collections.ObjectModel.ReadOnlyCollection<ManagedStoreRecord> Updates
        {
            get
            {
                lock(gate)
                {
                    if(updatesCatalog!=snapshot.Catalog || updatesInventory!=snapshot.Inventory || updatesEnvironment!=updateEnvironment)
                    {
                        updatesCatalog=snapshot.Catalog; updatesInventory=snapshot.Inventory; updatesEnvironment=updateEnvironment;
                        updates=ManagedStoreUpdates.Find(snapshot.Catalog,snapshot.Inventory,snapshot.Repository?.Endpoint,updateEnvironment);
                        updatesView=Array.AsReadOnly(updates);
                    }
                    return updatesView;
                }
            }
        }
        internal Task CheckUpdates(RepositoryEndpoint endpoint,ClientEnvironmentSnapshot environment)
        {
            lock(gate) updateEnvironment=environment;
            return Run(ManagedStoreState.Reading,async token=>
            {
                var inventory=Inventory(environment,token);
                // No installed official plugins: avoid a startup network request entirely.
                if(!inventory.Packages.Any(p=>p.Package.SourceId==endpoint.SourceId && p.Package.RepositoryIdentitySha256==endpoint.IdentityKey &&
                    p.Package.DesiredState!=ManagedExtensionDesiredState.PendingRemoval && p.Package.ContentState==ManagedExtensionContentState.ContentVerified))
                    return Result(ManagedStoreState.Idle,null,null,null,inventory,"ManagedUpdateCheckSkipped");
                var audit=new RepositoryDiagnostics(log,endpoint.SourceId);
                audit.Event("managed.update_check_started","UpdateMetadata");
                var cache=new ManagedRepositoryCache(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"),endpoint);
                using(var connection=ManagedRepositoryAccess.Create(endpoint,audit))
                {
                    var entry=await Metadata(t=>ManagedRepositoryBrowser.Refresh(endpoint,cache,connection,t),token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); cache.Save(entry,token);
                    inventory=Inventory(environment,token);
                    audit.Event("managed.update_check_complete","UpdateMetadata");
                    return Result(ManagedStoreState.Ready,entry.Catalog,new RepositoryBrowseInfo(endpoint,entry.CheckedUtc,false,false),null,inventory,"ManagedUpdateCheckComplete");
                }
            });
        }
        internal StoreFailureInfo ReportFailure(Exception error)
            => new RepositoryDiagnostics(log,Snapshot.Repository?.Endpoint.SourceId) {Operation="UserAction"}
                .Failure("managed.action_failed","ManagedStore",error,"ManagedStoreFailed");
        private void RequireEnvironment(ClientEnvironmentSnapshot environment)
        {
            if(environment==null || !environment.IsComplete || management==null)
            {
                var reasons=new List<string>();
                if(environment==null) reasons.Add("EnvironmentSnapshotUnavailable");
                else
                {
                    if(environment.Paths==null) reasons.Add("GamePathsUnavailable");
                    if(environment.HostModRoot==null) reasons.Add("HostRootUnavailable");
                    reasons.AddRange(environment.Diagnostics.Select(d=>d.Split(':')[0]));
                }
                if(management==null) reasons.Add("ManagedRuntimeUnavailable");
                new RepositoryDiagnostics(log,Snapshot.Repository?.Endpoint.SourceId) {Operation="Inventory",ContextReasons=reasons.ToArray()}
                    .Event("managed.environment_rejected","ManagedStore","IncompleteEnvironment");
                throw Error("IncompleteEnvironment");
            }
        }
        private ManagedExtensionManagementSnapshot Inventory(ClientEnvironmentSnapshot environment,CancellationToken token)
        {
            RequireEnvironment(environment);
            var inventory=management.Refresh(environment.DisabledModuleIds,token);
            if(inventory==null) throw Error("IncompleteEnvironment");
            var diagnostics=inventory.Diagnostics.Concat(inventory.Packages.Select(p=>p.Package.DiagnosticCode).Where(c=>c!=null)).ToArray();
            if(diagnostics.Length!=0)
                new RepositoryDiagnostics(log,Snapshot.Repository?.Endpoint.SourceId) {Operation="Inventory",ContextReasons=diagnostics}
                    .Event("managed.inventory_restricted","ManagedStore","ManagedInventoryUncertain");
            return inventory;
        }
        internal Task Refresh(RepositoryEndpoint endpoint,ClientEnvironmentSnapshot environment,bool offline,RepositoryTransport transport)
        { return Refresh(endpoint,environment,offline,new CloudflareRepositoryAccess(transport)); }
        internal Task Refresh(RepositoryEndpoint endpoint,ClientEnvironmentSnapshot environment,bool offline=false,IManagedRepositoryAccess transport=null)
        {
            lock(gate)
            {
                if(disposed) throw new ObjectDisposedException(nameof(ManagedStoreController));
                if(operation.Busy) throw Error("StoreBusy");
                updateEnvironment=environment;
                if(snapshot.Repository!=null && (snapshot.Repository.Endpoint.IdentityKey!=endpoint.IdentityKey || snapshot.Repository.Endpoint.AccessKey!=endpoint.AccessKey))
                {
                    operation.ResetRepository();
                    snapshot=new ManagedStoreSnapshot(snapshot.Revision+1,operation.State,null,null,null,snapshot.Inventory,null);
                }
            }
            return Run(ManagedStoreState.Reading,async token=>
            {
                var cache=new ManagedRepositoryCache(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"),endpoint);
                ManagedRepositoryCacheEntry entry;
                if(offline) entry=cache.TryRead(token)??throw Error("CacheUnavailable");
                else using(var connection=transport??ManagedRepositoryAccess.Create(endpoint,new RepositoryDiagnostics(log,endpoint.SourceId)))
                {
                    entry=await Metadata(t=>ManagedRepositoryBrowser.Refresh(endpoint,cache,connection,t),token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); cache.Save(entry,token);
                }
                bool stale=offline && (DateTime.UtcNow-entry.CheckedUtc>TimeSpan.FromMinutes(30) || entry.CheckedUtc>DateTime.UtcNow);
                return Result(ManagedStoreState.Ready,entry.Catalog,new RepositoryBrowseInfo(endpoint,entry.CheckedUtc,offline,stale),null,Inventory(environment,token),offline?"ManagedStoreOffline":"ManagedStoreReady");
            });
        }
        internal Task RefreshInventory(ClientEnvironmentSnapshot environment)
        { var before=Snapshot; return Run(ManagedStoreState.Managing,token=>Task.FromResult(Result(ManagedStoreState.Ready,before.Catalog,before.Repository,null,Inventory(environment,token),"ManagedInventoryRefreshed"))); }
        internal Task Plan(ManagedStoreRecord selected,ClientEnvironmentSnapshot environment,ManagedStoreCatalogSnapshot expected,bool replace=false)
        {
            var before=Snapshot; if(before.Catalog!=expected) throw Error("SnapshotChanged");
            return Run(ManagedStoreState.Planning,token=>
            {
                var inventory=Inventory(environment,token);
                if(before.Repository==null) throw Error("InvalidEndpoint");
                var plan=new ManagedStorePlanner(before.Catalog,environment,inventory,before.Repository.Endpoint,replace).Plan(selected,token);
                return Task.FromResult(Result(ManagedStoreState.PlanReady,before.Catalog,before.Repository,plan,inventory,"ManagedPlanReady"));
            });
        }
        internal Task Download(ManagedStorePlan expected,ClientEnvironmentSnapshot environment,bool install,RepositoryTransport transport)
        { return Download(expected,environment,install,new CloudflareRepositoryAccess(transport)); }
        internal Task Download(ManagedStorePlan expected,ClientEnvironmentSnapshot environment,bool install,IManagedRepositoryAccess transport=null)
        {
            var before=Snapshot;
            if(expected==null || expected!=before.Plan || expected.Catalog!=before.Catalog) throw Error("SnapshotChanged");
            if(before.Repository==null || before.Repository.Offline || before.Repository.Stale || DateTime.UtcNow-before.Repository.CheckedUtc>TimeSpan.FromMinutes(30) || before.Repository.CheckedUtc>DateTime.UtcNow) throw Error("RepositoryStale");
            if(install && installation==null) throw Error("ManagedManagementUnavailable");
            var endpoint=before.Repository.Endpoint; var audit=new RepositoryDiagnostics(log,endpoint.SourceId);
            return Run(install?ManagedStoreState.Installing:ManagedStoreState.Downloading,async token=>
            {
                using(var connection=transport??ManagedRepositoryAccess.Create(endpoint,audit))
                {
                    environment=await RefreshEnvironment(environment,token).ConfigureAwait(false);
                    audit.Event("managed.download_started","ManagedPackage");
                    var cache=new ManagedRepositoryCache(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"),endpoint);
                    var entry=await Fresh(connection,cache,endpoint,expected,token).ConfigureAwait(false);
                    var inventory=Inventory(environment,token);
                    var selected=entry.Catalog.Packages.Single(p=>p.Id==expected.Root.Id && !p.IsWorkshop && p.Manifest.Version.ToString()==expected.Root.Manifest.Version.ToString());
                    var current=new ManagedStorePlanner(entry.Catalog,environment,inventory,endpoint,expected.ReplacesPackages).Plan(selected,token);
                    if(current.Identity!=expected.Identity) throw Error("ManagedStateChanged");
                    var packages=new List<ManagedExtensionInstallPackage>();
                    var downloads=current.Items.Where(i=>i.RequiresDownload).ToArray();
                    long received=0; int index=0;
                    foreach(var item in downloads)
                    {
                        token.ThrowIfCancellationRequested();
                        index++;
                        long transferredBefore=received; int ordinal=index;
                        ReportProgress(token,new ManagedStoreProgress(ManagedProgressStage.Checking,item.Package,received,current.DownloadBytes,index,downloads.Length));
                        audit.Event("managed.package_checking","ManagedPackage",managed:item.Package);
                        var report=await connection.DownloadManagedPackage(endpoint,entry.Catalog,item.Package,environment.Paths,token,null,p=>
                            ReportProgress(token,new ManagedStoreProgress(p.Stage,item.Package,transferredBefore+p.Received,current.DownloadBytes,ordinal,downloads.Length))).ConfigureAwait(false);
                        received+=item.Package.Artifact.SizeBytes;
                        packages.Add(report.InstallationInput(endpoint,entry.Catalog,item.Installed));
                    }
                    if(packages.Count==0) throw Error("ManagedAlreadyInstalled");
                    if(install)
                    {
                        // Withdrawal or any pointer change during transfer requires a new user review.
                        ReportProgress(token,new ManagedStoreProgress(ManagedProgressStage.Rechecking,null,received,current.DownloadBytes,index,downloads.Length));
                        audit.Event("managed.precommit_checking","ManagedInstall",bytes:received);
                        await Fresh(connection,cache,endpoint,expected,token).ConfigureAwait(false);
                        environment=await RefreshEnvironment(environment,token).ConfigureAwait(false);
                        audit.Event("managed.environment_rechecked","ManagedInstall",bytes:received);
                        var finalInventory=Inventory(environment,token);
                        if(new ManagedStorePlanner(entry.Catalog,environment,finalInventory,endpoint,expected.ReplacesPackages).Plan(selected,token).Identity!=expected.Identity) throw Error("ManagedStateChanged");
                        ReportProgress(token,new ManagedStoreProgress(ManagedProgressStage.Committing,null,received,current.DownloadBytes,index,downloads.Length));
                        audit.Event("managed.commit_started","ManagedInstall",bytes:received);
                        var result=installation.Install(new ManagedExtensionInstallRequest(packages),environment.DisabledModuleIds,token);
                        audit.TransactionId=result.TransactionId; audit.Event("managed.install_result","ManagedInstall",result.Code,referenceFailure:result.ReferenceFailure);
                        if(!result.Succeeded) throw new StoreValidationException(result.Code,"Managed installation refused.") {ReferenceFailure=result.ReferenceFailure};
                        inventory=Inventory(environment,CancellationToken.None);
                    }
                    return Result(install?ManagedStoreState.Installed:ManagedStoreState.Verified,before.Catalog,new RepositoryBrowseInfo(endpoint,DateTime.UtcNow,false,false),expected,inventory,install?"ManagedInstallSaved":"ManagedPayloadsVerified",committed:install);
                }
            },audit);
        }
        private async Task<ClientEnvironmentSnapshot> RefreshEnvironment(ClientEnvironmentSnapshot previous,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current=captureEnvironment==null?previous:await AwaitOperation(captureEnvironment,token,"EnvironmentCaptureTimeout").ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RequireEnvironment(current);
            if(previous==null || previous.Paths==null ||
                !string.Equals(previous.Paths.SaveDataRoot,current.Paths.SaveDataRoot,ClientPathOwnership.Comparison) ||
                !string.Equals(previous.Paths.LocalModsRoot,current.Paths.LocalModsRoot,ClientPathOwnership.Comparison) ||
                !string.Equals(previous.HostModRoot,current.HostModRoot,ClientPathOwnership.Comparison) ||
                previous.RimWorldVersion!=current.RimWorldVersion || previous.PhinixCompatibilityVersion!=current.PhinixCompatibilityVersion ||
                previous.AbstractionsCompatibilityVersion!=current.AbstractionsCompatibilityVersion) throw Error("ManagedEnvironmentChanged");
            lock(gate) updateEnvironment=current;
            return current;
        }
        private static async Task<ManagedRepositoryCacheEntry> Fresh(IManagedRepositoryAccess transport,ManagedRepositoryCache cache,RepositoryEndpoint endpoint,ManagedStorePlan expected,CancellationToken token)
        {
            // Always read the live pointer; a browsing cache cannot authorize installation.
            var entry=await Metadata(t=>ManagedRepositoryBrowser.Refresh(endpoint,cache,transport,t,true),token).ConfigureAwait(false);
            if(entry.Catalog.SnapshotId!=expected.Catalog.SnapshotId || entry.Catalog.Sha256!=expected.Catalog.Sha256) throw Error("SnapshotChanged");
            return entry;
        }
        internal Task Change(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState state,ClientEnvironmentSnapshot environment)
        {
            var before=Snapshot;
            return Run(ManagedStoreState.Managing,token=>
            {
                var result=management.ChangeDesiredState(expected,state,environment.DisabledModuleIds,token);
                if(!result.Succeeded) throw Error(result.Code);
                return Task.FromResult(Result(ManagedStoreState.Ready,before.Catalog,before.Repository,null,Inventory(environment,CancellationToken.None),"ManagedStateSaved",committed:true));
            });
        }
        private Task Run(ManagedStoreState state,Func<CancellationToken,Task<OperationResult>> work,RepositoryDiagnostics audit=null)
        {
            lock(gate)
            {
                if(disposed) throw new ObjectDisposedException(nameof(ManagedStoreController));
                if(operation.Busy) throw Error("StoreBusy");
                long generation=operation.Begin(state);
                audit=audit??new RepositoryDiagnostics(log,snapshot.Repository?.Endpoint.SourceId);
                audit.Operation=state.ToString();
                var source=new CancellationTokenSource(); running=source;
                lastProgressTicks=0;
                snapshot=new ManagedStoreSnapshot(snapshot.Revision+1,operation.State,snapshot.Catalog,snapshot.Repository,snapshot.Plan,snapshot.Inventory,null);
                return Task.Run(async ()=>
                {
                    OperationResult outcome;
                    try
                    {
                        source.Token.ThrowIfCancellationRequested();
                        outcome=await work(source.Token).ConfigureAwait(false);
                    }
                    catch(OperationCanceledException) { outcome=Result(ManagedStoreState.Canceled,null,null,null,null,"ManagedStoreCanceled"); }
                    catch(Exception ex)
                    {
                        var validation=ex as StoreValidationException;
                        string code=validation?.Code??(ex as ManagedExtensionValidationException)?.Code??"ManagedStoreFailed";
                        var referenceFailure=validation?.ReferenceFailure??(ex as ManagedExtensionValidationException)?.ReferenceFailure;
                        var failure=audit.Failure("managed.operation_failed","ManagedStore",ex,"ManagedStoreFailed");
                        code=failure.Code;
                        outcome=Result(ManagedStoreState.Failed,null,null,null,null,code,failure.RequestId,validation?.LocalIdentity,referenceFailure,contextReasons:failure.ContextReasons);
                    }
                    lock(gate)
                    {
                        try
                        {
                            var result=outcome.Snapshot;
                            // Cancellation and completion linearize under the same gate.
                            // Durable confirmations win; transient late results do not.
                            if(operation.CancellationRequested && !outcome.Committed && result.State!=ManagedStoreState.Failed && result.State!=ManagedStoreState.Canceled)
                                result=Result(ManagedStoreState.Canceled,null,null,null,null,"ManagedStoreCanceled").Snapshot;
                            if(ReferenceEquals(running,source) && operation.Complete(generation,result.State))
                                snapshot=new ManagedStoreSnapshot(snapshot.Revision+1,operation.State,result.Catalog??snapshot.Catalog,result.Repository??snapshot.Repository,
                                    result.State==ManagedStoreState.Failed || result.State==ManagedStoreState.Canceled?null:result.Plan,result.Inventory??snapshot.Inventory,result.Code,result.RequestId,result.LocalIdentity,result.ReferenceFailure,contextReasons:result.ContextReasons);
                        }
                        finally
                        {
                            if(ReferenceEquals(running,source)) running=null;
                            source.Dispose();
                        }
                    }
                });
            }
        }
        private void ReportProgress(CancellationToken token,ManagedStoreProgress progress)
        {
            lock(gate)
            {
                // A timed-out adapter may finish late, after cancellation or even a new operation.
                if(running==null || !operation.AcceptsProgress(operation.Generation) || running.Token!=token || token.IsCancellationRequested) return;
                long ticks=Stopwatch.GetTimestamp(); var previous=snapshot.Progress;
                if(previous!=null && (progress.Index<previous.Index || progress.Received<previous.Received)) return;
                bool boundary=previous==null || previous.Stage!=progress.Stage || previous.Package!=progress.Package || progress.Received==progress.Total;
                if(!boundary && ticks-lastProgressTicks<Stopwatch.Frequency/4) return;
                lastProgressTicks=ticks;
                snapshot=new ManagedStoreSnapshot(snapshot.Revision+1,snapshot.State,snapshot.Catalog,snapshot.Repository,snapshot.Plan,snapshot.Inventory,
                    snapshot.Code,snapshot.RequestId,snapshot.LocalIdentity,snapshot.ReferenceFailure,progress,snapshot.ContextReasons);
            }
        }
        private static Task<T> Metadata<T>(Func<CancellationToken,Task<T>> work,CancellationToken token)
            => AwaitOperation(work,token,"RepositoryTimeout");
        private static async Task<T> AwaitOperation<T>(Func<CancellationToken,Task<T>> work,CancellationToken token,string timeoutCode)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var task=work(deadline.Token); var delay=Task.Delay(30000,deadline.Token);
                if(await Task.WhenAny(task,delay).ConfigureAwait(false)!=task)
                { deadline.Cancel(); _=task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted); token.ThrowIfCancellationRequested(); throw Error(timeoutCode); }
                deadline.Cancel(); return await task.ConfigureAwait(false);
            }
        }
        internal void Cancel()
        {
            lock(gate)
            {
                if(running!=null && operation.RequestCancel(operation.Generation)) CancelRunning();
            }
        }
        public void Dispose()
        {
            lock(gate)
            {
                if(disposed) return;
                disposed=true;
                operation.Stop();
                snapshot=new ManagedStoreSnapshot(snapshot.Revision+1,operation.State,snapshot.Catalog,snapshot.Repository,null,snapshot.Inventory,"ManagedStoreStopped");
                CancelRunning();
            }
        }
        private void CancelRunning()
        {
            try { running?.Cancel(); }
            catch(Exception)
            {
                // Cancellation marks the token before invoking callbacks; a throwing
                // callback/logger must not prevent terminal stop or other cleanup.
                new RepositoryDiagnostics(log,snapshot.Repository?.Endpoint.SourceId).Event("managed.cancel_callback_failed","ManagedStore","ManagedStoreFailed");
            }
        }
        private sealed class OperationResult
        {
            internal OperationResult(ManagedStoreSnapshot snapshot,bool committed) { Snapshot=snapshot; Committed=committed; }
            internal ManagedStoreSnapshot Snapshot { get; }
            internal bool Committed { get; }
        }
        private static OperationResult Result(ManagedStoreState state,ManagedStoreCatalogSnapshot catalog,RepositoryBrowseInfo repository,ManagedStorePlan plan,ManagedExtensionManagementSnapshot inventory,string code,string request=null,LocalIdentityDiagnostic localIdentity=null,ManagedExtensionAssemblyReferenceFailure referenceFailure=null,bool committed=false,IEnumerable<string> contextReasons=null)
        { return new OperationResult(new ManagedStoreSnapshot(0,state,catalog,repository,plan,inventory,code,request,localIdentity,referenceFailure,contextReasons:contextReasons),committed); }
        private static StoreValidationException Error(string code) { return new StoreValidationException(code,"Managed shop operation: "+code); }
    }
}
