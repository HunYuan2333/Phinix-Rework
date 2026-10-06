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
using PhinixClient.Framework;

internal static partial class Program
{
    private static async Task PackageDownloads()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhinixDownloadTests", Guid.NewGuid().ToString("N"));
        var paths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData"));
        var endpoint = new RepositoryEndpoint("https://repo.example.test", Source);
        var record = PayloadRecord(); byte[] manifest = PayloadManifest(record);
        byte[] zip = Zip(PayloadFiles(manifest, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fixture.Plugin.dll"))));
        PackageRecord expected = LockPayload(record, manifest, zip);
        var catalog = new CatalogSnapshot(Source, Revision, Hash, new[] { expected });
        var logs = new List<string>();
        try
        {
            Assert(endpoint.Package(catalog, expected).AbsolutePath == "/v1/sources/" + Source + "/snapshots/" + Revision + "/packages/payload/1.0.0/" + Digest(zip) + "/package", "Payload path binds source, frozen snapshot, exact package version and digest.");
            Expect("DownloadSnapshotMismatch", () => endpoint.Package(new CatalogSnapshot("other", Revision, Hash, new[] { expected }), expected));
            Expect("DownloadSnapshotMismatch", () => endpoint.Package(catalog, LockPayload(record, manifest, zip)));
            using (var handler = new DownloadHandler((r, t) => Task.FromResult(PackageResponse(zip))))
            using (var connection = new RepositoryTransport(handler, new RepositoryDiagnostics(logs.Add, Source)))
            using (var download = await connection.DownloadPackage(endpoint, catalog, expected, paths, CancellationToken.None))
            {
                Assert(download.Report.Sha256 == Digest(zip) && download.Report.Files.Count == 4, "Download feeds the same locked bytes to ZIP/manifest/About/PE static validation.");
                Assert(download.RequestId == "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Successful package check retains the gateway request ID.");
                download.Read(stream => { Assert(!stream.CanWrite && stream.CanSeek && stream.Length == zip.Length, "The owned held file exposes a read-only seekable view."); });
                Assert(handler.Calls == 1 && handler.LastUri == endpoint.Package(catalog, expected), "One whole-package attempt has no arbitrary artifact URL or retry.");
                Assert(!Directory.Exists(paths.LocalModsRoot), "Download preview never creates or writes Mods.");
            }
            Assert(TemporaryDownloads(paths).Length == 0, "Closing a validated download removes its temporary file.");
            Assert(logs.Any(line => line.Contains("package.payload_verified") && line.Contains(expected.Id) && line.Contains(expected.Artifact.Sha256)), "Fixed audit fields retain package identity, digest and request correlation.");
            Assert(logs.All(line => !line.Contains(endpoint.Origin) && !line.Contains(root) && !line.Contains("Authorization")), "Download audit excludes endpoint/path/header data.");
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin"), "Downloaded candidate assembly is not loaded.");

            foreach (var pair in new[] { Tuple.Create("redirect", "RedirectRejected"), Tuple.Create("partial", "RepositoryHttpError"), Tuple.Create("html", "InvalidResponseType"), Tuple.Create("encoded", "InvalidResponseType"), Tuple.Create("range", "InvalidResponseType"), Tuple.Create("declared", "PayloadSizeMismatch"), Tuple.Create("short", "PayloadSizeMismatch"), Tuple.Create("extra", "PayloadLimit"), Tuple.Create("digest", "PayloadDigestMismatch"), Tuple.Create("endpoint", "UnexpectedEndpoint"), Tuple.Create("missing-length", "PayloadSizeMismatch") })
            {
                using (var handler = new DownloadHandler((r, t) =>
                {
                    byte[] body = zip;
                    if (pair.Item1 == "short") body = zip.Take(zip.Length - 1).ToArray();
                    if (pair.Item1 == "extra") body = zip.Concat(new byte[] { 1 }).ToArray();
                    if (pair.Item1 == "digest") { body = (byte[])zip.Clone(); body[0] ^= 1; }
                    var response = PackageResponse(body); response.Content.Headers.ContentLength = zip.Length;
                    if (pair.Item1 == "redirect") response.StatusCode = HttpStatusCode.Found;
                    if (pair.Item1 == "partial") response.StatusCode = HttpStatusCode.PartialContent;
                    if (pair.Item1 == "html") response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                    if (pair.Item1 == "encoded") response.Content.Headers.ContentEncoding.Add("gzip");
                    if (pair.Item1 == "range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, zip.Length - 1, zip.Length);
                    if (pair.Item1 == "declared") response.Content.Headers.ContentLength = zip.Length + 1;
                    if (pair.Item1 == "endpoint") response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.test/");
                    if (pair.Item1 == "missing-length") { response.Content = new UnknownLengthContent(zip); response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); }
                    return Task.FromResult(response);
                }))
                using (var connection = new RepositoryTransport(handler))
                    Expect(pair.Item2, () => connection.DownloadPackage(endpoint, catalog, expected, paths, CancellationToken.None).GetAwaiter().GetResult());
                Assert(TemporaryDownloads(paths).Length == 0, "Rejected " + pair.Item1 + " leaves no complete or partial package.");
            }
            byte[] invalid = Zip(PayloadFiles(manifest, new byte[] { 0, 1, 2, 3 }));
            PackageRecord bad = LockPayload(record, manifest, invalid);
            using (var connection = new RepositoryTransport(new DownloadHandler((r, t) => Task.FromResult(PackageResponse(invalid)))))
                Expect("InvalidAssemblyPayload", () => connection.DownloadPackage(endpoint, new CatalogSnapshot(Source, Revision, Hash, new[] { bad }), bad, paths, CancellationToken.None).GetAwaiter().GetResult());
            Assert(TemporaryDownloads(paths).Length == 0, "A verified transfer digest does not bypass static payload validation or retain failed bytes.");
            Artifact(record)["payloadKind"] = "dll-with-manifest"; Artifact(record)["assetName"] = "Fixture.Plugin.dll";
            var dllRecord = Read(record).Packages[0]; var dllCatalog = new CatalogSnapshot(Source, Revision, Hash, new[] { dllRecord });
            using (var handler = new DownloadHandler((r, t) => Task.FromResult(PackageResponse(zip))))
            using (var connection = new RepositoryTransport(handler))
            { Expect("UnsupportedPayloadDownload", () => connection.DownloadPackage(endpoint, dllCatalog, dllRecord, paths, CancellationToken.None).GetAwaiter().GetResult()); Assert(handler.Calls == 0, "DLL companion metadata gap is rejected before networking."); }
            await PackageDeadlineTests(endpoint, catalog, expected, zip, paths);
            await PackageDownloadController(endpoint, record: PayloadRecord(), zip: zip, manifest: manifest, paths: paths);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task PackageDeadlineTests(RepositoryEndpoint endpoint, CatalogSnapshot catalog, PackageRecord expected, byte[] bytes, ClientEnvironmentPaths paths)
    {
        var late = new TaskCompletionSource<HttpResponseMessage>();
        using (var connection = new RepositoryTransport(new DownloadHandler((r, t) => late.Task)))
            Expect("PackageConnectTimeout", () => connection.DownloadPackage(endpoint, catalog, expected, paths, CancellationToken.None, new PackageDownloadBudget(headers:20)).GetAwaiter().GetResult());
        var lateContent = new TrackingPackageContent(bytes); var lateResponse = PackageResponse(bytes); lateResponse.Content = lateContent; late.SetResult(lateResponse);
        for (int i = 0; i < 100 && !lateContent.Closed; i++) await Task.Delay(2);
        Assert(lateContent.Closed, "A response arriving after the header deadline is closed and never accepted.");
        var hung = new BlockingPackageStream();
        using (var connection = new RepositoryTransport(new DownloadHandler((r, t) => Task.FromResult(StreamPackageResponse(hung, bytes.Length)))))
            Expect("PackageIdleTimeout", () => connection.DownloadPackage(endpoint, catalog, expected, paths, CancellationToken.None, new PackageDownloadBudget(idle:20)).GetAwaiter().GetResult());
        Assert(hung.Closed && TemporaryDownloads(paths).Length == 0, "A non-cooperative body is closed on idle timeout and its held temporary file is removed.");
        hung.Release.TrySetResult(0);
        using (var connection = new RepositoryTransport(new DownloadHandler((r, t) => Task.FromResult(StreamPackageResponse(new TricklePackageStream(bytes), bytes.Length)))))
            Expect("PackageDownloadTimeout", () => connection.DownloadPackage(endpoint, catalog, expected, paths, CancellationToken.None, new PackageDownloadBudget(headers:1000, idle:1000, total:60)).GetAwaiter().GetResult());
        Assert(TemporaryDownloads(paths).Length == 0, "Repeated small reads do not reset the total download deadline.");
        using (var cancelled = new CancellationTokenSource())
        using (var handler = new DownloadHandler((r, t) => Task.FromResult(PackageResponse(bytes))))
        using (var connection = new RepositoryTransport(handler))
        { cancelled.Cancel(); ExpectCancellation(() => connection.DownloadPackage(endpoint, catalog, expected, paths, cancelled.Token).GetAwaiter().GetResult()); Assert(handler.Calls == 0, "Precancelled package download performs no request."); }
    }

    private static async Task PackageDownloadController(RepositoryEndpoint endpoint, Dictionary<string, object> record, byte[] zip, byte[] manifest, ClientEnvironmentPaths paths)
    {
        LockPayload(record, manifest, zip);
        var data = RepositoryFixture("payload", packageChange: p => { p.Clear(); foreach (var pair in record) p.Add(pair.Key, pair.Value); });
        var input = StoreEnvironmentAdapter.FromSnapshot(new ClientEnvironmentSnapshot(paths, Path.Combine(paths.LocalModsRoot, "host"), "1.6", "0.9.7", "1.3.0", null, null, null, null));
        using (var browser = new StoreBrowserController(timeoutMilliseconds:50))
        {
            await browser.RefreshRepository(endpoint, paths.GetExtensionDataDirectory("phinix.plugin-store"), FixtureRepositoryTransport(endpoint, data));
            Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady, "Controlled metadata enters online ready state.");
            await browser.CreatePlan("payload", "1.0.0", input, browser.Snapshot.Catalog);
            var frozen = browser.Snapshot.Plan;
            using (var connection = new RepositoryTransport(new DownloadHandler(async (r, t) => { await Task.Delay(80, t); return PackageResponse(zip); })))
                await browser.CheckPlanDownloads(frozen, paths, connection);
            Assert(browser.Snapshot.State == StoreBrowserState.PayloadsVerified && ReferenceEquals(browser.Snapshot.Plan, frozen) && browser.Snapshot.Payloads.Count == 1, "Package checking has its own budget and outlives the metadata watchdog without callbacks.");
            Assert(TemporaryDownloads(paths).Length == 0 && !Directory.Exists(paths.LocalModsRoot), "UI check keeps reports only, removes temporary packages and installs nothing.");
            await browser.ReadCachedRepository(endpoint, paths.GetExtensionDataDirectory("phinix.plugin-store"));
            await browser.CreatePlan("payload", "1.0.0", input, browser.Snapshot.Catalog);
            Expect("OnlineSnapshotRequired", () => browser.CheckPlanDownloads(browser.Snapshot.Plan, paths));
        }
        using (var browser = new StoreBrowserController())
        {
            await browser.ReadIndex(t => data.Catalog, Source); await browser.CreatePlan("payload", "1.0.0", input, browser.Snapshot.Catalog);
            Expect("OnlineSnapshotRequired", () => browser.CheckPlanDownloads(browser.Snapshot.Plan, paths));
        }
    }
    private static RepositoryTransport FixtureRepositoryTransport(RepositoryEndpoint endpoint, RepoFixture data)
    { return new RepositoryTransport(FixtureRepository(endpoint, data)); }
    private static void ExpectCancellation(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { assertions++; return; }
        throw new Exception("Expected download cancellation.");
    }
    private static string[] TemporaryDownloads(ClientEnvironmentPaths paths)
    { string root = paths.GetExtensionDataDirectory("phinix.plugin-store"); return Directory.Exists(root) ? Directory.GetFiles(root, "*.partial", SearchOption.AllDirectories) : new string[0]; }
    private static HttpResponseMessage PackageResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        response.Headers.Add("X-Phinix-Request-Id", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"); return response;
    }
    private static HttpResponseMessage StreamPackageResponse(Stream stream, int length)
    { var response = PackageResponse(new byte[0]); response.Content = new StreamContent(stream); response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); response.Content.Headers.ContentLength = length; return response; }
    private sealed class DownloadHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply;
        internal int Calls; internal Uri LastUri;
        internal DownloadHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) { this.reply = reply; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; LastUri = request.RequestUri;
            Assert(request.RequestUri.Scheme == "https" && request.Headers.Accept.ToString() == "application/octet-stream", "Package request stays at the binary HTTPS boundary.");
            var response = await reply(request, token); if (response.RequestMessage == null) response.RequestMessage = request; return response;
        }
    }
    private sealed class TrackingPackageContent : ByteArrayContent
    {
        internal bool Closed;
        internal TrackingPackageContent(byte[] bytes) : base(bytes) { }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
    private sealed class BlockingPackageStream : MemoryStream
    {
        internal readonly TaskCompletionSource<int> Release = new TaskCompletionSource<int>(); internal bool Closed;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) { return Release.Task; }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
    private sealed class TricklePackageStream : MemoryStream
    {
        internal TricklePackageStream(byte[] bytes) : base(bytes) { }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { await Task.Delay(5, token); return base.Read(buffer, offset, Math.Min(1, count)); }
    }
}
