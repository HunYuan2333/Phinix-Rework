using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void ManagementRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-management-"+Guid.NewGuid().ToString("N"));
        var facts=Facts(files); var logs=new List<ManagedExtensionRuntimeAudit>();
        try
        {
            var paths=new ManagedExtensionPaths(Path.Combine(root,"states")); Install(paths,"test.source",Manifest(files),files,"disabled");
            string data=Path.Combine(paths.SaveDataRoot,"Phinix","ExtensionData","test.managed","keep.txt"); Directory.CreateDirectory(Path.GetDirectoryName(data)); File.WriteAllText(data,"keep");
            using(var runtime=new ManagedExtensionRuntime(paths,logs.Add))
            {
                runtime.Start(facts,new string[0],new string[0],CancellationToken.None);
                var view=runtime.Refresh(new string[0],CancellationToken.None); var row=view.Packages.Single();
                Assert(row.Package.DesiredState==ManagedExtensionDesiredState.Disabled && !row.Current.AssembliesLoaded && !row.RestartPending,"Disabled unloaded packages are visible without execution");
                Assert(view.Diagnostics.Count==0 && row.EnableBlockCode==null && row.RemovalBlockCode==null,"Verified unloaded package offers management intents");
                Assert(runtime.Refresh(new[]{"test.managed"},CancellationToken.None).Packages.Single().ModulesRestartPending,"Module switches are displayed separately from package restart intent");
                var result=runtime.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.Enabled,new string[0],CancellationToken.None);
                Assert(result.Succeeded && result.OperationId!=row.Package.StateOperationId,"Enable writes a new owned operation");
                var enabled=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single();
                Assert(enabled.RestartPending && enabled.Package.DesiredState==ManagedExtensionDesiredState.Enabled && !enabled.Current.AssembliesLoaded,"Intent does not load bytes in-session");
                Assert(runtime.Snapshot.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.Disabled,"Startup inventory remains immutable");
                var unchanged=runtime.ChangeDesiredState(enabled.Package,ManagedExtensionDesiredState.Enabled,new string[0],CancellationToken.None);
                Assert(unchanged.Succeeded && unchanged.Code=="ManagedStateUnchanged" && unchanged.OperationId==enabled.Package.StateOperationId,"Repeated identical intent is idempotent");
                var stale=runtime.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None);
                Assert(!stale.Succeeded && stale.Code=="ManagedStateChanged","Outdated view cannot overwrite a newer intent");
                var canceled=new CancellationTokenSource(); canceled.Cancel();
                bool canceledThrown=false; try { runtime.ChangeDesiredState(enabled.Package,ManagedExtensionDesiredState.Disabled,new string[0],canceled.Token); } catch(OperationCanceledException) { canceledThrown=true; }
                Assert(canceledThrown && ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages.Single().StateOperationId==enabled.Package.StateOperationId,"Pre-cancel preserves the last complete intent");
                var disabled=runtime.ChangeDesiredState(enabled.Package,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None);
                Assert(disabled.Succeeded && !runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().RestartPending,"Reversing enable before restart restores original desired state");
                var blocked=runtime.ChangeDesiredState(disabled.Package,ManagedExtensionDesiredState.Enabled,new[]{"builtin.host"},CancellationToken.None);
                Assert(!blocked.Succeeded && blocked.Code=="ManagedModuleDependencyUnavailable","Current host disable settings block dependent enable");
                var removal=runtime.ChangeDesiredState(disabled.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None);
                Assert(removal.Succeeded && Directory.Exists(paths.GetPackageDirectory("test.source","test.package")),"Removal saves intent without deleting current code");
                var undo=runtime.ChangeDesiredState(removal.Package,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None);
                Assert(undo.Succeeded,"Removal may be canceled before startup creates a journal");
                var removed=runtime.ChangeDesiredState(undo.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None);
                Assert(removed.Succeeded,"Removal can be requested again with fresh operation identity");
                Failure("ManagedStoreBusy",()=>ManagedExtensionLease.Acquire(paths));
            }
            using(var runtime=new ManagedExtensionRuntime(paths,logs.Add))
            { runtime.Start(facts,new string[0],new string[0],CancellationToken.None); Assert(runtime.Snapshot.Packages.Count==0 && File.ReadAllText(data)=="keep","Next startup recovers saved removal and preserves business data"); }
            Assert(logs.Any(e=>e.Code=="ManagedStateSavedPendingRemoval" && e.Package.StateOperationId!=null),"State audit correlates the new operation and stable stage");

            var reverse=new ManagedExtensionPaths(Path.Combine(root,"reverse")); Install(reverse,"test.source",Manifest(files),files,"disabled");
            var consumer=Manifest(files,"test.consumer","[{\"packageId\":\"test.package\",\"versionRange\":\"1.0.0\",\"optional\":false}]");
            Install(reverse,"test.source",consumer,files,"disabled");
            using(var runtime=new ManagedExtensionRuntime(reverse,logs.Add))
            {
                runtime.Start(facts,new string[0],new string[0],CancellationToken.None);
                var row=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId=="test.package");
                Assert(row.RemovalBlockCode=="ManagedHasDependents" && row.DisableBlockCode==null,"Disabled consumer still protects removal but permits provider disabling");
                Assert(!runtime.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None).Succeeded,"Reverse dependencies are rechecked on operation");
                Assert(row.ModuleSettingsBlockCode=="CandidateModuleConflict","Colliding unexecuted declarations cannot toggle another package's module settings");
                var dependent=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId=="test.consumer");
                Assert(runtime.ChangeDesiredState(dependent.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None).Succeeded,"Dependent may be marked for removal first");
                Assert(runtime.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None).Succeeded,"Provider removal is permitted after its dependent has explicit removal intent");
            }
            using(var runtime=new ManagedExtensionRuntime(reverse,logs.Add))
            { runtime.Start(facts,new string[0],new string[0],CancellationToken.None); Assert(runtime.Snapshot.Packages.Count==0,"Dependency removal chain recovers in one next startup"); }
            foreach(string mode in new[]{"tamper","transaction","host-dependent","bad-pe","provenance"})
            {
                var guarded=new ManagedExtensionPaths(Path.Combine(root,mode)); Install(guarded,"test.source",Manifest(files),files,"disabled");
                using(var runtime=new ManagedExtensionRuntime(guarded,logs.Add))
                {
                    var h=mode=="host-dependent"?new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",facts.Assemblies,facts.ModuleIds,facts.AvailableModuleIds,facts.ActiveModIds,new[]{new ManagedExtensionHostModule("builtin.host",new[]{"test.managed"})}):facts;
                    runtime.Start(h,new string[0],new string[0],CancellationToken.None);
                    var expected=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    if(mode=="tamper") File.AppendAllText(Path.Combine(guarded.GetPackageDirectory(expected.SourceId,expected.PackageId),"Assemblies/Fixture.Managed.Plugin.dll"),"edited");
                    if(mode=="transaction") { Directory.CreateDirectory(guarded.TransactionsDirectory); File.WriteAllText(Path.Combine(guarded.TransactionsDirectory,"unknown.json"),"preserve"); }
                    if(mode=="provenance")
                    {
                        string receipt=guarded.GetInstalledRecordPath("test.source","test.package");
                        File.WriteAllText(receipt,File.ReadAllText(receipt).Replace(new string('d',64),new string('a',64)));
                    }
                    if(mode=="bad-pe")
                    {
                        // Reinstall a byte-verified receipt with intentionally invalid CLR metadata.
                        var malformed=new Dictionary<string,byte[]>(files); malformed["Assemblies/Fixture.Managed.Plugin.dll"]=new byte[256];
                        string original=Manifest(files); string invalid=original.Replace(FileEntry("Assemblies/Fixture.Managed.Plugin.dll",files["Assemblies/Fixture.Managed.Plugin.dll"]).Substring(1),FileEntry("Assemblies/Fixture.Managed.Plugin.dll",malformed["Assemblies/Fixture.Managed.Plugin.dll"]).Substring(1));
                        Install(guarded,"test.source",invalid,malformed,"disabled"); expected=ManagedExtensionInventoryReader.Read(guarded,CancellationToken.None).Packages.Single();
                    }
                    byte[] before=File.ReadAllBytes(guarded.GetDesiredStatePath("test.source","test.package"));
                    var result=runtime.ChangeDesiredState(expected,mode=="bad-pe"?ManagedExtensionDesiredState.Enabled:ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None);
                    Assert(!result.Succeeded && File.ReadAllBytes(guarded.GetDesiredStatePath("test.source","test.package")).SequenceEqual(before),"Refused "+mode+" intent preserves state");
                    if(mode=="provenance") Assert(result.Code=="ManagedStateChanged","A stale provenance snapshot cannot authorize a state write even with unchanged manifest and operation");
                }
            }
            RunChild("managed-intent");
            foreach(string point in new[]{"state-flushed","state-committed"})
            {
                var crash=new ManagedExtensionPaths(Path.Combine(root,point)); Install(crash,"test.source",Manifest(files),files,"disabled");
                using(var lease=ManagedExtensionLease.Acquire(crash))
                {
                    var row=ManagedExtensionInventoryReader.Read(crash,CancellationToken.None).Packages.Single();
                    bool interrupted=false; try { ManagedExtensionDesiredStateWriter.Write(crash,row,ManagedExtensionDesiredState.Enabled,CancellationToken.None,p=>{if(p==point) throw new Crash();}); } catch(Crash) { interrupted=true; }
                    var current=ManagedExtensionInventoryReader.Read(crash,CancellationToken.None).Packages.Single();
                    Assert(interrupted && current.DiagnosticCode==null && current.DesiredState==(point=="state-flushed"?ManagedExtensionDesiredState.Disabled:ManagedExtensionDesiredState.Enabled),"Interrupted single-file state replacement preserves old or new complete intent");
                }
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
