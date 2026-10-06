using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    internal sealed class StoreEnvironmentInput
    {
        public StoreEnvironmentInput(ClientEnvironmentSnapshot environment, StoreRuntimeFacts runtime, IEnumerable<InstalledPackage> installed)
        {
            Environment = environment; Runtime = runtime; Installed = StoreCollections.Freeze(installed);
            DataDirectory = environment.Paths.GetExtensionDataDirectory("phinix.plugin-store");
        }
        public ClientEnvironmentSnapshot Environment { get; }
        public StoreRuntimeFacts Runtime { get; }
        public System.Collections.ObjectModel.ReadOnlyCollection<InstalledPackage> Installed { get; }
        public string DataDirectory { get; }
    }

    internal static class StoreEnvironmentAdapter
    {
        public static StoreEnvironmentInput FromSnapshot(ClientEnvironmentSnapshot environment)
        {
            if (environment == null || !environment.IsComplete)
                throw new StoreValidationException("IncompleteEnvironment", "Client paths or installed identities are unavailable; capture after extension discovery on the main thread.");
            PackageVersion phinix, abstractions;
            PackageVersion.TryParse(environment.PhinixCompatibilityVersion, out phinix);
            PackageVersion.TryParse(environment.AbstractionsCompatibilityVersion, out abstractions);
            var runtime = new StoreRuntimeFacts(environment.RimWorldVersion, phinix, abstractions,
                environment.Modules.Where(m => m.Active && (m.ManagedPackageId != null || IsHost(m.SourceModRoot, environment))).Select(m => m.Id),
                environment.LoadedAssemblies.Where(a => a.SourceModRoot == null || IsHost(a.SourceModRoot, environment)).Select(a => a.Name));
            var installed = new List<InstalledPackage>();
            foreach (ClientInstalledModSnapshot mod in environment.InstalledMods)
            {
                if (string.IsNullOrEmpty(mod.PackageId)) throw new StoreValidationException("IncompleteEnvironment", "A local mod has no canonical package identity.");
                bool host = IsHost(mod.RootDirectory, environment);
                // Metadata is not proof of source, version or content. All existing mods
                // remain unknown until an installer-record verifier is implemented.
                installed.Add(new InstalledPackage(null, null, mod.PackageId,
                    host ? new string[0] : environment.Modules.Where(m => SameRoot(m.SourceModRoot, mod.RootDirectory)).Select(m => m.Id),
                    host ? new string[0] : environment.LoadedAssemblies.Where(a => SameRoot(a.SourceModRoot, mod.RootDirectory)).Select(a => a.Name), mod.Enabled));
            }
            return new StoreEnvironmentInput(environment, runtime, installed);
        }

        private static bool IsHost(string root, ClientEnvironmentSnapshot environment) { return SameRoot(root, environment.HostModRoot); }
        private static bool SameRoot(string a, string b) { return a != null && string.Equals(a, b, ClientPathOwnership.Comparison); }
    }
}
