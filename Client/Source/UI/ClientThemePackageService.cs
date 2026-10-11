using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;
using Utils.Framework.Themes;
using Verse;

namespace PhinixClient
{
    internal sealed class ClientThemePackageService : IClientThemePackageService,IDisposable
    {
        private readonly UiTheme theme;
        private readonly string saveRoot;
        private readonly Action<string> log;
        private ManagedThemePackageStore store;
        private IReadOnlyList<InstalledThemePackage> packages=new InstalledThemePackage[0];
        private IReadOnlyList<string> diagnostics=new string[0];
        internal ClientThemePackageService(UiTheme theme,string saveRoot,Action<string> log)
        {
            this.theme=theme;this.saveRoot=saveRoot;this.log=log;
            try { store=new ManagedThemePackageStore(saveRoot,theme.CompletePackageRemoval); Refresh(); }
            catch(Exception error) { SetFailure(error); }
        }
        public IReadOnlyList<InstalledThemePackage> Packages => packages;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        private static void MainThread() { if(!UnityData.IsInMainThread) throw new InvalidOperationException("Theme package mutation requires main thread."); }
        public void Refresh()
        {
            MainThread(); if(store==null) return;
            var inventory=store.Refresh(CancellationToken.None);packages=inventory.Packages;diagnostics=inventory.Diagnostics;
            foreach(var diagnostic in diagnostics) log?.Invoke(diagnostic);
            theme.Refresh();
        }
        public ThemePackage Inspect(string absolutePath)
        {
            MainThread(); RequireStore();
            absolutePath=ClientEnvironmentPaths.NormalizeAbsolute(absolutePath);
            using(var input=new FileStream(absolutePath,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var package=ThemePackage.Read(input,CancellationToken.None);
                if(!package.Manifest.PhinixRange.Contains(ManagedExtensionVersion.Parse(Client.CompatibilityVersion))) throw new ManagedExtensionValidationException("CandidateHostIncompatible");
                return package;
            }
        }
        public void Install(ThemePackage package,string expectedInstalledArtifact)
        {
            MainThread();RequireStore();
            try {store.Install(package,expectedInstalledArtifact,Client.CompatibilityVersion,CheckDllConflict,CancellationToken.None);}
            catch
            {
                Refresh();
                if(diagnostics.Count==0 && packages.Any(p=>p.ArtifactSha256==package.Sha256 && p.Manifest.PackageId==package.Manifest.PackageId)) return;
                throw;
            }
            Refresh();
        }
        private void CheckDllConflict(string id)
        {
            var inventory=ManagedExtensionInventoryReader.Read(new ManagedExtensionPaths(saveRoot),CancellationToken.None);
            if(inventory.Diagnostics.Count!=0 || inventory.Packages.Any(p=>p.DiagnosticCode!=null)) throw new ManagedExtensionValidationException("ManagedInventoryUncertain");
            if(inventory.Packages.Any(p=>p.PackageId==id)) throw new ManagedExtensionValidationException("ManagedInstallPackageConflict");
        }
        public void Uninstall(string packageId,string expectedInstalledArtifact)
        {
            MainThread();RequireStore();
            try {store.Remove(packageId,expectedInstalledArtifact,theme.CapturePackageRemovalIntent(packageId),CancellationToken.None);}
            catch
            {
                Refresh();
                if(diagnostics.Count==0 && !packages.Any(p=>p.Manifest.PackageId==packageId)) return;
                throw;
            }
            Refresh();
        }
        internal IEnumerable<ThemeSource> Sources()
        {
            foreach(var package in packages)
            {
                if(!package.Manifest.PhinixRange.Contains(ManagedExtensionVersion.Parse(Client.CompatibilityVersion))) continue;
                foreach(var variant in package.Manifest.Themes)
                    yield return new ThemeSource {Id=package.Manifest.ThemeIdentity(variant),Source=package.SourceId,
                        Document=package.CopyDocument(variant.Path),Name=variant.Name,Author=package.Manifest.Author};
            }
        }
        private void RequireStore() {if(store==null) throw new InvalidOperationException(string.Join("\n",diagnostics));}
        private void SetFailure(Exception error) {diagnostics=Array.AsReadOnly(new[]{(error as ManagedExtensionValidationException)?.Code??"ThemeStoreUnavailable"});log?.Invoke(diagnostics[0]);}
        public void Dispose() {store?.Dispose();store=null;packages=new InstalledThemePackage[0];}
    }
}
