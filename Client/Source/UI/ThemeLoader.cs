using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace PhinixClient
{
    internal sealed class ThemeSource
    {
        internal string Id, Path, Source, Name, Author;
        internal byte[] Document;
        internal bool Migrate;
    }
    // File/mod/package discovery is isolated from theme runtime and business consumers.
    internal static class ThemeSources
    {
        internal static IEnumerable<ThemeSource> Discover(string modRoot, string userDirectory)
        {
            string builtin = Path.Combine(modRoot, "Themes", "default.xml");
            if (File.Exists(builtin)) yield return new ThemeSource { Id = "builtin.default", Path = builtin, Source = "builtin" };
            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod == null || !mod.Active || mod.RootDir == null || string.Equals(mod.PackageId, Client.PackageId, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var source in Files(Path.Combine(mod.RootDir.ToString(), "Themes"), "mod/" + mod.PackageId, "mod", true)) yield return source;
            }
            foreach (var source in Files(userDirectory, "user", "user", true)) yield return source;
        }
        private static IEnumerable<ThemeSource> Files(string directory, string prefix, string kind, bool migrate)
        {
            if (!Directory.Exists(directory)) yield break;
            // Enumeration failures belong to this source; no enumeration on a render path.
            string[] paths = Directory.GetFiles(directory, "*.xml").OrderBy(p => p, StringComparer.Ordinal).Take(256).ToArray();
            foreach (string path in paths) yield return new ThemeSource { Id = prefix + "/" + Path.GetFileName(path), Path = path, Source = kind, Migrate = migrate };
        }
    }
}
