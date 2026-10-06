using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    internal sealed class ManagedRepositoryCacheEntry
    {
        internal ManagedRepositoryCacheEntry(RepositoryEndpoint endpoint, byte[] stable, byte[] published, byte[] catalog, string etag, DateTime checkedUtc,string accessKey=null)
        {
            StableBytes = stable; PublishedBytes = published; CatalogBytes = catalog;
            Stable = RepositoryMetadata.ReadManagedStable(stable, endpoint.SourceId);
            Catalog = RepositoryMetadata.VerifyManaged(Stable, published, catalog);
            endpoint.Profile?.VerifyPublished(published); AccessKey=accessKey??endpoint.AccessKey;
            ETag = RepositoryTransport.ValidETag(etag); CheckedUtc = checkedUtc;
        }
        public byte[] StableBytes { get; }
        public byte[] PublishedBytes { get; }
        public byte[] CatalogBytes { get; }
        public RepositoryStable Stable { get; }
        public ManagedStoreCatalogSnapshot Catalog { get; }
        internal string AccessKey { get; }
        public string ETag { get; }
        public DateTime CheckedUtc { get; }
    }

    // Single bounded bundle: the live file always contains an entire validated chain.
    // This is a browsing cache, separate from future installation ownership/journals.
    internal sealed class ManagedRepositoryCache
    {
        private const int MaxBundleBytes = CatalogReader.MaxCatalogBytes + 2 * RepositoryMetadata.MaxMetadataBytes + 4096;
        private readonly RepositoryEndpoint endpoint;
        public ManagedRepositoryCache(string extensionDataDirectory, RepositoryEndpoint endpoint)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            DirectoryPath = Path.Combine(ClientEnvironmentPaths.NormalizeAbsolute(extensionDataDirectory), "repository-cache", endpoint.IdentityKey);
            FilePath = Path.Combine(DirectoryPath, "managed-catalog.cache");
        }
        public string DirectoryPath { get; }
        internal string FilePath { get; }
        public ManagedRepositoryCacheEntry TryRead(CancellationToken token)
        {
            try { return Read(token); }
            catch (StoreValidationException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public ManagedRepositoryCacheEntry Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); CheckLinks();
            using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
            {
                if (stream.Length > MaxBundleBytes) throw Invalid();
                if (reader.ReadInt32() != 0x34534350) throw Invalid(); // PCS4
                if (Decode(ReadBlock(reader, 128, token)) != endpoint.IdentityKey || Decode(ReadBlock(reader, 128, token)) != endpoint.SourceId) throw Invalid();
                long ticks = reader.ReadInt64();
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) throw Invalid();
                string accessKey=Decode(ReadBlock(reader,4096,token));
                string etag = Decode(ReadBlock(reader, 256, token));
                byte[] stable = ReadBlock(reader, RepositoryMetadata.MaxMetadataBytes, token);
                byte[] published = ReadBlock(reader, RepositoryMetadata.MaxMetadataBytes, token);
                byte[] catalog = ReadBlock(reader, CatalogReader.MaxCatalogBytes, token);
                if (stream.Position != stream.Length) throw Invalid();
                token.ThrowIfCancellationRequested();
                return new ManagedRepositoryCacheEntry(endpoint, stable, published, catalog, accessKey==endpoint.AccessKey?etag:null, new DateTime(ticks, DateTimeKind.Utc),accessKey);
            }
        }
        public void Save(ManagedRepositoryCacheEntry entry, CancellationToken token)
        {
            using (var staged = Stage(entry, token)) staged.Commit(token);
        }
        internal RepositoryCacheWrite Stage(ManagedRepositoryCacheEntry entry, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Revalidate before touching the prior valid bundle.
            RepositoryMetadata.VerifyManaged(RepositoryMetadata.ReadManagedStable(entry.StableBytes, endpoint.SourceId), entry.PublishedBytes, entry.CatalogBytes);
            endpoint.Profile?.VerifyPublished(entry.PublishedBytes);
            CheckLinks(); Directory.CreateDirectory(DirectoryPath); CheckLinks();
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true))
                    {
                        writer.Write(0x34534350);
                        WriteBlock(writer, Encoding.UTF8.GetBytes(endpoint.IdentityKey)); WriteBlock(writer, Encoding.UTF8.GetBytes(endpoint.SourceId));
                        writer.Write(entry.CheckedUtc.Ticks); WriteBlock(writer,Encoding.UTF8.GetBytes(entry.AccessKey)); WriteBlock(writer, Encoding.UTF8.GetBytes(entry.ETag ?? ""));
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

    internal static class ManagedRepositoryBrowser
    {
        public static Task<ManagedRepositoryCacheEntry> Refresh(RepositoryEndpoint endpoint,ManagedRepositoryCache cache,RepositoryTransport transport,CancellationToken token,bool fresh=false)
        { return Refresh(endpoint,cache,new CloudflareRepositoryAccess(transport,false),token,fresh); }
        public static async Task<ManagedRepositoryCacheEntry> Refresh(RepositoryEndpoint endpoint, ManagedRepositoryCache cache, IManagedRepositoryAccess transport, CancellationToken token,bool fresh=false)
        {
            ManagedRepositoryCacheEntry previous = cache.TryRead(token);
            string etag=previous?.AccessKey==endpoint.AccessKey?previous.ETag:null;
            var read=await transport.ReadMetadata(endpoint,etag,token,fresh).ConfigureAwait(false);
            var response=read.Stable;
            if(response.NotModified)
            {
                if(previous!=null && etag!=null && (response.ETag==null || response.ETag==etag))
                    return new ManagedRepositoryCacheEntry(endpoint,previous.StableBytes,previous.PublishedBytes,previous.CatalogBytes,etag,DateTime.UtcNow);
                read=await transport.ReadMetadata(endpoint,null,token,fresh).ConfigureAwait(false); response=read.Stable;
                if(response.NotModified) throw new StoreValidationException("UnexpectedNotModified","Repository returned 304 without a usable cached body.");
            }
            RepositoryStable stable=RepositoryMetadata.ReadManagedStable(response.Body,endpoint.SourceId);
            if(previous!=null && previous.Stable.SnapshotId==stable.SnapshotId &&
                (previous.Stable.PublishedSha256!=stable.PublishedSha256 || previous.Stable.CatalogSha256!=stable.CatalogSha256 || previous.Stable.PublishedSizeBytes!=stable.PublishedSizeBytes || previous.Stable.CatalogSizeBytes!=stable.CatalogSizeBytes))
                throw new StoreValidationException("SnapshotIdentityChanged","An observed snapshot cannot change its bytes.");
            byte[] published=read.Published,catalog=read.Catalog;
            token.ThrowIfCancellationRequested();
            var result = new ManagedRepositoryCacheEntry(endpoint, response.Body, published, catalog, response.ETag, DateTime.UtcNow);
            if (previous != null) VerifyContinuity(previous.Catalog, result.Catalog);
            return result;
        }
        private static void VerifyContinuity(ManagedStoreCatalogSnapshot previous, ManagedStoreCatalogSnapshot current)
        {
            var versions = previous.Packages.Where(p => !p.IsWorkshop).ToDictionary(p => p.Id + "@" + p.Manifest.Version, StringComparer.Ordinal);
            foreach (ManagedStoreRecord package in current.Packages.Where(p => !p.IsWorkshop))
            {
                ManagedStoreRecord old;
                if (!versions.TryGetValue(package.Id + "@" + package.Manifest.Version, out old)) continue;
                GitHubArtifact a = old.Artifact, b = package.Artifact;
                if (old.DeclarationHash != package.DeclarationHash || a.Repository != b.Repository || a.RepositoryId != b.RepositoryId || a.OwnerId != b.OwnerId ||
                    a.SourceCommit != b.SourceCommit || a.Tag != b.Tag || a.ReleaseId != b.ReleaseId || a.AssetId != b.AssetId || a.AssetName != b.AssetName ||
                    a.PayloadKind != b.PayloadKind || a.Sha256 != b.Sha256 || a.ManifestSha256 != b.ManifestSha256 || a.SizeBytes != b.SizeBytes)
                    throw new StoreValidationException("PackageIdentityChanged", package.Id + ": previously observed package version changed identity, metadata or artifact bytes.");
            }
        }
    }
}
