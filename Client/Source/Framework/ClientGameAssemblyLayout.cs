using System;
using System.Collections.Generic;
using System.IO;
using Utils;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Select one game build's owned assemblies. This does not select a client API or business adapter.
    internal static class ClientGameAssemblyLayout
    {
        internal static IEnumerable<string> GetProbeDirectories(string modRoot, int major, int minor, Action<string, LogLevel> log = null)
        {
            if (string.IsNullOrEmpty(modRoot)) yield break;
            if (!Path.IsPathRooted(modRoot)) throw new ArgumentException("An absolute mod root is required.", nameof(modRoot));
            if (major != 1 || (minor != 6 && minor != 7))
                throw new NotSupportedException("Unsupported game assembly layout: " + major + "." + minor);
            string root = Path.GetFullPath(modRoot);
            string versionRoot = Path.Combine(root, major + "." + minor);
            string versionAssemblies = Path.Combine(versionRoot, "Assemblies");
            bool hasVersionedSdk = File.Exists(Path.Combine(versionAssemblies, "10-ClientExtensionAbstractions.dll"));
            if (hasVersionedSdk)
            {
                yield return versionAssemblies;
                foreach (string directory in ExtensionBundleDirectories.GetProbeDirectories(Path.Combine(versionRoot, "Extensions"), log))
                    yield return directory;
                yield break;
            }
            // The maintained 1.6 build still emits Common. Never load those DLLs as a 1.7 backend.
            if (minor == 7) throw new InvalidOperationException("Missing RimWorld 1.7 versioned client assemblies.");
            yield return Path.Combine(root, "Common", "Assemblies");
            foreach (string directory in ExtensionBundleDirectories.GetProbeDirectories(Path.Combine(root, "Common", "Extensions"), log))
                yield return directory;
        }
    }
}
