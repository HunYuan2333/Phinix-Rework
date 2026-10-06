using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    internal sealed class RepositoryBrowseInfo
    {
        public RepositoryBrowseInfo(RepositoryEndpoint endpoint, DateTime checkedUtc, bool offline, bool stale)
        { Endpoint = endpoint; CheckedUtc = checkedUtc; Offline = offline; Stale = stale; }
        public RepositoryEndpoint Endpoint { get; }
        public DateTime CheckedUtc { get; }
        public bool Offline { get; }
        public bool Stale { get; }
        public RepositoryBrowseInfo AsStale() { return new RepositoryBrowseInfo(Endpoint, CheckedUtc, Offline, true); }
    }

    internal sealed class RepositoryCacheEntry
    {
        internal RepositoryCacheEntry(RepositoryEndpoint endpoint, byte[] stable, byte[] published, byte[] catalog, string etag, DateTime checkedUtc)
        {
            StableBytes = stable; PublishedBytes = published; CatalogBytes = catalog;
            Stable = RepositoryMetadata.ReadStable(stable, endpoint.SourceId);
            Catalog = RepositoryMetadata.Verify(Stable, published, catalog);
            ETag = RepositoryTransport.ValidETag(etag); CheckedUtc = checkedUtc;
        }
        public byte[] StableBytes { get; }
        public byte[] PublishedBytes { get; }
        public byte[] CatalogBytes { get; }
        public RepositoryStable Stable { get; }
        public CatalogSnapshot Catalog { get; }
        public string ETag { get; }
        public DateTime CheckedUtc { get; }
    }

    // Single bounded bundle: the live file always contains an entire validated chain.
    // This is a browsing cache, separate from future installation ownership/journals.
    internal sealed class RepositoryCache
    {
        private const int MaxBundleBytes = CatalogReader.MaxCatalogBytes + 2 * RepositoryMetadata.MaxMetadataBytes + 4096;
        private readonly RepositoryEndpoint endpoint;
        public RepositoryCache(string extensionDataDirectory, RepositoryEndpoint endpoint)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            DirectoryPath = Path.Combine(ClientEnvironmentPaths.NormalizeAbsolute(extensionDataDirectory), "repository-cache", endpoint.CacheKey);
            FilePath = Path.Combine(DirectoryPath, "catalog.cache");
        }
        public string DirectoryPath { get; }
        internal string FilePath { get; }
        public RepositoryCacheEntry TryRead(CancellationToken token)
        {
            try { return Read(token); }
            catch (StoreValidationException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public RepositoryCacheEntry Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); CheckLinks();
            using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                if (stream.Length > MaxBundleBytes) throw Invalid();
                if (reader.ReadInt32() != 0x31534350) throw Invalid(); // PCS1
                if (Decode(ReadBlock(reader, 2048, token)) != endpoint.Origin || Decode(ReadBlock(reader, 128, token)) != endpoint.SourceId) throw Invalid();
                long ticks = reader.ReadInt64();
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) throw Invalid();
                string etag = Decode(ReadBlock(reader, 256, token));
                byte[] stable = ReadBlock(reader, RepositoryMetadata.MaxMetadataBytes, token);
                byte[] published = ReadBlock(reader, RepositoryMetadata.MaxMetadataBytes, token);
                byte[] catalog = ReadBlock(reader, CatalogReader.MaxCatalogBytes, token);
                if (stream.Position != stream.Length) throw Invalid();
                token.ThrowIfCancellationRequested();
                return new RepositoryCacheEntry(endpoint, stable, published, catalog, etag, new DateTime(ticks, DateTimeKind.Utc));
            }
        }
        public void Save(RepositoryCacheEntry entry, CancellationToken token)
        {
            using (var staged = Stage(entry, token)) staged.Commit(token);
        }
        internal RepositoryCacheWrite Stage(RepositoryCacheEntry entry, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Revalidate before touching the prior valid bundle.
            RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(entry.StableBytes, endpoint.SourceId), entry.PublishedBytes, entry.CatalogBytes);
            CheckLinks(); Directory.CreateDirectory(DirectoryPath); CheckLinks();
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true))
                    {
                        writer.Write(0x31534350);
                        WriteBlock(writer, Encoding.UTF8.GetBytes(endpoint.Origin)); WriteBlock(writer, Encoding.UTF8.GetBytes(endpoint.SourceId));
                        writer.Write(entry.CheckedUtc.Ticks); WriteBlock(writer, Encoding.UTF8.GetBytes(entry.ETag ?? ""));
                        WriteBlock(writer, entry.StableBytes); WriteBlock(writer, entry.PublishedBytes); WriteBlock(writer, entry.CatalogBytes);
                    }
                    stream.Flush(true);
                }
                token.ThrowIfCancellationRequested(); CheckLinks();
                return new RepositoryCacheWrite(temporary, FilePath);
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }
        }
        private void CheckLinks()
        {
            if (File.Exists(FilePath) && (File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) != 0) throw Invalid();
            for (var directory = new DirectoryInfo(DirectoryPath); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw Invalid();
        }
        private static byte[] ReadBlock(BinaryReader reader, int maximum, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum) throw Invalid();
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw Invalid();
            return bytes;
        }
        private static string Decode(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException ex) { throw new StoreValidationException("InvalidCache", "Cache contains invalid UTF-8.", ex); }
        }
        private static void WriteBlock(BinaryWriter writer, byte[] bytes) { writer.Write(bytes.Length); writer.Write(bytes); }
        private static StoreValidationException Invalid() { return new StoreValidationException("InvalidCache", "Browsing cache is incomplete, corrupt or belongs to another endpoint/source."); }
    }

    internal sealed class RepositoryCacheWrite : IDisposable
    {
        private readonly string temporary, target;
        internal RepositoryCacheWrite(string temporary, string target) { this.temporary = temporary; this.target = target; }
        public void Commit(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
            // No delete-then-move fallback: preserve the prior cache if atomic replacement fails.
        }
        public void Dispose() { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static class RepositoryBrowser
    {
        public static async Task<RepositoryCacheEntry> Refresh(RepositoryEndpoint endpoint, RepositoryCache cache, RepositoryTransport transport, CancellationToken token)
        {
            RepositoryCacheEntry previous = cache.TryRead(token);
            string etag = previous?.ETag;
            RepositoryResponse response = await transport.Get(endpoint.Stable, RepositoryMetadata.MaxMetadataBytes, etag, token).ConfigureAwait(false);
            if (response.NotModified)
            {
                if (previous != null && etag != null && (response.ETag == null || response.ETag == etag))
                    return new RepositoryCacheEntry(endpoint, previous.StableBytes, previous.PublishedBytes, previous.CatalogBytes, etag, DateTime.UtcNow);
                // A 304 cannot bootstrap an absent/corrupt body, or legitimize a different validator.
                response = await transport.Get(endpoint.Stable, RepositoryMetadata.MaxMetadataBytes, null, token).ConfigureAwait(false);
                if (response.NotModified) throw new StoreValidationException("UnexpectedNotModified", "Repository returned 304 without a usable cached body.");
            }
            RepositoryStable stable = RepositoryMetadata.ReadStable(response.Body, endpoint.SourceId);
            if (previous != null && previous.Stable.SnapshotId == stable.SnapshotId &&
                (previous.Stable.PublishedSha256 != stable.PublishedSha256 || previous.Stable.CatalogSha256 != stable.CatalogSha256 ||
                 previous.Stable.PublishedSizeBytes != stable.PublishedSizeBytes || previous.Stable.CatalogSizeBytes != stable.CatalogSizeBytes))
                throw new StoreValidationException("SnapshotIdentityChanged", "An already observed snapshot cannot change its published descriptor or catalog bytes.");
            byte[] published, catalog;
            if (previous != null && previous.Stable.SnapshotId == stable.SnapshotId && previous.Stable.PublishedSha256 == stable.PublishedSha256 &&
                previous.Stable.CatalogSha256 == stable.CatalogSha256 && previous.Stable.PublishedSizeBytes == stable.PublishedSizeBytes && previous.Stable.CatalogSizeBytes == stable.CatalogSizeBytes)
            { published = previous.PublishedBytes; catalog = previous.CatalogBytes; }
            else
            {
                RepositoryResponse descriptor = await transport.Get(endpoint.Published(stable), stable.PublishedSizeBytes, null, token).ConfigureAwait(false);
                if (descriptor.NotModified) throw new StoreValidationException("UnexpectedNotModified", "Published descriptor needs a complete body.");
                published = descriptor.Body;
                RepositoryResponse index = await transport.Get(endpoint.Catalog(stable), stable.CatalogSizeBytes, null, token).ConfigureAwait(false);
                if (index.NotModified) throw new StoreValidationException("UnexpectedNotModified", "Catalog needs a complete body.");
                catalog = index.Body;
            }
            token.ThrowIfCancellationRequested();
            var result = new RepositoryCacheEntry(endpoint, response.Body, published, catalog, response.ETag, DateTime.UtcNow);
            if (previous != null) VerifyContinuity(previous.Catalog, result.Catalog);
            return result;
        }
        private static void VerifyContinuity(CatalogSnapshot previous, CatalogSnapshot current)
        {
            var versions = previous.Packages.Where(p => !p.IsWorkshop).ToDictionary(p => p.Id + "@" + p.Version, StringComparer.Ordinal);
            foreach (PackageRecord package in current.Packages.Where(p => !p.IsWorkshop))
            {
                PackageRecord old;
                if (!versions.TryGetValue(package.Id + "@" + package.Version, out old)) continue;
                GitHubArtifact a = old.Artifact, b = package.Artifact;
                if (!CatalogReader.SameManifest(old, package) || a.Repository != b.Repository || a.RepositoryId != b.RepositoryId || a.OwnerId != b.OwnerId ||
                    a.SourceCommit != b.SourceCommit || a.Tag != b.Tag || a.ReleaseId != b.ReleaseId || a.AssetId != b.AssetId || a.AssetName != b.AssetName ||
                    a.PayloadKind != b.PayloadKind || a.Sha256 != b.Sha256 || a.ManifestSha256 != b.ManifestSha256 || a.SizeBytes != b.SizeBytes)
                    throw new StoreValidationException("PackageIdentityChanged", package.Id + ": previously observed package version changed identity, metadata or artifact bytes.");
            }
        }
    }
}
