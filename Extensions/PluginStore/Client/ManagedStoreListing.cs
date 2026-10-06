using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Phinix.PluginStore
{
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
