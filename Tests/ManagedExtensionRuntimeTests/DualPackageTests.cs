using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework.ManagedExtensions;
using Utils.Framework;
using Phinix.PluginStore;
using PhinixClient.Framework;

internal static partial class Program
{
    // Real package bytes; installation and profile switching never execute business code.
        private static int DualPackageProbe(string file,string hostCompatibility="0.10.1")
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-dual-package-"+Guid.NewGuid().ToString("N"));
        try
        {
            ManagedExtensionZip package;
            using(var stream=File.OpenRead(file))package=ManagedExtensionZip.Read(stream,CancellationToken.None);
            Assert(package.Manifest.Variants.Count==2,"Both game variants are retained");
            Assert(package.Files.Count==package.Manifest.AllFiles.Count()+1,"ZIP has a single manifest and both complete payloads");
            foreach(string game in new[]{"1.6","1.7"})
            {
                var selected=package.Manifest.ForGame(game);
                Assert(selected.SelectedGame==game && selected.SupportsGame(game),"Explicit game selection");
                Assert(!selected.SupportsGame(game=="1.6"?"1.7":"1.6"),"Selected snapshot cannot cross runtime profiles");
                Assert(selected.Assemblies.All(a=>a.File.Path.StartsWith(game+"/",StringComparison.Ordinal)),"Only selected paths are executable");
                var bytes=package.Manifest.Variants.SelectMany(v=>v.Assemblies).ToDictionary(a=>a.File.Path,a=>package.CopyFile(a.File.Path));
                var inspected=ManagedExtensionPayloadInspector.InspectAll(selected,bytes,CancellationToken.None);
                Assert(inspected.Assemblies.All(a=>a.Metadata.TargetFramework==ManagedExtensionTargetFramework.MetadataName(selected.TargetFramework)),"Real selected DLL target framework");
                string inactive=bytes.Keys.First(k=>!k.StartsWith(game+"/",StringComparison.Ordinal));bytes[inactive][0]^=1;
                Failure("AssemblyDigestMismatch",()=>ManagedExtensionPayloadInspector.InspectAll(selected,bytes,CancellationToken.None));
                var owned=selected.Assemblies.Select(a=>a.Name).ToArray();
                var references=inspected.Assemblies.SelectMany(a=>a.Metadata.References).Where(a=>!owned.Contains(a.Name)).GroupBy(a=>a.FullName).Select(g=>g.First());
                if(game=="1.7" && AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!=null)
                {
                    // Do not invent framework facts from plugin references: use
                    // the production runtime inventory, including lazy BCLs.
                    var framework=ClientRuntimeAssemblyFacts.FrameworkReferences().Select(ManagedAssemblyIdentity.FromAssemblyName).ToArray();
                    references=references.Where(r=>!r.Name.StartsWith("System.",StringComparison.Ordinal)).Concat(framework).GroupBy(r=>r.FullName).Select(g=>g.First());
                    Assert(framework.Any(a=>a.Name=="System.Runtime.Serialization.Xml") && framework.Any(a=>a.Name=="System.Net.WebHeaderCollection"),"Production facts contain both log-reported lazy framework dependencies");
                }
                var moduleIds=selected.Modules.Select(m=>m.Id).ToArray();
                var facts=new ManagedExtensionHostFacts(game,hostCompatibility,"3.0.0",references,selected.Modules.SelectMany(m=>m.DependsOn).Where(id=>!moduleIds.Contains(id)),selected.ExternalMods.Select(m=>m.PackageId));
                var paths=new ManagedExtensionPaths(Path.Combine(root,game));
                var logs=new List<ManagedExtensionRuntimeAudit>();
                string codeRoot;
                string manifestText=System.Text.Encoding.UTF8.GetString(package.CopyManifestBytes());
                string listing=ManagedListing(manifestText,File.ReadAllBytes(file)).Replace(Q("id")+":"+Q("test.package"),Q("id")+":"+Q(selected.PackageId)).Replace(Q("v1.0.0"),Q("v"+selected.Version.ToString()));
                var catalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing)),ManagedSource);
                Assert(catalog.Packages.Count==1 && catalog.Packages.Single().RimWorldVersions.Count==2,"Store uses one listing/version for both games");
                Assert(ManagedStoreCatalogReader.VerifyManifest(catalog.Packages.Single(),package.CopyManifestBytes()).Variants.Count==2,"Store verifies the complete envelope digest");
                using(var runtime=EmptyRuntime(paths,facts,logs))
                {
                    var baseline=FlowEnvironment(paths,facts);
                    var environment=new ClientEnvironmentSnapshot(baseline.Paths,baseline.HostModRoot,game,hostCompatibility,"3.0.0",baseline.InstalledMods,baseline.LoadedAssemblies,
                        facts.ModuleIds.Select(id=>new ClientModuleSnapshot(id,baseline.HostModRoot,true)),new string[0]);
                    var planner=new ManagedStorePlanner(catalog,environment,runtime.Refresh(new string[0],CancellationToken.None),new RepositoryEndpoint("https://store.test",ManagedSource));
                    Assert(planner.Plan(catalog.Packages.Single(),CancellationToken.None)!=null,"Store plans the same package for this game");
                    var result=runtime.Install(new ManagedExtensionInstallRequest(new[]{package.LocalInstallation()}),new string[0],CancellationToken.None);
                    Assert(result.Succeeded,"Real dual package installs: "+result.Code);
                    var row=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None,game).Packages.Single();
                    Assert(row.DiagnosticCode==null && row.Manifest.SelectedGame==game,"Receipt verifies both payloads and selects game");
                    codeRoot=paths.GetPackageDirectory(row.SourceId,row.PackageId);
                    Assert(package.Manifest.AllFiles.All(f=>File.Exists(Path.Combine(codeRoot,f.Path))),"Both variants installed under one ownership record");
                    Assert(runtime.Refresh(new string[0],CancellationToken.None).Packages.Single().RestartPending,"Install waits for restart");
                    Assert(runtime.ChangeDesiredState(row,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None).Succeeded,"Business code remains disabled for metadata probe");
                }
                string other=game=="1.6"?"1.7":"1.6";
                var switched=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None,other).Packages.Single();
                Assert(switched.DiagnosticCode==null && switched.Manifest.SelectedGame==other,"Switch game without reinstalling or changing package version");
                using(var runtime=EmptyRuntime(paths,facts,logs))
                {
                    var row=runtime.Refresh(new string[0],CancellationToken.None).Packages.Single();
                    Assert(!row.Current.AssembliesLoaded,"Disabled restart executes no plugin");
                    Assert(runtime.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None).Succeeded,"Single uninstall intent owns both variants");
                }
                using(var runtime=EmptyRuntime(paths,facts,logs))
                    Assert(!Directory.Exists(codeRoot) && runtime.Refresh(new string[0],CancellationToken.None).Packages.Count==0,"Restart removes both game payloads");
            }
            Console.WriteLine("Dual package: "+assertions+" assertions passed; real DLLs, install/profile selection/restart/removal; no business activation.");
            return 0;
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
