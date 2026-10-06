using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    [PhinixExtension("builtin.host")]
    public sealed class HostModule : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    { public string ExtensionId=>"builtin.host"; public void Register(IExtensionBuilder builder) {} public void Activate(ExtensionHostContext context) {} public void Shutdown(ExtensionHostContext context) {} }
    private sealed class Crash : Exception { }
    private static ManagedExtensionHostFacts Facts(Dictionary<string,byte[]> files)
    {
        // Only trusted test fixtures request the framework compatibility shim on .NET 10.
        foreach(var reference in files.Values.SelectMany(bytes=>ManagedExtensionMetadataReader.Read(bytes).References))
            if(reference.Name=="mscorlib") Assembly.Load(new AssemblyName(reference.FullName));
        return new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>ManagedAssemblyIdentity.FromAssemblyName(a.GetName())),new[]{"builtin.host"},new string[0]);
    }
    private static void StartupRegression(Dictionary<string,byte[]> files)
    {
        var facts=Facts(files); var logs=new List<ManagedExtensionRuntimeAudit>();
        string root=Path.Combine(Path.GetTempPath(),"phinix-startup-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            foreach(string state in new[]{"disabled","pending-removal","enabled"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,state)); Install(paths,"test.source",Manifest(files),files,state);
                using(var runtime=new ManagedExtensionRuntime(paths,logs.Add))
                {
                    runtime.Start(facts,new[]{"test.managed","test.nested"},new string[0],CancellationToken.None);
                    Assert(runtime.Snapshot.Packages.All(p=>!p.AssembliesLoaded),"Excluded states and all disabled modules never load DLLs");
                    if(state=="enabled") Assert(runtime.Snapshot.Packages.Single().DiagnosticCode=="ManagedAllModulesDisabled","Legacy module disable is honored before byte loading");
                    if(state=="pending-removal") Assert(runtime.Snapshot.Packages.Count==0 && !Directory.Exists(paths.GetPackageDirectory("test.source","test.package")),"Removal finishes before candidates are inspected");
                    Assert(logs.Any(e=>e.Stage=="startup"),"Startup outcome is logged without shop activation");
                    Failure("ManagedStoreBusy",()=>ManagedExtensionLease.Acquire(paths));
                }
                using(var lease=ManagedExtensionLease.Acquire(paths)) Assert(true,"Runtime disposal releases the lease");
            }
            var cyclic=new ManagedExtensionPaths(Path.Combine(root,"host-cycle")); Install(cyclic,"test.source",Manifest(files),files,"enabled");
            var cyclicFacts=new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",facts.Assemblies,facts.ModuleIds,facts.AvailableModuleIds,facts.ActiveModIds,new[]{new ManagedExtensionHostModule("builtin.host",new[]{"test.managed"})});
            using(var runtime=new ManagedExtensionRuntime(cyclic,logs.Add))
            { runtime.Start(cyclicFacts,new string[0],new string[0],CancellationToken.None); Assert(runtime.Snapshot.Packages.Single().DiagnosticCode=="ManagedModuleDependencyCycle" && !runtime.Snapshot.Packages.Single().AssembliesLoaded,"Cross host/managed module cycle is rejected before loading"); }
            foreach(string point in new[]{"journal-written","package-moved","file-deleted","package-deleted","receipt-deleted","state-deleted"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,point)); Install(paths,"test.source",Manifest(files),files,"pending-removal");
                string data=Path.Combine(paths.SaveDataRoot,"Phinix","ExtensionData","test.managed","keep.txt"); Directory.CreateDirectory(Path.GetDirectoryName(data)); File.WriteAllText(data,"preserve");
                using(var lease=ManagedExtensionLease.Acquire(paths))
                {
                    bool crashed=false;
                    try { ManagedExtensionRemovalRecovery.Recover(paths,new string[0],new string[0],null,CancellationToken.None,stage=>{if(stage==point) throw new Crash();}); }
                    catch(Crash) { crashed=true; }
                    Assert(crashed,"Injected interruption at "+point);
                    Assert(Directory.EnumerateFiles(paths.TransactionsDirectory,"*.json").Any(),"Durable intent survives interruption");
                    Assert(ManagedExtensionRemovalRecovery.Recover(paths,new string[0],new string[0],null,CancellationToken.None).Count==0,"Recovery completes at "+point);
                    Assert(ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages.Count==0,"Owned record removed after replay");
                    Assert(!Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Journal/quarantine cleanup is idempotent");
                    Assert(ManagedExtensionRemovalRecovery.Recover(paths,new string[0],new string[0],null,CancellationToken.None).Count==0,"Second replay is a no-op");
                    Assert(File.ReadAllText(data)=="preserve","Business data survives removal and replay");
                }
            }
            foreach(string change in new[]{"changed-file","unknown-file","unknown-dir","changed-state"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,change)); Install(paths,"test.source",Manifest(files),files,"pending-removal");
                using(var lease=ManagedExtensionLease.Acquire(paths))
                {
                    try { ManagedExtensionRemovalRecovery.Recover(paths,new string[0],new string[0],null,CancellationToken.None,stage=>{if(stage=="package-moved") throw new Crash();}); } catch(Crash) {}
                    string quarantine=Path.Combine(paths.TransactionsDirectory,"rm-"+new string('f',32),"package");
                    if(change=="changed-file") File.AppendAllText(Path.Combine(quarantine,"Assemblies/Fixture.Managed.Plugin.dll"),"changed");
                    if(change=="unknown-file") File.WriteAllText(Path.Combine(quarantine,"unknown.txt"),"keep");
                    if(change=="unknown-dir") Directory.CreateDirectory(Path.Combine(quarantine,"unknown"));
                    if(change=="changed-state") File.AppendAllText(paths.GetDesiredStatePath("test.source","test.package")," ");
                    Assert(ManagedExtensionRemovalRecovery.Recover(paths,new string[0],new string[0],null,CancellationToken.None).Count!=0,"Changed quarantine/intent blocks replay");
                    Assert(Directory.Exists(quarantine) && File.Exists(paths.GetInstalledRecordPath("test.source","test.package")),"Failed recovery preserves files and ownership evidence");
                }
            }
            var blocked=new ManagedExtensionPaths(Path.Combine(root,"reverse")); Install(blocked,"test.source",Manifest(files),files,"pending-removal");
            var codes=new List<string>();
            using(var lease=ManagedExtensionLease.Acquire(blocked))
            {
                ManagedExtensionRemovalRecovery.Recover(blocked,new[]{"test.managed"},new string[0],(code,row)=>codes.Add(code),CancellationToken.None);
                Assert(codes.Contains("RemovalHasDependents") && Directory.Exists(blocked.GetPackageDirectory("test.source","test.package")),"Host module reverse dependency prevents removal");
                codes.Clear(); ManagedExtensionRemovalRecovery.Recover(blocked,new string[0],new[]{"Fixture.Managed.Plugin"},(code,row)=>codes.Add(code),CancellationToken.None);
                Assert(codes.Contains("RemovalAssemblyAlreadyLoaded"),"Loaded assembly prevents deletion");
            }
            using(var runtime=new ManagedExtensionRuntime(blocked,logs.Add))
            { runtime.Start(facts,new string[0],new[]{"test.managed"},CancellationToken.None); Assert(runtime.Snapshot.Packages.Single().DiagnosticCode=="RemovalHasDependents","Blocked pending removal exposes its actual reason in the runtime inventory"); }
            var malformed=new ManagedExtensionPaths(Path.Combine(root,"malformed-journal")); Install(malformed,"test.source",Manifest(files),files,"pending-removal");
            using(var lease=ManagedExtensionLease.Acquire(malformed))
            {
                try { ManagedExtensionRemovalRecovery.Recover(malformed,new string[0],new string[0],null,CancellationToken.None,stage=>{if(stage=="journal-written") throw new Crash();}); } catch(Crash) {}
                string journal=Directory.GetFiles(malformed.TransactionsDirectory,"*.json").Single(); File.AppendAllText(journal," ");
                Assert(ManagedExtensionRemovalRecovery.Recover(malformed,new string[0],new string[0],null,CancellationToken.None).Contains("RemovalJournalNotCanonical"),"Changed journal is refused before moving files");
                Assert(Directory.Exists(malformed.GetPackageDirectory("test.source","test.package")),"Invalid journal preserves the original package");
            }
            var unknown=new ManagedExtensionPaths(Path.Combine(root,"unknown-transaction")); Install(unknown,"test.source",Manifest(files),files,"enabled");
            Directory.CreateDirectory(unknown.TransactionsDirectory); File.WriteAllText(Path.Combine(unknown.TransactionsDirectory,"unknown.json"),"keep");
            var details=new List<Exception>();
            using(var runtime=new ManagedExtensionRuntime(unknown,logs.Add,details.Add))
            { runtime.Start(facts,new string[0],new string[0],CancellationToken.None); Assert(runtime.Snapshot.Diagnostics.Contains("UnknownManagedTransaction") && runtime.Snapshot.Packages.All(p=>!p.AssembliesLoaded),"Unknown transaction closes managed load domain"); }
            Assert(details.OfType<ManagedExtensionValidationException>().Any(error=>error.Code=="UnknownManagedTransaction"),"Recovery failures reach the internal diagnostic sink");
            Assert(logs.All(entry=>!ManagedExtensionAuditJson.Format(entry).Contains(root)),"Public audit contains no local paths");
            Assert(logs.Any(entry=>entry.Package?.InstallationTransactionId!=null && entry.Package.StateOperationId!=null),"Ownership/state correlation is emitted");
            Assert(logs.All(entry=>entry.TimeUtc.Kind==DateTimeKind.Utc && ManagedExtensionAuditJson.Format(entry).Contains("\"recordKey\"")) &&
                logs.GroupBy(entry=>entry.StartupId).All(group=>group.Select(entry=>entry.Sequence).SequenceEqual(Enumerable.Range(1,group.Count()).Select(value=>(long)value))),
                "Audit records have UTC timestamps and consecutive per-startup sequence numbers");
            RunChild("positive"); RunChild("lease"); RunChild("host-upgrade");
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static int StartupChild(string[] args)
    {
        if(args[0]=="lease")
        { try { using(var lease=ManagedExtensionLease.Acquire(new ManagedExtensionPaths(args[1]))) return 7; } catch(ManagedExtensionValidationException ex) { return ex.Code=="ManagedStoreBusy"?0:8; } }
        if(args[0]!="positive" && args[0]!="managed-intent" && args[0]!="install-startup" && args[0]!="upgrade-live" && args[0]!="upgrade-restart" && args[0]!="host-upgrade") return 9;
        string root=args[0]=="upgrade-restart"?args[1]:Path.Combine(Path.GetTempPath(),"phinix-loaded-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Fixtures");
            var files=new Dictionary<string,byte[]> {{"Assemblies/Fixture.Managed.Plugin.dll",File.ReadAllBytes(Path.Combine(fixture,"Fixture.Managed.Plugin.dll"))},{"Assemblies/Fixture.Managed.Helper.dll",File.ReadAllBytes(Path.Combine(fixture,"Fixture.Managed.Helper.dll"))}};
            var paths=new ManagedExtensionPaths(root);
            if(args[0]=="host-upgrade")
            {
                files.Remove("Assemblies/Fixture.Managed.Helper.dll");
                Assembly.Load(File.ReadAllBytes(Path.Combine(fixture,"HostUpgrade","Fixture.Managed.Helper.dll")));
            }
            if(args[0]=="install-startup")
            {
                using(var installing=EmptyRuntime(paths,Facts(files),new List<ManagedExtensionRuntimeAudit>()))
                { Assert(installing.Install(InstallationRequest(files),new string[0],CancellationToken.None).Succeeded,"Actual host installation completes before next-start registry loading"); }
            }
            else if(args[0]!="upgrade-restart")
            {
                var content=new Dictionary<string,byte[]>(files); string json=Manifest(files);
                if(args[0]=="positive")
                {
                    var language=new Dictionary<string,byte[]> {{"Resources/Localization/zh-CN.json",Language("zh-CN","{\"tab\":\"托管翻译\"}")}};
                    json=json.Replace("\"resources\":[]","\"resources\":["+string.Join(",",language.Select(p=>FileEntry(p.Key,p.Value)))+"],\"localization\":{\"files\":["+string.Join(",",language.Keys.Select(Q))+"]}");
                    foreach(var pair in language) content.Add(pair.Key,pair.Value);
                }
                Install(paths,"test.source",json,content,"enabled");
            }
            string sentinel=Path.Combine(root,args[0]=="upgrade-restart"?"executed-restart":"executed"); Environment.SetEnvironmentVariable("PHINIX_MANAGED_METADATA_SENTINEL",sentinel);
            var logs=new List<ManagedExtensionRuntimeAudit>();
            using(var runtime=new ManagedExtensionRuntime(paths,logs.Add,error=>Console.Error.WriteLine(error)))
            {
                var originalFacts=Facts(files);
                var chainFacts=new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",originalFacts.Assemblies,originalFacts.ModuleIds,originalFacts.AvailableModuleIds,originalFacts.ActiveModIds,new[]{new ManagedExtensionHostModule("builtin.host",new[]{"test.nested"})});
                runtime.Start(args[0]=="managed-intent"?originalFacts:chainFacts,new string[0],new string[0],CancellationToken.None);
                Assert(runtime.Snapshot.Packages.Single().DiagnosticCode==null && runtime.Snapshot.Packages.Single().AssembliesLoaded,"Actual frozen byte loading succeeds");
                Assert(!File.Exists(sentinel),"Loading/type enumeration do not instantiate module");
                var context=new ExtensionHostContext{HostKind="test",ResolveSourcePackageId=a=>{ManagedExtensionPackageSnapshot row;return runtime.TryGetOwner(a,out row)?row.SourceId+":"+row.PackageId:null;}};
                context.AddService<IExtensionDiscoveryPolicy>(runtime);
                var discovered=PhinixExtensionRegistry.DiscoverExtensions(context);
                Assert(discovered.ExtensionResults.Count(r=>r.ExtensionId=="test.managed" || r.ExtensionId=="test.nested")==2,"Generic registry discovers declared owned entries");
                Assert(discovered.ExtensionResults.Where(r=>r.ExtensionId.StartsWith("test.",StringComparison.Ordinal)).All(r=>r.State==ExtensionModuleState.Registered || r.State==ExtensionModuleState.Active),"Normal registration succeeds with helper DLL resolution");
                Assert(File.Exists(sentinel),"Generic registry, not loader, instantiates module");
                var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Fixture.Managed.Plugin");
                ManagedExtensionPackageSnapshot owner;
                Assert(runtime.TryGetOwner(assembly,out owner) && owner.SourceId=="test.source" && string.IsNullOrEmpty(assembly.Location),"Byte assemblies have explicit source ownership without Mod location");
                if(args[0]=="positive")
                {
                    var language=runtime.GetLocalization(assembly);
                    Assert(language.Resolve("tab","en-US")=="托管翻译","Managed startup loads a single non-English file without a store service");
                    File.WriteAllText(Path.Combine(paths.GetPackageDirectory("test.source","test.package"),"Resources/Localization/zh-CN.json"),"changed after startup");
                    Assert(language.Resolve("tab","zh-CN")=="托管翻译","Loaded language data is frozen, never read from a changed file during GUI lookup");
                    var service=new PhinixClient.Framework.ClientLocalizationService(discovered.Modules.Select(m=>m.GetType()),t=>runtime.TryGetOwner(t.Assembly,out owner)?runtime.GetLocalization(t.Assembly):ExtensionLocalizationCatalog.Empty,()=>true,null);
                    context.AddService<PhinixClient.Framework.IClientLocalizationService>(service); context.AddService<IExtensionModuleLifecycleObserver>(service);
                }
                var resolver=typeof(ManagedExtensionRuntime).GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic);
                var helper=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Fixture.Managed.Helper");
                string helperReference=ManagedExtensionMetadataReader.Read(files["Assemblies/Fixture.Managed.Plugin.dll"]).References.Single(r=>r.Name=="Fixture.Managed.Helper").FullName;
                Assert(ReferenceEquals(resolver.Invoke(runtime,new object[]{null,new ResolveEventArgs(helperReference,assembly)}),helper),"Declared CLR reference resolves to the prepared helper object");
                if(args[0]=="host-upgrade")
                {
                    Assert(helper.GetName().Version==new Version(1,3,0,0),"Actual newer host helper is used by registration");
                    Assert(logs.Any(e=>e.Code=="ManagedHostReferenceUpgraded" && e.ReferenceFailure.RequiredReference==helperReference && e.ReferenceFailure.AvailableReferences.Single()==helper.FullName),"Upgrade audit records requested and selected host identities");
                    Assert(resolver.Invoke(runtime,new object[]{null,new ResolveEventArgs(helper.FullName,assembly)})==null,"New host alias does not authorize undeclared reference requests");
                }
                Assert(resolver.Invoke(runtime,new object[]{null,new ResolveEventArgs("Unknown.Plugin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",assembly)})==null,"Undeclared request has no fallback probing");
                Assert(resolver.Invoke(runtime,new object[]{null,new ResolveEventArgs(helper.FullName,typeof(Program).Assembly)})==null,"Resolver does not handle another caller's request");
                PhinixExtensionRegistry.ActivateExtensions(discovered,context);
                if(args[0]=="positive")
                {
                    var localizer=context.GetRequiredService<PhinixClient.Framework.IClientLocalizationService>().ForModule(discovered.Modules.Single(m=>m.ExtensionId=="test.managed"));
                    Assert(localizer.Text("tab")=="托管翻译","Actual discovered managed module binds its verified package language after activation");
                }
                var baseType=assembly.GetType("Fixture.Managed.ModuleBase");
                Assert((int)baseType.GetField("Activations").GetValue(null)==2 && discovered.ExtensionResults.Where(r=>r.ExtensionId.StartsWith("test.",StringComparison.Ordinal)).All(r=>r.State==ExtensionModuleState.Active),"Generic activation executes both managed modules");
                if(args[0]=="upgrade-live")
                {
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    Assert(runtime.Install(ReplacementRequest(files,old),new string[0],CancellationToken.None).Succeeded,"Loaded owned package can save a next-start version replacement");
                    var saved=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single();
                    Assert(saved.Package.Version=="1.1.0" && saved.RestartPending,"New receipt is installed while activation waits for restart");
                    Assert(runtime.TryGetOwner(assembly,out owner) && owner.Version=="1.0.0" && runtime.ShouldScanAssembly(assembly),"Current-session owner and code stay on the previous loaded version");
                    Assert((int)baseType.GetField("Activations").GetValue(null)==2,"Replacement does not activate plugin again");
                }
                if(args[0]=="upgrade-restart") Assert(runtime.TryGetOwner(assembly,out owner) && owner.Version=="1.1.0","A fresh process discovers and activates the replaced version");
                if(args[0]=="managed-intent")
                {
                    var loaded=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single();
                    var disabled=runtime.ChangeDesiredState(loaded.Package,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None);
                    Assert(disabled.Succeeded && runtime.Snapshot.Packages.Single().AssembliesLoaded && runtime.ShouldScanAssembly(assembly),"Disabling loaded package preserves current-session assembly discovery");
                    var removal=runtime.ChangeDesiredState(disabled.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None);
                    Assert(removal.Succeeded && runtime.ShouldScanAssembly(assembly) && Directory.Exists(paths.GetPackageDirectory(removal.Package.SourceId,removal.Package.PackageId)),"Loaded-package removal only writes next-start intent");
                    Assert(discovered.ExtensionResults.Where(r=>r.ExtensionId.StartsWith("test.",StringComparison.Ordinal)).All(r=>r.State==ExtensionModuleState.Active),"State operations never invoke hot shutdown or change module activation");
                }
                PhinixExtensionRegistry.ShutdownExtensions(discovered,context);
                Assert((int)baseType.GetField("Shutdowns").GetValue(null)==2,"Generic shutdown is paired for every module");
                runtime.RecordLifecycle(discovered.ExtensionResults);
                Assert(logs.Any(e=>e.Stage=="lifecycle" && e.ModuleId=="test.managed"),"Registration outcome is correlated");
                Assert(!runtime.ShouldDiscoverType(assembly.GetType("Fixture.Managed.ModuleBase")),"Undeclared helper types never enter discovery");
                runtime.Dispose(); Assert(!runtime.ShouldScanAssembly(assembly),"Disposed managed domain remains excluded from discovery");
                Assert(!PhinixExtensionRegistry.DiscoverExtensions(context).ExtensionResults.Any(r=>r.ExtensionId.StartsWith("test.",StringComparison.Ordinal)),"Registry actually applies the discovery gate after disposal");
            }
            if(args[0]=="upgrade-live") RunChild("upgrade-restart",root);
            Console.WriteLine("Actual managed startup/registry child passed: "+assertions+" assertions."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Environment.SetEnvironmentVariable("PHINIX_MANAGED_METADATA_SENTINEL",null); if(args[0]!="upgrade-restart" && Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static void RunChild(string mode,string existing=null)
    {
        string path=existing??Path.Combine(Path.GetTempPath(),"phinix-lease-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            using(var lease=mode=="lease"?ManagedExtensionLease.Acquire(new ManagedExtensionPaths(path)):null)
            {
                string exe=Process.GetCurrentProcess().MainModule.FileName;
                string prefix=Path.GetFileName(exe).StartsWith("mono",StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(exe)=="dotnet"?QuoteArg(typeof(Program).Assembly.Location)+" ":"";
                var info=new ProcessStartInfo(exe,prefix+mode+(mode=="lease" || existing!=null?" "+QuoteArg(path):"")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var process=Process.Start(info))
                {
                    var outputTask=process.StandardOutput.ReadToEndAsync(); var errorTask=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(15000)) { process.Kill(); process.WaitForExit(5000); throw new Exception("Startup child timed out"); }
                    string output=outputTask.GetAwaiter().GetResult(),error=errorTask.GetAwaiter().GetResult();
                    Assert(process.ExitCode==0,"Independent "+mode+" child: "+output+error); if(output.Length!=0) Console.Write(output);
                }
            }
        }
        finally { if(existing==null && Directory.Exists(path)) Directory.Delete(path,true); }
    }
    private static string QuoteArg(string value)
    {
        var result=new System.Text.StringBuilder("\""); int backslashes=0;
        foreach(char c in value)
        {
            if(c=='\\') { backslashes++; continue; }
            result.Append('\\',c=='"'?backslashes*2+1:backslashes); result.Append(c); backslashes=0;
        }
        return result.Append('\\',backslashes*2).Append('"').ToString();
    }
}
