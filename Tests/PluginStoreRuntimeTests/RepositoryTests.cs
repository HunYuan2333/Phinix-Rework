using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void Repositories()
    {
        string vectors = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RepositoryProtocol");
        byte[] vectorStable = File.ReadAllBytes(Path.Combine(vectors, "stable.json"));
        RepositoryStable vector = RepositoryMetadata.ReadStable(vectorStable, "phinix.official");
        Assert(RepositoryMetadata.Verify(vector, File.ReadAllBytes(Path.Combine(vectors, "published", vector.SnapshotId + ".json")), File.ReadAllBytes(Path.Combine(vectors, "catalog.json"))).Packages.Count == 0,
            "Fixed draft generator bytes bind the known bootstrap empty catalog without claiming a real release.");
        var endpoint = new RepositoryEndpoint("https://repo.example.test", Source);
        Assert(endpoint.Stable.AbsoluteUri == "https://repo.example.test/v1/sources/test.local/stable", "Stable path is constructed from canonical endpoint/source.");
        foreach (string origin in new[] { "http://repo.example.test", "https://repo.example.test/path", "https://user:pass@repo.example.test", "https://repo.example.test?x=1", "https://repo.example.test#x", "https://repo.example.test//", "https://repo.example.test/%2F", " https://repo.example.test", "https://REPO.example.test", "https://repo.example.test:443", "https://repo.example.test\\evil" })
            Expect("InvalidEndpoint", () => new RepositoryEndpoint(origin, Source));
        foreach (string source in new[] { "../evil", "test.local\n", "test/local", "TEST", "test%2elocal" })
            Expect("InvalidId", () => new RepositoryEndpoint(endpoint.Origin, source));
        Assert(new RepositoryEndpoint(endpoint.Origin + "/", Source).CacheKey == endpoint.CacheKey, "Optional root slash does not split the origin cache key.");
        var data = RepositoryFixture();
        RepositoryStable stable = RepositoryMetadata.ReadStable(data.Stable, Source);
        Assert(endpoint.Catalog(stable).AbsolutePath.EndsWith("/catalog/" + Digest(data.Catalog), StringComparison.Ordinal), "Catalog path binds the raw catalog hash.");
        Assert(endpoint.Published(stable).AbsolutePath.EndsWith("/published/" + Digest(data.Published), StringComparison.Ordinal), "Published path binds the raw descriptor hash.");
        Assert(RepositoryMetadata.Verify(stable, data.Published, data.Catalog).Packages.Count == 1, "Stable, published and raw catalog are cross-validated.");
        Expect("DocumentLimit", () => RepositoryMetadata.ReadStable(new byte[RepositoryMetadata.MaxMetadataBytes + 1], Source));
        Expect("UnknownField", () => RepositoryMetadata.ReadStable(EncodingBytes(System.Text.Encoding.UTF8.GetString(data.Stable).TrimEnd('}') + ",\"url\":\"https://evil.test\"}"), Source));
        Expect("DuplicateField", () => RepositoryMetadata.ReadStable(EncodingBytes(System.Text.Encoding.UTF8.GetString(data.Stable).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")), Source));
        Expect("InvalidNumber", () => RepositoryMetadata.ReadStable(EncodingBytes(System.Text.Encoding.UTF8.GetString(data.Stable).Replace("\"catalogSchemaVersion\":1", "\"catalogSchemaVersion\":2")), Source));
        Expect("SourceMismatch", () => RepositoryMetadata.ReadStable(data.Stable, "another.source"));
        Expect("PublishedSizeMismatch", () => RepositoryMetadata.Verify(stable, new byte[1], data.Catalog));
        byte[] bad = (byte[])data.Published.Clone(); bad[bad.Length - 1] = (byte)' ';
        Expect("PublishedDigestMismatch", () => RepositoryMetadata.Verify(stable, bad, data.Catalog));
        Expect("CatalogSizeMismatch", () => RepositoryMetadata.Verify(stable, data.Published, new byte[1]));
        bad = (byte[])data.Catalog.Clone(); bad[bad.Length - 1] = (byte)' ';
        Expect("CatalogDigestMismatch", () => RepositoryMetadata.Verify(stable, data.Published, bad));
        var mismatch = RepositoryFixture(publishedSource: "another.source");
        Expect("PublishedMismatch", () => RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(mismatch.Stable, Source), mismatch.Published, mismatch.Catalog));
        mismatch = RepositoryFixture(catalogRevision: new string('3', 40));
        Expect("SnapshotMismatch", () => RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(mismatch.Stable, Source), mismatch.Published, mismatch.Catalog));
        foreach (string name in new[] { "repositoryId", "ownerId", "releaseId", "assetId" })
        {
            mismatch = RepositoryFixture(descriptorChange: d => d[name] = "01");
            Expect("InvalidSourceId", () => RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(mismatch.Stable, Source), mismatch.Published, mismatch.Catalog));
        }
        mismatch = RepositoryFixture(descriptorChange: d => d["repository"] = "https://github.com/test/index");
        Expect("InvalidRepository", () => RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(mismatch.Stable, Source), mismatch.Published, mismatch.Catalog));
        mismatch = RepositoryFixture(descriptorChange: d => d["assetName"] = "../catalog.json");
        Expect("InvalidCatalogAsset", () => RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(mismatch.Stable, Source), mismatch.Published, mismatch.Catalog));
        RepositoryTransportFailures(endpoint).GetAwaiter().GetResult();
        RepositoryCacheAndBrowser(endpoint, data).GetAwaiter().GetResult();
    }

    private static async Task RepositoryTransportFailures(RepositoryEndpoint endpoint)
    {
        foreach (var pair in new[] { Tuple.Create(302, "RedirectRejected"), Tuple.Create(307, "RedirectRejected"), Tuple.Create(429, "RepositoryRateLimited"), Tuple.Create(503, "RepositoryUnavailable"), Tuple.Create(404, "RepositoryHttpError"), Tuple.Create(206, "RepositoryHttpError") })
        {
            using (var transport = new RepositoryTransport(new MockRepository((r, t) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)pair.Item1)))))
                Expect(pair.Item2, () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        }
        foreach (string type in new[] { "text/html", "application/json; charset=utf-16", "application/octet-stream" })
        {
            using (var transport = new RepositoryTransport(new MockRepository((r, t) => { var response = JsonResponse(EncodingBytes("{}")); response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(type); return Task.FromResult(response); })))
                Expect("InvalidResponseType", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        }
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => { var response = JsonResponse(EncodingBytes("{}")); response.Content.Headers.ContentEncoding.Add("gzip"); return Task.FromResult(response); })))
            Expect("InvalidResponseType", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => Task.FromResult(JsonResponse(new byte[101])))))
            Expect("DocumentLimit", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => { var response = JsonResponse(new byte[101]); response.Content = new UnknownLengthContent(new byte[101]); response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); return Task.FromResult(response); })))
            Expect("DocumentLimit", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => { var response = JsonResponse(EncodingBytes("{}")); response.Content.Headers.ContentLength = 50; return Task.FromResult(response); })))
            Expect("ResponseSizeMismatch", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => { var response = JsonResponse(EncodingBytes("{}")); response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.test/"); return Task.FromResult(response); })))
            Expect("UnexpectedEndpoint", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => { throw new HttpRequestException("Simulated unavailable origin"); })))
            Expect("RepositoryUnavailable", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var source = new CancellationTokenSource())
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => Task.FromResult(JsonResponse(EncodingBytes("{}"))))))
        {
            source.Cancel();
            bool cancelled = false;
            try { transport.Get(endpoint.Stable, 100, null, source.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Transport respects cancellation before consuming metadata.");
        }
        Assert(RepositoryTransport.ValidETag("*") == null && RepositoryTransport.ValidETag("\"ok\"\r\nX: y") == null, "Invalid HTTP validators cannot be replayed from cache.");
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => Task.FromResult(JsonResponse(EncodingBytes("{}"), "\"stable\"")))))
        {
            var result = await transport.Get(endpoint.Stable, 100, null, CancellationToken.None);
            Assert(!result.NotModified && result.ETag == "\"stable\"" && result.Body.Length == 2, "Transport preserves raw bytes and a usable HTTP validator.");
        }
    }

    private static async Task RepositoryCacheAndBrowser(RepositoryEndpoint endpoint, RepoFixture data)
    {
        string root = Path.Combine(Path.GetTempPath(), "PhinixRepository", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cache = new RepositoryCache(root, endpoint);
            var handler = FixtureRepository(endpoint, data);
            using (var browser = new StoreBrowserController())
            {
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(handler));
                Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady && browser.Snapshot.Repository != null && !browser.Snapshot.Repository.Offline, "Remote browser verifies the whole chain and records provenance.");
                Assert(handler.Paths.Count == 3 && handler.Paths[0] == endpoint.Stable.AbsolutePath && handler.Paths[1] == endpoint.Published(RepositoryMetadata.ReadStable(data.Stable, Source)).AbsolutePath, "First load requests stable, descriptor and catalog through one origin.");
                Assert(cache.Read(CancellationToken.None).Catalog.Sha256 == Digest(data.Catalog), "Successful background completion persists a fully validated cache.");
                var initialEntry = cache.Read(CancellationToken.None);
                CheckCacheStaging(cache.DirectoryPath, cache.FilePath, () => cache.Stage(initialEntry, CancellationToken.None));
                byte[] acceptedBytes = File.ReadAllBytes(cache.FilePath);
                CatalogSnapshot accepted = browser.Snapshot.Catalog;
                var conditional = new MockRepository((r, t) =>
                {
                    Assert(r.Headers.IfNoneMatch.ToString() == "\"stable\"", "Conditional request is sent only for a verified complete cache.");
                    return Task.FromResult(NotModified("\"stable\""));
                });
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(conditional));
                Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady && conditional.Paths.Count == 1, "304 reuses validated descriptor/catalog without extra requests.");
                accepted = browser.Snapshot.Catalog; acceptedBytes = File.ReadAllBytes(cache.FilePath);
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(new MockRepository((r, t) => Task.FromResult(JsonResponse(EncodingBytes("{}"))))));
                Assert(browser.Snapshot.State == StoreBrowserState.Failed && ReferenceEquals(browser.Snapshot.Catalog, accepted) && browser.Snapshot.Repository.Stale, "Malformed refresh preserves the accepted catalog and labels it old.");
                Assert(File.ReadAllBytes(cache.FilePath).SequenceEqual(acceptedBytes), "Failed validation does not replace live cache bytes.");
                var mutation = RepositoryFixture(name: "mutated");
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(FixtureRepository(endpoint, mutation)));
                Assert(browser.Snapshot.ErrorCode == "SnapshotIdentityChanged" && File.ReadAllBytes(cache.FilePath).SequenceEqual(acceptedBytes), "Observed snapshot descriptors/catalog cannot be overwritten in place.");
                mutation = RepositoryFixture(revision: new string('5', 40), packageChange: p => Artifact(p)["sha256"] = new string('3', 64));
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(FixtureRepository(endpoint, mutation)));
                Assert(browser.Snapshot.ErrorCode == "PackageIdentityChanged" && File.ReadAllBytes(cache.FilePath).SequenceEqual(acceptedBytes), "An existing package version cannot quietly change its artifact in a new snapshot.");
                var withdrawn = RepositoryFixture(revision: new string('6', 40), packageChange: p => p["state"] = "withdrawn");
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(FixtureRepository(endpoint, withdrawn)));
                Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady && browser.Snapshot.Catalog.Packages[0].State == "withdrawn", "A new snapshot can withdraw unchanged package content.");
                acceptedBytes = File.ReadAllBytes(cache.FilePath);
                var updated = RepositoryFixture(name: "updated", revision: new string('4', 40));
                var broken = FixtureRepository(endpoint, updated, corruptCatalog: true);
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(broken));
                Assert(browser.Snapshot.State == StoreBrowserState.Failed && browser.Snapshot.ErrorCode == "CatalogDigestMismatch", "Raw catalog tampering is rejected after a valid pointer/descriptor.");
                Assert(File.ReadAllBytes(cache.FilePath).SequenceEqual(acceptedBytes), "Hash failure preserves the entire old bundle.");
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(FixtureRepository(endpoint, updated)));
                Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady && browser.Snapshot.Catalog.SnapshotId == new string('4', 40), "New snapshot replaces a complete previous bundle atomically.");
                Assert(!Directory.GetFiles(cache.DirectoryPath, "*.tmp").Any(), "Completed refresh leaves no staging file.");
            }
            using (var restarted = new StoreBrowserController())
            {
                await restarted.ReadCachedRepository(endpoint, root);
                Assert(restarted.Snapshot.Repository.Offline && restarted.Snapshot.Catalog.SnapshotId == new string('4', 40), "A new controller browses the persisted validated cache with no transport.");
                var other = new RepositoryEndpoint("https://other.example.test", Source);
                await restarted.ReadCachedRepository(other, root);
                Assert(restarted.Snapshot.ErrorCode == "CacheUnavailable" && restarted.Snapshot.Catalog == null, "Endpoint changes do not expose another origin's old catalog.");
                other = new RepositoryEndpoint(endpoint.Origin, "another.source");
                Assert(new RepositoryCache(root, other).TryRead(CancellationToken.None) == null, "Caches are isolated by source as well as origin.");
            }
            byte[] goodBundle = File.ReadAllBytes(cache.FilePath);
            File.AppendAllText(cache.FilePath, "tamper");
            Assert(cache.TryRead(CancellationToken.None) == null, "Trailing cache bytes invalidate the bundle.");
            int requests = 0;
            var noBody304 = new MockRepository((r, t) =>
            {
                Assert(!r.Headers.IfNoneMatch.Any(), "Corrupt body never supplies a conditional validator.");
                if (++requests == 1) return Task.FromResult(NotModified(null));
                return FixtureReply(endpoint, data, r);
            });
            using (var browser = new StoreBrowserController())
            {
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(noBody304));
                Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady && requests == 4, "304 without a body retries stable unconditionally before reconstructing the chain.");
            }
            File.WriteAllBytes(cache.FilePath, new byte[] { 1, 2, 3 });
            Assert(cache.TryRead(CancellationToken.None) == null, "Truncated cache is rejected.");
            using (var browser = new StoreBrowserController())
            {
                var repeated304 = new MockRepository((r, t) => Task.FromResult(NotModified(null)));
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(repeated304));
                Assert(browser.Snapshot.ErrorCode == "UnexpectedNotModified" && repeated304.Paths.Count == 2, "Repeated invalid 304 fails after one bounded retry.");
            }
            File.WriteAllBytes(cache.FilePath, goodBundle);
            var foreignCache = new RepositoryCache(root, new RepositoryEndpoint("https://foreign.example.test", Source));
            Directory.CreateDirectory(foreignCache.DirectoryPath); File.WriteAllBytes(foreignCache.FilePath, goodBundle);
            Assert(foreignCache.TryRead(CancellationToken.None) == null, "A copied bundle with another embedded origin fails verification.");
            var oldEntry = cache.Read(CancellationToken.None);
            cache.Save(new RepositoryCacheEntry(endpoint, oldEntry.StableBytes, oldEntry.PublishedBytes, oldEntry.CatalogBytes, oldEntry.ETag, DateTime.UtcNow.AddHours(-1)), CancellationToken.None);
            using (var browser = new StoreBrowserController())
            {
                await browser.ReadCachedRepository(endpoint, root);
                Assert(browser.Snapshot.Repository.Offline && browser.Snapshot.Repository.Stale, "Expired offline cache stays browsable and is visibly stale.");
            }
            await RepositoryLateCancellation(endpoint, root, cache);
            // A failed atomic replacement must retain previous cache (directory at target prevents replacement).
            string alternate = Path.Combine(root, "blocked");
            var blockedCache = new RepositoryCache(alternate, endpoint);
            Directory.CreateDirectory(blockedCache.FilePath);
            using (var browser = new StoreBrowserController())
            {
                await browser.RefreshRepository(endpoint, alternate, new RepositoryTransport(FixtureRepository(endpoint, data)));
                Assert(browser.Snapshot.State == StoreBrowserState.Failed && browser.Snapshot.Catalog == null && Directory.Exists(blockedCache.FilePath), "Cache commit failures are terminal and do not claim persisted success.");
                Assert(!Directory.GetFiles(blockedCache.DirectoryPath, "*.tmp").Any(), "Failed commit removes only its own staging file.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task RepositoryLateCancellation(RepositoryEndpoint endpoint, string root, RepositoryCache cache)
    {
        foreach (bool timeout in new[] { false, true })
        {
            byte[] original = File.ReadAllBytes(cache.FilePath);
            var entered = new ManualResetEventSlim();
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var browser = new StoreBrowserController(timeout ? 50 : 10000))
            {
                Task operation = browser.RefreshRepository(endpoint, root, new RepositoryTransport(new MockRepository((r, t) => { entered.Set(); return release.Task; })));
                Assert(entered.Wait(5000), "Stalled simulated HTTPS request entered.");
                if (timeout) Assert(SpinWait.SpinUntil(() => browser.Snapshot.State == StoreBrowserState.Failed, 5000), "Metadata watchdog records timeout independently of stalled IO.");
                else browser.Cancel();
                Assert(browser.Snapshot.State == (timeout ? StoreBrowserState.Failed : StoreBrowserState.Cancelled), "Cancelled/timed-out metadata has an immediate terminal state.");
                await browser.ReadCachedRepository(endpoint, root);
                CatalogSnapshot newer = browser.Snapshot.Catalog;
                release.SetResult(JsonResponse(RepositoryFixture(name: "late").Stable));
                await operation;
                Assert(ReferenceEquals(browser.Snapshot.Catalog, newer), "Late network completion cannot overwrite a newer browsing operation.");
                Assert(File.ReadAllBytes(cache.FilePath).SequenceEqual(original), "Cancelled/timed-out network worker cannot write live cache.");
            }
            entered.Dispose();
        }
    }

    private sealed class RepoFixture { public byte[] Stable, Published, Catalog; }
    private static RepoFixture RepositoryFixture(string name = "a", string revision = Revision, string publishedSource = Source, string catalogRevision = null, Action<Dictionary<string, object>> descriptorChange = null, Action<Dictionary<string, object>> packageChange = null)
    {
        var package = Package(name); packageChange?.Invoke(package);
        byte[] catalog = Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "sourceId", Source }, { "snapshotId", catalogRevision ?? revision }, { "packages", new[] { package } } });
        var publishedFields = new Dictionary<string, object> { { "schemaVersion", 1 }, { "sourceId", publishedSource }, { "snapshotId", revision }, { "catalogSchemaVersion", 1 }, { "catalogSha256", Digest(catalog) }, { "catalogSizeBytes", catalog.Length }, { "repository", "test-owner/index" }, { "repositoryId", "1" }, { "ownerId", "2" }, { "releaseId", "3" }, { "assetId", "4" }, { "assetName", "catalog.json" } };
        descriptorChange?.Invoke(publishedFields);
        byte[] published = Serialize(publishedFields);
        byte[] stable = Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "sourceId", Source }, { "snapshotId", revision }, { "catalogSchemaVersion", 1 }, { "catalogSha256", Digest(catalog) }, { "catalogSizeBytes", catalog.Length }, { "publishedSha256", Digest(published) }, { "publishedSizeBytes", published.Length } });
        return new RepoFixture { Catalog = catalog, Published = published, Stable = stable };
    }
    private static MockRepository FixtureRepository(RepositoryEndpoint endpoint, RepoFixture data, bool corruptCatalog = false)
    { return new MockRepository((r, t) => FixtureReply(endpoint, data, r, corruptCatalog)); }
    private static Task<HttpResponseMessage> FixtureReply(RepositoryEndpoint endpoint, RepoFixture data, HttpRequestMessage request, bool corruptCatalog = false)
    {
        var stable = RepositoryMetadata.ReadStable(data.Stable, Source);
        if (request.RequestUri == endpoint.Stable) return Task.FromResult(JsonResponse(data.Stable, "\"stable\""));
        if (request.RequestUri == endpoint.Published(stable)) return Task.FromResult(JsonResponse(data.Published));
        if (request.RequestUri == endpoint.Catalog(stable))
        {
            byte[] bytes = (byte[])data.Catalog.Clone();
            if (corruptCatalog) bytes[bytes.Length - 1] = (byte)' ';
            return Task.FromResult(JsonResponse(bytes));
        }
        throw new Exception("Unexpected mock resource " + request.RequestUri);
    }
    private static HttpResponseMessage JsonResponse(byte[] body, string etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (etag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }
    private static HttpResponseMessage NotModified(string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotModified);
        if (etag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }
    private static byte[] EncodingBytes(string value) { return System.Text.Encoding.UTF8.GetBytes(value); }
    private sealed class MockRepository : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply;
        public readonly List<string> Paths = new List<string>();
        public MockRepository(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) { this.reply = reply; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri.AbsolutePath);
            Assert(request.RequestUri.Scheme == "https" && request.Headers.Accept.ToString() == "application/json", "All mock metadata requests obey the HTTPS JSON boundary.");
            HttpResponseMessage response = await reply(request, token);
            if (response.RequestMessage == null) response.RequestMessage = request;
            return response;
        }
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthContent(byte[] bytes) { this.bytes = bytes; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) { return stream.WriteAsync(bytes, 0, bytes.Length); }
    }
}
