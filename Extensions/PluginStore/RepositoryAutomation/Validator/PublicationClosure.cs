using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.PluginStore;
using Utils.Framework.ManagedExtensions;

// Repository closure only. Runtime host/game/installed-state checks still run in the client.
internal static class PublicationClosure
{
    internal static bool ValidModuleGraph(Dictionary<string,string[]> modules)
    {
        return !modules.Values.Any(deps=>deps.Any(d=>!modules.ContainsKey(d))) &&
            modules.Keys.All(id=>Visit(id,new HashSet<string>(),new HashSet<string>(),key=>modules[key]));
    }
    internal static void Validate(ManagedStoreCatalogSnapshot catalog, IEnumerable<PublicationHostProfile> hostProfiles = null)
    {
        foreach (var root in catalog.Packages.Where(p => !p.IsWorkshop && p.State == "active"))
        {
            int steps = 0;
            if (!Search(catalog, root, new Dictionary<string, ManagedStoreRecord>(), (hostProfiles ?? new PublicationHostProfile[0]).ToList(), ref steps))
                throw new InvalidOperationException("PublicationDependencyClosure");
        }
    }

    private static bool Search(ManagedStoreCatalogSnapshot catalog, ManagedStoreRecord root,
        Dictionary<string, ManagedStoreRecord> selected, List<PublicationHostProfile> hostProfiles, ref int steps)
    {
        if (++steps > 4096 || selected.Count > 32) throw new InvalidOperationException("PublicationResolutionLimit");
        var required = new Dictionary<string, List<ManagedExtensionVersionRange>>();
        Add(required, root.Id, ManagedExtensionVersionRange.Parse(root.Manifest.Version.ToString()));
        foreach (var package in selected.Values)
            foreach (var dependency in package.Manifest.Dependencies)
                if (!dependency.Optional || catalog.Packages.Any(p => !p.IsWorkshop && p.State == "active" && p.Id == dependency.PackageId))
                    Add(required, dependency.PackageId, dependency.VersionRange);
        if (selected.Any(p => required.ContainsKey(p.Key) && required[p.Key].Any(r => !r.Contains(p.Value.Manifest.Version)))) return false;
        string next = required.Keys.Where(k => !selected.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
        if (next == null) return IdentitiesAndCycles(selected,hostProfiles);
        foreach (var candidate in catalog.Packages.Where(p => !p.IsWorkshop && p.State == "active" && p.Id == next &&
            required[next].All(r => r.Contains(p.Manifest.Version))).OrderByDescending(p => p.Manifest.Version))
        {
            selected.Add(next, candidate);
            if (Search(catalog, root, selected, hostProfiles, ref steps)) return true;
            selected.Remove(next);
        }
        return false;
    }

    private static void Add(Dictionary<string, List<ManagedExtensionVersionRange>> required, string id, ManagedExtensionVersionRange range)
    {
        List<ManagedExtensionVersionRange> values;
        if (!required.TryGetValue(id, out values)) required[id] = values = new List<ManagedExtensionVersionRange>();
        values.Add(range);
    }

    private static bool IdentitiesAndCycles(Dictionary<string, ManagedStoreRecord> selected, List<PublicationHostProfile> hostProfiles)
    {
        var matches=hostProfiles.Where(p=>selected.Values.All(package=>p.Covers(package.Manifest.Compatibility.PhinixRange))).ToList();
        if(matches.Count>1) throw new InvalidOperationException("HostProfileAmbiguous");
        var host=matches.SingleOrDefault();
        var assemblies = host==null?new HashSet<string>(StringComparer.OrdinalIgnoreCase):new HashSet<string>(host.Assemblies,StringComparer.OrdinalIgnoreCase);
        var modules = host==null?new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase):new Dictionary<string,string[]>(host.Modules,StringComparer.OrdinalIgnoreCase);
        foreach (var package in selected.Values)
        {
            foreach (var assembly in package.Manifest.Assemblies)
            {
                var names=ManagedExtensionManifestReader.AssemblyNames(assembly).ToArray();
                if(names.Any(assemblies.Contains)) return false;
                assemblies.UnionWith(names);
            }
            foreach (var module in package.Manifest.Modules)
            {
                if (modules.ContainsKey(module.Id)) return false;
                modules.Add(module.Id, module.DependsOn.ToArray());
            }
        }
        // Only discovered modules from a version-scoped, maintainer-owned profile
        // supplement repository providers. Missing/unknown modules still refuse publication.
        if (!ValidModuleGraph(modules)) return false;
        foreach (string id in selected.Keys)
            if (!Visit(id, new HashSet<string>(), new HashSet<string>(), key => selected[key].Manifest.Dependencies
                .Where(d => selected.ContainsKey(d.PackageId)).Select(d => d.PackageId))) return false;
        return true;
    }

    private static bool Visit(string id, HashSet<string> seen, HashSet<string> stack, Func<string, IEnumerable<string>> children)
    {
        if (stack.Contains(id)) return false;
        if (!seen.Add(id)) return true;
        stack.Add(id);
        foreach (string child in children(id)) if (!Visit(child, seen, stack, children)) return false;
        stack.Remove(id);
        return true;
    }
}
