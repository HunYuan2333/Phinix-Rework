using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    // Public anonymous API access. No token, cookies, automatic redirect or arbitrary catalog URL.
    internal sealed class GitHubRepositoryAccess : IManagedRepositoryAccess
    {
        private readonly HttpClient client;
        private readonly RepositoryDiagnostics diagnostics;
        internal GitHubRepositoryAccess(RepositoryDiagnostics diagnostics=null) : this(new HttpClientHandler { AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None,UseCookies=false },diagnostics) { }
        internal GitHubRepositoryAccess(HttpMessageHandler handler,RepositoryDiagnostics diagnostics=null)
        { client=new HttpClient(handler,true) {Timeout=Timeout.InfiniteTimeSpan}; this.diagnostics=diagnostics??new RepositoryDiagnostics(); }
        public async Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh)
        {
            try { return await ReadMetadataCore(endpoint,etag,token,fresh).ConfigureAwait(false); }
            catch(OperationCanceledException)
            { token.ThrowIfCancellationRequested(); diagnostics.Event("github.metadata_failed","Metadata","RepositoryTimeout");throw new StoreValidationException("RepositoryTimeout","GitHub metadata deadline exceeded."); }
            catch(StoreValidationException ex) {diagnostics.Event("github.metadata_failed","Metadata",ex.Code);throw;}
        }
        private async Task<ManagedMetadataRead> ReadMetadataCore(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh)
        {
            RequireProfile(endpoint); diagnostics.AccessMethod="GitHub"; diagnostics.RepositoryIdentity=endpoint.IdentityKey;
            using(var operation=new OriginOperation(client,diagnostics,token,30000,fresh))
            {
                var profile=endpoint.Profile;
                await operation.Repository(profile.Repository,profile.RepositoryId,profile.OwnerId).ConfigureAwait(false);
                var reference=await operation.Json("/repos/"+profile.Repository+"/git/ref/heads/"+profile.PublicationBranch).ConfigureAwait(false);
                Require(Text(reference.Element("ref"),256)=="refs/heads/"+profile.PublicationBranch,"PublicationRefMismatch");
                var target=reference.Element("object");
                Require(Text(target?.Element("type"),16)=="commit","PublicationRefMismatch");
                string commit=Hex(target.Element("sha"),"commit",40);
                var stableResponse=await operation.File(profile.Repository,commit,"stable.json",etag).ConfigureAwait(false);
                if(stableResponse.NotModified) return new ManagedMetadataRead(stableResponse);
                var stable=RepositoryMetadata.ReadManagedStable(stableResponse.Body,endpoint.SourceId);
                var descriptor=await operation.File(profile.Repository,commit,"published/"+stable.SnapshotId+".json",null).ConfigureAwait(false);
                Require(!descriptor.NotModified,"UnexpectedNotModified");
                RepositoryMetadata.VerifyPublished(stable,descriptor.Body); profile.VerifyPublished(descriptor.Body);
                var p=CatalogReader.ReadJson(descriptor.Body);
                var asset=new AssetIdentity(profile.Repository,profile.RepositoryId,profile.OwnerId,
                    CatalogReader.PositiveId(p.Element("releaseId"),"releaseId"),CatalogReader.PositiveId(p.Element("assetId"),"assetId"),"catalog.json",stable.CatalogSizeBytes,stable.CatalogSha256);
                await operation.Asset(asset,null,null).ConfigureAwait(false);
                using(var response=await operation.Binary(asset).ConfigureAwait(false))
                {
                    var catalog=await operation.Body(response,stable.CatalogSizeBytes).ConfigureAwait(false);
                    // Shared browser still revalidates every chain, including cached continuity.
                    RepositoryMetadata.VerifyManaged(stable,descriptor.Body,catalog);
                    return new ManagedMetadataRead(stableResponse,descriptor.Body,catalog);
                }
            }
        }
        public async Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null)
        {
            RequireProfile(endpoint); diagnostics.AccessMethod="GitHub"; diagnostics.RepositoryIdentity=endpoint.IdentityKey; endpoint.Package(catalog,package); // Locked object membership, source, state and canonical IDs.
            if(paths==null) throw new ArgumentNullException(nameof(paths));
            budget=budget??new PackageDownloadBudget();
            diagnostics.Event("package.download_started","GitHubPackage",managed:package,snapshot:catalog.SnapshotId,catalogHash:catalog.Sha256);
            using(var operation=new OriginOperation(client,diagnostics,token,budget.Total,true,budget.Headers,budget.Idle))
            {
                try
                {
                    var a=package.Artifact;
                    var asset=new AssetIdentity(a.Repository,a.RepositoryId,a.OwnerId,a.ReleaseId,a.AssetId,a.AssetName,a.SizeBytes,a.Sha256);
                    await operation.Asset(asset,a.Tag,a.SourceCommit).ConfigureAwait(false);
                    using(var response=await operation.Binary(asset).ConfigureAwait(false))
                        return await ManagedPayloadTransfer.Read(response,endpoint,catalog,package,paths,budget,operation.Token,token,diagnostics,null,true,progress).ConfigureAwait(false);
                }
                catch(OperationCanceledException)
                { token.ThrowIfCancellationRequested(); throw new StoreValidationException("PackageDownloadTimeout","GitHub operation exceeded its deadline."); }
                catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException || ex is HttpRequestException)
                { diagnostics.Event("package.failed","GitHubPackage","PackageUnavailable",managed:package,snapshot:catalog.SnapshotId,catalogHash:catalog.Sha256);throw new StoreValidationException("PackageUnavailable","GitHub transfer or held-file storage failed.",ex); }
                catch(StoreValidationException ex)
                { diagnostics.Event("package.failed","GitHubPackage",ex.Code,managed:package,snapshot:catalog.SnapshotId,catalogHash:catalog.Sha256); throw; }
            }
        }
        private static void RequireProfile(RepositoryEndpoint endpoint)
        { Require(endpoint?.Profile!=null && endpoint.AccessMethod==RepositoryAccessMethod.GitHub,"AccessMethodMismatch"); }
        public void Dispose() { client.Dispose(); }
        private static void Require(bool condition,string code)
        { if(!condition) throw new StoreValidationException(code,"GitHub repository verification refused: "+code); }
        private static string Text(XElement node,int bound) { Require(node!=null,"OriginMetadataInvalid"); return CatalogReader.Text(node,"github",bound); }
        private static string Id(XElement node)
        { Require(node!=null,"OriginMetadataInvalid"); return CatalogReader.Integer(node,"githubId",1,long.MaxValue).ToString(CultureInfo.InvariantCulture); }

        private static string Hex(XElement node,string path,int length)
        {Require(node!=null,"OriginMetadataInvalid");return CatalogReader.Hex(node,path,length);}
        private static bool Bool(XElement node) { Require(node!=null,"OriginMetadataInvalid"); return CatalogReader.Boolean(node,"github"); }
        private static long Number(XElement node,long maximum) { Require(node!=null,"OriginMetadataInvalid"); return CatalogReader.Integer(node,"github",1,maximum); }
        private static List<XElement> Array(XElement node) { Require(node!=null,"OriginMetadataInvalid"); return CatalogReader.Array(node,"github",1024); }

        private sealed class AssetIdentity
        {
            internal AssetIdentity(string repo,string repoId,string ownerId,string releaseId,string assetId,string name,long size,string hash)
            { Repository=repo; RepositoryId=repoId; OwnerId=ownerId; ReleaseId=releaseId; AssetId=assetId; Name=name; Size=size; Hash=hash; }
            internal readonly string Repository,RepositoryId,OwnerId,ReleaseId,AssetId,Name,Hash;
            internal readonly long Size;
        }
        private sealed class OriginOperation : IDisposable
        {
            private readonly HttpClient client;
            private readonly RepositoryDiagnostics audit;
            private readonly CancellationToken caller;
            private readonly CancellationTokenSource deadline;
            private readonly bool fresh;
            private readonly int headersMilliseconds,idleMilliseconds;
            private readonly HashSet<string> repos=new HashSet<string>(StringComparer.Ordinal);
            private int calls;
            internal OriginOperation(HttpClient client,RepositoryDiagnostics audit,CancellationToken token,int milliseconds,bool fresh,int headersMilliseconds=10000,int idleMilliseconds=10000)
            { this.client=client; this.audit=audit; caller=token; deadline=CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(milliseconds); this.fresh=fresh; this.headersMilliseconds=headersMilliseconds;this.idleMilliseconds=idleMilliseconds; }
            internal CancellationToken Token => deadline.Token;
            internal async Task Repository(string repository,string repositoryId,string ownerId)
            {
                string key=repository+":"+repositoryId+":"+ownerId;
                if(repos.Contains(key)) return;
                var value=await Json("/repos/"+repository).ConfigureAwait(false);
                Require(Bool(value.Element("private"))==false && Text(value.Element("visibility"),16)=="public" && Text(value.Element("full_name"),140)==repository && Id(value.Element("id"))==repositoryId && Id(value.Element("owner")?.Element("id"))==ownerId,"OriginRepositoryMismatch");
                repos.Add(key); audit.Event("github.identity_verified","Repository");
            }
            internal async Task<XElement> Json(string path)
            {
                using(var response=await Send(new Uri("https://api.github.com"+path),"application/vnd.github+json").ConfigureAwait(false))
                {
                    Require(response.StatusCode==HttpStatusCode.OK,HttpCode(response));
                    ValidateType(response,"application/json","application/vnd.github+json");
                    var root=CatalogReader.ReadJson(await Body(response,CatalogReader.MaxCatalogBytes).ConfigureAwait(false));
                    Require((string)root.Attribute("type")=="object","OriginMetadataInvalid");
                    Require(!root.DescendantsAndSelf().Any(n=>(string)n.Attribute("type")=="object" && n.Elements().GroupBy(e=>e.Name.LocalName).Any(g=>g.Count()>1)),"DuplicateField");
                    return root;
                }
            }
            internal async Task<RepositoryResponse> File(string repository,string commit,string path,string etag)
            {
                var uri=new Uri("https://api.github.com/repos/"+repository+"/contents/"+path+"?ref="+commit);
                using(var response=await Send(uri,"application/vnd.github.raw+json",etag).ConfigureAwait(false))
                {
                    string returned=RepositoryTransport.ValidETag(response.Headers.ETag?.ToString());
                    if(response.StatusCode==HttpStatusCode.NotModified) return new RepositoryResponse(true,null,returned);
                    Require(response.StatusCode==HttpStatusCode.OK,HttpCode(response));
                    ValidateType(response,"application/json","application/vnd.github.raw+json","application/octet-stream");
                    return new RepositoryResponse(false,await Body(response,RepositoryMetadata.MaxMetadataBytes).ConfigureAwait(false),returned);
                }
            }
            internal async Task Asset(AssetIdentity a,string tag,string commit)
            {
                await Repository(a.Repository,a.RepositoryId,a.OwnerId).ConfigureAwait(false);
                var release=await Json("/repos/"+a.Repository+"/releases/"+a.ReleaseId).ConfigureAwait(false);
                Require(Id(release.Element("id"))==a.ReleaseId && !Bool(release.Element("draft")) && !Bool(release.Element("prerelease")),"OriginReleaseMismatch");
                if(tag!=null)
                {
                    Require(Text(release.Element("tag_name"),128)==tag,"OriginTagMismatch");
                    var reference=await Json("/repos/"+a.Repository+"/git/ref/tags/"+tag).ConfigureAwait(false);
                    Require(Text(reference.Element("ref"),256)=="refs/tags/"+tag,"OriginTagMismatch");
                    for(int depth=0; Text(reference.Element("object")?.Element("type"),16)=="tag" && depth<5;depth++)
                        reference=await Json("/repos/"+a.Repository+"/git/tags/"+Hex(reference.Element("object").Element("sha"),"tagCommit",40)).ConfigureAwait(false);
                    Require(Text(reference.Element("object")?.Element("type"),16)=="commit" && Hex(reference.Element("object").Element("sha"),"commit",40)==commit,"OriginCommitMismatch");
                }
                var asset=await Json("/repos/"+a.Repository+"/releases/assets/"+a.AssetId).ConfigureAwait(false);
                Require(Id(asset.Element("id"))==a.AssetId && Text(asset.Element("name"),128)==a.Name && Text(asset.Element("state"),16)=="uploaded" && Number(asset.Element("size"),CatalogReader.MaxPackageBytes)==a.Size,"OriginAssetMismatch");
                Require(Array(release.Element("assets")).Any(item=>Id(item.Element("id"))==a.AssetId && Text(item.Element("name"),128)==a.Name && Number(item.Element("size"),CatalogReader.MaxPackageBytes)==a.Size),"OriginAssetMembershipMismatch");
                var digest=asset.Element("digest");
                if(digest!=null && (string)digest.Attribute("type")!="null") Require(Text(digest,80)=="sha256:"+a.Hash,"OriginDigestMismatch");
                audit.Event("github.identity_verified","Asset",bytes:a.Size);
            }
            internal async Task<HttpResponseMessage> Binary(AssetIdentity a)
            {
                var uri=new Uri("https://api.github.com/repos/"+a.Repository+"/releases/assets/"+a.AssetId);
                for(int hop=0;hop<=5;hop++)
                {
                    var response=await Send(uri,"application/octet-stream").ConfigureAwait(false);
                    try
                    {
                        if(response.StatusCode==HttpStatusCode.OK)
                        {
                            ValidateType(response,"application/octet-stream","application/zip");
                            Require(response.Content.Headers.ContentType.CharSet==null && response.Content.Headers.ContentLength==a.Size,"OriginLengthMismatch");
                            return response;
                        }
                        int status=(int)response.StatusCode;
                        Require(status==301 || status==302 || status==303 || status==307 || status==308,HttpCode(response));
                        Require(hop<5 && response.Headers.Location!=null,"OriginRedirectLimit");
                        var next=new Uri(uri,response.Headers.Location);
                        Require(IsAssetCdn(next),"OriginRedirectRejected");
                        uri=next; audit.Event("github.redirect","Asset");
                    }
                    catch { response.Dispose(); throw; }
                    response.Dispose();
                }
                throw new StoreValidationException("OriginRedirectLimit","Too many asset redirects.");
            }
            private async Task<HttpResponseMessage> Send(Uri uri,string accept,string etag=null)
            {
                Token.ThrowIfCancellationRequested(); Require(++calls<=24,"OriginCallLimit");
                bool api=uri.GetLeftPart(UriPartial.Authority)=="https://api.github.com";
                Require(api || (accept=="application/octet-stream" && IsAssetCdn(uri)),"OriginRedirectRejected");
                using(var request=new HttpRequestMessage(HttpMethod.Get,uri))
                {
                    request.Headers.UserAgent.ParseAdd("Phinix-PluginStore/2");
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
                    if(api) request.Headers.Add("X-GitHub-Api-Version","2022-11-28");
                    if(fresh) request.Headers.CacheControl=new CacheControlHeaderValue {NoCache=true};
                    if(etag!=null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
                    audit.Event("github.request",api?"Metadata":"Asset");
                    HttpResponseMessage response=null;
                    try
                    {
                        response=await RepositoryTransport.PackageOperation(ct=>client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct),headersMilliseconds,Token,caller,"OriginConnectTimeout",null,late=>late.Dispose()).ConfigureAwait(false);
                        Require(response.RequestMessage!=null && response.RequestMessage.RequestUri==uri,"UnexpectedEndpoint");
                        audit.Event("github.response",api?"Metadata":"Asset",status:(int)response.StatusCode,githubRequestId:Header(response,"X-GitHub-Request-Id"),rateLimitRemaining:NumericHeader(response,"X-RateLimit-Remaining"),rateLimitReset:NumericHeader(response,"X-RateLimit-Reset"),retryAfterSeconds:NumericHeader(response,"Retry-After"));
                        return response;
                    }
                    catch(Exception ex) when(ex is HttpRequestException || ex is IOException)
                    { response?.Dispose(); audit.Event("github.failed","Origin","RepositoryUnavailable"); throw new StoreValidationException("RepositoryUnavailable","GitHub request failed.",ex); }
                    catch(StoreValidationException ex) { response?.Dispose(); audit.Event("github.failed","Origin",ex.Code); throw; }
                }
            }
            internal async Task<byte[]> Body(HttpResponseMessage response,int maximum)
            {
                var length=response.Content?.Headers.ContentLength;
                Require(response.Content!=null && (!length.HasValue || length.Value>=1 && length.Value<=maximum),"OriginDocumentLimit");
                using(var stream=await RepositoryTransport.PackageOperation(ct=>response.Content.ReadAsStreamAsync(),idleMilliseconds,Token,caller,"OriginBodyTimeout",()=>response.Dispose(),late=>late.Dispose()).ConfigureAwait(false))
                using(var output=new MemoryStream())
                {
                    var buffer=new byte[8192];
                    while(true)
                    {
                        int count=await RepositoryTransport.PackageOperation(ct=>stream.ReadAsync(buffer,0,Math.Min(buffer.Length,maximum+1-(int)output.Length),ct),idleMilliseconds,Token,caller,"OriginBodyTimeout",()=>response.Dispose()).ConfigureAwait(false);
                        if(count==0) break;
                        Require(output.Length+count<=maximum,"OriginDocumentLimit"); output.Write(buffer,0,count);
                    }
                    Require(output.Length>0 && (!length.HasValue || length.Value==output.Length),"OriginLengthMismatch");
                    return output.ToArray();
                }
            }
            private static string Header(HttpResponseMessage response,string name)
            { IEnumerable<string> values; if(!response.Headers.TryGetValues(name,out values)) return null;var found=values.Take(2).ToArray();return found.Length==1?found[0]:null; }
            private static long? NumericHeader(HttpResponseMessage response,string name)
            { string value=Header(response,name);long n;return value!=null && long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out n) && n.ToString(CultureInfo.InvariantCulture)==value?(long?)n:null; }
            private static bool IsAssetCdn(Uri uri)
            { return uri.Scheme=="https" && uri.GetLeftPart(UriPartial.Authority)=="https://release-assets.githubusercontent.com" && uri.UserInfo.Length==0 && uri.Fragment.Length==0 && uri.AbsolutePath.StartsWith("/github-production-release-asset/",StringComparison.Ordinal); }
            private static void ValidateType(HttpResponseMessage response,params string[] types)
            {
                var h=response.Content?.Headers;
                Require(h!=null && types.Contains(h.ContentType?.MediaType) && h.ContentEncoding.Count==0 && h.ContentRange==null && (h.ContentType.CharSet==null || string.Equals(h.ContentType.CharSet.Trim('"'),"utf-8",StringComparison.OrdinalIgnoreCase)),"OriginResponseType");
            }
            private static string HttpCode(HttpResponseMessage response)
            { int status=(int)response.StatusCode; return status==403 || status==429?"RepositoryRateLimited":status==404?"OriginNotFound":status>=300 && status<400?"OriginRedirectRejected":"RepositoryUnavailable"; }
            public void Dispose() { deadline.Dispose(); }
        }
    }
}
