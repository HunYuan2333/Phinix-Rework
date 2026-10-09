using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    // Presentation only. Installed-only entries never become catalog download candidates.
    internal sealed class ManagedStoreEntry
    {
        internal ManagedStoreEntry(string key,ManagedStoreRecord catalog,ManagedExtensionManagementPackage installed)
        { Key=key; CatalogRecord=catalog; Installed=installed; }
        internal string Key { get; }
        internal ManagedStoreRecord CatalogRecord { get; }
        internal ManagedExtensionManagementPackage Installed { get; }
        internal bool IsInstalledOnly => CatalogRecord==null;
        internal bool IsLocalDevelopment => Installed?.Package.IsLocalDevelopment==true;
        internal bool IsWorkshop => CatalogRecord?.IsWorkshop==true;
        internal string Id => CatalogRecord?.Id??Installed?.Package.PackageId??Installed?.Package.RecordKey;
        internal string Author => CatalogRecord?.Author??Installed?.Package.SourceId;
        internal string License => CatalogRecord?.License;
        internal string State => CatalogRecord?.State??"installed";
        internal ManagedExtensionManifest Manifest => CatalogRecord?.Manifest??Installed?.Package.Manifest;
        internal string Version => Manifest?.Version.ToString()??Installed?.Package.Version??"?";
        internal IEnumerable<string> RimWorldVersions => CatalogRecord?.RimWorldVersions??(IEnumerable<string>)Manifest?.Compatibility.RimWorldVersions??Enumerable.Empty<string>();
        internal IEnumerable<string> Tags => CatalogRecord?.Tags??(IEnumerable<string>)Enumerable.Empty<string>();
        internal string DisplayName(string locale) => CatalogRecord?.DisplayName(locale)??Manifest?.Name??Id??"?";
        internal string DisplaySummary(string locale) => CatalogRecord?.DisplaySummary(locale)??Id;
        internal string DisplayChangelog(string locale) => CatalogRecord?.DisplayChangelog(locale);
    }
    internal sealed class ManagedStoreEntryGroup
    {
        internal ManagedStoreEntryGroup(IEnumerable<ManagedStoreEntry> entries)
        {
            Versions=StoreCollections.Freeze(entries.OrderByDescending(p=>p.Manifest?.Version));
            Preferred=Versions.FirstOrDefault(p=>p.State=="active")??Versions.First();
        }
        internal ReadOnlyCollection<ManagedStoreEntry> Versions { get; }
        internal ManagedStoreEntry Preferred { get; }
    }
    internal sealed class ManagedStoreGroup
    {
        internal ManagedStoreGroup(IEnumerable<ManagedStoreRecord> versions)
        {
            Versions=StoreCollections.Freeze(versions.OrderByDescending(p=>p.Manifest?.Version));
            Preferred=Versions.FirstOrDefault(p=>p.State=="active")??Versions.First();
        }
        internal ReadOnlyCollection<ManagedStoreRecord> Versions { get; }
        internal ManagedStoreRecord Preferred { get; }
    }

    internal static class ManagedStoreListing
    {
        internal static ManagedStoreEntryGroup[] BuildEntries(ManagedStoreCatalogSnapshot catalog,
            ManagedExtensionManagementSnapshot inventory,RepositoryEndpoint endpoint,string search,string locale)
        {
            var entries=new List<ManagedStoreEntry>();
            var represented=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in catalog?.Packages??Enumerable.Empty<ManagedStoreRecord>())
            {
                var matches=inventory?.Packages.Where(p=>!record.IsWorkshop && p.Package.PackageId==record.Id &&
                    p.Package.SourceId==catalog.SourceId && endpoint!=null && p.Package.RepositoryIdentitySha256==endpoint.IdentityKey).ToArray();
                var installed=matches?.Length==1?matches[0]:null;
                if(installed!=null) represented.Add(installed.Package.RecordKey);
                entries.Add(new ManagedStoreEntry("catalog:"+catalog.SourceId+":"+endpoint?.IdentityKey+":"+record.Id,record,installed));
            }
            int unknown=0;
            foreach(var installed in inventory?.Packages??Enumerable.Empty<ManagedExtensionManagementPackage>())
                if(!represented.Contains(installed.Package.RecordKey))
                    entries.Add(new ManagedStoreEntry("installed:"+(installed.Package.RecordKey??"unknown-"+unknown++),null,installed));
            return entries.GroupBy(e=>e.Key,StringComparer.Ordinal).Select(g=>new ManagedStoreEntryGroup(g))
                .Where(g=>string.IsNullOrEmpty(search) || g.Versions.Any(e=>
                    (e.DisplayName(locale)+" "+e.Id+" "+e.Author+" "+e.DisplaySummary(locale)+" "+string.Join(" ",e.Tags)+
                    (e.IsLocalDevelopment?" local-development dev":"")).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderBy(g=>g.Preferred.DisplayName(locale),StringComparer.Ordinal).ThenBy(g=>g.Preferred.Key,StringComparer.Ordinal).ToArray();
        }
        internal static ManagedStoreEntry RestoreEntry(IEnumerable<ManagedStoreEntryGroup> groups,ManagedStoreEntry previous)
        {
            if(previous==null) return null;
            var entries=groups.SelectMany(g=>g.Versions).ToArray();
            var exact=entries.FirstOrDefault(e=>e.Key==previous.Key &&
                (e.IsInstalledOnly && previous.IsInstalledOnly || e.CatalogRecord!=null && previous.CatalogRecord!=null &&
                 e.IsWorkshop==previous.IsWorkshop && (e.IsWorkshop || e.Version==previous.Version)));
            if(exact!=null || previous.Installed?.Package.RecordKey==null) return exact;
            // A catalog can disappear/return without changing the owned installation.
            return entries.FirstOrDefault(e=>e.Installed?.Package.RecordKey==previous.Installed.Package.RecordKey &&
                (e.IsInstalledOnly || previous.IsInstalledOnly && e.Version==previous.Installed.Package.Version));
        }
        internal static ManagedStoreRecord RestoreSelection(ManagedStoreCatalogSnapshot catalog, ManagedStoreRecord previous)
        {
            if(catalog==null || previous==null) return null;
            return catalog.Packages.FirstOrDefault(p=>p.Id==previous.Id &&
                (p.IsWorkshop ? previous.IsWorkshop : !previous.IsWorkshop && p.Manifest.Version.CompareTo(previous.Manifest.Version)==0));
        }
        internal static ManagedStoreGroup[] Build(ManagedStoreCatalogSnapshot catalog,string search,string locale)
        {
            return (catalog?.Packages??Enumerable.Empty<ManagedStoreRecord>()).GroupBy(p=>p.Id,StringComparer.Ordinal)
                .Select(g=>new ManagedStoreGroup(g))
                .Where(g=>string.IsNullOrEmpty(search) || g.Versions.Any(p=>
                    (p.DisplayName(locale)+" "+p.Id+" "+p.Author+" "+p.DisplaySummary(locale)+" "+string.Join(" ",p.Tags))
                    .IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0))
                .OrderBy(g=>g.Preferred.DisplayName(locale),StringComparer.Ordinal).ThenBy(g=>g.Preferred.Id,StringComparer.Ordinal).ToArray();
        }
    }

    // Consumed only by Draw on the main thread. A completed plan cannot replay an installation.
    internal sealed class ManagedStoreInstallIntent
    {
        private readonly ManagedStoreCatalogSnapshot catalog;
        private readonly ManagedStoreRecord root;
        private bool pending=true;
        internal ManagedStoreInstallIntent(ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord root)
        { this.catalog=catalog; this.root=root; }
        internal ManagedStorePlan Take(ManagedStoreSnapshot snapshot,ManagedStoreRecord selected)
        {
            if(!pending) return null;
            if(snapshot.Catalog!=catalog || selected!=root) { pending=false; return null; }
            if(snapshot.State==ManagedStoreState.Planning) return null;
            pending=false;
            return snapshot.State==ManagedStoreState.PlanReady && snapshot.Plan?.Catalog==catalog && snapshot.Plan.Root==root ? snapshot.Plan : null;
        }
        internal static bool NeedsConfirmation(ManagedStorePlan plan)
        { return plan.ReplacesPackages || plan.Items.Any(i=>i.Package.Id!=plan.Root.Id); }
    }
}
