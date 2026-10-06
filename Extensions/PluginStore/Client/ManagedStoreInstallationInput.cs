using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    // Installation adaptation belongs to the client, outside shared static ZIP inspection.
    internal sealed partial class ManagedStorePayloadReport
    {
        internal ManagedExtensionInstallPackage InstallationInput(RepositoryEndpoint endpoint,ManagedStoreCatalogSnapshot catalog,ManagedExtensionPackageSnapshot replacement=null)
        {
            endpoint.Package(catalog,Package);
            return new ManagedExtensionInstallPackage(catalog.SourceId,endpoint.IdentityKey,catalog.SnapshotId,catalog.Sha256,Sha256,manifestBytes,content,replacement);
        }
    }
}
