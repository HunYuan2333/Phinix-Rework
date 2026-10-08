using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;

internal static partial class Program
{
    private static async Task RepositoryAudit()
    {
        const string id = "12345678-1234-1234-1234-123456789abc";
        var endpoint = new RepositoryEndpoint("https://repo.example.test", Source);
        var logs = new List<string>(); var audit = new RepositoryDiagnostics(logs.Add, Source);
        var contextLogs=new List<string>();
        new RepositoryDiagnostics(contextLogs.Add,Source) {Operation="Installing",ExceptionType="IOException",
            ContextReasons=new[]{"GamePathsUnavailable","ModuleOwnershipUnknown","/home/SECRET","SECRET token","GamePathsUnavailable"}}
            .Event("managed.operation_failed","ManagedStore","IncompleteEnvironment");
        Assert(contextLogs.Single().Contains("\"operation\":\"Installing\"") && contextLogs.Single().Contains("\"exceptionType\":\"IOException\"") &&
            contextLogs.Single().Contains("GamePathsUnavailable"),"Failure audit records operation, exception type and safe readiness causes.");
        Assert(!contextLogs.Single().Contains("SECRET"),"Readiness diagnostics exclude paths and uncontrolled text.");
        using (var transport = new RepositoryTransport(new MockRepository((r, t) =>
        {
            Assert(r.Headers.GetValues("X-Phinix-Client-Request-Id").Single() == audit.ClientRequestId, "One refresh correlation ID is sent to the gateway.");
            var response = JsonResponse(EncodingBytes("{\"code\":\"OriginAssetMismatch\",\"retryable\":false,\"requestId\":\"" + id + "\"}"));
            response.StatusCode = HttpStatusCode.BadGateway; response.Headers.Add("X-Phinix-Request-Id", id); return Task.FromResult(response);
        }), audit))
        {
            try { await transport.Get(endpoint.Stable, 100, null, CancellationToken.None); Assert(false, "Structured error must fail."); }
            catch (StoreValidationException ex) { Assert(ex.Code == "OriginAssetMismatch" && ex.RequestId == id, "Gateway error code and request ID survive into client diagnostics."); }
        }
        Assert(logs.Any(l => l.Contains("repository.failed") && l.Contains(id)), "Failure is logged before a UI observes it.");
        foreach (string bad in new[] { "{\"code\":\"SECRET token\",\"retryable\":false,\"requestId\":\"" + id + "\"}",
            "{\"code\":\"OriginAssetMismatch\",\"retryable\":\"false\",\"requestId\":\"" + id + "\"}",
            "{\"code\":\"OriginAssetMismatch\",\"retryable\":false,\"requestId\":\"" + id + "\",\"url\":\"SECRET\"}",
            "{\"code\":\"A\",\"code\":\"B\",\"retryable\":false,\"requestId\":\"" + id + "\"}", "<html>SECRET</html>", new string('x', 5000) })
        {
            using (var transport = new RepositoryTransport(new MockRepository((r, t) =>
            { var response = JsonResponse(EncodingBytes(bad)); response.StatusCode = HttpStatusCode.ServiceUnavailable; response.Headers.Add("X-Phinix-Request-Id", id); return Task.FromResult(response); }), audit))
                Expect("RepositoryUnavailable", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        }
        using (var transport = new RepositoryTransport(new MockRepository((r, t) =>
        { var response = JsonResponse(EncodingBytes("{\"code\":\"BadBinding\",\"retryable\":false,\"requestId\":\"" + id.Replace("abc", "def") + "\"}")); response.StatusCode = HttpStatusCode.BadGateway; response.Headers.Add("X-Phinix-Request-Id", id); return Task.FromResult(response); }), audit))
            Expect("RepositoryUnavailable", () => transport.Get(endpoint.Stable, 100, null, CancellationToken.None).GetAwaiter().GetResult());
        using (var transport = new RepositoryTransport(new MockRepository((r, t) =>
        { var response = JsonResponse(EncodingBytes("{}")); response.Headers.Add("X-Phinix-Request-Id", "SECRET: bad id"); return Task.FromResult(response); }), audit))
            Assert((await transport.Get(endpoint.Stable, 100, null, CancellationToken.None)).RequestId == null, "Malformed request headers are not trusted as diagnostic identities.");
        using (var transport = new RepositoryTransport(new MockRepository((r, t) => Task.FromResult(JsonResponse(EncodingBytes("{}")))), new RepositoryDiagnostics(l => { throw new IOException("Sink unavailable"); })))
            Assert((await transport.Get(endpoint.Stable, 100, null, CancellationToken.None)).Body.Length == 2, "A broken log sink cannot turn successful metadata into failure.");
        Assert(!string.Join("\n", logs).Contains("SECRET") && !string.Join("\n", logs).Contains(endpoint.Origin), "Raw error bodies, URLs and invalid IDs never enter structured client logs.");
        foreach (string line in logs) { using (var document = JsonDocument.Parse(line)) Assert(document.RootElement.GetProperty("clientRequestId").GetString() == audit.ClientRequestId, "JSON audit records retain the refresh correlation ID."); }
        string root = Path.Combine(Path.GetTempPath(), "PhinixAudit", Guid.NewGuid().ToString("N"));
        try
        {
            var data = RepositoryFixture(); logs.Clear();
            using (var browser = new StoreBrowserController(repositoryLog: logs.Add))
            {
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(FixtureRepository(endpoint, data)));
                Assert(logs.Any(l => l.Contains("repository.chain_verified")) && logs.Any(l => l.Contains("repository.cache_committed")), "Verified chain and atomic cache commit are separate events.");
                logs.Clear();
                await browser.RefreshRepository(endpoint, root, new RepositoryTransport(new MockRepository((r, t) =>
                { var response = JsonResponse(EncodingBytes("{}")); response.StatusCode = HttpStatusCode.ServiceUnavailable; response.Headers.Add("X-Phinix-Request-Id", id); return Task.FromResult(response); })));
                Assert(browser.Snapshot.Error.Contains(id), "The player can report the gateway request ID shown with the failure.");
                Assert(logs.Any(l => l.Contains("repository.task_failed") && l.Contains(id)) && !logs.Any(l => l.Contains("repository.cache_committed")), "Failed refresh logs a terminal failure and never claims cache commit.");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
