using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static ManagedExtensionInstallRequest ReplacementRequest(Dictionary<string,byte[]> files,ManagedExtensionPackageSnapshot old,string manifest=null)
    {
        return new ManagedExtensionInstallRequest(new[]{new ManagedExtensionInstallPackage(old.SourceId,old.RepositoryIdentitySha256,new string('1',40),
            new string('2',64),new string('3',64),Utf8(manifest??Manifest(files).Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"")),files,old)});
    }
    private static void ReplacementRuntimeRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-replacement-"+Guid.NewGuid().ToString("N")); var facts=Facts(files);
        string[] before={"journal-written","file-written","package-staged","transition-written"};
        string[] after={"commit-decided","replacement-original-moved","package-moved","metadata-flushed","receipt-committed","state-committed","replacement-backup-file-deleted","work-deleted"};
        try
        {
            foreach(string point in before.Concat(after).Concat(new[]{"none"}))
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,point)); var logs=new List<ManagedExtensionRuntimeAudit>();
                string data=Path.Combine(paths.SaveDataRoot,"extension-data","test.package","keep.txt"); Directory.CreateDirectory(Path.GetDirectoryName(data)); File.WriteAllText(data,"retained");
                using(var runtime=EmptyRuntime(paths,facts,logs))
                {
                    Assert(runtime.Install(InstallationRequest(files),new string[0],CancellationToken.None).Succeeded,"Install old version before replacement "+point);
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    if(point!="none") runtime.InstallationFault=p=>{ if(p==point) throw new Crash(); };
                    var result=runtime.Install(ReplacementRequest(files,old),new string[0],CancellationToken.None);
                    Assert(result.Succeeded==(point=="none"),"Replacement reports interrupted transaction "+point+": "+result.Code);
                    Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Managed.Plugin"),"Replacement never hot-loads candidate code");
                }
                using(var lease=ManagedExtensionLease.Acquire(paths))
                {
                    var recovered=ManagedExtensionInstallationRecovery.Recover(paths,(c,r)=>{},CancellationToken.None);
                    Assert(recovered.Count==0,"Replacement recovery succeeds "+point+": "+string.Join(",",recovered));
                    Assert(ManagedExtensionInstallationRecovery.Recover(paths,(c,r)=>{},CancellationToken.None).Count==0,"Replacement recovery is repeatable "+point);
                    var row=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages.Single();
                    Assert(row.DiagnosticCode==null && row.Version==(before.Contains(point)?"1.0.0":"1.1.0"),"Durable decision chooses complete old/new version "+point);
                    Assert(!Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Recovered replacement leaves no pending transaction "+point);
                    Assert(File.ReadAllText(data)=="retained","Replacement preserves plugin data "+point);
                }
            }
            foreach(string changed in new[]{"stale-state","unknown-file","old-byte","after-commit-backup","after-commit-new"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,changed)); var logs=new List<ManagedExtensionRuntimeAudit>();
                using(var runtime=EmptyRuntime(paths,facts,logs))
                {
                    Assert(runtime.Install(InstallationRequest(files),new string[0],CancellationToken.None).Succeeded,"Prepare edited replacement "+changed);
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    var request=ReplacementRequest(files,old); string tree=paths.GetPackageDirectory(old.SourceId,old.PackageId);
                    if(changed=="stale-state") runtime.ChangeDesiredState(old,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None);
                    if(changed=="unknown-file") File.WriteAllText(Path.Combine(tree,"keep.txt"),"unowned");
                    if(changed=="old-byte") File.AppendAllText(Path.Combine(tree,files.Keys.First()),"edited");
                    if(changed.StartsWith("after-commit",StringComparison.Ordinal)) runtime.InstallationFault=p=>{ if(p=="package-moved") throw new Crash(); };
                    var result=runtime.Install(request,new string[0],CancellationToken.None);
                    Assert(!result.Succeeded,"Changed ownership closes replacement "+changed);
                    if(changed.StartsWith("after-commit",StringComparison.Ordinal))
                    {
                        string work=Path.Combine(paths.TransactionsDirectory,"in-"+result.TransactionId);
                        string guard=changed=="after-commit-backup"?Path.Combine(work,"backup",old.RecordKey,"keep.txt"):Path.Combine(tree,files.Keys.First());
                        Directory.CreateDirectory(Path.GetDirectoryName(guard)); File.AppendAllText(guard,"preserve"); byte[] bytes=File.ReadAllBytes(guard);
                        Assert(ManagedExtensionInstallationRecovery.Recover(paths,(c,r)=>{},CancellationToken.None).Count!=0,"Unverifiable replacement recovery refuses mutation "+changed);
                        Assert(File.ReadAllBytes(guard).SequenceEqual(bytes) && File.Exists(work+".json"),"Uncertain bytes and journal survive "+changed);
                    }
                    else Assert(!Directory.EnumerateFileSystemEntries(paths.TransactionsDirectory).Any(),"Invalid replacement creates no journal "+changed);
                }
            }
            foreach(string point in new[]{"journal-written","commit-decided"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,"cancel-"+point));
                using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
                using(var cancel=new CancellationTokenSource())
                {
                    runtime.Install(InstallationRequest(files),new string[0],CancellationToken.None);
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    runtime.InstallationFault=p=>{ if(p==point) cancel.Cancel(); };
                    bool canceled=false; ManagedExtensionInstallResult result=null;
                    try { result=runtime.Install(ReplacementRequest(files,old),new string[0],cancel.Token); } catch(OperationCanceledException) { canceled=true; }
                    Assert(point=="journal-written"?canceled:result?.Succeeded==true,"Replacement cancellation respects durable commit decision "+point);
                    Assert(runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package.Version==(canceled?"1.0.0":"1.1.0"),"Canceled replacement preserves complete authoritative outcome "+point);
                }
            }
            var providerFiles=new Dictionary<string,byte[]> {{"Assemblies/Fixture.Managed.Provider.dll",File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Fixtures","Fixture.Managed.Provider.dll"))}};
            string providerJson=Manifest(providerFiles,"test.provider");
            providerJson=providerJson.Substring(0,providerJson.IndexOf("\"modules\":[",StringComparison.Ordinal))+"\"modules\":[{\"id\":\"test.provider\",\"assemblyName\":\"Fixture.Managed.Provider\",\"entryType\":\"Fixture.Managed.Provider\",\"dependsOn\":[]}]}";
            string consumerJson=Manifest(files,dependencies:"[{\"packageId\":\"test.provider\",\"versionRange\":\"1.0.0\",\"optional\":false}]");
            var reversePaths=new ManagedExtensionPaths(Path.Combine(root,"reverse-dependent"));
            using(var runtime=EmptyRuntime(reversePaths,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                Assert(runtime.Install(new ManagedExtensionInstallRequest(InstallationRequest(providerFiles,providerJson).Packages.Concat(InstallationRequest(files,consumerJson).Packages)),new string[0],CancellationToken.None).Succeeded,"Prepare version-pinned reverse dependent");
                var provider=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single(p=>p.Package.PackageId=="test.provider").Package;
                var result=runtime.Install(ReplacementRequest(providerFiles,provider,providerJson.Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"")),new string[0],CancellationToken.None);
                Assert(!result.Succeeded && result.Code=="CandidatePackageDependencyUnavailable","Replacing a provider cannot break an installed consumer's pinned version");
                Assert(!Directory.EnumerateFileSystemEntries(reversePaths.TransactionsDirectory).Any(),"Broken reverse dependency is refused before writing a journal");
            }
            foreach(string point in new[]{"file-written","commit-decided","receipt-committed"})
            {
                var paths=new ManagedExtensionPaths(Path.Combine(root,"mixed-"+point));
                using(var runtime=EmptyRuntime(paths,facts,new List<ManagedExtensionRuntimeAudit>()))
                {
                    runtime.Install(InstallationRequest(providerFiles,providerJson),new string[0],CancellationToken.None);
                    var old=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().Package;
                    string v2=providerJson.Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"");
                    var upgrade=ReplacementRequest(providerFiles,old,v2).Packages.Single();
                    var consumer=new ManagedExtensionInstallPackage(upgrade.SourceId,upgrade.RepositoryIdentitySha256,upgrade.CatalogSnapshotId,upgrade.CatalogSha256,upgrade.ArtifactSha256,
                        Utf8(consumerJson.Replace("\"versionRange\":\"1.0.0\"","\"versionRange\":\"1.1.0\"")),files);
                    runtime.InstallationFault=p=>{ if(p==point) throw new Crash(); };
                    Assert(!runtime.Install(new ManagedExtensionInstallRequest(new[]{upgrade,consumer}),new string[0],CancellationToken.None).Succeeded,"Mixed new-package/replacement batch interrupted "+point);
                }
                using(var lease=ManagedExtensionLease.Acquire(paths))
                {
                    Assert(ManagedExtensionInstallationRecovery.Recover(paths,(c,r)=>{},CancellationToken.None).Count==0,"Mixed batch recovers "+point);
                    var rows=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None).Packages;
                    Assert(rows.All(p=>p.DiagnosticCode==null) && (point=="file-written"?rows.Count==1 && rows.Single().Version=="1.0.0":rows.Count==2 && rows.Single(p=>p.PackageId=="test.provider").Version=="1.1.0"),"Mixed batch remains wholly old or wholly new "+point);
                }
            }
            RunChild("upgrade-live");
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
