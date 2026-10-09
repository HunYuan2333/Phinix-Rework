using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    internal sealed partial class ManagedStoreController
    {
        private readonly Func<CancellationToken,Task<bool>> captureDeveloperMode;
        private long localConfirmationRevision=-1;
        internal bool ClaimLocalConfirmation(ManagedStoreSnapshot prepared)
        {
            lock(gate)
            {
                if(!ReferenceEquals(snapshot,prepared) || prepared.LocalPackage==null || prepared.Busy || localConfirmationRevision==prepared.Revision) return false;
                localConfirmationRevision=prepared.Revision; return true;
            }
        }
        internal void DiscardLocal(ManagedStoreSnapshot prepared)
        {
            lock(gate)
            {
                if(!ReferenceEquals(snapshot,prepared) || snapshot.Busy || snapshot.LocalPackage==null) return;
                operation.ResetRepository();
                snapshot=new ManagedStoreSnapshot(snapshot.Revision+1,operation.State,snapshot.Catalog,snapshot.Repository,null,snapshot.Inventory,"ManagedStoreCanceled");
            }
        }
        private async Task RequireDeveloperMode(CancellationToken token)
        {
            if(captureDeveloperMode==null || !await AwaitOperation(captureDeveloperMode,token,"EnvironmentCaptureTimeout").ConfigureAwait(false))
                throw Error("DeveloperModeRequired");
            token.ThrowIfCancellationRequested();
        }
        internal Task PrepareLocal(string path,ClientEnvironmentSnapshot environment,bool developerMode)
        {
            if(!developerMode) throw Error("DeveloperModeRequired");
            RequireEnvironment(environment);
            string selected=ClientEnvironmentPaths.NormalizeAbsolute(path);
            if(!string.Equals(Path.GetExtension(selected),".zip",StringComparison.OrdinalIgnoreCase)) throw Error("InvalidArchive");
            return Run(ManagedStoreState.Downloading,async token=>
            {
                await RequireDeveloperMode(token).ConfigureAwait(false);
                var zip=StageLocal(selected,environment,token);
                var inventory=Inventory(environment,token);
                LocalReplacement(zip,inventory);
                await RequireDeveloperMode(token).ConfigureAwait(false);
                return new OperationResult(new ManagedStoreSnapshot(0,ManagedStoreState.Verified,null,null,null,inventory,"LocalPayloadVerified",localPackage:zip),false);
            },new RepositoryDiagnostics(log,ManagedExtensionInstallPackage.LocalDevelopmentSourceId));
        }
        internal Task InstallLocal(ManagedStoreSnapshot prepared,ClientEnvironmentSnapshot environment,bool developerMode)
        {
            if(!developerMode) throw Error("DeveloperModeRequired");
            if(prepared?.LocalPackage==null || !ReferenceEquals(prepared,Snapshot)) throw Error("ManagedStateChanged");
            var package=prepared.LocalPackage.LocalInstallation(LocalReplacement(prepared.LocalPackage,prepared.Inventory));
            return Run(ManagedStoreState.Installing,async token=>
            {
                environment=await RefreshEnvironment(environment,token).ConfigureAwait(false);
                await RequireDeveloperMode(token).ConfigureAwait(false);
                if(installation==null) throw Error("IncompleteEnvironment");
                lock(gate) inventoryKnown=false;
                var result=installation.Install(new ManagedExtensionInstallRequest(new[]{package},RevalidateLocalAuthorization),environment.DisabledModuleIds,token);
                if(!result.Succeeded) throw new StoreValidationException(result.Code,"Local installation rejected.") {ReferenceFailure=result.ReferenceFailure};
                return CompleteCommit(ManagedStoreState.Installed,null,null,null,environment,"LocalInstallSaved");
            },new RepositoryDiagnostics(log,ManagedExtensionInstallPackage.LocalDevelopmentSourceId));
        }
        private void RevalidateLocalAuthorization(CancellationToken token)
        {
            try { RequireDeveloperMode(token).GetAwaiter().GetResult(); }
            catch(StoreValidationException ex)
            {
                if(ex.Code=="DeveloperModeRequired") throw new OperationCanceledException("Local import authorization was revoked.");
                throw new ManagedExtensionValidationException(ex.Code,ex);
            }
        }
        private static ManagedExtensionPackageSnapshot LocalReplacement(ManagedExtensionZip zip,ManagedExtensionManagementSnapshot inventory)
        {
            var existing=inventory.Packages.Where(p=>p.Package.PackageId==zip.Manifest.PackageId).ToArray();
            if(existing.Length==0) return null;
            if(existing.Length!=1 || !existing[0].Package.IsLocalDevelopment) throw Error("LocalOfficialPackageConflict");
            var old=existing[0].Package;
            if(old.ArtifactSha256==zip.Sha256) throw Error("LocalPackageAlreadyInstalled");
            if(zip.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(old.Version))<0) throw Error("LocalPackageDowngrade");
            zip.LocalInstallation(old);
            return old;
        }
        private static ManagedExtensionZip StageLocal(string selected,ClientEnvironmentSnapshot environment,CancellationToken token)
        {
            string root=Path.Combine(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"),"local-import");
            NoLocalLinks(root); Directory.CreateDirectory(root); NoLocalLinks(root);
            string temporary=Path.Combine(root,Guid.NewGuid().ToString("N")+".zip");
            try
            {
                using(var input=new FileStream(selected,FileMode.Open,FileAccess.Read,FileShare.Read))
                using(var staged=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
                {
                    long length=input.Length;
                    if(length<1 || length>ManagedExtensionZip.MaxPackageBytes) throw Error("PayloadLimit");
                    byte[] buffer=new byte[65536]; long total=0; int count;
                    while((count=input.Read(buffer,0,buffer.Length))>0)
                    {
                        token.ThrowIfCancellationRequested(); total+=count;
                        if(total>length) throw Error("PayloadSizeMismatch");
                        staged.Write(buffer,0,count);
                    }
                    if(total!=length) throw Error("PayloadSizeMismatch");
                    staged.Flush(true); staged.Position=0;
                    return ManagedExtensionZip.Read(staged,token,length);
                }
            }
            finally { NoLocalLinks(temporary); if(File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void NoLocalLinks(string path)
        {
            for(string current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            {
                try { if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw Error("ManagedPathLink"); }
                catch(FileNotFoundException) { } catch(DirectoryNotFoundException) { }
            }
        }
    }
}
