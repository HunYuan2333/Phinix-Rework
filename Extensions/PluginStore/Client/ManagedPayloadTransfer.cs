using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    // One bounded held-file/hash/ZIP/PE/resource validation path for both adapters.
    internal static class ManagedPayloadTransfer
    {
        internal static async Task<ManagedStorePayloadReport> Read(HttpResponseMessage response,RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord expected,ClientEnvironmentPaths paths,PackageDownloadBudget budget,CancellationToken token,CancellationToken userToken,RepositoryDiagnostics diagnostics,string requestId,bool allowZip=false,Action<ManagedPackageProgress> progress=null)
        {
            FileStream file=null;
            try
            {
                    var headers = response.Content?.Headers;
                    if (headers == null || (headers.ContentType?.MediaType != "application/octet-stream" && !(allowZip && headers.ContentType?.MediaType == "application/zip")) || headers.ContentType.CharSet != null ||
                        headers.ContentEncoding.Count != 0 || headers.ContentRange != null)
                        throw new StoreValidationException("InvalidResponseType", "Package must be an uncompressed binary response.");
                    if (!headers.ContentLength.HasValue || headers.ContentLength.Value != expected.Artifact.SizeBytes)
                        throw new StoreValidationException("PayloadSizeMismatch", "Content-Length differs from the locked package.");
                    string directory = Path.Combine(paths.GetExtensionDataDirectory("phinix.plugin-store"), "package-downloads", endpoint.IdentityKey);
                    RepositoryTransport.CheckDownloadLinks(directory); Directory.CreateDirectory(directory); RepositoryTransport.CheckDownloadLinks(directory);
                    file = new FileStream(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial"), FileMode.CreateNew,
                        FileAccess.ReadWrite, FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
                    progress?.Invoke(new ManagedPackageProgress(ManagedProgressStage.Downloading,0));
                    using (Stream body = await RepositoryTransport.PackageOperation(ct => response.Content.ReadAsStreamAsync(), budget.Idle, token, userToken,
                        "PackageIdleTimeout", () => response.Dispose(), late => late.Dispose()).ConfigureAwait(false))
                    using (var hash = SHA256.Create())
                    {
                        byte[] buffer = new byte[8192]; long bytes = 0;
                        while (true)
                        {
                            int count = await RepositoryTransport.PackageOperation(ct => body.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, expected.Artifact.SizeBytes - bytes + 1), ct),
                                budget.Idle, token, userToken, "PackageIdleTimeout", () => response.Dispose()).ConfigureAwait(false);
                            if (count == 0) break;
                            bytes += count;
                            if (bytes > expected.Artifact.SizeBytes) throw new StoreValidationException("PayloadLimit", "Package body exceeds the locked byte limit.");
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                            await RepositoryTransport.PackageOperation(async ct => { await file.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false); return true; },
                                budget.Idle, token, userToken, "PackageWriteTimeout", () => file.Dispose()).ConfigureAwait(false);
                            progress?.Invoke(new ManagedPackageProgress(ManagedProgressStage.Downloading,bytes));
                        }
                        if (bytes != expected.Artifact.SizeBytes) throw new StoreValidationException("PayloadSizeMismatch", "Package body was truncated.");
                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        if (BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant() != expected.Artifact.Sha256)
                            throw new StoreValidationException("PayloadDigestMismatch", "Package SHA-256 differs from the locked snapshot.");
                        diagnostics.Event("package.bytes_verified", "Package", requestId: requestId, bytes: bytes, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256);
                    }
                    await RepositoryTransport.PackageOperation(async ct => { await file.FlushAsync(ct).ConfigureAwait(false); return true; },
                        budget.Idle, token, userToken, "PackageWriteTimeout", () => file.Dispose()).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); file.Position = 0;
                    progress?.Invoke(new ManagedPackageProgress(ManagedProgressStage.Validating,file.Length));
                    ManagedStorePayloadReport report = ManagedStorePayloadValidator.Validate(expected, file, token);
                    token.ThrowIfCancellationRequested(); userToken.ThrowIfCancellationRequested();
                    diagnostics.Event("package.payload_verified", "Package", requestId: requestId, bytes: file.Length, managed: expected, snapshot: catalog.SnapshotId, catalogHash: catalog.Sha256);
                    return report;
            }
            finally { file?.Dispose(); }
        }
    }
}
