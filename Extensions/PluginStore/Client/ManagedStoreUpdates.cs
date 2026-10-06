using System;
using System.Linq;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    internal static class ManagedStoreUpdates
    {
        // This is a notice, not authorization to replace files or to skip dependency planning.
        internal static ManagedStoreRecord[] Find(ManagedStoreCatalogSnapshot catalog,ManagedExtensionManagementSnapshot inventory,RepositoryEndpoint endpoint,ClientEnvironmentSnapshot environment)
        {
            if(catalog==null || inventory==null || endpoint==null || environment==null || !environment.IsComplete || inventory.Diagnostics.Count!=0) return new ManagedStoreRecord[0];
            var latest=catalog.Packages.Where(p=>!p.IsWorkshop && p.State=="active")
                .GroupBy(p=>p.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.OrderByDescending(p=>p.Manifest.Version).ToArray(),StringComparer.Ordinal);
            return inventory.Packages.Where(p=>p.Package.SourceId==catalog.SourceId && p.Package.RepositoryIdentitySha256==endpoint.IdentityKey &&
                    p.Package.DesiredState!=ManagedExtensionDesiredState.PendingRemoval && p.Package.ContentState==ManagedExtensionContentState.ContentVerified &&
                    p.Package.DiagnosticCode==null && inventory.Packages.Count(i=>i.Package.PackageId==p.Package.PackageId)==1)
                .Select(p=>latest.TryGetValue(p.Package.PackageId,out var candidates)?candidates.FirstOrDefault(c=>
                    c.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(p.Package.Version))>0 &&
                    c.Manifest.Compatibility.RimWorldVersions.Contains(environment.RimWorldVersion) &&
                    c.Manifest.Compatibility.PhinixRange.Contains(ManagedExtensionVersion.Parse(environment.PhinixCompatibilityVersion)) &&
                    c.Manifest.Compatibility.AbstractionsRange.Contains(ManagedExtensionVersion.Parse(environment.AbstractionsCompatibilityVersion))):null)
                .Where(p=>p!=null).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        }
    }
}
