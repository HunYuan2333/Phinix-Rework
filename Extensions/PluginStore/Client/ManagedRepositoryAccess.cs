using System;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    internal sealed class ManagedMetadataRead
    {
        internal ManagedMetadataRead(RepositoryResponse stable,byte[] published=null,byte[] catalog=null)
        { Stable=stable; Published=published; Catalog=catalog; }
        internal RepositoryResponse Stable { get; }
        internal byte[] Published { get; }
        internal byte[] Catalog { get; }
    }
    // Adapters return the same publication bytes; wire headers/redirect policy stay inside each adapter.
    internal interface IManagedRepositoryAccess : IDisposable
    {
        Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh);
        Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null);
    }
    internal sealed class CloudflareRepositoryAccess : IManagedRepositoryAccess
    {
        private readonly RepositoryTransport transport;
        private readonly bool owns;
        internal CloudflareRepositoryAccess(RepositoryTransport transport,bool owns=true) { this.transport=transport; this.owns=owns; }
        public async Task<ManagedMetadataRead> ReadMetadata(RepositoryEndpoint endpoint,string etag,CancellationToken token,bool fresh)
        {
            transport.ConfigureAudit(endpoint);
            if(endpoint.AccessMethod!=RepositoryAccessMethod.Cloudflare) throw new StoreValidationException("AccessMethodMismatch","Use the adapter selected by the repository profile.");
            var response=await transport.Get(endpoint.Stable,RepositoryMetadata.MaxMetadataBytes,etag,token,fresh).ConfigureAwait(false);
            if(response.NotModified) return new ManagedMetadataRead(response);
            var stable=RepositoryMetadata.ReadManagedStable(response.Body,endpoint.SourceId);
            var published=await transport.Get(endpoint.Published(stable),stable.PublishedSizeBytes,null,token,fresh).ConfigureAwait(false);
            if(published.NotModified) throw Error();
            RepositoryMetadata.VerifyPublished(stable,published.Body); endpoint.Profile?.VerifyPublished(published.Body);
            var catalog=await transport.Get(endpoint.Catalog(stable),stable.CatalogSizeBytes,null,token,fresh).ConfigureAwait(false);
            if(catalog.NotModified) throw Error();
            return new ManagedMetadataRead(response,published.Body,catalog.Body);
        }
        public Task<ManagedStorePayloadReport> DownloadManagedPackage(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord package,ClientEnvironmentPaths paths,CancellationToken token,PackageDownloadBudget budget=null,Action<ManagedPackageProgress> progress=null)
        {
            transport.ConfigureAudit(endpoint);
            if(endpoint.AccessMethod!=RepositoryAccessMethod.Cloudflare) throw new StoreValidationException("AccessMethodMismatch","Use the adapter selected by the repository profile.");
            return transport.DownloadManagedPackage(endpoint,catalog,package,paths,token,budget,progress);
        }
        public void Dispose() { if(owns) transport.Dispose(); }
        private static StoreValidationException Error() { return new StoreValidationException("UnexpectedNotModified","Immutable metadata needs a complete body."); }
    }
    internal static class ManagedRepositoryAccess
    {
        internal static IManagedRepositoryAccess Create(RepositoryEndpoint endpoint,RepositoryDiagnostics diagnostics)
        { diagnostics.AccessMethod=endpoint.AccessMethod.ToString(); diagnostics.RepositoryIdentity=endpoint.IdentityKey; return endpoint.AccessMethod==RepositoryAccessMethod.GitHub?(IManagedRepositoryAccess)new GitHubRepositoryAccess(diagnostics):new CloudflareRepositoryAccess(new RepositoryTransport(diagnostics)); }
    }
}
