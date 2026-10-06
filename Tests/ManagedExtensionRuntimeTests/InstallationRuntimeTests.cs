using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static ManagedExtensionInstallRequest InstallationRequest(Dictionary<string,byte[]> files,string manifest=null)
    {
        return new ManagedExtensionInstallRequest(new[]{new ManagedExtensionInstallPackage("test.source",new string('a',64),new string('b',40),
            new string('c',64),new string('d',64),Utf8(manifest??Manifest(files)),files)});
    }
    private static ManagedExtensionRuntime EmptyRuntime(ManagedExtensionPaths paths,ManagedExtensionHostFacts facts,List<ManagedExtensionRuntimeAudit> logs)
    {
        var runtime=new ManagedExtensionRuntime(paths,logs.Add); runtime.Start(facts,new string[0],new string[0],CancellationToken.None); return runtime;
    }
    private static void InstallationRuntimeRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-installation-"+Guid.NewGuid().ToString("N"));
        var facts=Facts(files); var logs=new List<ManagedExtensionRuntimeAudit>();
        try
        {
            var paths=new ManagedExtensionPaths(Path.Combine(root,"success"));
            var input=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone()); var request=InstallationRequest(input);
            foreach(var bytes in input.Values) bytes[0]^=1;
            using(var runtime=EmptyRuntime(paths,facts,logs))
            {
                var result=runtime.Install(request,new string[0],CancellationToken.None);
                Assert(result.Succeeded && result.Code=="ManagedInstallSaved" && result.Packages.Count==1,"Host installs a new frozen package");
                var row=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages.Single();
                Assert(row.DiagnosticCode==null && row.DesiredState==ManagedExtensionDesiredState.Enabled && row.InstallationTransactionId==result.TransactionId,"Installation binds verified ownership and next-start enabled intent");
                Assert(files.All(p=>File.ReadAllBytes(Path.Combine(paths.GetPackageDirectory(row.SourceId,row.PackageId),p.Key)).SequenceEqual(p.Value)),"Caller mutation cannot replace frozen installed bytes");
                Assert(runtime.Snapshot.Packages.Count==0 && runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().RestartPending,"Install never alters current startup execution state");
                Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Managed.Plugin"),"Installation does not execute or load plugin DLLs");
                Assert(!Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Completed installation removes only its transaction records");
                var repeat=runtime.Install(request,new string[0],CancellationToken.None);
                Assert(!repeat.Succeeded && repeat.Code=="ManagedInstallPackageConflict","Existing package cannot be overwritten by reinstall");
            }
            Assert(logs.Any(e=>e.Code=="ManagedInstallCommitRevalidated" && e.Package.InstallationTransactionId!=null) && logs.Any(e=>e.Code=="InstallCommitted"),"Audits correlate precommit verification and durable completion");
            var differentReferences=facts.Assemblies.Select(a=>a.Name=="Utils"?ManagedAssemblyIdentity.FromAssemblyName(new System.Reflection.AssemblyName(a.FullName.Replace("0.9.7.0","0.9.6.0"))):a);
            var differentHost=new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",differentReferences,facts.ModuleIds,facts.ActiveModIds);
            var wrongPaths=new ManagedExtensionPaths(Path.Combine(root,"wrong-reference")); var wrongLogs=new List<ManagedExtensionRuntimeAudit>();
            using(var runtime=EmptyRuntime(wrongPaths,differentHost,wrongLogs))
            {
                var result=runtime.Install(InstallationRequest(files),new string[0],CancellationToken.None);
                Assert(!result.Succeeded && result.Code=="CandidateAssemblyReferenceUnavailable","Host downgrade still refuses install");
                Assert(result.ReferenceFailure?.ReferencingAssembly=="Fixture.Managed.Plugin" && result.ReferenceFailure.RequiredReference.Contains("Utils, Version=0.9.7.0") && result.ReferenceFailure.AvailableReferences.Single().Contains("Utils, Version=0.9.6.0"),"Install result reports required and available identities");
                Assert(!Directory.Exists(wrongPaths.PackagesDirectory) && !Directory.Exists(wrongPaths.TransactionsDirectory),"Reference refusal precedes transaction/files");
                string audit=ManagedExtensionAuditJson.Format(wrongLogs.Single(e=>e.Code=="CandidateAssemblyReferenceUnavailable"));
                Assert(audit.Contains("\"requiredReference\":\"Utils, Version=0.9.7.0") && audit.Contains("\"availableReferences\":[\"Utils, Version=0.9.6.0") && !audit.Contains(wrongPaths.RootDirectory),"Host reference audit is correlated and excludes local paths");
            }

            var providerFiles=new Dictionary<string,byte[]> {{"Assemblies/Fixture.Managed.Provider.dll",File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Fixtures","Fixture.Managed.Provider.dll"))}};
            string providerJson=Manifest(providerFiles,"test.provider");
            providerJson=providerJson.Substring(0,providerJson.IndexOf("\"modules\":[",StringComparison.Ordinal))+"\"modules\":[{\"id\":\"test.provider\",\"assemblyName\":\"Fixture.Managed.Provider\",\"entryType\":\"Fixture.Managed.Provider\",\"dependsOn\":[]}]}";
            string consumerJson=Manifest(files,dependencies:"[{\"packageId\":\"test.provider\",\"versionRange\":\"1.0.0\",\"optional\":false}]");
            var batch=new ManagedExtensionInstallRequest(InstallationRequest(files,consumerJson).Packages.Concat(InstallationRequest(providerFiles,providerJson).Packages));
            foreach(string point in new[]{"none","receipt-committed"})
            {
                var joint=new ManagedExtensionPaths(Path.Combine(root,"batch-"+point));
                using(var runtime=EmptyRuntime(joint,facts,logs))
                {
                    if(point!="none") runtime.InstallationFault=p=>{if(p==point) throw new Crash();};
                    var result=runtime.Install(batch,new string[0],CancellationToken.None);
                    Assert(result.Succeeded==(point=="none"),"Joint dependency batch respects interrupted commit");
                }
                using(var lease=ManagedExtensionLease.Acquire(joint))
                {
                    Assert(ManagedExtensionInstallationRecovery.Recover(joint,(c,r)=>{},CancellationToken.None).Count==0,"Interrupted batch recovers completely");
                    var rows=ManagedExtensionInventoryReader.Read(joint,CancellationToken.None).Packages;
                    Assert(rows.Count==2 && rows.All(p=>p.DiagnosticCode==null) && rows.Select(p=>p.InstallationTransactionId).Distinct().Count()==1,"Both dependency packages share a complete transaction");
                }
            }
            var changed=new ManagedExtensionPaths(Path.Combine(root,"provider-changed"));
            using(var runtime=EmptyRuntime(changed,facts,logs))
            {
                Assert(runtime.Install(InstallationRequest(providerFiles,providerJson),new string[0],CancellationToken.None).Succeeded,"An unloaded verified provider can be installed first");
                runtime.InstallationFault=p=>{if(p=="transition-written")
                { string state=changed.GetDesiredStatePath("test.source","test.provider"); File.WriteAllText(state,File.ReadAllText(state).Replace("\"enabled\"","\"disabled\"")); }};
                var result=runtime.Install(InstallationRequest(files,consumerJson),new string[0],CancellationToken.None);
                Assert(!result.Succeeded && result.Code=="ManagedInstallRecoveryRequired" && logs.Any(e=>e.Code=="ManagedStateChanged"),"Provider intent is rechecked immediately before durable decision");
                Assert(!Directory.Exists(changed.GetPackageDirectory("test.source","test.package")),"Changed provider cannot authorize visible consumer installation");
            }
            using(var lease=ManagedExtensionLease.Acquire(changed))
                Assert(ManagedExtensionInstallationRecovery.Recover(changed,(c,r)=>{},CancellationToken.None).Count==0,"Rejected precommit batch rolls back without undoing provider intent");

            var resources=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone()); resources.Add("Resources/empty.txt",new byte[0]);
            string resourceManifest=Manifest(files).Replace("\"resources\":[]","\"resources\":["+FileEntry("Resources/empty.txt",new byte[0])+"]");
            using(var runtime=EmptyRuntime(new ManagedExtensionPaths(Path.Combine(root,"resources")),facts,logs))
                Assert(runtime.Install(InstallationRequest(resources,resourceManifest),new string[0],CancellationToken.None).Succeeded,"Declared empty resources are verified without entering PE inspection");

            string[] points={"journal-written","file-written","package-staged","transition-written","commit-decided","package-moved","metadata-flushed","receipt-committed","state-committed","work-deleted"};
            foreach(string point in points)
            {
                var interrupted=new ManagedExtensionPaths(Path.Combine(root,"crash-"+point)); string operation;
                using(var runtime=EmptyRuntime(interrupted,facts,logs))
                {
                    runtime.InstallationFault=p=>{if(p==point) throw new Crash();};
                    var result=runtime.Install(InstallationRequest(files),new string[0],CancellationToken.None); operation=result.TransactionId;
                    Assert(!result.Succeeded && result.Code=="ManagedInstallRecoveryRequired","Interrupted "+point+" preserves a recoverable transaction");
                    Assert(runtime.Refresh(new string[0],CancellationToken.None).Diagnostics.Contains("ManagedTransactionPending"),"Pending transaction blocks management mutations "+point);
                }
                using(var lease=ManagedExtensionLease.Acquire(interrupted))
                {
                    var errors=ManagedExtensionInstallationRecovery.Recover(interrupted,(c,r)=>{},CancellationToken.None);
                    Assert(errors.Count==0,"Recovery succeeds at "+point+": "+string.Join(",",errors));
                    bool committed=Array.IndexOf(points,point)>=4;
                    var inventory=ManagedExtensionInventoryReader.Read(interrupted,CancellationToken.None);
                    Assert(inventory.Diagnostics.Count==0 && inventory.Packages.Count==(committed?1:0) && inventory.Packages.All(p=>p.DiagnosticCode==null && p.InstallationTransactionId==operation),"Recovery rolls "+(committed?"forward":"back")+" exact owned bytes at "+point);
                    Assert(ManagedExtensionInstallationRecovery.Recover(interrupted,(c,r)=>{},CancellationToken.None).Count==0 && !Directory.EnumerateFileSystemEntries(interrupted.TransactionsDirectory).Any(),"Repeated replay is idempotent at "+point);
                }
            }
            foreach(string point in points.Take(5))
            {
                var canceled=new ManagedExtensionPaths(Path.Combine(root,"cancel-"+point));
                using(var runtime=EmptyRuntime(canceled,facts,logs))
                using(var source=new CancellationTokenSource())
                {
                    runtime.InstallationFault=p=>{if(p==point) source.Cancel();}; bool thrown=false; ManagedExtensionInstallResult result=null;
                    try { result=runtime.Install(InstallationRequest(files),new string[0],source.Token); } catch(OperationCanceledException) { thrown=true; }
                    bool commit=point=="commit-decided";
                    Assert(commit?result?.Succeeded==true:thrown,"Cancellation obeys durable commit boundary at "+point);
                    Assert(ManagedExtensionInventoryReader.Read(canceled,CancellationToken.None).Packages.Count==(commit?1:0),"Cancellation preserves all-or-nothing visible installation at "+point);
                    Assert(!Directory.EnumerateFileSystemEntries(canceled.TransactionsDirectory).Any(),"Clean cancellation/completion leaves no journal at "+point);
                }
            }
            foreach(string mode in new[]{"unknown-work","changed-stage","changed-journal","target-conflict"})
            {
                var guarded=new ManagedExtensionPaths(Path.Combine(root,mode)); string operation;
                using(var runtime=EmptyRuntime(guarded,facts,logs))
                {
                    runtime.InstallationFault=p=>{if(p=="package-staged") throw new Crash();};
                    operation=runtime.Install(InstallationRequest(files),new string[0],CancellationToken.None).TransactionId;
                }
                string work=Path.Combine(guarded.TransactionsDirectory,"in-"+operation);
                string sentinel=mode=="target-conflict"?Path.Combine(guarded.GetPackageDirectory("test.source","test.package"),"keep.txt"):
                    mode=="changed-journal"?work+".json":mode=="unknown-work"?Path.Combine(work,"unknown.txt"):
                    Path.Combine(work,"stage",ManagedExtensionPaths.PackageKey("test.source","test.package"),"Assemblies/Fixture.Managed.Plugin.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(sentinel)); File.AppendAllText(sentinel,"preserve"); byte[] before=File.ReadAllBytes(sentinel);
                using(var lease=ManagedExtensionLease.Acquire(guarded))
                {
                    Assert(ManagedExtensionInstallationRecovery.Recover(guarded,(c,r)=>{},CancellationToken.None).Count!=0,"Changed "+mode+" closes recovery");
                    Assert(File.ReadAllBytes(sentinel).SequenceEqual(before) && File.Exists(work+".json"),"Recovery never deletes unverifiable evidence "+mode);
                }
                using(var runtime=EmptyRuntime(guarded,facts,logs)) Assert(runtime.Snapshot.Diagnostics.Count!=0 && runtime.Snapshot.Packages.All(p=>!p.AssembliesLoaded),"Startup fails managed loading closed for "+mode);
            }
            foreach(string mode in new[]{"digest","dependency","disabled-modules","disabled-host","incompatible","external-mod","pending-transaction"})
            {
                var guarded=new ManagedExtensionPaths(Path.Combine(root,"reject-"+mode)); var content=files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone()); string json=Manifest(files);
                if(mode=="digest") content.Values.First()[0]^=1;
                if(mode=="dependency") json=Manifest(files,dependencies:"[{\"packageId\":\"test.missing\",\"versionRange\":\"1.0.0\",\"optional\":false}]");
                if(mode=="incompatible") json=json.Replace("\"1.6\"","\"1.5\"");
                if(mode=="external-mod") json=Manifest(files,external:"[{\"packageId\":\"missing.mod\"}]");
                using(var runtime=EmptyRuntime(guarded,facts,logs))
                {
                    if(mode=="pending-transaction") { Directory.CreateDirectory(guarded.TransactionsDirectory); File.WriteAllText(Path.Combine(guarded.TransactionsDirectory,"unknown.json"),"keep"); }
                    var disabled=mode=="disabled-modules"?new[]{"test.managed","test.nested"}:mode=="disabled-host"?new[]{"builtin.host"}:new string[0];
                    var result=runtime.Install(InstallationRequest(content,json),disabled,CancellationToken.None);
                    Assert(!result.Succeeded && result.Code!="ManagedInstallRecoveryRequired", "Preflight rejects "+mode+" without a transaction: "+result.Code);
                    Assert(!Directory.Exists(guarded.PackagesDirectory),"Rejected "+mode+" creates no package tree");
                }
            }
            RunChild("install-startup");
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
