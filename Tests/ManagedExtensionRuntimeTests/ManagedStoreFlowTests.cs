using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private sealed class ManagedFlowHandler : HttpMessageHandler
    {
        internal byte[] Catalog,Published,Stable,Payload;
        internal string Mode;
        internal int Pointers,Packages;
        internal int FreshPointers;
        internal Action Sending;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); string path=request.RequestUri.AbsolutePath;
            Sending?.Invoke();
            byte[] body; string type="application/json"; var status=HttpStatusCode.OK;
            if(path.EndsWith("/stable",StringComparison.Ordinal)) { Pointers++; if(request.Headers.CacheControl?.NoCache==true) FreshPointers++; body=Stable; }
            else if(path.Contains("/published/")) body=Published;
            else if(path.Contains("/catalog/")) body=Catalog;
            else
            {
                Packages++; body=(byte[])Payload.Clone(); type="application/octet-stream";
                if(Mode=="digest") body[0]^=1;
                if(Mode=="redirect") status=HttpStatusCode.Redirect;
                if(Mode=="withdraw")
                {
                    Catalog=Utf8(Encoding.UTF8.GetString(Catalog).Replace(new string('b',40),new string('e',40)));
                    SetMetadata();
                }
            }
            var response=new HttpResponseMessage(status) { RequestMessage=request,Content=new ByteArrayContent(body) };
            response.Content.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue(type);
            response.Content.Headers.ContentLength=body.Length;
            return Task.FromResult(response);
        }
        internal bool Disposed;
        protected override void Dispose(bool disposing) { Disposed=true; base.Dispose(disposing); }
        internal void SetMetadata()
        {
            string snapshot=Encoding.UTF8.GetString(Catalog).Contains(new string('e',40))?new string('e',40):new string('b',40);
            string common="\"schemaVersion\":1,\"sourceId\":\"test.source\",\"snapshotId\":"+Q(snapshot)+",\"catalogSchemaVersion\":3,\"catalogSha256\":"+Q(CatalogReader.Hash(Catalog))+",\"catalogSizeBytes\":"+Catalog.Length;
            Published=Utf8("{"+common+",\"repository\":\"test-owner/index\",\"repositoryId\":\"11\",\"ownerId\":\"12\",\"releaseId\":\"13\",\"assetId\":\"14\",\"assetName\":\"catalog.json\"}");
            Stable=Utf8("{"+common+",\"publishedSha256\":"+Q(CatalogReader.Hash(Published))+",\"publishedSizeBytes\":"+Published.Length+"}");
        }
    }
    private sealed class ProgressFlowAccess : IManagedRepositoryAccess
    {
        private readonly CloudflareRepositoryAccess inner;
        private readonly Action observe;
        internal Action<ManagedPackageProgress> Late;
        internal ProgressFlowAccess(ManagedFlowHandler handler,Action observe)
        { inner=new CloudflareRepositoryAccess(new RepositoryTransport(handler)); this.observe=observe; }
        public Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh)
        { return inner.ReadMetadata(endpoint,etag,token,fresh); }
        public Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null)
        {
            Late=progress;
            return inner.DownloadManagedPackage(endpoint,catalog,package,paths,token,budget,p=>{ progress(p); observe(); });
        }
        public void Dispose() { inner.Dispose(); }
    }
    private static ClientEnvironmentSnapshot FlowEnvironment(ManagedExtensionPaths paths,ManagedExtensionHostFacts facts,IEnumerable<string> disabled=null)
    {
        string mods=Path.Combine(paths.SaveDataRoot,"Mods"),host=Path.Combine(mods,"Host");
        return new ClientEnvironmentSnapshot(new ClientEnvironmentPaths(mods,paths.SaveDataRoot),host,"1.6","0.9.7","1.5.0",
            new[]{new ClientInstalledModSnapshot("test.host",host,true)},facts.Assemblies.Select(a=>new ClientLoadedAssemblySnapshot(a.Name,a.Version,host)),
            new[]{new ClientModuleSnapshot("builtin.host",host,true)},new string[0],disabled??new string[0]);
    }
    private static void ManagedStoreFlowRegression(Dictionary<string,byte[]> files)
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-managed-shop-"+Guid.NewGuid().ToString("N"));
        var facts=Facts(files); var endpoint=new RepositoryEndpoint("https://store.test","test.source"); string manifest=Manifest(files);
        byte[] zip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(manifest))}));
        try
        {
            foreach(string mode in new[]{"success","digest","redirect","withdraw","offline","reference"})
            {
                var selectedFacts=mode=="reference"?new ManagedExtensionHostFacts("1.6","0.9.7","1.5.0",facts.Assemblies.Select(a=>a.Name=="Utils"?ManagedAssemblyIdentity.FromAssemblyName(new System.Reflection.AssemblyName(a.FullName.Replace("0.9.7.0","0.9.6.0"))):a),facts.ModuleIds,facts.ActiveModIds):facts;
                var paths=new ManagedExtensionPaths(Path.Combine(root,mode)); var logs=new List<ManagedExtensionRuntimeAudit>(); var env=FlowEnvironment(paths,selectedFacts);
                using(var runtime=EmptyRuntime(paths,selectedFacts,logs))
                using(var controller=new ManagedStoreController(runtime,runtime))
                {
                    var handler=new ManagedFlowHandler {Mode=mode,Catalog=Utf8(ManagedCatalog(ManagedListing(manifest,zip))),Payload=zip}; handler.SetMetadata();
                    controller.Refresh(endpoint,env,false,new RepositoryTransport(handler)).GetAwaiter().GetResult();
                    Assert(controller.Snapshot.State==ManagedStoreState.Ready && controller.Snapshot.Catalog.Packages.Count==1,"Managed network chain publishes validated catalog for "+mode+": "+controller.Snapshot.Code);
                    var catalog=controller.Snapshot.Catalog; var selected=catalog.Packages.Single();
                    controller.Plan(selected,env,catalog).GetAwaiter().GetResult();
                    Assert(controller.Snapshot.State==ManagedStoreState.PlanReady && controller.Snapshot.Plan.DownloadBytes==zip.Length,"Managed plan freezes exact download identity for "+mode);
                    var plan=controller.Snapshot.Plan;
                    if(mode=="offline")
                    {
                        controller.Refresh(endpoint,env,true).GetAwaiter().GetResult();
                        Assert(controller.Snapshot.Repository.Offline && controller.Snapshot.Catalog.Sha256==catalog.Sha256,"Distinct managed cache supports offline browsing");
                        controller.Plan(controller.Snapshot.Catalog.Packages.Single(),env,controller.Snapshot.Catalog).GetAwaiter().GetResult();
                        StoreReject("RepositoryStale",()=>controller.Download(controller.Snapshot.Plan,env,true));
                        Assert(!Directory.Exists(paths.PackagesDirectory),"Offline browsing never authorizes installation"); continue;
                    }
                    var download=new ManagedFlowHandler {Mode=mode,Catalog=handler.Catalog,Payload=zip}; download.SetMetadata();
                    var reported=new List<ManagedStoreProgress>();
                    var adapter=new ProgressFlowAccess(download,()=>reported.Add(controller.Snapshot.Progress));
                    controller.Download(plan,env,true,adapter).GetAwaiter().GetResult();
                    Assert(controller.Snapshot.Progress==null,"Terminal outcome clears transient progress for "+mode);
                    if(mode=="success")
                    {
                        Assert(reported.First().Stage==ManagedProgressStage.Downloading && reported.First().Received==0,"Controller reports actual download start");
                        Assert(reported.Last().Stage==ManagedProgressStage.Validating && reported.Last().Received==zip.Length && reported.Last().Total==zip.Length,"Controller separates verified bytes from transaction success");
                        var prior=controller.Snapshot;
                        adapter.Late(new ManagedPackageProgress(ManagedProgressStage.Downloading,0));
                        Assert(ReferenceEquals(prior,controller.Snapshot),"Late transfer callback cannot revive a finished operation");
                        Assert(controller.Snapshot.State==ManagedStoreState.Installed && download.Pointers==2 && download.FreshPointers==2 && download.Packages==1,"Real production controller bypasses gateway metadata cache before and after download");
                        var row=controller.Snapshot.Inventory.Packages.Single();
                        Assert(row.Package.RepositoryIdentitySha256==endpoint.IdentityKey && row.RestartPending,"Installed ownership matches repository and remains next-start only");
                        var refresh=new ManagedFlowHandler {Catalog=handler.Catalog,Payload=zip}; refresh.SetMetadata();
                        refresh.Sending=()=>
                        {
                            adapter.Late(new ManagedPackageProgress(ManagedProgressStage.Validating,zip.Length));
                            Assert(controller.Snapshot.Progress==null,"Old transfer callback cannot contaminate a new metadata operation");
                        };
                        controller.Refresh(endpoint,env,false,new RepositoryTransport(refresh)).GetAwaiter().GetResult();
                        string nextManifest=manifest.Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"");
                        byte[] nextZip=ManagedZip(files.Concat(new[]{new KeyValuePair<string,byte[]>("manifest.json",Utf8(nextManifest))}));
                        var nextCatalog=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(nextManifest,nextZip).Replace("v1.0.0","v1.1.0"))),"test.source");
                        Assert(ManagedStoreUpdates.Find(nextCatalog,controller.Snapshot.Inventory,endpoint,env).Single().Manifest.Version.ToString()=="1.1.0","Compatible newer installed version produces a metadata-only update notice");
                        Assert(ManagedStoreUpdates.Find(catalog,controller.Snapshot.Inventory,endpoint,env).Length==0,"Same installed version produces no update notice");
                        var retired=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(nextManifest,nextZip).Replace("v1.0.0","v1.1.0").Replace("\"state\":\"active\"","\"state\":\"withdrawn\""))),"test.source");
                        Assert(ManagedStoreUpdates.Find(retired,controller.Snapshot.Inventory,endpoint,env).Length==0,"Withdrawn versions never produce update notices");
                        Assert(ManagedStoreUpdates.Find(nextCatalog,controller.Snapshot.Inventory,new RepositoryEndpoint("https://other.test","test.source"),env).Length==0,"Different repository identity cannot claim an installed plugin for update");
                        var incompatible=new ClientEnvironmentSnapshot(env.Paths,env.HostModRoot,"1.5","0.9.7","1.5.0",env.InstalledMods,env.LoadedAssemblies,env.Modules,new string[0]);
                        Assert(ManagedStoreUpdates.Find(nextCatalog,controller.Snapshot.Inventory,endpoint,incompatible).Length==0,"Host-incompatible newer versions do not produce actionable notices");
                        var updateHandler=new ManagedFlowHandler {Catalog=Utf8(ManagedCatalog(ManagedListing(nextManifest,nextZip).Replace("v1.0.0","v1.1.0")).Replace(new string('b',40),new string('e',40))),Payload=nextZip}; updateHandler.SetMetadata();
                        controller.Refresh(endpoint,env,false,new RepositoryTransport(updateHandler)).GetAwaiter().GetResult();
                        var updateRoot=controller.Snapshot.Catalog.Packages.Single();
                        controller.Plan(updateRoot,env,controller.Snapshot.Catalog,true).GetAwaiter().GetResult();
                        var updatePlan=controller.Snapshot.Plan;
                        Assert(updatePlan!=null && updatePlan.ReplacesPackages && updatePlan.DownloadBytes==nextZip.Length && ManagedStoreInstallIntent.NeedsConfirmation(updatePlan),"Explicit update plans lock old receipt and require confirmation: "+controller.Snapshot.Code);
                        var transfer=new ManagedFlowHandler {Catalog=updateHandler.Catalog,Payload=nextZip}; transfer.SetMetadata();
                        controller.Download(updatePlan,env,true,new RepositoryTransport(transfer)).GetAwaiter().GetResult();
                        Assert(controller.Snapshot.State==ManagedStoreState.Installed && controller.Snapshot.Inventory.Packages.Single().Package.Version=="1.1.0" && transfer.FreshPointers==2,"Production update rechecks live metadata before and after transfer then replaces exact owned version");
                        row=controller.Snapshot.Inventory.Packages.Single();
                        var future=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(nextManifest.Replace("1.1.0","1.2.0"),nextZip).Replace("v1.0.0","v1.2.0"))),"test.source");
                        controller.Change(row.Package,ManagedExtensionDesiredState.Disabled,env).GetAwaiter().GetResult();
                        Assert(controller.Snapshot.Inventory.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.Disabled,"Shop can save package disable intent through generic host API");
                        Assert(ManagedStoreUpdates.Find(future,controller.Snapshot.Inventory,endpoint,env).Length==1,"Disabled packages can still report available versions without being enabled");
                        controller.Change(controller.Snapshot.Inventory.Packages.Single().Package,ManagedExtensionDesiredState.PendingRemoval,env).GetAwaiter().GetResult();
                        Assert(controller.Snapshot.Inventory.Packages.Single().Package.DesiredState==ManagedExtensionDesiredState.PendingRemoval,"Shop uninstall saves protected next-start removal intent");
                        Assert(ManagedStoreUpdates.Find(nextCatalog,controller.Snapshot.Inventory,endpoint,env).Length==0,"Pending uninstall does not advertise updates");
                    }
                    else
                    {
                        string expected=mode=="digest"?"PayloadDigestMismatch":mode=="redirect"?"RedirectRejected":mode=="reference"?"CandidateAssemblyReferenceUnavailable":"SnapshotChanged";
                        Assert(controller.Snapshot.State==ManagedStoreState.Failed && controller.Snapshot.Code==expected,"Network failure is visible for "+mode+": "+controller.Snapshot.Code);
                        Assert(!Directory.Exists(paths.PackagesDirectory) && controller.Snapshot.Plan==null,"Rejected transfer invalidates plan and writes no package "+mode);
                        if(mode=="reference") Assert(controller.Snapshot.ReferenceFailure?.RequiredReference.Contains("Utils, Version=0.9.7.0")==true && controller.Snapshot.ReferenceFailure.AvailableReferences.Single().Contains("0.9.6.0"),"Async controller preserves exact reference failure for the UI");
                    }
                }
            }
            var emptyPaths=new ManagedExtensionPaths(Path.Combine(root,"startup-empty"));
            using(var runtime=EmptyRuntime(emptyPaths,facts,new List<ManagedExtensionRuntimeAudit>()))
            using(var controller=new ManagedStoreController(runtime,runtime))
            {
                controller.CheckUpdates(new RepositoryEndpoint(RepositoryProfile.Official,RepositoryAccessMethod.GitHub),FlowEnvironment(emptyPaths,facts)).GetAwaiter().GetResult();
                Assert(controller.Snapshot.State==ManagedStoreState.Idle && controller.Snapshot.Code=="ManagedUpdateCheckSkipped" && controller.Updates.Count==0,"No official installs skips startup network checks");
            }
            var inactive=new ManagedExtensionPaths(Path.Combine(root,"inactive-mod")); var envBase=FlowEnvironment(inactive,facts);
            string mod=Path.Combine(root,"inactive-old-mod"),dll=Path.Combine(mod,"Custom","Assemblies","renamed.dll"); Directory.CreateDirectory(Path.GetDirectoryName(dll));
            File.WriteAllBytes(dll,files["Assemblies/Fixture.Managed.Plugin.dll"]); File.WriteAllText(Path.Combine(mod,"LoadFolders.xml"),"<loadFolders><v1.6><li>Custom</li></v1.6></loadFolders>");
            var withOld=new ClientEnvironmentSnapshot(envBase.Paths,envBase.HostModRoot,"1.6","0.9.7","1.5.0",envBase.InstalledMods.Concat(new[]{new ClientInstalledModSnapshot("old.mod",mod,false)}),envBase.LoadedAssemblies,envBase.Modules,new string[0]);
            var catalogForOld=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,zip))),"test.source");
            using(var runtime=EmptyRuntime(inactive,facts,new List<ManagedExtensionRuntimeAudit>()))
            {
                var inventory=runtime.Refresh(new string[0],CancellationToken.None);
                Assert(new ManagedStorePlanner(catalogForOld,withOld,inventory,endpoint).Plan(catalogForOld.Packages.Single(),CancellationToken.None)!=null,"Inactive disk copies do not reserve CLR names");
                File.WriteAllBytes(dll,new byte[]{1,2,3});
                File.WriteAllText(Path.Combine(mod,"LoadFolders.xml"),"<broken");
                Assert(new ManagedStorePlanner(catalogForOld,withOld,inventory,endpoint).Plan(catalogForOld.Packages.Single(),CancellationToken.None)!=null,"Unrelated corrupt files do not veto planning");
                var loaded=new ClientEnvironmentSnapshot(withOld.Paths,withOld.HostModRoot,"1.6","0.9.7","1.5.0",withOld.InstalledMods,
                    withOld.LoadedAssemblies.Concat(new[]{new ClientLoadedAssemblySnapshot("Fixture.Managed.Plugin","1.2.3.4",mod)}),withOld.Modules,new string[0]);
                StoreReject("CandidateAssemblyConflict",()=>new ManagedStorePlanner(catalogForOld,loaded,inventory,endpoint).Plan(catalogForOld.Packages.Single(),CancellationToken.None));
            }
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
