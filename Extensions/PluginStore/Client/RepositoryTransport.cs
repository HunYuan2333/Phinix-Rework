using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Phinix.PluginStore
{
    internal sealed class RepositoryResponse
    {
        internal RepositoryResponse(bool notModified, byte[] body, string etag, string requestId = null)
        { NotModified = notModified; Body = body; ETag = etag; RequestId = requestId; }
        public string RequestId { get; }
        public bool NotModified { get; }
        public byte[] Body { get; }
        public string ETag { get; }
    }

    internal sealed partial class RepositoryTransport : IDisposable
    {
        private readonly HttpClient client;
        private readonly RepositoryDiagnostics diagnostics;
        internal string LastRequestId { get; private set; }
        public RepositoryTransport() : this((RepositoryDiagnostics)null) { }
        internal RepositoryTransport(RepositoryDiagnostics diagnostics) : this(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false }, diagnostics) { }
        // Tests use a simulated HTTPS handler; the production origin policy stays unchanged.
        internal RepositoryTransport(HttpMessageHandler handler, RepositoryDiagnostics diagnostics = null)
        {
            this.diagnostics = diagnostics ?? new RepositoryDiagnostics();
            client = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Phinix-PluginStore-Preview/1");
        }
        internal void ConfigureAudit(RepositoryEndpoint endpoint)
        { diagnostics.AccessMethod=endpoint.AccessMethod.ToString();diagnostics.RepositoryIdentity=endpoint.IdentityKey; }
        public async Task<RepositoryResponse> Get(Uri resource, int maximumBytes, string etag, CancellationToken token,bool fresh=false)
        {
            string stage = resource.AbsolutePath.EndsWith("/stable", StringComparison.Ordinal) ? "Stable" : resource.AbsolutePath.Contains("/published/") ? "Published" : "Catalog";
            LastRequestId = null; diagnostics.Event("repository.request", stage);
            using (var request = new HttpRequestMessage(HttpMethod.Get, resource))
            {
                request.Headers.Add("X-Phinix-Client-Request-Id", diagnostics.ClientRequestId);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if(fresh) request.Headers.CacheControl=new CacheControlHeaderValue { NoCache=true };
                if (etag != null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
                try
                {
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        if (response.RequestMessage == null || response.RequestMessage.RequestUri != resource)
                            throw new StoreValidationException("UnexpectedEndpoint", "Response did not come from the requested repository resource.");
                        System.Collections.Generic.IEnumerable<string> ids;
                        if (response.Headers.TryGetValues("X-Phinix-Request-Id", out ids))
                        { var values = ids.ToArray(); if (values.Length == 1) LastRequestId = RepositoryDiagnostics.RequestId(values[0]); }
                        diagnostics.Event("repository.response", stage, requestId: LastRequestId, status: (int)response.StatusCode);
                        if (response.StatusCode == HttpStatusCode.NotModified)
                            return new RepositoryResponse(true, null, ValidETag(response.Headers.ETag?.ToString()), LastRequestId);
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400) throw new StoreValidationException("RedirectRejected", "Repository redirects are not supported.");
                        if (response.StatusCode != HttpStatusCode.OK)
                            throw await ReadFailure(response, status, token).ConfigureAwait(false);
                        var headers = response.Content?.Headers;
                        if (headers == null || headers.ContentType?.MediaType != "application/json" ||
                            (headers.ContentType.CharSet != null && !string.Equals(headers.ContentType.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                            headers.ContentEncoding.Count != 0 || headers.ContentRange != null)
                            throw new StoreValidationException("InvalidResponseType", "Repository metadata must be uncompressed UTF-8 application/json.");
                        long? declared = headers.ContentLength;
                        if (declared.HasValue && (declared.Value < 1 || declared.Value > maximumBytes))
                            throw new StoreValidationException("DocumentLimit", "Repository response exceeds its byte limit.");
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            byte[] buffer = new byte[8192];
                            int count;
                            while ((count = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, maximumBytes + 1 - (int)output.Length), token).ConfigureAwait(false)) != 0)
                            {
                                token.ThrowIfCancellationRequested();
                                if (output.Length + count > maximumBytes) throw new StoreValidationException("DocumentLimit", "Repository response exceeds its byte limit.");
                                output.Write(buffer, 0, count);
                            }
                            if (output.Length == 0 || (declared.HasValue && declared.Value != output.Length))
                                throw new StoreValidationException("ResponseSizeMismatch", "Repository body length differs from its Content-Length.");
                            diagnostics.Event("repository.body_complete", stage, requestId: LastRequestId, bytes: output.Length);
                            return new RepositoryResponse(false, output.ToArray(), ValidETag(response.Headers.ETag?.ToString()), LastRequestId);
                        }
                    }
                }
                catch (StoreValidationException ex) { ex.RequestId = LastRequestId; diagnostics.Event("repository.failed", stage, ex.Code, LastRequestId); throw; }
                catch (OperationCanceledException) { diagnostics.Event("repository.cancelled", stage, requestId: LastRequestId); throw; }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                { diagnostics.Event("repository.failed", stage, "RepositoryUnavailable", LastRequestId); throw new StoreValidationException("RepositoryUnavailable", "Cannot reach the configured repository.", ex) { RequestId = LastRequestId }; }
            }
        }
        private async Task<StoreValidationException> ReadFailure(HttpResponseMessage response, int status, CancellationToken token)
        {
            string code = status == 429 ? "RepositoryRateLimited" : status >= 500 ? "RepositoryUnavailable" : "RepositoryHttpError";
            var headers = response.Content?.Headers;
            // Error envelopes are diagnostic only, never retry/download/publication authority.
            if (headers != null && headers.ContentType?.MediaType == "application/json" && headers.ContentEncoding.Count == 0 && headers.ContentRange == null &&
                (headers.ContentType.CharSet == null || string.Equals(headers.ContentType.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) &&
                (!headers.ContentLength.HasValue || headers.ContentLength.Value <= 4096))
            {
                try
                {
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        byte[] buffer = new byte[1024]; int count;
                        while ((count = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, 4097 - (int)output.Length), token).ConfigureAwait(false)) > 0)
                        { output.Write(buffer, 0, count); if (output.Length > 4096) break; }
                        token.ThrowIfCancellationRequested();
                        if (output.Length > 0 && output.Length <= 4096 && (!headers.ContentLength.HasValue || output.Length == headers.ContentLength.Value))
                        {
                            var fields = CatalogReader.Object(CatalogReader.ReadJson(output.ToArray()), "error", "code", "retryable", "requestId");
                            string remoteCode = RepositoryDiagnostics.Code(CatalogReader.Text(CatalogReader.Required(fields, "code"), "code", 64));
                            string remoteId = RepositoryDiagnostics.RequestId(CatalogReader.Text(CatalogReader.Required(fields, "requestId"), "requestId", 36));
                            CatalogReader.Boolean(CatalogReader.Required(fields, "retryable"), "retryable");
                            if (remoteCode != null && remoteId != null && (LastRequestId == null || LastRequestId == remoteId))
                            { code = remoteCode; LastRequestId = remoteId; }
                        }
                    }
                }
                catch (StoreValidationException) { /* Retain HTTP diagnosis for untrusted/malformed envelopes. */ }
                catch (IOException) { /* Keep the status if the error body itself is truncated. */ }
            }
            return new StoreValidationException(code, "Repository returned HTTP " + status + ".") { RequestId = LastRequestId };
        }
        internal static string ValidETag(string value)
        {
            EntityTagHeaderValue parsed;
            return value != null && value.Length <= 256 && EntityTagHeaderValue.TryParse(value, out parsed) && parsed.Tag != "*" ? parsed.ToString() : null;
        }
        public void Dispose() { client.Dispose(); }
    }
}
