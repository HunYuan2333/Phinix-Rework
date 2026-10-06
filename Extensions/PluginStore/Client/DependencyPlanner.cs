using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Phinix.PluginStore
{
    internal sealed class DependencyPlanner
    {
        private const int MaxPackages = 64;
        private const int MaxDepth = 32;
        private const int MaxSearchSteps = 4096;
        private readonly CatalogSnapshot snapshot;
        private readonly StoreRuntimeFacts runtime;
        private readonly List<InstalledPackage> installed;
        private readonly Dictionary<string, List<PackageRecord>> catalog;

        public DependencyPlanner(CatalogSnapshot snapshot, StoreRuntimeFacts runtime, IEnumerable<InstalledPackage> installed)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.installed = new List<InstalledPackage>();
            foreach (InstalledPackage item in installed ?? new InstalledPackage[0])
            {
                if (this.installed.Count >= 1024) throw Error("LocalLimit", "More than 1024 local package records.");
                if (item == null || string.IsNullOrEmpty(item.RimWorldPackageId))
                    throw Error("InvalidLocalSnapshot", "Local package identity is missing.");
                if (item.VerifiedRecord != null && (item.VerifiedRecord.IsWorkshop || item.VerifiedRecord.Artifact == null ||
                    item.VerifiedRecord.RimWorldPackageId != item.RimWorldPackageId ||
                    !SameSet(item.ModuleIds, item.VerifiedRecord.Modules.Select(m => m.Id)) ||
                    !SameSet(item.AssemblyNames, item.VerifiedRecord.Assemblies.Select(a => a.Name))))
                    throw Error("InvalidLocalSnapshot", item.RimWorldPackageId + ": verified manifest disagrees with local identities.");
                this.installed.Add(item);
            }
            catalog = snapshot.Packages.GroupBy(p => p.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Version).ToList(), StringComparer.Ordinal);
        }

        public InstallPlan CreatePlan(string packageId, string version, CancellationToken cancellationToken = default(CancellationToken))
        {
            PackageVersionRange requested;
            if (!PackageVersionRange.TryParse(version, out requested) || requested.Exact == null)
                throw Error("InvalidVersion", "Select an exact stable package version before planning.");
            List<PackageRecord> roots;
            if (packageId == null || !catalog.TryGetValue(packageId, out roots)) throw Error("MissingPackage", "Package is not in the selected snapshot: " + packageId + ".");
            if (roots[0].IsWorkshop) throw Error("WorkshopLinkOnly", packageId + ": open the Workshop page; Steam manages installation and dependencies.");
            if (!roots.Any(p => requested.Contains(p.Version))) throw Error("MissingVersion", packageId + ": requested version is absent from this snapshot.");
            ValidateLocalIdentities();
            SearchContext context = new SearchContext(cancellationToken);
            Dictionary<string, PackageRecord> selected = new Dictionary<string, PackageRecord>(StringComparer.Ordinal);
            StoreValidationException failure;
            if (!Search(packageId, requested, selected, context, out failure)) throw failure;
            List<PackageRecord> ordered = DependencyOrder(packageId, selected, context);
            List<PlannedPackage> packages = new List<PlannedPackage>();
            foreach (PackageRecord package in ordered)
            {
                InstalledPackage local = FindLocal(package);
                packages.Add(new PlannedPackage(package, local != null, local != null && !local.Enabled));
            }
            return new InstallPlan(snapshot, packages, ExternalMods(ordered));
        }

        private bool Search(string rootId, PackageVersionRange rootRange, Dictionary<string, PackageRecord> selected,
            SearchContext context, out StoreValidationException failure)
        {
            context.Check();
            Dictionary<string, List<Requirement>> requirements = Requirements(rootId, rootRange, selected);
            if (requirements.Count > MaxPackages) throw Error("PlanLimit", "Dependency plan exceeds 64 packages.");
            foreach (KeyValuePair<string, PackageRecord> pair in selected.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!Satisfies(pair.Value.Version, requirements[pair.Key]))
                {
                    failure = Conflict(pair.Key, requirements[pair.Key]);
                    return false;
                }
            }
            string next = requirements.Keys.Where(id => !selected.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();
            if (next == null)
            {
                try
                {
                    DependencyOrder(rootId, selected, context);
                    ValidateSelectedIdentities(selected);
                    ValidateModuleDependencies(selected, context);
                    ExternalMods(selected.Values);
                    failure = null;
                    return true;
                }
                catch (StoreValidationException ex)
                {
                    if (ex.Code == "PlanLimit" || ex.Code == "ResolutionLimit") throw;
                    failure = ex;
                    return false;
                }
            }
            List<PackageRecord> candidates;
            try { candidates = Candidates(next, requirements[next]); }
            catch (StoreValidationException ex) { failure = ex; return false; }
            failure = Conflict(next, requirements[next]);
            foreach (PackageRecord candidate in candidates)
            {
                context.Check();
                StoreValidationException compatibilityError = CompatibilityError(candidate);
                if (compatibilityError != null) { failure = compatibilityError; continue; }
                selected.Add(next, candidate);
                bool solved;
                try { solved = Search(rootId, rootRange, selected, context, out failure); }
                catch { selected.Remove(next); throw; }
                if (solved) return true;
                selected.Remove(next);
            }
            return false;
        }

        private static Dictionary<string, List<Requirement>> Requirements(string rootId, PackageVersionRange rootRange,
            Dictionary<string, PackageRecord> selected)
        {
            Dictionary<string, List<Requirement>> result = new Dictionary<string, List<Requirement>>(StringComparer.Ordinal);
            AddRequirement(result, rootId, rootRange, "requested " + rootId);
            foreach (PackageRecord package in selected.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
                foreach (PackageDependency dependency in package.Dependencies)
                    if (!dependency.Optional) AddRequirement(result, dependency.Id, dependency.Range, package.Id + "@" + package.Version);
            return result;
        }

        private static void AddRequirement(Dictionary<string, List<Requirement>> result, string id, PackageVersionRange range, string origin)
        {
            List<Requirement> list;
            if (!result.TryGetValue(id, out list)) { list = new List<Requirement>(); result.Add(id, list); }
            list.Add(new Requirement(range, origin));
        }

        private List<PackageRecord> Candidates(string id, List<Requirement> requirements)
        {
            List<PackageRecord> available;
            if (!catalog.TryGetValue(id, out available)) throw Error("MissingPackage", id + ": missing from selected source; required by " + Describe(requirements) + ".");
            if (available[0].IsWorkshop) throw Error("WorkshopDependency", id + ": declare this as an external mod with a Workshop link, not a GitHub version dependency.");
            InstalledPackage local = FindLocal(available[0]);
            if (local != null)
            {
                if (local.VerifiedRecord == null) throw Error("UnknownLocalVersion", id + ": local version/ownership is unknown; no duplicate download is allowed.");
                PackageRecord existing = local.VerifiedRecord;
                if (local.SourceId != snapshot.SourceId) throw Error("LocalSourceConflict", id + ": installed package belongs to a different or unknown source.");
                if (existing.Id != id || existing.RimWorldPackageId != available[0].RimWorldPackageId)
                    throw Error("LocalIdentityConflict", id + ": local mod identity belongs to a different package.");
                if (!Satisfies(existing.Version, requirements))
                    throw Error("InstalledVersionConflict", id + "@" + existing.Version + ": installed version does not satisfy " + Describe(requirements) + "; first version does not overwrite/upgrade packages.");
                PackageRecord current = available.FirstOrDefault(p => p.Version.CompareTo(existing.Version) == 0);
                if (existing.State != "active" || (current != null && current.State != "active"))
                    throw Error("WithdrawnPackage", id + ": existing dependency is withdrawn or unmaintained.");
                if (current != null && (existing.Artifact.Sha256 != current.Artifact.Sha256 ||
                    existing.Artifact.ManifestSha256 != current.Artifact.ManifestSha256 || !SameArtifact(existing.Artifact, current.Artifact) ||
                    !CatalogReader.SameManifest(existing, current)))
                    throw Error("LocalContentConflict", id + ": accepted catalog metadata/digest differs from the verified installed version.");
                return new List<PackageRecord> { existing };
            }
            List<PackageRecord> candidates = available.Where(p => p.State == "active" && Satisfies(p.Version, requirements)).ToList();
            if (candidates.Count == 0 && available.Any(p => Satisfies(p.Version, requirements) && p.State != "active"))
                throw Error("WithdrawnPackage", id + ": matching versions are withdrawn or unmaintained.");
            return candidates;
        }

        private InstalledPackage FindLocal(PackageRecord package)
        {
            return installed.FirstOrDefault(p => string.Equals(p.RimWorldPackageId, package.RimWorldPackageId, StringComparison.OrdinalIgnoreCase) ||
                (p.VerifiedRecord != null && p.VerifiedRecord.Id == package.Id));
        }

        private StoreValidationException CompatibilityError(PackageRecord package)
        {
            PackageCompatibility compatibility = package.Compatibility;
            if (string.IsNullOrEmpty(runtime.RimWorldVersion)) return Error("UnknownRuntime", package.Id + ": local RimWorld version is unknown.");
            if (!compatibility.RimWorldVersions.Contains(runtime.RimWorldVersion)) return Error("IncompatibleRuntime", package.Id + ": incompatible RimWorld version.");
            if (compatibility.PhinixRange != null)
            {
                if (runtime.PhinixVersion == null) return Error("UnknownRuntime", package.Id + ": local Phinix compatibility version is unknown.");
                if (!compatibility.PhinixRange.Contains(runtime.PhinixVersion)) return Error("IncompatibleRuntime", package.Id + ": Phinix requires " + compatibility.PhinixRange.Text + ".");
            }
            if (compatibility.AbstractionsRange != null)
            {
                if (runtime.AbstractionsVersion == null) return Error("UnknownRuntime", package.Id + ": local client contract version is unknown.");
                if (!compatibility.AbstractionsRange.Contains(runtime.AbstractionsVersion)) return Error("IncompatibleRuntime", package.Id + ": client contracts require " + compatibility.AbstractionsRange.Text + ".");
            }
            return null;
        }

        private void ValidateLocalIdentities()
        {
            Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < installed.Count; i++)
            {
                InstalledPackage package = installed[i];
                string owner = "local[" + i + "]:" + package.RimWorldPackageId;
                Claim(identities, "mod:" + package.RimWorldPackageId, owner);
                if (package.VerifiedRecord != null) Claim(identities, "package:" + package.VerifiedRecord.Id, owner);
                foreach (string id in package.ModuleIds) Claim(identities, "module:" + id, owner);
                foreach (string name in package.AssemblyNames) Claim(identities, "assembly:" + name, owner);
            }
        }

        private void ValidateSelectedIdentities(Dictionary<string, PackageRecord> selected)
        {
            Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in runtime.ProvidedModuleIds) Claim(identities, "module:" + id, "host");
            foreach (string name in runtime.ProvidedAssemblyNames) Claim(identities, "assembly:" + name, "host");
            foreach (InstalledPackage local in installed)
            {
                string owner = local.VerifiedRecord == null ? "unknown-local:" + local.RimWorldPackageId : "package:" + local.VerifiedRecord.Id;
                Claim(identities, "mod:" + local.RimWorldPackageId, owner);
                foreach (string id in local.ModuleIds) Claim(identities, "module:" + id, owner);
                foreach (string name in local.AssemblyNames) Claim(identities, "assembly:" + name, owner);
            }
            foreach (PackageRecord package in selected.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                string owner = "package:" + package.Id;
                Claim(identities, "mod:" + package.RimWorldPackageId, owner);
                foreach (PackageModule module in package.Modules) Claim(identities, "module:" + module.Id, owner);
                foreach (PackageAssembly assembly in package.Assemblies) Claim(identities, "assembly:" + assembly.Name, owner);
            }
        }

        private void ValidateModuleDependencies(Dictionary<string, PackageRecord> selected, SearchContext context)
        {
            Dictionary<string, PackageModule> modules = new Dictionary<string, PackageModule>(StringComparer.Ordinal);
            foreach (string id in runtime.ProvidedModuleIds) modules[id] = new PackageModule(id, new string[0]);
            foreach (PackageRecord package in selected.Values)
                foreach (PackageModule module in package.Modules) modules.Add(module.Id, module);
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> heights = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string id in modules.Keys.OrderBy(id => id, StringComparer.Ordinal)) VisitModule(id, modules, visiting, heights, 0, context);
        }

        private static int VisitModule(string id, Dictionary<string, PackageModule> modules, HashSet<string> visiting,
            Dictionary<string, int> heights, int depth, SearchContext context)
        {
            context.Check();
            if (depth > MaxDepth) throw Error("PlanLimit", "Module dependency depth exceeds 32.");
            int height;
            if (heights.TryGetValue(id, out height)) return height;
            PackageModule module;
            if (!modules.TryGetValue(id, out module)) throw Error("MissingModule", "Required module " + id + " is not provided by the selected package closure or host.");
            if (!visiting.Add(id)) throw Error("ModuleCycle", "Module dependency cycle includes " + id + ".");
            height = 0;
            foreach (string dependency in module.DependsOn)
                height = Math.Max(height, 1 + VisitModule(dependency, modules, visiting, heights, depth + 1, context));
            if (height > MaxDepth) throw Error("PlanLimit", "Module dependency depth exceeds 32.");
            visiting.Remove(id); heights.Add(id, height);
            return height;
        }

        private static List<PackageRecord> DependencyOrder(string rootId, Dictionary<string, PackageRecord> selected, SearchContext context)
        {
            List<PackageRecord> ordered = new List<PackageRecord>();
            VisitPackage(rootId, selected, new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, int>(StringComparer.Ordinal), ordered, 0, context);
            return ordered;
        }

        private static int VisitPackage(string id, Dictionary<string, PackageRecord> selected, HashSet<string> visiting,
            Dictionary<string, int> heights, List<PackageRecord> ordered, int depth, SearchContext context)
        {
            context.Check();
            if (depth > MaxDepth) throw Error("PlanLimit", "Package dependency depth exceeds 32.");
            int height;
            if (heights.TryGetValue(id, out height)) return height;
            if (!visiting.Add(id)) throw Error("DependencyCycle", "Package dependency cycle includes " + id + ".");
            PackageRecord package = selected[id];
            height = 0;
            foreach (PackageDependency dependency in package.Dependencies.Where(d => !d.Optional).OrderBy(d => d.Id, StringComparer.Ordinal))
                height = Math.Max(height, 1 + VisitPackage(dependency.Id, selected, visiting, heights, ordered, depth + 1, context));
            if (height > MaxDepth) throw Error("PlanLimit", "Package dependency depth exceeds 32.");
            visiting.Remove(id); heights.Add(id, height); ordered.Add(package);
            return height;
        }

        private static List<ExternalModRequirement> ExternalMods(IEnumerable<PackageRecord> packages)
        {
            Dictionary<string, ExternalModRequirement> requirements = new Dictionary<string, ExternalModRequirement>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> selectedMods = new HashSet<string>(packages.Select(p => p.RimWorldPackageId), StringComparer.OrdinalIgnoreCase);
            foreach (PackageRecord package in packages.OrderBy(p => p.Id, StringComparer.Ordinal))
                foreach (ExternalModRequirement requirement in package.ExternalMods)
                {
                    if (selectedMods.Contains(requirement.PackageId))
                        throw Error("ExternalModConflict", requirement.PackageId + ": declare a package dependency instead of an external mod supplied by this plan.");
                    ExternalModRequirement previous;
                    if (requirements.TryGetValue(requirement.PackageId, out previous) && previous.WorkshopId != requirement.WorkshopId)
                        throw Error("ExternalModConflict", requirement.PackageId + ": conflicting Workshop identity declarations.");
                    requirements[requirement.PackageId] = requirement;
                }
            return requirements.Values.OrderBy(r => r.PackageId, StringComparer.Ordinal).ToList();
        }

        private static bool Satisfies(PackageVersion version, IEnumerable<Requirement> requirements) { return requirements.All(r => r.Range.Contains(version)); }
        private static bool SameArtifact(GitHubArtifact a, GitHubArtifact b)
        {
            return a.Repository == b.Repository && a.RepositoryId == b.RepositoryId && a.OwnerId == b.OwnerId &&
                a.SourceCommit == b.SourceCommit && a.Tag == b.Tag && a.ReleaseId == b.ReleaseId &&
                a.AssetId == b.AssetId && a.AssetName == b.AssetName && a.PayloadKind == b.PayloadKind && a.SizeBytes == b.SizeBytes;
        }
        private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) { return new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b); }
        private static string Describe(IEnumerable<Requirement> requirements) { return string.Join("; ", requirements.Select(r => r.Origin + " requires " + r.Range.Text)); }
        private static StoreValidationException Conflict(string id, IEnumerable<Requirement> requirements) { return Error("VersionConflict", id + ": no version satisfies " + Describe(requirements) + "."); }
        private static StoreValidationException Error(string code, string message) { return new StoreValidationException(code, message); }

        private static void Claim(Dictionary<string, string> identities, string key, string owner)
        {
            string existing;
            if (identities.TryGetValue(key, out existing) && existing != owner)
                throw Error("IdentityConflict", key + " conflicts between " + existing + " and " + owner + ".");
            identities[key] = owner;
        }

        private sealed class Requirement
        {
            public Requirement(PackageVersionRange range, string origin) { Range = range; Origin = origin; }
            public PackageVersionRange Range { get; }
            public string Origin { get; }
        }

        private sealed class SearchContext
        {
            private readonly Stopwatch elapsed = Stopwatch.StartNew();
            private readonly CancellationToken cancellationToken;
            private int steps;
            public SearchContext(CancellationToken cancellationToken) { this.cancellationToken = cancellationToken; }
            public void Check()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++steps > MaxSearchSteps || elapsed.ElapsedMilliseconds > 5000)
                    throw Error("ResolutionLimit", "Dependency resolution exceeded its search/time budget; this is not proof that no solution exists.");
            }
        }
    }
}
