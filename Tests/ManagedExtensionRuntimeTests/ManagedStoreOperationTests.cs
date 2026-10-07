using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void ManagedStoreOperationRegression(Dictionary<string,byte[]> files)
    {
        int before=assertions;
        StoreTransitionMatrix();
        StoreControllerCancellation(files);
        StoreControllerLateProgress(files);
        StoreControllerCommitBoundary(files);
        Console.WriteLine("Store operation regression passed: "+(assertions-before)+" assertions; "+typeof(Stateless.StateMachine<int,int>).Assembly.FullName);
    }
    private static ManagedStoreOperation StoreMachineAt(ManagedStoreState state)
    {
        var machine=new ManagedStoreOperation();
        if(state==ManagedStoreState.Idle) return machine;
        ManagedStoreState start=state==ManagedStoreState.PlanReady?ManagedStoreState.Planning:
            state==ManagedStoreState.Verified?ManagedStoreState.Downloading:state==ManagedStoreState.Installed?ManagedStoreState.Installing:ManagedStoreState.Reading;
        machine.Complete(machine.Begin(start),state);
        return machine;
    }
    private static void StoreOperationReject(Action action,string message)
    {
        bool rejected=false; try { action(); } catch(InvalidOperationException) { rejected=true; } catch(ArgumentException) { rejected=true; }
        Assert(rejected,message);
    }
    private static void StoreTransitionMatrix()
    {
        var stable=new[]{ManagedStoreState.Idle,ManagedStoreState.Ready,ManagedStoreState.PlanReady,ManagedStoreState.Verified,ManagedStoreState.Installed,ManagedStoreState.Failed,ManagedStoreState.Canceled};
        var rows=new Dictionary<ManagedStoreState,ManagedStoreState[]> {
            {ManagedStoreState.Reading,new[]{ManagedStoreState.Ready,ManagedStoreState.Idle}},
            {ManagedStoreState.Planning,new[]{ManagedStoreState.PlanReady}},
            {ManagedStoreState.Downloading,new[]{ManagedStoreState.Verified}},
            {ManagedStoreState.Installing,new[]{ManagedStoreState.Installed}},
            {ManagedStoreState.Managing,new[]{ManagedStoreState.Ready}} };
        foreach(var initial in stable)
        foreach(var row in rows)
        foreach(var outcome in row.Value.Concat(new[]{ManagedStoreState.Failed,ManagedStoreState.Canceled}))
        {
            var machine=StoreMachineAt(initial); long generation=machine.Begin(row.Key);
            Assert(machine.State==row.Key && machine.Busy && machine.AcceptsProgress(generation),"Start is passive and marks the expected busy state: "+initial+"/"+row.Key);
            StoreOperationReject(()=>machine.Begin(ManagedStoreState.Reading),"Busy operations cannot overlap");
            StoreOperationReject(machine.ResetRepository,"Repository changes cannot erase a running operation");
            foreach(var invalid in stable.Except(row.Value).Except(new[]{ManagedStoreState.Failed,ManagedStoreState.Canceled}))
            {
                StoreOperationReject(()=>machine.Complete(generation,invalid),"An unrelated success cannot complete "+row.Key+" as "+invalid);
                Assert(machine.State==row.Key,"Rejected transition must leave the active operation unchanged");
            }
            Assert(!machine.Complete(generation+1,outcome) && machine.State==row.Key,"A foreign generation cannot complete active work");
            Assert(machine.Complete(generation,outcome) && machine.State==outcome && !machine.Busy,"Only the declared terminal outcome is accepted");
            Assert(!machine.Complete(generation,outcome) && !machine.AcceptsProgress(generation),"Duplicate results/progress cannot revive completed work");
            long next=machine.Begin(ManagedStoreState.Reading);
            Assert(next>generation && !machine.Complete(generation,ManagedStoreState.Failed),"A retry rejects the previous operation generation");
            Assert(machine.RequestCancel(next) && machine.RequestCancel(next) && machine.State==ManagedStoreState.Reading && !machine.AcceptsProgress(next),"Cancel is idempotent and stays busy until the worker exits");
            machine.Stop(); machine.Stop();
            Assert(machine.State==ManagedStoreState.Stopped && !machine.Complete(next,ManagedStoreState.Ready),"Stop remains terminal despite late completion");
            StoreOperationReject(()=>machine.Begin(ManagedStoreState.Reading),"Stopped operation cannot restart");
            StoreOperationReject(machine.ResetRepository,"Stopped operation cannot reset to Idle");
        }
        foreach(var initial in stable)
        {
            var machine=StoreMachineAt(initial); machine.ResetRepository();
            Assert(machine.State==ManagedStoreState.Idle,"Changing repository resets only stable browse state");
            machine.Stop(); machine.Stop();
            Assert(machine.State==ManagedStoreState.Stopped,"Stable-state stop is terminal and idempotent");
        }
        var invalidStart=new ManagedStoreOperation();
        StoreOperationReject(()=>invalidStart.Begin(ManagedStoreState.Ready),"Success states cannot start work");
        Assert(invalidStart.State==ManagedStoreState.Idle && invalidStart.Generation==0,"Rejected start acquires no generation");
        Assert(new ManagedStoreOperation().State==ManagedStoreState.Idle,"A new controller state machine has no old operation state");
    }

    // Blocks only the requested Refresh call. No sleeps, real HTTP or player directories.
    private sealed class StoreBarrierManagement : IManagedExtensionManagementService,IDisposable
    {
        private readonly IManagedExtensionManagementService inner;
        internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim();
        internal readonly ManualResetEventSlim Release=new ManualResetEventSlim();
        internal bool BlockNext,IgnoreCancellation,ThrowCancellation;
        internal string Failure;
        internal int CancelCalls;
        internal Action AfterChange;
        internal StoreBarrierManagement(IManagedExtensionManagementService inner) { this.inner=inner; }
        public ManagedExtensionRuntimeSnapshot Snapshot => inner.Snapshot;
        internal void Block()
        { Entered.Reset(); Release.Reset(); BlockNext=true; }
        public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> disabled,CancellationToken token)
        {
            if(!BlockNext) return inner.Refresh(disabled,token);
            BlockNext=false;
            using(token.Register(()=>{ Interlocked.Increment(ref CancelCalls); if(ThrowCancellation) throw new Exception("cancellation callback"); }))
            {
                Entered.Set();
                if(!Release.Wait(TimeSpan.FromSeconds(10))) throw new Exception("test barrier timed out");
                if(Failure!=null) throw new StoreValidationException(Failure,"controlled failure");
                return inner.Refresh(disabled,IgnoreCancellation?CancellationToken.None:token);
            }
        }
        public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState desired,IEnumerable<string> disabled,CancellationToken token)
        {
            var result=inner.ChangeDesiredState(expected,desired,disabled,token);
            if(result.Succeeded) AfterChange?.Invoke();
            return result;
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    private static void StoreWait(ManualResetEventSlim signal)
    { if(!signal.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Store operation failed to reach its test barrier."); }

    private static void StoreControllerCancellation(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-store-state-cancel-"+Guid.NewGuid().ToString("N")); var facts=Facts(files);
        try
        {
            foreach(string mode in new[]{"cancel","stop","throwing-cancel","failure"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,mode)); var environment=FlowEnvironment(paths,facts);
                using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
                using(var management=new StoreBarrierManagement(runtime) {IgnoreCancellation=true,ThrowCancellation=mode=="throwing-cancel",Failure=mode=="failure"?"ControlledStoreFailure":null})
                using(var controller=new ManagedStoreController(management,runtime,_=>{throw new Exception("logger");}))
                {
                    management.Block(); var task=controller.RefreshInventory(environment); StoreWait(management.Entered);
                    try
                    {
                        Assert(controller.Snapshot.State==ManagedStoreState.Managing && controller.Snapshot.Busy,"Live controller publishes busy before blocked work");
                        StoreReject("StoreBusy",()=>controller.RefreshInventory(environment));
                        if(mode=="stop" || mode=="throwing-cancel")
                        {
                            controller.Dispose(); controller.Dispose(); var stopped=controller.Snapshot;
                            Assert(stopped.State==ManagedStoreState.Stopped && !stopped.Busy && stopped.Plan==null,"Dispose publishes terminal stop before cancellation callbacks");
                            StoreOperationReject(()=>controller.Refresh(new RepositoryEndpoint("https://another.test","test.source"),environment),"Refresh after stop cannot clear terminal state");
                            Assert(ReferenceEquals(stopped,controller.Snapshot),"Rejected endpoint change leaves the stopped snapshot unchanged");
                        }
                        else if(mode=="cancel")
                        {
                            controller.Cancel(); controller.Cancel();
                            Assert(controller.Snapshot.State==ManagedStoreState.Managing,"Cancel does not release the operation while its worker still owns resources");
                            StoreReject("StoreBusy",()=>controller.RefreshInventory(environment));
                        }
                    }
                    finally { management.Release.Set(); task.GetAwaiter().GetResult(); }
                    if(mode=="stop" || mode=="throwing-cancel")
                    {
                        Assert(controller.Snapshot.State==ManagedStoreState.Stopped && controller.Snapshot.Code=="ManagedStoreStopped","Late noncooperative success cannot overwrite Stopped");
                        Assert(management.CancelCalls==1,"Repeated dispose cannot repeat cancellation callbacks");
                        using(var rebuilt=new ManagedStoreController(runtime,runtime))
                        {
                            Assert(rebuilt.Snapshot.State==ManagedStoreState.Idle,"Rebuilt controller starts without stale state");
                            rebuilt.RefreshInventory(environment).GetAwaiter().GetResult();
                            Assert(rebuilt.Snapshot.State==ManagedStoreState.Ready,"New controller can use borrowed services left alive by old disposal");
                        }
                    }
                    else
                    {
                        Assert(controller.Snapshot.State==(mode=="cancel"?ManagedStoreState.Canceled:ManagedStoreState.Failed),"Canceled/noncooperative work or explicit failure publishes the correct outcome");
                        Assert(controller.Snapshot.Plan==null && controller.Snapshot.Progress==null,"Failure/cancellation removes stale plan and progress");
                        controller.RefreshInventory(environment).GetAwaiter().GetResult();
                        Assert(controller.Snapshot.State==ManagedStoreState.Ready,"Failure/cancellation releases ownership and allows retry");
                    }
                }
            }
        }
        finally { Directory.Delete(root,true); }
    }

    private sealed class StoreBarrierAccess : IManagedRepositoryAccess
    {
        private readonly CloudflareRepositoryAccess inner;
        internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action<ManagedPackageProgress> Late;
        internal bool IgnoreCancellation;
        internal int Downloads;
        internal StoreBarrierAccess(ManagedFlowHandler handler) { inner=new CloudflareRepositoryAccess(new RepositoryTransport(handler)); }
        public Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh)
        { return inner.ReadMetadata(endpoint,etag,token,fresh); }
        public async Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null)
        {
            Downloads++; Late=progress; Entered.TrySetResult(true);
            await Release.Task.ConfigureAwait(false);
            return await inner.DownloadManagedPackage(endpoint,catalog,package,paths,IgnoreCancellation?CancellationToken.None:token,budget,progress).ConfigureAwait(false);
        }
        public void Dispose() { inner.Dispose(); }
    }
    private static ManagedFlowHandler StoreHandler(string manifest,byte[] zip)
    {
        var handler=new ManagedFlowHandler {Mode="success",Catalog=Utf8(ManagedCatalog(ManagedListing(manifest,zip))),Payload=zip};
        handler.SetMetadata(); return handler;
    }
    private static void StoreControllerLateProgress(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-store-state-progress-"+Guid.NewGuid().ToString("N")); var facts=Facts(files);
        var paths=new ManagedExtensionPaths(root); var environment=FlowEnvironment(paths,facts); var endpoint=new RepositoryEndpoint("https://store.test","test.source");
        string manifest=Manifest(files); byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        try
        {
            using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
            using(var management=new StoreBarrierManagement(runtime))
            using(var controller=new ManagedStoreController(management,runtime))
            {
                controller.Refresh(endpoint,environment,false,new CloudflareRepositoryAccess(new RepositoryTransport(StoreHandler(manifest,zip)))).GetAwaiter().GetResult();
                var catalog=controller.Snapshot.Catalog; var selected=catalog.Packages.Single();
                controller.Plan(selected,environment,catalog).GetAwaiter().GetResult();
                var adapter=new StoreBarrierAccess(StoreHandler(manifest,zip)) {IgnoreCancellation=true};
                var transfer=controller.Download(controller.Snapshot.Plan,environment,false,adapter);
                if(!adapter.Entered.Task.Wait(TimeSpan.FromSeconds(10))) throw new Exception("Payload barrier timed out.");
                controller.Cancel(); var canceled=controller.Snapshot;
                adapter.Late(new ManagedPackageProgress(ManagedProgressStage.Downloading,1));
                Assert(ReferenceEquals(canceled,controller.Snapshot),"Progress is rejected immediately after cancellation request");
                adapter.Release.TrySetResult(true); transfer.GetAwaiter().GetResult();
                Assert(controller.Snapshot.State==ManagedStoreState.Canceled && runtime.Refresh(new string[0],CancellationToken.None).Packages.Count==0,"Canceled noncooperative download cannot publish Verified or install anything");
                management.Block(); var next=controller.Plan(selected,environment,catalog); StoreWait(management.Entered);
                try
                {
                    var current=controller.Snapshot;
                    adapter.Late(new ManagedPackageProgress(ManagedProgressStage.Validating,zip.Length));
                    Assert(ReferenceEquals(current,controller.Snapshot) && current.State==ManagedStoreState.Planning,"Old transfer progress cannot mutate a new active operation");
                }
                finally { management.Release.Set(); next.GetAwaiter().GetResult(); }
                Assert(controller.Snapshot.State==ManagedStoreState.PlanReady,"New planning survives stale transfer callbacks");
                using(var success=new StoreBarrierAccess(StoreHandler(manifest,zip)))
                {
                    success.Release.TrySetResult(true);
                    controller.Download(controller.Snapshot.Plan,environment,false,success).GetAwaiter().GetResult();
                    Assert(controller.Snapshot.State==ManagedStoreState.Verified && success.Downloads==1,"Retry verifies one download without repeated entry actions");
                    var done=controller.Snapshot; success.Late(new ManagedPackageProgress(ManagedProgressStage.Downloading,0));
                    Assert(ReferenceEquals(done,controller.Snapshot),"Completed-operation progress is ignored");
                }
            }
        }
        finally { Directory.Delete(root,true); }
    }
    private static void StoreControllerCommitBoundary(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-store-state-commit-"+Guid.NewGuid().ToString("N")); var facts=Facts(files);
        string manifest=Manifest(files); byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        try
        {
            foreach(string mode in new[]{"before-commit","after-commit","stop-after-commit","management-commit"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,mode)); var environment=FlowEnvironment(paths,facts); var endpoint=new RepositoryEndpoint("https://store.test","test.source");
                using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
                using(var management=new StoreBarrierManagement(runtime))
                using(var controller=new ManagedStoreController(management,runtime))
                {
                    controller.Refresh(endpoint,environment,false,new CloudflareRepositoryAccess(new RepositoryTransport(StoreHandler(manifest,zip)))).GetAwaiter().GetResult();
                    var catalog=controller.Snapshot.Catalog; controller.Plan(catalog.Packages.Single(),environment,catalog).GetAwaiter().GetResult();
                    int reached=0;
                    if(mode!="management-commit") runtime.InstallationFault=point=>
                    {
                        if(point!=(mode=="before-commit"?"transition-written":"commit-decided")) return;
                        reached++;
                        Assert(controller.Snapshot.State==ManagedStoreState.Installing && controller.Snapshot.Progress.Stage==ManagedProgressStage.Committing,"Full transfer/precommit state must not be reported as Installed");
                        if(mode=="stop-after-commit") controller.Dispose(); else controller.Cancel();
                    };
                    using(var transfer=new StoreBarrierAccess(StoreHandler(manifest,zip)))
                    {
                        transfer.Release.TrySetResult(true);
                        controller.Download(controller.Snapshot.Plan,environment,true,transfer).GetAwaiter().GetResult();
                        Assert(transfer.Downloads==1,"State transitions never rerun the actual download");
                    }
                    var inventory=runtime.Refresh(new string[0],CancellationToken.None);
                    if(mode=="before-commit")
                        Assert(reached==1 && controller.Snapshot.State==ManagedStoreState.Canceled && inventory.Packages.Count==0,"Precommit cancellation preserves rollback and no false Installed: reached="+reached+", state="+controller.Snapshot.State+", code="+controller.Snapshot.Code+", packages="+inventory.Packages.Count);
                    else if(mode=="stop-after-commit")
                    {
                        Assert(reached==1 && controller.Snapshot.State==ManagedStoreState.Stopped && inventory.Packages.Count==1,"A committed transaction stays durable while stopped UI rejects its late completion");
                        using(var rebuilt=new ManagedStoreController(runtime,runtime))
                        {
                            rebuilt.RefreshInventory(environment).GetAwaiter().GetResult();
                            Assert(rebuilt.Snapshot.Inventory.Packages.Count==1,"Rebuilt controller reads committed state without replaying installation");
                        }
                    }
                    else if(mode=="after-commit")
                        Assert(reached==1 && controller.Snapshot.State==ManagedStoreState.Installed && inventory.Packages.Count==1,"Authoritative install confirmation survives cancellation after the durable decision");
                    else
                    {
                        management.AfterChange=controller.Cancel;
                        controller.Change(inventory.Packages.Single().Package,ManagedExtensionDesiredState.Disabled,environment).GetAwaiter().GetResult();
                        Assert(controller.Snapshot.State==ManagedStoreState.Ready && controller.Snapshot.Code=="ManagedStateSaved" && controller.Snapshot.Inventory.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.Disabled,"Durable management confirmation survives cancellation and refreshes authoritative inventory");
                    }
                }
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
