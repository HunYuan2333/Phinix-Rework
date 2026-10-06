using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private sealed class BlockingManagementService : IManagedExtensionManagementService
    {
        internal IManagedExtensionManagementService Inner;
        internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(), Release=new ManualResetEventSlim();
        internal string[] Captured; internal bool Block=true, FailRefresh;
        public ManagedExtensionRuntimeSnapshot Snapshot => Inner.Snapshot;
        public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> disabled, CancellationToken token)
        {
            Captured=disabled.ToArray(); Entered.Set();
            if(Block) Release.Wait(token);
            if(FailRefresh) throw new IOException("private-path-detail");
            return Inner.Refresh(disabled,token);
        }
        public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState desired,IEnumerable<string> disabled,CancellationToken token)
        { return Inner.ChangeDesiredState(expected,desired,disabled,token); }
    }
    private static void ManagementControllerRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-manager-controller-"+Guid.NewGuid().ToString("N"));
        try
        {
            var paths=new ManagedExtensionPaths(root); Install(paths,"test.source",Manifest(files),files,"disabled");
            using(var runtime=new ManagedExtensionRuntime(paths))
            {
                runtime.Start(Facts(files),new string[0],new string[0],CancellationToken.None);
                var wrapper=new BlockingManagementService{Inner=runtime}; var disabled=new List<string>();
                int details=0, thread=Thread.CurrentThread.ManagedThreadId;
                using(var controller=new ManagedExtensionManagerController(error=>{ Assert(Thread.CurrentThread.ManagedThreadId==thread,"Internal manager callbacks run during UI polling"); details++; }))
                {
                    controller.Refresh(wrapper,disabled); Assert(wrapper.Entered.Wait(5000),"Background refresh entered service");
                    Assert(controller.Busy && controller.Snapshot==null,"Drawing can continue while filesystem work is blocked");
                    disabled.Add("changed-after-capture"); controller.Refresh(wrapper,disabled);
                    wrapper.Release.Set(); WaitController(controller);
                    Assert(wrapper.Captured.Length==0 && controller.Snapshot.Packages.Count==1,"Settings are copied before background work and duplicate starts are ignored");
                    Assert(!controller.Busy && controller.MessageCode=="ManagedInventoryReady","Refresh has a terminal UI outcome");
                    wrapper.Block=false;
                    var expected=controller.Snapshot.Packages.Single().Package;
                    controller.Change(wrapper,expected,ManagedExtensionDesiredState.Enabled,new string[0]); WaitController(controller);
                    Assert(controller.LastChange.Succeeded && controller.Snapshot.Packages.Single().RestartPending,"Successful intent refreshes desired state without loading DLLs");
                    wrapper.FailRefresh=true;
                    expected=controller.Snapshot.Packages.Single().Package;
                    controller.Change(wrapper,expected,ManagedExtensionDesiredState.Disabled,new string[0]); WaitController(controller);
                    Assert(controller.LastChange.Succeeded && controller.MessageCode=="ManagedStateSavedRefreshFailed","Post-commit refresh failure is not reported as a failed write");
                    Assert(ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages.Single().DesiredState==ManagedExtensionDesiredState.Disabled && details==1,"Saved intent persists and internal detail is delivered once");
                    controller.Refresh(wrapper,new string[0]); WaitController(controller);
                    Assert(controller.MessageCode=="ManagedInventoryReadFailed" && controller.Snapshot!=null && !controller.MessageCode.Contains("private"),"Read failure retains valid view with safe public code");
                }
                var pending=new BlockingManagementService{Inner=runtime};
                var closed=new ManagedExtensionManagerController(); closed.Refresh(pending,new string[0]);
                Assert(pending.Entered.Wait(5000),"Close test entered blocked service"); closed.Dispose(); pending.Release.Set();
                bool disposed=false; try { closed.Refresh(pending,new string[0]); } catch(ObjectDisposedException) { disposed=true; }
                Assert(disposed,"Closed management controller rejects later actions");
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static void WaitController(ManagedExtensionManagerController controller)
    {
        var end=DateTime.UtcNow.AddSeconds(5);
        while(controller.Busy && DateTime.UtcNow<end) { controller.Poll(); if(controller.Busy) Thread.Sleep(5); }
        Assert(!controller.Busy,"Management background work has a bounded terminal wait");
    }
}
