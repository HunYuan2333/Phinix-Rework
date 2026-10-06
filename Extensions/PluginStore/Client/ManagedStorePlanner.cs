using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    internal sealed class ManagedStorePlanItem
    {
        internal ManagedStorePlanItem(ManagedStoreRecord package,ManagedExtensionPackageSnapshot installed) { Package=package; Installed=installed; }
        internal ManagedStoreRecord Package { get; }
        internal ManagedExtensionPackageSnapshot Installed { get; }
        internal bool RequiresDownload => Installed==null || Installed.Version!=Package.Manifest.Version.ToString();
    }
    internal sealed class ManagedStorePlan
    {
        internal ManagedStorePlan(ManagedStoreCatalogSnapshot catalog,ManagedStoreRecord root,IEnumerable<ManagedStorePlanItem> items)
        { Catalog=catalog; Root=root; Items=StoreCollections.Freeze(items); DownloadBytes=Items.Where(i=>i.RequiresDownload).Sum(i=>i.Package.Artifact.SizeBytes); }
        internal ManagedStoreCatalogSnapshot Catalog { get; }
        internal ManagedStoreRecord Root { get; }
        internal ReadOnlyCollection<ManagedStorePlanItem> Items { get; }
        internal long DownloadBytes { get; }
        internal bool ReplacesPackages => Items.Any(i=>i.Installed!=null && i.RequiresDownload);
        internal string Identity => string.Join("\n",Items.Select(i=>i.Package.Id+"@"+i.Package.Manifest.Version+":"+i.Package.Artifact.Sha256+":"+(i.Installed?.InstallationTransactionId??"new")+":"+i.Installed?.StateOperationId+
            ":"+i.Installed?.Version+":"+i.Installed?.ManifestSha256+":"+i.Installed?.ArtifactSha256+":"+i.Installed?.CatalogSnapshotId+":"+i.Installed?.CatalogSha256+":"+i.Installed?.RepositoryIdentitySha256));
    }
    internal sealed class ManagedStorePlanner
    {
        private readonly ManagedStoreCatalogSnapshot catalog;
        private readonly ClientEnvironmentSnapshot environment;
        private readonly ManagedExtensionManagementSnapshot inventory;
        private readonly RepositoryEndpoint endpoint;
        private CancellationToken token;
        private int steps;
        private readonly bool replace;
        internal ManagedStorePlanner(ManagedStoreCatalogSnapshot catalog,ClientEnvironmentSnapshot environment,ManagedExtensionManagementSnapshot inventory,RepositoryEndpoint endpoint,bool replace=false)
        { this.catalog=catalog; this.environment=environment; this.inventory=inventory; this.endpoint=endpoint; this.replace=replace; }
        internal ManagedStorePlan Plan(ManagedStoreRecord root,CancellationToken cancellation)
        {
            token=cancellation; token.ThrowIfCancellationRequested();
            if(environment==null || !environment.IsComplete || inventory==null || inventory.Diagnostics.Count!=0 || inventory.Packages.Any(p=>p.Package.DiagnosticCode!=null)) throw Error("IncompleteEnvironment");
            if(root==null || !catalog.Packages.Contains(root) || root.IsWorkshop || root.State!="active") throw Error("PackageUnavailable");
            var selected=new Dictionary<string,ManagedStoreRecord>();
            if(!Search(root,selected)) throw Error("ManagedDependencyConflict");
            var ordered=new List<ManagedStoreRecord>(); var seen=new HashSet<string>(); var stack=new HashSet<string>();
            Visit(root.Id,selected,seen,stack,ordered);
            var items=ordered.Select(p=>new ManagedStorePlanItem(p,Local(p))).ToList();
            ManagedStoreLocalGate.Check(environment,items.Where(i=>i.RequiresDownload).Select(i=>i.Package),token);
            if(items.Count>ManagedExtensionInstallRequest.MaxPackages || items.Where(i=>i.RequiresDownload).Sum(i=>i.Package.Manifest.Assemblies.Sum(a=>a.File.Length)+i.Package.Manifest.Resources.Sum(r=>r.Length)+ManagedExtensionManifestReader.MaxManifestBytes)>ManagedExtensionManifestReader.MaxExpandedBytes) throw Error("ManagedInstallMemoryLimit");
            return new ManagedStorePlan(catalog,root,items);
        }
        private bool Search(ManagedStoreRecord root,Dictionary<string,ManagedStoreRecord> selected)
        {
            token.ThrowIfCancellationRequested(); if(++steps>4096 || selected.Count>32) throw Error("ResolutionLimit");
            var required=new Dictionary<string,List<ManagedExtensionVersionRange>>(); Add(required,root.Id,ManagedExtensionVersionRange.Parse(root.Manifest.Version.ToString()));
            foreach(var p in selected.Values) foreach(var d in p.Manifest.Dependencies)
                if(!d.Optional || catalog.Packages.Any(c=>c.Id==d.PackageId) || inventory.Packages.Any(c=>c.Package.PackageId==d.PackageId)) Add(required,d.PackageId,d.VersionRange);
            if(replace) foreach(var p in inventory.Packages.Where(p=>p.Package.DesiredState!=ManagedExtensionDesiredState.PendingRemoval && !selected.ContainsKey(p.Package.PackageId)))
                foreach(var d in p.Package.Manifest.Dependencies.Where(d=>required.ContainsKey(d.PackageId))) Add(required,d.PackageId,d.VersionRange);
            if(selected.Any(p=>required.ContainsKey(p.Key) && required[p.Key].Any(r=>!r.Contains(p.Value.Manifest.Version)))) return false;
            string next=required.Keys.Where(k=>!selected.ContainsKey(k)).OrderBy(k=>k,StringComparer.Ordinal).FirstOrDefault();
            if(next==null)
            {
                try { var ordered=new List<ManagedStoreRecord>(); Visit(root.Id,selected,new HashSet<string>(),new HashSet<string>(),ordered); CheckIdentities(ordered); return true; }
                catch(StoreValidationException) { return false; }
            }
            foreach(var candidate in catalog.Packages.Where(p=>p.Id==next && !p.IsWorkshop && p.State=="active" && required[next].All(r=>r.Contains(p.Manifest.Version))).OrderByDescending(p=>p.Manifest.Version))
            {
                try { Compatible(candidate); Local(candidate); } catch(StoreValidationException) { continue; }
                selected.Add(next,candidate); if(Search(root,selected)) return true; selected.Remove(next);
            }
            return false;
        }
        private static void Add(Dictionary<string,List<ManagedExtensionVersionRange>> required,string id,ManagedExtensionVersionRange range)
        { List<ManagedExtensionVersionRange> values; if(!required.TryGetValue(id,out values)) required[id]=values=new List<ManagedExtensionVersionRange>(); values.Add(range); }
        private void Visit(string id,Dictionary<string,ManagedStoreRecord> selected,HashSet<string> seen,HashSet<string> stack,List<ManagedStoreRecord> ordered)
        {
            token.ThrowIfCancellationRequested(); if(stack.Contains(id)) throw Error("CandidatePackageDependencyCycle"); if(!seen.Add(id)) return;
            stack.Add(id); foreach(var dep in selected[id].Manifest.Dependencies) if(selected.ContainsKey(dep.PackageId)) Visit(dep.PackageId,selected,seen,stack,ordered);
            stack.Remove(id); ordered.Add(selected[id]);
        }
        private void Compatible(ManagedStoreRecord p)
        {
            var c=p.Manifest.Compatibility;
            if(!c.RimWorldVersions.Contains(environment.RimWorldVersion) || !c.PhinixRange.Contains(ManagedExtensionVersion.Parse(environment.PhinixCompatibilityVersion)) || !c.AbstractionsRange.Contains(ManagedExtensionVersion.Parse(environment.AbstractionsCompatibilityVersion))) throw Error("CandidateHostIncompatible");
            if(p.Manifest.ExternalMods.Any(m=>!environment.InstalledMods.Any(i=>i.Enabled && string.Equals(i.PackageId,m.PackageId,StringComparison.OrdinalIgnoreCase)))) throw Error("CandidateExternalModMissing");
            if(p.Manifest.Modules.All(m=>environment.DisabledModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase))) throw Error("ManagedAllModulesDisabled");
        }
        private ManagedExtensionPackageSnapshot Local(ManagedStoreRecord p)
        {
            var rows=inventory.Packages.Where(i=>i.Package.PackageId==p.Id).ToList(); if(rows.Count==0) return null;
            if(rows.Count!=1) throw Error("ManagedInstallPackageConflict"); var row=rows[0].Package;
            if(row.SourceId!=catalog.SourceId || row.RepositoryIdentitySha256!=endpoint.IdentityKey) throw Error("ManagedInstalledIdentityChanged");
            if(row.Version==p.Manifest.Version.ToString())
            { if(row.ManifestSha256!=p.Artifact.ManifestSha256 || row.ArtifactSha256!=p.Artifact.Sha256) throw Error("ManagedInstalledIdentityChanged"); }
            else if(!replace || p.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(row.Version))<=0) throw Error("ManagedInstalledIdentityChanged");
            if(row.DesiredState!=ManagedExtensionDesiredState.Enabled || row.ContentState!=ManagedExtensionContentState.ContentVerified) throw Error("ManagedDependencyDisabled");
            return row;
        }
        private void CheckIdentities(List<ManagedStoreRecord> selected)
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var modules=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var available=new HashSet<string>(environment.Modules.Where(m=>m.Active && !environment.DisabledModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase)).Select(m=>m.Id),StringComparer.OrdinalIgnoreCase);
            foreach(var p in selected)
            {
                Compatible(p); var local=Local(p);
                foreach(var a in p.Manifest.Assemblies)
                foreach(string name in ManagedExtensionManifestReader.AssemblyNames(a))
                {
                    if(!names.Add(name)) throw Error("CandidateAssemblyConflict");
                    if(environment.LoadedAssemblies.Any(h=>string.Equals(h.Name,name,StringComparison.OrdinalIgnoreCase) && (local==null || h.ManagedSourceId!=local.SourceId || h.ManagedPackageId!=local.PackageId))) throw Error("CandidateAssemblyConflict");
                    if(inventory.Packages.Any(i=>i.Package.PackageId!=p.Id && i.Package.Manifest.Assemblies.SelectMany(ManagedExtensionManifestReader.AssemblyNames).Contains(name,StringComparer.OrdinalIgnoreCase))) throw Error("CandidateAssemblyConflict");
                }
                foreach(var m in p.Manifest.Modules)
                {
                    if(!modules.Add(m.Id) || environment.Modules.Any(h=>h.Id==m.Id && (local==null || h.ManagedPackageId!=p.Id || h.ManagedSourceId!=local.SourceId)) ||
                        inventory.Packages.Any(i=>i.Package.PackageId!=p.Id && i.Package.Manifest.Modules.Any(h=>h.Id==m.Id))) throw Error("CandidateModuleConflict");
                    if(!environment.DisabledModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase)) available.Add(m.Id);
                }
            }
            foreach(var p in selected) foreach(var m in p.Manifest.Modules.Where(m=>!environment.DisabledModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase)))
                if(m.DependsOn.Any(d=>!available.Contains(d))) throw Error("CandidateModuleDependencyUnavailable");
        }
        private static StoreValidationException Error(string code) { return new StoreValidationException(code,"Managed installation plan rejected: "+code); }
    }
}
