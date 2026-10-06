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
    internal sealed partial class RepositoryTransport
    {
        internal async Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint, ManagedStoreCatalogSnapshot catalog,
            ManagedStoreRecord expected, ClientEnvironmentPaths paths, CancellationToken token, PackageDownloadBudget budget = null,Action<ManagedPackageProgress> progress=null)
        {
            Uri resource = endpoint.Package(catalog, expected);
            if (expected.Artifact.PayloadKind != "managed-dll-zip")
                throw new StoreValidationException("UnsupportedPayloadDownload", "This download preview supports ZIP packages only.");
            if (expected.Artifact.SizeBytes < 1 || expected.Artifact.SizeBytes > CatalogReader.MaxPackageBytes)
                throw new StoreValidationException("PayloadLimit", "Locked package exceeds the download limit.");
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            budget = budget ?? new PackageDownloadBudget();
            LastRequestId = null; diagnostics.Event("package.download_started", "Package", managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256);
            HttpResponseMessage response = null;
            using (var total = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(HttpMethod.Get, resource))
            {
                total.CancelAfter(budget.Total);
                request.Headers.Add("X-Phinix-Client-Request-Id", diagnostics.ClientRequestId);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                request.Headers.CacheControl=new CacheControlHeaderValue { NoCache=true };
                try
                {
                    response = await PackageOperation(ct => client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct),
                        budget.Headers, total.Token, token, "PackageConnectTimeout", null, late => late.Dispose()).ConfigureAwait(false);
                    if (response.RequestMessage == null || response.RequestMessage.RequestUri != resource)
                        throw new StoreValidationException("UnexpectedEndpoint", "Response did not come from the locked repository resource.");
                    System.Collections.Generic.IEnumerable<string> ids;
                    if (response.Headers.TryGetValues("X-Phinix-Request-Id", out ids))
                    { var values = System.Linq.Enumerable.ToArray(ids); if (values.Length == 1) LastRequestId = RepositoryDiagnostics.RequestId(values[0]); }
                    diagnostics.Event("package.response", "Package", requestId: LastRequestId, status: (int)response.StatusCode, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256);
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400) throw new StoreValidationException("RedirectRejected", "Package redirects are not supported.");
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw await PackageOperation(ct => ReadFailure(response, status, ct), budget.Idle, total.Token, token,
                            "PackageIdleTimeout", () => response.Dispose()).ConfigureAwait(false);
                    return await ManagedPayloadTransfer.Read(response,endpoint,catalog,expected,paths,budget,total.Token,token,diagnostics,LastRequestId,false,progress).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                    { diagnostics.Event("package.cancelled", "Package", requestId: LastRequestId, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256); throw; }
                    string code = total.IsCancellationRequested ? "PackageDownloadTimeout" : "PackageUnavailable";
                    diagnostics.Event("package.failed", "Package", code, LastRequestId, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256);
                    throw new StoreValidationException(code, "Package operation did not complete.") { RequestId = LastRequestId };
                }
                catch (StoreValidationException ex)
                { ex.RequestId = LastRequestId; diagnostics.Event("package.failed", "Package", ex.Code, LastRequestId, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256); throw; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is HttpRequestException)
                { diagnostics.Event("package.failed", "Package", "PackageUnavailable", LastRequestId, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256); throw new StoreValidationException("PackageUnavailable", "Package transfer or temporary storage failed.", ex) { RequestId = LastRequestId }; }
                finally { response?.Dispose(); }
            }
        }

    }
}
