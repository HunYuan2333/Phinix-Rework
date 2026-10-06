using System;
using System.Text.RegularExpressions;

namespace Phinix.PluginStore
{
    internal enum RepositoryAccessMethod { Cloudflare, GitHub }

    // Trusted configuration, never supplied by a catalog or redirect.
    internal sealed class RepositoryProfile
    {
        internal RepositoryProfile(string sourceId, string repository, string repositoryId, string ownerId, string publicationBranch, string gatewayOrigin)
        {
            RepositoryEndpoint.ValidateId(sourceId);
            if(!Regex.IsMatch(repository??"", @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z") || repository.EndsWith(".git",StringComparison.OrdinalIgnoreCase)) throw Error();
            foreach(var id in new[]{repositoryId,ownerId})
            { ulong n; if(!ulong.TryParse(id,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out n) || n==0 || n.ToString(System.Globalization.CultureInfo.InvariantCulture)!=id) throw Error(); }
            if(string.IsNullOrEmpty(publicationBranch) || publicationBranch.Length>128 || !Regex.IsMatch(publicationBranch,@"\A[A-Za-z0-9][A-Za-z0-9._/-]*\z") || publicationBranch.Contains("..") || publicationBranch.Contains("//") || publicationBranch.EndsWith("/") || publicationBranch.EndsWith(".")) throw Error();
            SourceId=sourceId; Repository=repository; RepositoryId=repositoryId; OwnerId=ownerId; PublicationBranch=publicationBranch;
            GatewayOrigin=new RepositoryEndpoint(gatewayOrigin,sourceId).Origin;
            IdentityKey=CatalogReader.Hash(System.Text.Encoding.UTF8.GetBytes("repository-profile-v1\n"+sourceId+"\n"+repositoryId+"\n"+ownerId+"\n"+publicationBranch));
        }
        internal string SourceId { get; }
        internal string Repository { get; }
        internal string RepositoryId { get; }
        internal string OwnerId { get; }
        internal string PublicationBranch { get; }
        internal string GatewayOrigin { get; }
        internal string IdentityKey { get; }
        internal void VerifyPublished(byte[] bytes)
        {
            var f=CatalogReader.ReadJson(bytes);
            if(CatalogReader.Text(f.Element("repository"),"repository",140)!=Repository || CatalogReader.PositiveId(f.Element("repositoryId"),"repositoryId")!=RepositoryId || CatalogReader.PositiveId(f.Element("ownerId"),"ownerId")!=OwnerId)
                throw new StoreValidationException("RepositoryProfileMismatch","Published catalog belongs to another approved repository identity.");
        }
        internal static RepositoryProfile Official => new RepositoryProfile("phinix.official","HunYuan2333/Phinix-Plugin-Index","1402564805","64630568","main","https://plugins.hunyuan2333.com");
        private static StoreValidationException Error() { return new StoreValidationException("InvalidRepositoryProfile","Configure fixed public repository IDs, branch and gateway."); }
    }

    // One configured origin. Resource paths never come from catalog URLs or redirects.
    internal sealed class RepositoryEndpoint
    {
        public RepositoryEndpoint(string origin, string sourceId)
        {
            Uri uri;
            if (string.IsNullOrEmpty(origin) || origin.Length > 2048 ||
                !Regex.IsMatch(origin, @"\Ahttps://[A-Za-z0-9.\-:\[\]]+/?\z", RegexOptions.CultureInvariant) ||
                !Uri.TryCreate(origin, UriKind.Absolute, out uri) || uri.Scheme != "https" ||
                uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
                origin.TrimEnd('/') != uri.GetLeftPart(UriPartial.Authority))
                throw new StoreValidationException("InvalidEndpoint", "Configure a canonical HTTPS origin without a path, credentials, query or fragment.");
            ValidateId(sourceId);
            Origin = uri.GetLeftPart(UriPartial.Authority); SourceId = sourceId;
        }
        internal RepositoryEndpoint(RepositoryProfile profile,RepositoryAccessMethod method) : this(method==RepositoryAccessMethod.GitHub?"https://api.github.com":profile?.GatewayOrigin,profile?.SourceId)
        { if(profile==null || !Enum.IsDefined(typeof(RepositoryAccessMethod),method)) throw new StoreValidationException("InvalidRepositoryProfile","Select a configured access method."); Profile=profile; AccessMethod=method; }
        internal RepositoryProfile Profile { get; }
        internal RepositoryAccessMethod AccessMethod { get; }
        internal string IdentityKey => Profile?.IdentityKey ?? CatalogReader.Hash(System.Text.Encoding.UTF8.GetBytes("gateway-profile-v1\n"+SourceId+"\n"+Origin));
        internal string AccessKey => AccessMethod.ToString()+":"+Origin;
        public string Origin { get; }
        public string SourceId { get; }
        public string CacheKey => CatalogReader.Hash(System.Text.Encoding.UTF8.GetBytes(Origin + "\n" + SourceId));
        public Uri Stable => Resource("stable");
        public Uri Published(RepositoryStable stable) => Resource("snapshots/" + stable.SnapshotId + "/published/" + stable.PublishedSha256);
        public Uri Catalog(RepositoryStable stable) => Resource("snapshots/" + stable.SnapshotId + "/catalog/" + stable.CatalogSha256);
        public Uri Package(CatalogSnapshot catalog, PackageRecord package)
        {
            if (catalog == null || catalog.SourceId != SourceId || package == null || package.IsWorkshop ||
                package.State != "active" || package.Version == null || package.Artifact == null ||
                !System.Linq.Enumerable.Any(catalog.Packages, item => ReferenceEquals(item, package)))
                throw new StoreValidationException("DownloadSnapshotMismatch", "Download a locked active package from this source and snapshot.");
            ValidateId(package.Id);
            if (catalog.SnapshotId == null || !Regex.IsMatch(catalog.SnapshotId, @"\A[a-f0-9]{40}\z", RegexOptions.CultureInvariant) ||
                package.Artifact.Sha256 == null || !Regex.IsMatch(package.Artifact.Sha256, @"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant))
                throw new StoreValidationException("DownloadSnapshotMismatch", "Download identity must contain canonical snapshot and payload hashes.");
            return Resource("snapshots/" + catalog.SnapshotId + "/packages/" + package.Id + "/" + package.Version + "/" + package.Artifact.Sha256 + "/package");
        }
        internal Uri Package(ManagedStoreCatalogSnapshot catalog, ManagedStoreRecord package)
        {
            if(catalog==null || catalog.SourceId!=SourceId || package==null || package.IsWorkshop || package.Manifest==null ||
                package.State!="active" || package.Artifact?.PayloadKind!="managed-dll-zip" ||
                !System.Linq.Enumerable.Any(catalog.Packages,item=>ReferenceEquals(item,package)))
                throw new StoreValidationException("DownloadSnapshotMismatch","Select an active managed package from the locked catalog.");
            ValidateId(package.Id);
            if(!Regex.IsMatch(catalog.SnapshotId??"",@"\A[a-f0-9]{40}\z") || !Regex.IsMatch(package.Artifact.Sha256??"",@"\A[a-f0-9]{64}\z"))
                throw new StoreValidationException("DownloadSnapshotMismatch","Managed download identities must be canonical.");
            return Resource("snapshots/"+catalog.SnapshotId+"/packages/"+package.Id+"/"+package.Manifest.Version+"/"+package.Artifact.Sha256+"/package");
        }
        private Uri Resource(string path) { return new Uri(Origin + "/v1/sources/" + SourceId + "/" + path); }
        internal static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128 || !Regex.IsMatch(id, @"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z", RegexOptions.CultureInvariant))
                throw new StoreValidationException("InvalidId", "Expected a canonical lowercase source ID.");
        }
    }

    internal sealed class RepositoryStable
    {
        internal RepositoryStable(string source, string snapshot, string catalogHash, int catalogSize, string publishedHash, int publishedSize, int catalogSchemaVersion = 1)
        { SourceId = source; SnapshotId = snapshot; CatalogSha256 = catalogHash; CatalogSizeBytes = catalogSize; PublishedSha256 = publishedHash; PublishedSizeBytes = publishedSize; CatalogSchemaVersion = catalogSchemaVersion; }
        public int CatalogSchemaVersion { get; }
        public string SourceId { get; }
        public string SnapshotId { get; }
        public string CatalogSha256 { get; }
        public int CatalogSizeBytes { get; }
        public string PublishedSha256 { get; }
        public int PublishedSizeBytes { get; }
    }

    internal static class RepositoryMetadata
    {
        public const int MaxMetadataBytes = 16 * 1024;
        public static RepositoryStable ReadStable(byte[] bytes, string source)
        { return ReadStableCore(bytes,source,1); }

        internal static RepositoryStable ReadManagedStable(byte[] bytes, string source)
        { return ReadStableCore(bytes,source,ManagedStoreCatalogReader.SchemaVersion); }

        private static RepositoryStable ReadStableCore(byte[] bytes, string source, int catalogSchema)
        {
            Limit(bytes);
            var f = CatalogReader.Object(CatalogReader.ReadJson(bytes), "stable", "schemaVersion", "sourceId", "snapshotId", "catalogSchemaVersion",
                "catalogSha256", "catalogSizeBytes", "publishedSha256", "publishedSizeBytes");
            CatalogReader.Integer(CatalogReader.Required(f, "schemaVersion"), "schemaVersion", 1, 1);
            CatalogReader.Integer(CatalogReader.Required(f, "catalogSchemaVersion"), "catalogSchemaVersion", catalogSchema, catalogSchema);
            string actualSource = CatalogReader.Identifier(CatalogReader.Required(f, "sourceId"), "sourceId");
            if (actualSource != source) throw new StoreValidationException("SourceMismatch", "Stable source differs from the configured source.");
            return new RepositoryStable(actualSource, CatalogReader.Hex(CatalogReader.Required(f, "snapshotId"), "snapshotId", 40),
                CatalogReader.Hex(CatalogReader.Required(f, "catalogSha256"), "catalogSha256", 64),
                (int)CatalogReader.Integer(CatalogReader.Required(f, "catalogSizeBytes"), "catalogSizeBytes", 1, CatalogReader.MaxCatalogBytes),
                CatalogReader.Hex(CatalogReader.Required(f, "publishedSha256"), "publishedSha256", 64),
                (int)CatalogReader.Integer(CatalogReader.Required(f, "publishedSizeBytes"), "publishedSizeBytes", 1, MaxMetadataBytes),catalogSchema);
        }

        public static CatalogSnapshot Verify(RepositoryStable stable, byte[] published, byte[] catalog)
        {
            RequireCatalogSchema(stable,1);
            VerifyEnvelope(stable,published,catalog);
            CatalogSnapshot result=CatalogReader.Read(catalog,stable.SourceId);
            if(result.SnapshotId!=stable.SnapshotId) throw new StoreValidationException("SnapshotMismatch","Catalog snapshot differs from stable.");
            return result;
        }

        internal static ManagedStoreCatalogSnapshot VerifyManaged(RepositoryStable stable, byte[] published, byte[] catalog)
        {
            RequireCatalogSchema(stable,ManagedStoreCatalogReader.SchemaVersion);
            Limit(published);
            if(catalog==null || catalog.Length==0 || catalog.Length>CatalogReader.MaxCatalogBytes)
                throw new StoreValidationException("DocumentLimit","Managed catalog exceeds its byte bound.");
            published=(byte[])published.Clone(); catalog=(byte[])catalog.Clone();
            VerifyEnvelope(stable,published,catalog);
            var result=ManagedStoreCatalogReader.Read(catalog,stable.SourceId);
            if(result.SnapshotId!=stable.SnapshotId) throw new StoreValidationException("SnapshotMismatch","Managed catalog snapshot differs from stable.");
            return result;
        }
        private static void RequireCatalogSchema(RepositoryStable stable,int expected)
        {
            if(stable==null) throw new ArgumentNullException(nameof(stable));
            if(stable.CatalogSchemaVersion!=expected) throw new StoreValidationException("UnsupportedSchema","Catalog schema differs from the selected route.");
        }
        private static void VerifyEnvelope(RepositoryStable stable,byte[] published,byte[] catalog)
        {
            VerifyPublished(stable,published);
            VerifyBytes(catalog, stable.CatalogSizeBytes, stable.CatalogSha256, "Catalog");
        }

        internal static void VerifyPublished(RepositoryStable stable,byte[] published)
        {
            VerifyBytes(published, stable.PublishedSizeBytes, stable.PublishedSha256, "Published");
            Limit(published);
            var f = CatalogReader.Object(CatalogReader.ReadJson(published), "published", "schemaVersion", "sourceId", "snapshotId", "catalogSchemaVersion",
                "catalogSha256", "catalogSizeBytes", "repository", "repositoryId", "ownerId", "releaseId", "assetId", "assetName");
            CatalogReader.Integer(CatalogReader.Required(f, "schemaVersion"), "schemaVersion", 1, 1);
            CatalogReader.Integer(CatalogReader.Required(f, "catalogSchemaVersion"), "catalogSchemaVersion", stable.CatalogSchemaVersion, stable.CatalogSchemaVersion);
            if (CatalogReader.Identifier(CatalogReader.Required(f, "sourceId"), "sourceId") != stable.SourceId ||
                CatalogReader.Hex(CatalogReader.Required(f, "snapshotId"), "snapshotId", 40) != stable.SnapshotId ||
                CatalogReader.Hex(CatalogReader.Required(f, "catalogSha256"), "catalogSha256", 64) != stable.CatalogSha256 ||
                CatalogReader.Integer(CatalogReader.Required(f, "catalogSizeBytes"), "catalogSizeBytes", 1, CatalogReader.MaxCatalogBytes) != stable.CatalogSizeBytes)
                throw new StoreValidationException("PublishedMismatch", "Published identity, catalog digest or size differs from stable.");
            string repository = CatalogReader.Text(CatalogReader.Required(f, "repository"), "repository", 140);
            if (!Regex.IsMatch(repository, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z", RegexOptions.CultureInvariant) ||
                repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                throw new StoreValidationException("InvalidRepository", "Published repository must be owner/repo.");
            foreach (string name in new[] { "repositoryId", "ownerId", "releaseId", "assetId" })
                CatalogReader.PositiveId(CatalogReader.Required(f, name), name);
            if (CatalogReader.Text(CatalogReader.Required(f, "assetName"), "assetName", 128) != "catalog.json")
                throw new StoreValidationException("InvalidCatalogAsset", "This protocol draft requires a catalog.json release asset.");
        }

        private static void Limit(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxMetadataBytes)
                throw new StoreValidationException("DocumentLimit", "Repository metadata must contain 1 to 16384 bytes.");
        }
        private static void VerifyBytes(byte[] bytes, int size, string hash, string role)
        {
            if (bytes == null || bytes.Length != size) throw new StoreValidationException(role + "SizeMismatch", role + " length differs from stable.");
            if (CatalogReader.Hash(bytes) != hash) throw new StoreValidationException(role + "DigestMismatch", role + " SHA-256 differs from stable.");
        }
    }
}
