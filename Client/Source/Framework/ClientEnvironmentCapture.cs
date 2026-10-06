using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimWorld;
using Utils;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace PhinixClient.Framework
{
    internal sealed class ClientEnvironmentCapture
    {
        private readonly string configuredHostModRoot;
        private readonly Func<IEnumerable<ExtensionDiscoveryResult>> getModules;
        private readonly Action<string, LogLevel> log;
        private readonly Func<ManagedExtensionRuntime> getManagedRuntime;

        public ClientEnvironmentCapture(string hostModRoot, Func<IEnumerable<ExtensionDiscoveryResult>> getModules, Action<string, LogLevel> log, Func<ManagedExtensionRuntime> getManagedRuntime = null)
        { configuredHostModRoot = hostModRoot; this.getModules = getModules; this.log = log; this.getManagedRuntime=getManagedRuntime; }

        public ClientEnvironmentSnapshot Capture()
        {
            var diagnostics = new List<string>();
            string hostModRoot = null;
            ClientEnvironmentPaths paths = null;
            string rimWorldVersion = null;
            var mods = new List<ClientInstalledModSnapshot>();
            var assemblies = new List<ClientLoadedAssemblySnapshot>();
            var modules = new List<ClientModuleSnapshot>();
            try { hostModRoot = ClientEnvironmentPaths.NormalizeAbsolute(configuredHostModRoot); }
            catch (Exception ex) { Report(diagnostics, "HostRootUnavailable", ex); }
            try { paths = new ClientEnvironmentPaths(GenFilePaths.ModsFolderPath, GenFilePaths.SaveDataFolderPath); }
            catch (Exception ex) { Report(diagnostics, "GamePathsUnavailable", ex); }
            try
            {
                rimWorldVersion = VersionControl.CurrentMajor.ToString(CultureInfo.InvariantCulture) + "." +
                    VersionControl.CurrentMinor.ToString(CultureInfo.InvariantCulture);
                foreach (ModMetaData mod in ModLister.AllInstalledMods)
                {
                    if (mod == null) { diagnostics.Add("NullInstalledMod"); continue; }
                    try { mods.Add(new ClientInstalledModSnapshot(mod.PackageIdNonUnique, mod.RootDir.FullName, mod.Active)); }
                    catch (Exception ex) { Report(diagnostics, "InstalledModUnavailable", ex); }
                }
            }
            catch (Exception ex) { Report(diagnostics, "InstalledModsUnavailable", ex); }
            if (mods.Count(m => string.Equals(m.RootDirectory, hostModRoot, ClientPathOwnership.Comparison)) != 1)
                diagnostics.Add("HostModOwnershipUnknown");
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Dynamic helper assemblies have no installable file identity.
                if (assembly.IsDynamic) continue;
                try
                {
                    AssemblyName name = assembly.GetName();
                    string owner = null;
                    try { owner = ClientPathOwnership.ResolveModRoot(assembly.Location, mods); }
                    catch (NotSupportedException) { }
                    ManagedExtensionPackageSnapshot package=null;
                    getManagedRuntime?.Invoke()?.TryGetOwner(assembly,out package);
                    string managedRoot=package==null || paths==null?null:paths.ManagedExtensions.GetPackageDirectory(package.SourceId,package.PackageId);
                    assemblies.Add(new ClientLoadedAssemblySnapshot(name.Name, name.Version?.ToString(), owner,package?.SourceId,package?.PackageId,managedRoot));
                }
                catch (Exception ex) { Report(diagnostics, "AssemblyIdentityUnavailable", ex); }
            }
            IEnumerable<ExtensionDiscoveryResult> results = getModules?.Invoke();
            if (results == null) diagnostics.Add("ExtensionDiscoveryNotReady");
            else foreach (ExtensionDiscoveryResult result in results)
            {
                if (result == null || string.IsNullOrEmpty(result.ExtensionId)) continue;
                ClientLoadedAssemblySnapshot[] owners = assemblies.Where(a => a.Name == result.AssemblyName).ToArray();
                string owner = owners.Length == 1 ? owners[0].SourceModRoot : null;
                var managed=owners.Length==1?owners[0]:null;
                if (owner == null && managed?.ManagedPackageId == null) diagnostics.Add("ModuleOwnershipUnknown:" + result.ExtensionId);
                modules.Add(new ClientModuleSnapshot(result.ExtensionId, owner, result.State == ExtensionModuleState.Active,managed?.ManagedSourceId,managed?.ManagedPackageId,managed?.ManagedPackageRoot));
            }
            return new ClientEnvironmentSnapshot(paths, hostModRoot, rimWorldVersion, Client.CompatibilityVersion,
                ClientAbstractionsCompatibility.Version, mods, assemblies, modules, diagnostics,Client.Instance?.Settings?.DisabledExtensions);
        }

        private void Report(List<string> diagnostics, string code, Exception ex)
        {
            diagnostics.Add(code);
            log?.Invoke("Client environment: " + code + ": " + ex, LogLevel.WARNING);
        }
    }
}
