using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient.Framework
{
    /// <summary>General game facts, captured on the host's main thread, never during Draw.</summary>
    public interface IClientEnvironmentService
    {
        ClientEnvironmentSnapshot Capture();
    }

    public static class ClientAbstractionsCompatibility
    {
        // A declared API compatibility version, separate from a package or CLR version.
        public const string Version = "1.7.0";
    }

    /// <summary>Absolute game paths. Allocating a path does not create or certify a directory.</summary>
    public sealed class ClientEnvironmentPaths
    {
        private static readonly Regex ExtensionIdPattern = new Regex("^[a-z0-9]+(?:[._-][a-z0-9]+)*\\z", RegexOptions.CultureInvariant);

        public ClientEnvironmentPaths(string localModsRoot, string saveDataRoot)
        {
            LocalModsRoot = NormalizeAbsolute(localModsRoot);
            SaveDataRoot = NormalizeAbsolute(saveDataRoot);
            ExtensionDataRoot = Path.Combine(SaveDataRoot, "Phinix", "ExtensionData");
            ManagedExtensions = new ManagedExtensionPaths(SaveDataRoot);
            if (ClientPathOwnership.Contains(LocalModsRoot, ExtensionDataRoot) ||
                ClientPathOwnership.Contains(LocalModsRoot, ManagedExtensions.RootDirectory) ||
                ClientPathOwnership.Contains(ManagedExtensions.RootDirectory, LocalModsRoot))
                throw new ArgumentException("Extension data must be outside the local Mods root.");
        }

        public string LocalModsRoot { get; }
        public string SaveDataRoot { get; }
        public string ExtensionDataRoot { get; }
        /// <summary>Host-owned package files and state, independent of the shop and RimWorld Mods.</summary>
        public ManagedExtensionPaths ManagedExtensions { get; }

        public string GetExtensionDataDirectory(string extensionId)
        {
            if (string.IsNullOrEmpty(extensionId) || extensionId.Length > 128 || !ExtensionIdPattern.IsMatch(extensionId) ||
                Regex.IsMatch(extensionId, "^(con|prn|aux|nul|com[0-9]|lpt[0-9])(?:[._-]|$)", RegexOptions.CultureInvariant))
                throw new ArgumentException("Expected a portable lowercase extension ID.", nameof(extensionId));
            return Path.Combine(ExtensionDataRoot, extensionId);
        }

        public static string NormalizeAbsolute(string path)
        {
            // Never make a game location depend on the current working directory.
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("An absolute game path is required.", nameof(path));
            string pathRoot = Path.GetPathRoot(path);
            if (Path.DirectorySeparatorChar == '\\' && (pathRoot == "\\" || pathRoot == "/" ||
                (pathRoot.Length == 2 && pathRoot[1] == ':')))
                throw new ArgumentException("A fully qualified game path is required.", nameof(path));
            string fullPath = Path.GetFullPath(path);
            string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length < Path.GetPathRoot(fullPath).Length ? Path.GetPathRoot(fullPath) : trimmed;
        }
    }

    public sealed class ClientInstalledModSnapshot
    {
        public ClientInstalledModSnapshot(string packageId, string rootDirectory, bool enabled)
        { PackageId = packageId; RootDirectory = ClientEnvironmentPaths.NormalizeAbsolute(rootDirectory); Enabled = enabled; }
        /// <summary>Canonical package ID without RimWorld's duplicate/Steam disambiguation suffix.</summary>
        public string PackageId { get; }
        public string RootDirectory { get; }
        public bool Enabled { get; }
    }

    public sealed class ClientLoadedAssemblySnapshot
    {
        public ClientLoadedAssemblySnapshot(string name, string clrVersion, string sourceModRoot)
            : this(name,clrVersion,sourceModRoot,null,null,null) { }
        public ClientLoadedAssemblySnapshot(string name,string clrVersion,string sourceModRoot,string managedSourceId,string managedPackageId,string managedPackageRoot)
        { Name=name; ClrVersion=clrVersion; SourceModRoot=sourceModRoot; ManagedSourceId=managedSourceId; ManagedPackageId=managedPackageId; ManagedPackageRoot=managedPackageRoot; }
        public string Name { get; }
        public string ClrVersion { get; }
        /// <summary>Null if location is unavailable or ownership is ambiguous.</summary>
        public string SourceModRoot { get; }
        public string ManagedSourceId { get; }
        public string ManagedPackageId { get; }
        public string ManagedPackageRoot { get; }
    }

    public sealed class ClientModuleSnapshot
    {
        public ClientModuleSnapshot(string id, string sourceModRoot, bool active)
            : this(id,sourceModRoot,active,null,null,null) { }
        public ClientModuleSnapshot(string id,string sourceModRoot,bool active,string managedSourceId,string managedPackageId,string managedPackageRoot)
        { Id=id; SourceModRoot=sourceModRoot; Active=active; ManagedSourceId=managedSourceId; ManagedPackageId=managedPackageId; ManagedPackageRoot=managedPackageRoot; }
        public string Id { get; }
        public string SourceModRoot { get; }
        public string ManagedSourceId { get; }
        public string ManagedPackageId { get; }
        public string ManagedPackageRoot { get; }
        public bool Active { get; }
    }

    public sealed class ClientEnvironmentSnapshot
    {
        public ClientEnvironmentSnapshot(ClientEnvironmentPaths paths, string hostModRoot, string rimWorldVersion,
            string phinixCompatibilityVersion, string abstractionsCompatibilityVersion,
            IEnumerable<ClientInstalledModSnapshot> installedMods, IEnumerable<ClientLoadedAssemblySnapshot> loadedAssemblies,
            IEnumerable<ClientModuleSnapshot> modules, IEnumerable<string> diagnostics)
            : this(paths,hostModRoot,rimWorldVersion,phinixCompatibilityVersion,abstractionsCompatibilityVersion,installedMods,loadedAssemblies,modules,diagnostics,new string[0]) { }

        public ClientEnvironmentSnapshot(ClientEnvironmentPaths paths,string hostModRoot,string rimWorldVersion,
            string phinixCompatibilityVersion,string abstractionsCompatibilityVersion,IEnumerable<ClientInstalledModSnapshot> installedMods,
            IEnumerable<ClientLoadedAssemblySnapshot> loadedAssemblies,IEnumerable<ClientModuleSnapshot> modules,IEnumerable<string> diagnostics,IEnumerable<string> disabledModules)
        {
            Paths = paths; HostModRoot = hostModRoot == null ? null : ClientEnvironmentPaths.NormalizeAbsolute(hostModRoot); RimWorldVersion = rimWorldVersion;
            PhinixCompatibilityVersion = phinixCompatibilityVersion; AbstractionsCompatibilityVersion = abstractionsCompatibilityVersion;
            InstalledMods = Copy(installedMods); LoadedAssemblies = Copy(loadedAssemblies);
            Modules = Copy(modules); Diagnostics = Copy(diagnostics);
            DisabledModuleIds=Copy(disabledModules);
        }
        // Null paths and diagnostics preserve unknown facts; callers must not assume readiness.
        public ClientEnvironmentPaths Paths { get; }
        public string HostModRoot { get; }
        public string RimWorldVersion { get; }
        public string PhinixCompatibilityVersion { get; }
        public string AbstractionsCompatibilityVersion { get; }
        public ReadOnlyCollection<ClientInstalledModSnapshot> InstalledMods { get; }
        public ReadOnlyCollection<ClientLoadedAssemblySnapshot> LoadedAssemblies { get; }
        public ReadOnlyCollection<ClientModuleSnapshot> Modules { get; }
        public ReadOnlyCollection<string> Diagnostics { get; }
        public ReadOnlyCollection<string> DisabledModuleIds { get; }
        public bool IsComplete => Paths != null && HostModRoot != null && Diagnostics.Count == 0;

        private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
        { return new List<T>(values ?? new T[0]).AsReadOnly(); }
    }

    /// <summary>Lexical ownership only; installers must separately check links and filesystem state.</summary>
    public static class ClientPathOwnership
    {
        public static StringComparison Comparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static bool Contains(string root, string path)
        {
            string canonicalRoot = ClientEnvironmentPaths.NormalizeAbsolute(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string canonicalPath = ClientEnvironmentPaths.NormalizeAbsolute(path);
            return string.Equals(canonicalRoot, canonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Comparison) ||
                canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, Comparison);
        }

        public static string ResolveModRoot(string assemblyPath, IEnumerable<ClientInstalledModSnapshot> mods)
        {
            if (string.IsNullOrEmpty(assemblyPath)) return null;
            string owner = null;
            foreach (ClientInstalledModSnapshot mod in mods)
            {
                if (!Contains(mod.RootDirectory, assemblyPath)) continue;
                if (owner != null) return null; // Duplicate/nested roots cannot establish one owner.
                owner = mod.RootDirectory;
            }
            return owner;
        }
    }
}
