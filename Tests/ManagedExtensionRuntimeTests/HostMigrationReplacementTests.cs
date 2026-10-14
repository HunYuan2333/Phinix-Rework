using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void HostMigrationReplacementRegression(Dictionary<string,byte[]> files)
    {
        var baseline=Facts(files);
        var facts=new ManagedExtensionHostFacts("1.6","0.9.7","2.0.0",baseline.Assemblies,baseline.ModuleIds,baseline.ActiveModIds);
        var providerFiles=new Dictionary<string,byte[]> {{"Assemblies/Fixture.Managed.Provider.dll",File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Fixtures","Fixture.Managed.Provider.dll"))}};
        string provider=Manifest(providerFiles,"test.provider");
        provider=provider.Substring(0,provider.IndexOf("\"modules\":[",StringComparison.Ordinal))+"\"modules\":[{\"id\":\"test.provider\",\"assemblyName\":\"Fixture.Managed.Provider\",\"entryType\":\"Fixture.Managed.Provider\",\"dependsOn\":[]}]}";
        var payloads=new Dictionary<string,Dictionary<string,byte[]>> {{"test.package",files},{"test.provider",providerFiles}};
        var manifests=new Dictionary<string,string> {{"test.package",Manifest(files)},{"test.provider",provider}};
        string root=Path.Combine(Path.GetTempPath(),"phinix-host-migration-"+Guid.NewGuid().ToString("N"));
        Func<string,string> upgraded=json=>json.Replace(">=1.3.0 <2.0.0",">=2.0.0 <3.0.0").Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"");
        try
        {
            foreach(bool providerFirst in new[]{false,true})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,providerFirst?"provider-first":"consumer-first"));
                foreach(var pair in manifests) Install(paths,"test.source",pair.Value,payloads[pair.Key],"enabled");
                string data=Path.Combine(paths.SaveDataRoot,"extension-data","test.package","keep.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(data)); File.WriteAllText(data,"retained");
                var logs=new List<ManagedExtensionRuntimeAudit>();
                using(var runtime=EmptyRuntime(paths,facts,logs))
                {
                    Assert(runtime.Snapshot.Packages.Count==2 && runtime.Snapshot.Packages.All(p=>p.DiagnosticCode=="CandidateHostIncompatible" && !p.AssembliesLoaded),"Host API upgrade rejects both old packages without executing them");
                    string[] order=providerFirst?new[]{"test.provider","test.package"}:new[]{"test.package","test.provider"};
                    foreach(string id in order)
                    {
                        var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId==id).Package;
                        var result=runtime.Install(ReplacementRequest(payloads[id],old,upgraded(manifests[id])),new string[0],CancellationToken.None);
                        Assert(result.Succeeded,"Independent host migration succeeds in either order: "+id+": "+result.Code);
                        Assert(runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId==id).Package.Version=="1.1.0","Upgraded receipt is authoritative");
                    }
                    Assert(File.ReadAllText(data)=="retained","Host migration preserves business data");
                    Assert(!Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Sequential migration leaves no pending journal");
                    Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Managed.Plugin" || a.GetName().Name=="Fixture.Managed.Provider"),"Migration never hot-loads plugin code");
                }
            }
            foreach(string scenario in new[]{"incoming-incompatible","package-dependency"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,scenario));
                Install(paths,"test.source",Manifest(files),files,"enabled");
                Install(paths,"test.source",provider,providerFiles,"enabled");
                var logs=new List<ManagedExtensionRuntimeAudit>();
                using(var runtime=EmptyRuntime(paths,facts,logs))
                {
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId=="test.package").Package;
                    string candidate=scenario=="incoming-incompatible"?Manifest(files).Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\""):upgraded(Manifest(files));
                    if(scenario=="package-dependency") candidate=candidate.Replace("\"dependencies\":[]","\"dependencies\":[{\"packageId\":\"test.provider\",\"versionRange\":\"1.0.0\",\"optional\":false}]");
                    var result=runtime.Install(ReplacementRequest(files,old,candidate),new string[0],CancellationToken.None);
                    string expected=scenario=="incoming-incompatible"?"CandidateHostIncompatible":"CandidatePackageDependencyUnavailable";
                    Assert(!result.Succeeded && result.Code==expected,"Migration retains candidate/dependency gates: "+scenario+": "+result.Code);
                    Assert(logs.Any(e=>e.Stage=="management-preflight" && e.Package?.PackageId=="test.package" && e.Code==expected),"Rejection audit retains the actual failed package");
                    Assert(runtime.Refresh(new string[0],CancellationToken.None).Packages.All(p=>p.Package.Version=="1.0.0"),"Refusal preserves installed versions");
                    Assert(!Directory.Exists(paths.TransactionsDirectory) || !Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Refusal creates no journal");
                }
            }
            var newPaths=new ManagedExtensionPaths(Path.Combine(root,"new-independent"));
            Install(newPaths,"test.source",Manifest(files),files,"enabled");
            using(var runtime=EmptyRuntime(newPaths,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                var result=runtime.Install(InstallationRequest(providerFiles,upgraded(provider)),new string[0],CancellationToken.None);
                Assert(result.Succeeded,"An independent new compatible package can install beside a host-incompatible old package: "+result.Code);
                Assert(runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId=="test.package").Package.Version=="1.0.0","Unrelated old package is retained without implicit disabling or rewriting");
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
