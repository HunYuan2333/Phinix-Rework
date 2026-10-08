using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static async Task RepositoryAdapters()
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-adapters-"+Guid.NewGuid().ToString("N"));
        var fixture=new AdapterFixture();
        var profile=new RepositoryProfile(Source,"test-owner/registry","123","45","codex/test","https://cache.example.test");
        var github=new RepositoryEndpoint(profile,RepositoryAccessMethod.GitHub);
        var cf=new RepositoryEndpoint(profile,RepositoryAccessMethod.Cloudflare);
        Assert(github.IdentityKey==cf.IdentityKey && github.AccessKey!=cf.AccessKey,"Transport choices share ownership but not HTTP validators.");
        Assert(new RepositoryEndpoint(new RepositoryProfile(Source,profile.Repository,"123","45","codex/test","https://other-cache.example.test"),RepositoryAccessMethod.Cloudflare).IdentityKey==github.IdentityKey,"Changing an approved gateway does not change repository identity.");
        Assert(new RepositoryEndpoint(new RepositoryProfile(Source,profile.Repository,"124","45","codex/test",profile.GatewayOrigin),RepositoryAccessMethod.GitHub).IdentityKey!=github.IdentityKey,"Recreated repositories cannot reuse ownership.");
        Expect("InvalidRepositoryProfile",()=>new RepositoryProfile(Source,profile.Repository,"123","45","../other",profile.GatewayOrigin));
        var paths=new ClientEnvironmentPaths(Path.Combine(root,"Mods"),Path.Combine(root,"SaveData"));
        var ghCache=new ManagedRepositoryCache(paths.GetExtensionDataDirectory("phinix.plugin-store"),github);
        var cfCache=new ManagedRepositoryCache(paths.GetExtensionDataDirectory("phinix.plugin-store"),cf);
        try
        {
            using(var gh=new GitHubRepositoryAccess(fixture.Handler()))
            using(var cloud=new CloudflareRepositoryAccess(new RepositoryTransport(fixture.Handler())))
            {
                var a=await ManagedRepositoryBrowser.Refresh(github,ghCache,gh,CancellationToken.None,true);
                ghCache.Save(a,CancellationToken.None);
                CheckCacheStaging(ghCache.DirectoryPath, ghCache.FilePath, () => ghCache.Stage(a, CancellationToken.None));
                Assert(cfCache.Read(CancellationToken.None).ETag==null,"GitHub ETag is not sent to CF after switching.");
                var b=await ManagedRepositoryBrowser.Refresh(cf,cfCache,cloud,CancellationToken.None,true);
                Assert(a.CatalogBytes.SequenceEqual(b.CatalogBytes) && a.PublishedBytes.SequenceEqual(b.PublishedBytes) && a.StableBytes.SequenceEqual(b.StableBytes),"Both adapters return identical publication bytes.");
                cfCache.Save(b,CancellationToken.None);
                Assert(ghCache.Read(CancellationToken.None).ETag==null,"CF ETag is not sent to GitHub after switching back.");
                var package=a.Catalog.Packages.Single();
                var directProgress=new List<ManagedPackageProgress>(); var cfProgress=new List<ManagedPackageProgress>();
                var direct=await gh.DownloadManagedPackage(github,a.Catalog,package,paths,CancellationToken.None,null,directProgress.Add);
                var cached=await cloud.DownloadManagedPackage(cf,b.Catalog,b.Catalog.Packages.Single(),paths,CancellationToken.None,null,cfProgress.Add);
                Assert(direct.Sha256==cached.Sha256 && direct.Sha256==Digest(fixture.Zip),"GitHub CDN and CF transfers use the same ZIP validation.");
                foreach(var progress in new[]{directProgress,cfProgress})
                {
                    Assert(progress.First().Stage==ManagedProgressStage.Downloading && progress.First().Received==0,"Transfer starts with real zero received bytes.");
                    Assert(progress.Last().Stage==ManagedProgressStage.Validating && progress.Last().Received==fixture.Zip.Length,"Full bytes still mean verification, not installation success.");
                    Assert(progress.Zip(progress.Skip(1),(x,y)=>x.Received<=y.Received).All(x=>x),"Received-byte reports never move backwards.");
                }
                Assert(direct.InstallationInput(github,a.Catalog).RepositoryIdentitySha256==cached.InstallationInput(cf,b.Catalog).RepositoryIdentitySha256,"Install inputs remain the same owner across a provider switch.");
                using(var canceled=new CancellationTokenSource())
                { canceled.Cancel(); int before=fixture.Requests.Count; try {gh.DownloadManagedPackage(github,a.Catalog,package,paths,canceled.Token).GetAwaiter().GetResult();throw new Exception("Cancellation was ignored.");} catch(OperationCanceledException) {} Assert(fixture.Requests.Count==before,"Pre-cancelled GitHub transfer sends no request."); }
                var next=await ManagedRepositoryBrowser.Refresh(github,ghCache,gh,CancellationToken.None,true);
                ghCache.Save(next,CancellationToken.None);
                int calls=fixture.Requests.Count;
                var unchanged=await ManagedRepositoryBrowser.Refresh(github,ghCache,gh,CancellationToken.None,true);
                Assert(unchanged.Catalog.Sha256==a.Catalog.Sha256 && fixture.Requests.Count-calls==3,"GitHub 304 still verifies repository/ref and reuses a complete cached chain.");

                // Production planner checks an actual verified receipt, not just equality of two hashes.
                var document=ManagedManifest(fixture.Dll); WriteManagedFixture(paths.ManagedExtensions,Source,document,fixture.Dll,"enabled");
                string receipt=paths.ManagedExtensions.GetInstalledRecordPath(Source,"test.managed");
                string receiptText=File.ReadAllText(receipt).Replace("\"repositoryIdentitySha256\":\""+Hash+"\"","\"repositoryIdentitySha256\":\""+github.IdentityKey+"\"").Replace("\"artifactSha256\":\""+Hash+"\"","\"artifactSha256\":\""+Digest(fixture.Zip)+"\"");
                File.WriteAllText(receipt,receiptText);
                var row=ManagedExtensionInventoryReader.Read(paths.ManagedExtensions,CancellationToken.None).Packages.Single();
                Assert(row.DiagnosticCode==null,"Identity-based receipt passes actual inventory verification.");
                var inventory=new ManagedExtensionManagementSnapshot(new[]{new ManagedExtensionManagementPackage(row,null,null,null,null,null,false)},new string[0]);
                var env=new ClientEnvironmentSnapshot(paths,Path.Combine(root,"Host"),"1.6","0.9.7","1.7.0",new[]{new ClientInstalledModSnapshot("test.host",Path.Combine(root,"Host"),true)},new ClientLoadedAssemblySnapshot[0],new ClientModuleSnapshot[0],new string[0]);
                Assert(new ManagedStorePlanner(a.Catalog,env,inventory,github).Plan(package,CancellationToken.None).DownloadBytes==0 && new ManagedStorePlanner(b.Catalog,env,inventory,cf).Plan(b.Catalog.Packages.Single(),CancellationToken.None).DownloadBytes==0,"Both providers recognize the same verified installation without downloading again.");
            }
            Assert(!Directory.GetFiles(root,"*.partial",SearchOption.AllDirectories).Any(),"Both adapters clean held temporary files.");
            foreach(var fault in new[]{"repo","release","tag","commit","asset","membership","digest","redirect","redirectPort","zipHash","zipLength","gzip","rate","duplicate","missingSha","unexpected"})
            {
                fixture.Fault=fault;
                using(var gh=new GitHubRepositoryAccess(fixture.Handler()))
                {
                    string code=new Dictionary<string,string>{{"repo","OriginRepositoryMismatch"},{"release","OriginReleaseMismatch"},{"tag","OriginTagMismatch"},{"commit","OriginCommitMismatch"},{"asset","OriginAssetMismatch"},{"membership","OriginAssetMembershipMismatch"},{"digest","OriginDigestMismatch"},{"redirect","OriginRedirectRejected"},{"redirectPort","OriginRedirectRejected"},{"zipHash","PayloadDigestMismatch"},{"zipLength","OriginLengthMismatch"},{"gzip","OriginResponseType"},{"rate","RepositoryRateLimited"},{"duplicate","DuplicateField"},{"missingSha","OriginMetadataInvalid"},{"unexpected","UnexpectedEndpoint"}}[fault];
                    var catalog=ManagedStoreCatalogReader.Read(fixture.Catalog,Source);
                    Expect(code,()=>gh.DownloadManagedPackage(github,catalog,catalog.Packages.Single(),paths,CancellationToken.None).GetAwaiter().GetResult());
                }
            }
            fixture.Fault=null;
            // Content continuity is shared across providers, even when HTTP validators are discarded.
            var changedStable=EncodingBytes(System.Text.Encoding.UTF8.GetString(fixture.Stable).Replace(Digest(fixture.Catalog),Hash));
            using(var fake=new FixedAdapter(new ManagedMetadataRead(new RepositoryResponse(false,changedStable,null),fixture.Published,fixture.Catalog)))
                Expect("SnapshotIdentityChanged",()=>ManagedRepositoryBrowser.Refresh(cf,cfCache,fake,CancellationToken.None,true).GetAwaiter().GetResult());
            var changedCatalog=EncodingBytes(System.Text.Encoding.UTF8.GetString(fixture.Catalog).Replace("\"snapshotId\":\""+Revision+"\"","\"snapshotId\":\""+new string('4',40)+"\"").Replace(Digest(fixture.Zip),Hash));
            var changedPublished=Serialize(new {schemaVersion=1,sourceId=Source,snapshotId=new string('4',40),catalogSchemaVersion=3,catalogSha256=Digest(changedCatalog),catalogSizeBytes=changedCatalog.Length,repository=profile.Repository,repositoryId=profile.RepositoryId,ownerId=profile.OwnerId,releaseId="200",assetId="201",assetName="catalog.json"});
            changedStable=Serialize(new {schemaVersion=1,sourceId=Source,snapshotId=new string('4',40),catalogSchemaVersion=3,catalogSha256=Digest(changedCatalog),catalogSizeBytes=changedCatalog.Length,publishedSha256=Digest(changedPublished),publishedSizeBytes=changedPublished.Length});
            using(var fake=new FixedAdapter(new ManagedMetadataRead(new RepositoryResponse(false,changedStable,null),changedPublished,changedCatalog)))
                Expect("PackageIdentityChanged",()=>ManagedRepositoryBrowser.Refresh(cf,cfCache,fake,CancellationToken.None,true).GetAwaiter().GetResult());
            var ghEntry=ghCache.Read(CancellationToken.None);cfCache.Save(ghEntry,CancellationToken.None);
            using(var fake=new FixedAdapter(new ManagedMetadataRead(new RepositoryResponse(true,null,"\"cf-only\""))))
                Expect("UnexpectedNotModified",()=>ManagedRepositoryBrowser.Refresh(cf,cfCache,fake,CancellationToken.None,true).GetAwaiter().GetResult());
            var originalCache=File.ReadAllBytes(ghCache.FilePath);var badCache=(byte[])originalCache.Clone();badCache[0]^=1;File.WriteAllBytes(ghCache.FilePath,badCache);
            Expect("InvalidCache",()=>ghCache.Read(CancellationToken.None));File.WriteAllBytes(ghCache.FilePath,originalCache);
            using(var gh=new GitHubRepositoryAccess(new AdapterStallHandler()))
            {
                var catalog=ManagedStoreCatalogReader.Read(fixture.Catalog,Source);
                Expect("OriginConnectTimeout",()=>gh.DownloadManagedPackage(github,catalog,catalog.Packages.Single(),paths,CancellationToken.None,new PackageDownloadBudget(10,10,1000)).GetAwaiter().GetResult());
            }
            fixture.Fault=null;
            var logs=new List<string>();
            using(var gh=new GitHubRepositoryAccess(fixture.Handler(),new RepositoryDiagnostics(logs.Add,Source)))
            {
                var catalog=ManagedStoreCatalogReader.Read(fixture.Catalog,Source);
                await gh.DownloadManagedPackage(github,catalog,catalog.Packages.Single(),paths,CancellationToken.None);
            }
            Assert(!string.Join("\n",logs).Contains("signed-secret") && !string.Join("\n",logs).Contains("https://"),"GitHub signed asset URLs are never logged.");
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Plugin"),"Neither adapter executes downloaded DLLs.");
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private sealed class FixedAdapter : IManagedRepositoryAccess
    {
        private readonly ManagedMetadataRead read;
        internal FixedAdapter(ManagedMetadataRead read) {this.read=read;}
        public Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh) {token.ThrowIfCancellationRequested();return Task.FromResult(read);}
        public Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null) {throw new Exception("Metadata must not initiate transfer.");}
        public void Dispose() { }
    }
    private sealed class AdapterStallHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) { return new TaskCompletionSource<HttpResponseMessage>().Task; }
    }
    private sealed class AdapterFixture
    {
        internal readonly byte[] Dll,Zip,Catalog,Published,Stable;
        internal string Fault;
        internal readonly List<string> Requests=new List<string>();
        internal AdapterFixture()
        {
            Dll=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"AdapterFixtures","Fixture.Plugin.dll"));
            byte[] manifest=Serialize(ManagedManifest(Dll));
            using(var output=new MemoryStream())
            {
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
                    foreach(var entry in new Dictionary<string,byte[]>{{"manifest.json",manifest},{"Assemblies/Fixture.Plugin.dll",Dll}})
                        using(var stream=zip.CreateEntry(entry.Key).Open()) stream.Write(entry.Value,0,entry.Value.Length);
                Zip=output.ToArray();
            }
            Catalog=Serialize(new {schemaVersion=3,sourceId=Source,snapshotId=Revision,packages=new[]{new {
                id="test.managed",author="Test Author",license="MIT",localization=new {translations=new Dictionary<string,object>{{"en-US",new {name="Managed test",summary="Test"}}}},tags=new[]{"test"},state="active",channel="github-release",management="phinix-dll",manifest=ManagedManifest(Dll),
                artifact=new {repository="test-owner/registry",repositoryId="123",ownerId="45",sourceCommit=Revision,tag="v1.0.0",releaseId="202",assetId="203",assetName="test.zip",payloadKind="managed-dll-zip",sha256=Digest(Zip),manifestSha256=Digest(manifest),sizeBytes=Zip.Length}}}});
            Published=Serialize(new {schemaVersion=1,sourceId=Source,snapshotId=Revision,catalogSchemaVersion=3,catalogSha256=Digest(Catalog),catalogSizeBytes=Catalog.Length,repository="test-owner/registry",repositoryId="123",ownerId="45",releaseId="200",assetId="201",assetName="catalog.json"});
            Stable=Serialize(new {schemaVersion=1,sourceId=Source,snapshotId=Revision,catalogSchemaVersion=3,catalogSha256=Digest(Catalog),catalogSizeBytes=Catalog.Length,publishedSha256=Digest(Published),publishedSizeBytes=Published.Length});
        }
        internal HttpMessageHandler Handler() { return new AdapterHandler(this); }
        private sealed class AdapterHandler : HttpMessageHandler
        {
            private readonly AdapterFixture fixture;
            internal AdapterHandler(AdapterFixture fixture) {this.fixture=fixture;}
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {token.ThrowIfCancellationRequested();Assert(request.RequestUri.Scheme=="https","Adapters use HTTPS.");var response=fixture.Respond(request);if(response.RequestMessage==null)response.RequestMessage=request;return Task.FromResult(response);}
        }
        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Requests.Add(request.RequestUri.AbsoluteUri);
            Assert(request.Headers.Authorization==null && !request.Headers.Contains("Cookie"),"Anonymous adapter sends no credentials/cookies, including to CDN.");
            string path=request.RequestUri.AbsolutePath;
            bool api=request.RequestUri.Host=="api.github.com";
            if(api && Fault=="rate") return new HttpResponseMessage((HttpStatusCode)429);
            HttpResponseMessage response;
            if(api && path=="/repos/test-owner/registry")
                response=JsonResponse(Serialize(new {id=Fault=="repo"?124:123,owner=new{id=45},full_name="test-owner/registry",@private=false,visibility="public"}));
            else if(api && path.Contains("/git/ref/heads/")) response=JsonResponse(Serialize(new {@ref="refs/heads/codex/test",@object=new {type="commit",sha=new string('3',40)}}));
            else if(api && path.Contains("/contents/stable.json"))
            {
                Assert(request.RequestUri.Query=="?ref="+new string('3',40),"Mutable publication is pinned to the verified branch commit.");
                response=JsonResponse(Stable); response.Headers.ETag=new EntityTagHeaderValue("\"github-stable\"");
                if(request.Headers.IfNoneMatch.Any()) { Assert(request.Headers.IfNoneMatch.Single().Tag=="\"github-stable\"","GitHub only receives its own ETag."); response.StatusCode=HttpStatusCode.NotModified; }
            }
            else if(api && path.Contains("/contents/published/")) response=JsonResponse(Published);
            else if(api && path.Contains("/git/ref/tags/")) response=JsonResponse(Serialize(new {@ref="refs/tags/v1.0.0",@object=new {type="commit",sha=Fault=="commit"?new string('4',40):Revision}}));
            else if(api && path.Contains("/releases/assets/"))
            {
                bool catalog=path.EndsWith("/201"); int asset=catalog?201:203; byte[] bytes=catalog?Catalog:Zip; string name=catalog?"catalog.json":"test.zip";
                if(request.Headers.Accept.Single().MediaType=="application/octet-stream")
                {
                    response=new HttpResponseMessage(HttpStatusCode.Found);
                    response.Headers.Location=new Uri(Fault=="redirect"?"https://evil.test/signed-secret":Fault=="redirectPort"?"https://release-assets.githubusercontent.com:8443/github-production-release-asset/signed-secret":"https://release-assets.githubusercontent.com/github-production-release-asset/123/"+asset+"?signed-secret");
                }
                else response=JsonResponse(Serialize(new {id=Fault=="asset"?999:asset,name,state="uploaded",size=bytes.Length,digest="sha256:"+(Fault=="digest"?Hash:Digest(bytes))}));
            }
            else if(api && path.Contains("/releases/"))
            {
                bool catalog=path.EndsWith("/200"); response=JsonResponse(Serialize(new {id=catalog?200:202,draft=Fault=="release",prerelease=false,tag_name=Fault=="tag"?"v9.0.0":"v1.0.0",assets=Fault=="membership"?new object[0]:new object[]{new {id=catalog?201:203,name=catalog?"catalog.json":"test.zip",size=catalog?Catalog.Length:Zip.Length}}}));
            }
            else if(request.RequestUri.Host=="release-assets.githubusercontent.com")
            {
                bool catalog=path.EndsWith("/201"); var bytes=(byte[])(catalog?Catalog:Zip).Clone(); if(Fault=="zipHash") bytes[0]^=1;
                response=AdapterBinary(bytes); if(Fault=="zipLength") response.Content.Headers.ContentLength=bytes.Length+1;
                if(Fault=="gzip") response.Content.Headers.ContentEncoding.Add("gzip");
            }
            else if(path.EndsWith("/stable"))
            { response=JsonResponse(Stable); response.Headers.ETag=new EntityTagHeaderValue("\"cf-stable\""); if(request.Headers.IfNoneMatch.Any()) {Assert(request.Headers.IfNoneMatch.Single().Tag=="\"cf-stable\"","CF only receives its own ETag.");response.StatusCode=HttpStatusCode.NotModified;} }
            else if(path.Contains("/published/")) response=JsonResponse(Published);
            else if(path.Contains("/catalog/")) response=JsonResponse(Catalog);
            else if(path.EndsWith("/package")) response=AdapterBinary(Zip);
            else throw new Exception("Unexpected mock request "+path);
            if(Fault=="duplicate" && api && path=="/repos/test-owner/registry") {response.Dispose();response=JsonResponse(EncodingBytes("{\"id\":123,\"id\":123}"));}
            if(Fault=="missingSha" && path.Contains("/git/ref/tags/")) {response.Dispose();response=JsonResponse(Serialize(new {@ref="refs/tags/v1.0.0",@object=new {type="commit"}}));}
            if(Fault=="unexpected") response.RequestMessage=new HttpRequestMessage(HttpMethod.Get,"https://evil.test/");
            return response;
        }
        private static HttpResponseMessage AdapterBinary(byte[] bytes)
        { var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};r.Content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");return r; }
    }
}
