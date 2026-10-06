using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    internal sealed class PackageDownloadBudget
    {
        internal PackageDownloadBudget(int headers = 10000, int idle = 10000, int total = 120000)
        {
            if (headers < 1 || headers > 10000 || idle < 1 || idle > 10000 || total < 1 || total > 120000)
                throw new ArgumentOutOfRangeException(nameof(total));
            Headers = headers; Idle = idle; Total = total;
        }
        internal int Headers { get; }
        internal int Idle { get; }
        internal int Total { get; }
    }

    // Owns one exclusive, delete-on-close temporary file. A report is not install authorization.
    internal sealed class ValidatedPackageDownload : IDisposable
    {
        private FileStream file;
        internal ValidatedPackageDownload(FileStream file, PayloadValidationReport report, string requestId)
        { this.file = file; Report = report; RequestId = requestId; }
        internal PayloadValidationReport Report { get; }
        internal string RequestId { get; }
        internal void Read(Action<Stream> consume)
        {
            if (file == null) throw new ObjectDisposedException(nameof(ValidatedPackageDownload));
            file.Position = 0;
            using (var view = new ReadOnlyView(file)) consume(view);
        }
        public void Dispose() { var owned = file; file = null; owned?.Dispose(); }
        private sealed class ReadOnlyView : Stream
        {
            private readonly Stream source;
            internal ReadOnlyView(Stream source) { this.source = source; }
            public override bool CanRead => source.CanRead;
            public override bool CanSeek => source.CanSeek;
            public override bool CanWrite => false;
            public override long Length => source.Length;
            public override long Position { get => source.Position; set => source.Position = value; }
            public override int Read(byte[] buffer, int offset, int count) { return source.Read(buffer, offset, count); }
            public override long Seek(long offset, SeekOrigin origin) { return source.Seek(offset, origin); }
            public override void Flush() { }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }

    internal sealed partial class RepositoryTransport
    {
        internal async Task<ValidatedPackageDownload> DownloadPackage(RepositoryEndpoint endpoint, CatalogSnapshot catalog,
            PackageRecord expected, ClientEnvironmentPaths paths, CancellationToken token, PackageDownloadBudget budget = null)
        {
            Uri resource = endpoint.Package(catalog, expected);
            if (expected.Artifact.PayloadKind != "rimworld-mod-zip")
                throw new StoreValidationException("UnsupportedPayloadDownload", "This download preview supports ZIP packages only.");
            if (expected.Artifact.SizeBytes < 1 || expected.Artifact.SizeBytes > CatalogReader.MaxPackageBytes)
                throw new StoreValidationException("PayloadLimit", "Locked package exceeds the download limit.");
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            budget = budget ?? new PackageDownloadBudget();
            LastRequestId = null; diagnostics.Event("package.download_started", "Package", package: expected);
            FileStream file = null; HttpResponseMessage response = null;
            using (var total = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(HttpMethod.Get, resource))
            {
                total.CancelAfter(budget.Total);
                request.Headers.Add("X-Phinix-Client-Request-Id", diagnostics.ClientRequestId);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                try
                {
                    response = await PackageOperation(ct => client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct),
                        budget.Headers, total.Token, token, "PackageConnectTimeout", null, late => late.Dispose()).ConfigureAwait(false);
                    if (response.RequestMessage == null || response.RequestMessage.RequestUri != resource)
                        throw new StoreValidationException("UnexpectedEndpoint", "Response did not come from the locked repository resource.");
                    System.Collections.Generic.IEnumerable<string> ids;
                    if (response.Headers.TryGetValues("X-Phinix-Request-Id", out ids))
                    { var values = System.Linq.Enumerable.ToArray(ids); if (values.Length == 1) LastRequestId = RepositoryDiagnostics.RequestId(values[0]); }
                    diagnostics.Event("package.response", "Package", requestId: LastRequestId, status: (int)response.StatusCode, package: expected);
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400) throw new StoreValidationException("RedirectRejected", "Package redirects are not supported.");
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw await PackageOperation(ct => ReadFailure(response, status, ct), budget.Idle, total.Token, token,
                            "PackageIdleTimeout", () => response.Dispose()).ConfigureAwait(false);
                    var headers = response.Content?.Headers;
                    if (headers == null || headers.ContentType?.MediaType != "application/octet-stream" || headers.ContentType.CharSet != null ||
                        headers.ContentEncoding.Count != 0 || headers.ContentRange != null)
                        throw new StoreValidationException("InvalidResponseType", "Package must be an uncompressed binary response.");
                    if (!headers.ContentLength.HasValue || headers.ContentLength.Value != expected.Artifact.SizeBytes)
                        throw new StoreValidationException("PayloadSizeMismatch", "Content-Length differs from the locked package.");
                    string directory = Path.Combine(paths.GetExtensionDataDirectory("phinix.plugin-store"), "package-downloads", endpoint.CacheKey);
                    CheckDownloadLinks(directory); Directory.CreateDirectory(directory); CheckDownloadLinks(directory);
                    file = new FileStream(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial"), FileMode.CreateNew,
                        FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
                    using (Stream body = await PackageOperation(ct => response.Content.ReadAsStreamAsync(), budget.Idle, total.Token, token,
                        "PackageIdleTimeout", () => response.Dispose(), late => late.Dispose()).ConfigureAwait(false))
                    using (var hash = SHA256.Create())
                    {
                        byte[] buffer = new byte[8192]; long bytes = 0;
                        while (true)
                        {
                            int count = await PackageOperation(ct => body.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, expected.Artifact.SizeBytes - bytes + 1), ct),
                                budget.Idle, total.Token, token, "PackageIdleTimeout", () => response.Dispose()).ConfigureAwait(false);
                            if (count == 0) break;
                            bytes += count;
                            if (bytes > expected.Artifact.SizeBytes) throw new StoreValidationException("PayloadLimit", "Package body exceeds the locked byte limit.");
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                            await PackageOperation(async ct => { await file.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false); return true; },
                                budget.Idle, total.Token, token, "PackageWriteTimeout", () => file.Dispose()).ConfigureAwait(false);
                        }
                        if (bytes != expected.Artifact.SizeBytes) throw new StoreValidationException("PayloadSizeMismatch", "Package body was truncated.");
                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        if (BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant() != expected.Artifact.Sha256)
                            throw new StoreValidationException("PayloadDigestMismatch", "Package SHA-256 differs from the locked snapshot.");
                        diagnostics.Event("package.bytes_verified", "Package", requestId: LastRequestId, bytes: bytes, package: expected);
                    }
                    await PackageOperation(async ct => { await file.FlushAsync(ct).ConfigureAwait(false); return true; },
                        budget.Idle, total.Token, token, "PackageWriteTimeout", () => file.Dispose()).ConfigureAwait(false);
                    total.Token.ThrowIfCancellationRequested(); file.Position = 0;
                    PayloadValidationReport report = PayloadValidator.Validate(expected, file, null, total.Token);
                    total.Token.ThrowIfCancellationRequested(); token.ThrowIfCancellationRequested();
                    diagnostics.Event("package.payload_verified", "Package", requestId: LastRequestId, bytes: file.Length, package: expected);
                    var result = new ValidatedPackageDownload(file, report, LastRequestId); file = null; return result;
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                    { diagnostics.Event("package.cancelled", "Package", requestId: LastRequestId, package: expected); throw; }
                    string code = total.IsCancellationRequested ? "PackageDownloadTimeout" : "PackageUnavailable";
                    diagnostics.Event("package.failed", "Package", code, LastRequestId, package: expected);
                    throw new StoreValidationException(code, "Package operation did not complete.") { RequestId = LastRequestId };
                }
                catch (StoreValidationException ex)
                { ex.RequestId = LastRequestId; diagnostics.Event("package.failed", "Package", ex.Code, LastRequestId, package: expected); throw; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is HttpRequestException)
                { diagnostics.Event("package.failed", "Package", "PackageUnavailable", LastRequestId, package: expected); throw new StoreValidationException("PackageUnavailable", "Package transfer or temporary storage failed.", ex) { RequestId = LastRequestId }; }
                finally { response?.Dispose(); file?.Dispose(); }
            }
        }

        // Racing the deadline also handles handlers/Mono streams which ignore cancellation.
        // Late responses are disposed; a timeout never resumes writing or returns a verified result.
        internal static async Task<T> PackageOperation<T>(Func<CancellationToken, Task<T>> operation, int milliseconds,
            CancellationToken total, CancellationToken caller, string timeoutCode, Action abort, Action<T> disposeLate = null)
        {
            total.ThrowIfCancellationRequested();
            using (var io = CancellationTokenSource.CreateLinkedTokenSource(total))
            using (var delay = CancellationTokenSource.CreateLinkedTokenSource(total))
            {
                Task<T> work = operation(io.Token);
                try
                {
                    Task deadline = Task.Delay(milliseconds, delay.Token);
                    if (await Task.WhenAny(work, deadline).ConfigureAwait(false) != work || total.IsCancellationRequested)
                    {
                        try { io.Cancel(); } catch { /* The result remains rejected even if a cancellation callback fails. */ }
                        try { abort?.Invoke(); } catch { /* Best effort; eventual task completion is still observed. */ }
                        _ = ObserveLate(work, disposeLate);
                        caller.ThrowIfCancellationRequested();
                        throw new StoreValidationException(total.IsCancellationRequested ? "PackageDownloadTimeout" : timeoutCode, "Package operation timed out.");
                    }
                    return await work.ConfigureAwait(false);
                }
                finally { delay.Cancel(); }
            }
        }
        private static async Task ObserveLate<T>(Task<T> operation, Action<T> dispose)
        { try { var result = await operation.ConfigureAwait(false); dispose?.Invoke(result); } catch { /* Observed, never committed. */ } }
        internal static void CheckDownloadLinks(string directory)
        {
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                if ((Directory.Exists(current.FullName) || File.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new StoreValidationException("UnsafeDownloadPath", "Temporary package paths must not traverse symbolic links.");
        }
    }
}
