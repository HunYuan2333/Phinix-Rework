using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
internal static class Smoke
{
    private sealed class Mock : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(new byte[] { 123, 125 }) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
    public static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "PhinixRepositoryMono", Guid.NewGuid().ToString("N"));
        try
        {
            var endpoint = new RepositoryEndpoint("https://repo.example.test", "phinix.official");
            byte[] stable = File.ReadAllBytes(Path.Combine(args[0], "stable.json"));
            var pointer = RepositoryMetadata.ReadStable(stable, endpoint.SourceId);
            var entry = new RepositoryCacheEntry(endpoint, stable,
                File.ReadAllBytes(Path.Combine(args[0], "published", pointer.SnapshotId + ".json")),
                File.ReadAllBytes(Path.Combine(args[0], "catalog.json")), "\"stable\"", DateTime.UtcNow);
            var cache = new RepositoryCache(root, endpoint);
            cache.Save(entry, CancellationToken.None); cache.Save(entry, CancellationToken.None);
            if (cache.Read(CancellationToken.None).Catalog.Sha256 != pointer.CatalogSha256) throw new Exception("Cache mismatch");
            File.AppendAllText(cache.FilePath, "tamper");
            if (cache.TryRead(CancellationToken.None) != null) throw new Exception("Corrupt cache accepted");
            var auditLines = new System.Collections.Generic.List<string>();
            using (var connection = new RepositoryTransport(new Mock(), new RepositoryDiagnostics(auditLines.Add, endpoint.SourceId)))
                if (connection.Get(endpoint.Stable, 16, null, CancellationToken.None).GetAwaiter().GetResult().Body.Length != 2) throw new Exception("Transport mismatch");
            if (auditLines.Count < 3 || !auditLines[0].Contains("clientRequestId")) throw new Exception("Audit serialization mismatch");
            Console.WriteLine("Mono production metadata/transport mock/cache create-replace-corruption smoke passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
