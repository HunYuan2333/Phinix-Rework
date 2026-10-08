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
    private static void StoreEnvironmentRefreshRegression(Dictionary<string,byte[]> files)
    {
        var queue=new Queue<Action>(); int captured=0;
        var task=StoreEnvironmentRefresh.Capture(()=>{captured++; return null;},queue.Enqueue,CancellationToken.None);
        Assert(captured==0 && !task.IsCompleted && queue.Count==1,"Environment capture waits for the main thread queue.");
        queue.Dequeue()();
        Assert(task.GetAwaiter().GetResult()==null && captured==1,"Capture executes only when the dispatcher runs it.");
        using(var source=new CancellationTokenSource())
        {
            task=StoreEnvironmentRefresh.Capture(()=>{captured++; return null;},queue.Enqueue,source.Token);
            source.Cancel(); bool canceled=false;
            try { task.GetAwaiter().GetResult(); } catch(OperationCanceledException) { canceled=true; }
            queue.Dequeue()();
            Assert(canceled && captured==1,"Cancellation releases waiting work and late callbacks do not capture.");
        }
        task=StoreEnvironmentRefresh.Capture(()=>{throw new InvalidOperationException("capture failed");},queue.Enqueue,CancellationToken.None);
        queue.Dequeue()(); bool failed=false;
        try { task.GetAwaiter().GetResult(); } catch(InvalidOperationException) { failed=true; }
        Assert(failed,"Capture errors propagate to the operation.");
        task=StoreEnvironmentRefresh.Capture(()=>null,action=>{throw new InvalidOperationException("queue failed");},CancellationToken.None);
        failed=false; try { task.GetAwaiter().GetResult(); } catch(InvalidOperationException) { failed=true; }
        Assert(failed,"Dispatcher errors do not leave pending capture tasks.");

        string root=Path.Combine(Path.GetTempPath(),"phinix-environment-refresh-"+Guid.NewGuid().ToString("N"));
        var facts=Facts(files); var endpoint=new RepositoryEndpoint("https://store.test","test.source"); string manifest=Manifest(files);
        byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        try
        {
            foreach(string mode in new[]{"stable","disabled","conflict","paths","incomplete","cancel"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,mode)); var env=FlowEnvironment(paths,facts); int refreshes=0;
                var audits=new List<string>(); var waiting=new TaskCompletionSource<ClientEnvironmentSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                var logs=new List<ManagedExtensionRuntimeAudit>();
                using(var runtime=EmptyRuntime(paths,facts,logs))
                using(var controller=new ManagedStoreController(runtime,runtime,audits.Add,token=>
                {
                    refreshes++;
                    if(refreshes==1) return Task.FromResult(env);
                    if(mode=="cancel")
                    {
                        token.Register(()=>waiting.TrySetCanceled()); return waiting.Task;
                    }
                    if(mode=="disabled") return Task.FromResult(FlowEnvironment(paths,facts,new[]{"test.managed","test.nested"}));
                    if(mode=="paths") return Task.FromResult(FlowEnvironment(new ManagedExtensionPaths(Path.Combine(root,"other")),facts));
                    return Task.FromResult(new ClientEnvironmentSnapshot(env.Paths,env.HostModRoot,env.RimWorldVersion,env.PhinixCompatibilityVersion,env.AbstractionsCompatibilityVersion,
                        env.InstalledMods,mode=="conflict"?env.LoadedAssemblies.Concat(new[]{new ClientLoadedAssemblySnapshot("Fixture.Managed.Plugin","1.0.0.0",env.HostModRoot)}):env.LoadedAssemblies,
                        env.Modules,mode=="incomplete"?new[]{"AssemblyIdentityUnavailable"}:new string[0],env.DisabledModuleIds));
                }))
                {
                    var handler=new ManagedFlowHandler {Mode="success",Catalog=Utf8(ManagedCatalog(ManagedListing(manifest,zip))),Payload=zip}; handler.SetMetadata();
                    controller.Refresh(endpoint,env,false,new RepositoryTransport(handler)).GetAwaiter().GetResult();
                    controller.Plan(controller.Snapshot.Catalog.Packages.Single(),env,controller.Snapshot.Catalog).GetAwaiter().GetResult();
                    var download=new ManagedFlowHandler {Mode="success",Catalog=handler.Catalog,Payload=zip}; download.SetMetadata();
                    var operation=controller.Download(controller.Snapshot.Plan,env,true,new RepositoryTransport(download));
                    if(mode=="cancel")
                    {
                        Assert(SpinWait.SpinUntil(()=>Volatile.Read(ref refreshes)==2,5000),"Install reaches queued precommit capture before cancellation.");
                        controller.Cancel();
                    }
                    operation.GetAwaiter().GetResult();
                    if(mode!="stable" && mode!="cancel")
                    {
                        Func<string,string> correlation=line=>System.Text.RegularExpressions.Regex.Match(line,"\"clientRequestId\":\"([a-f0-9]+)\"").Groups[1].Value;
                        Assert(correlation(audits.Single(a=>a.Contains("managed.download_started")))==correlation(audits.Single(a=>a.Contains("managed.operation_failed"))),
                            "Download failures keep the operation correlation ID: "+mode);
                    }
                    Assert(download.Disposed,"Environment recheck outcomes release the repository adapter: "+mode);
                    Assert(refreshes==2,"Environment is refreshed before transfer and before commit: "+mode);
                    string expected=mode=="stable"?"ManagedInstallSaved":mode=="disabled"?"ManagedAllModulesDisabled":mode=="conflict"?"CandidateAssemblyConflict":
                        mode=="paths"?"ManagedEnvironmentChanged":mode=="cancel"?"ManagedStoreCanceled":"IncompleteEnvironment";
                    Assert(controller.Snapshot.Code==expected,"Fresh environment controls installation: "+mode+" / "+controller.Snapshot.Code);
                    Assert(Directory.Exists(paths.PackagesDirectory)==(mode=="stable"),"Failed/canceled recapture creates no package tree: "+mode);
                    Assert(!Directory.Exists(paths.TransactionsDirectory) || !Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Recapture failure leaves no transaction: "+mode);
                    Assert(logs.Any(l=>l.Code=="ManagedInstallRequested")== (mode=="stable"),"Recapture precedes installation authority: "+mode);
                    if(mode=="stable") Assert(audits.Any(a=>a.Contains("managed.environment_rechecked")),"Successful precommit recapture is audited.");
                    if(mode=="incomplete") Assert(audits.Any(a=>a.Contains("AssemblyIdentityUnavailable")),"Incomplete recapture records the actual missing fact.");
                }
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
