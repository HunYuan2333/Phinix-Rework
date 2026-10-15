using System;
using System.Threading;
using System.Threading.Tasks;
using Utils.Framework.Themes;

namespace Phinix.PluginStore
{
    // Repository work only. Host package mutation is claimed once by the main-thread view.
    internal sealed class ThemeStoreController : IDisposable
    {
        private readonly object gate=new object();
        private readonly Func<RepositoryAccessMethod,CancellationToken,bool,Task<ThemeStoreCatalog>> metadata;
        private readonly Func<RepositoryAccessMethod,ThemeStoreCatalog,ThemeStoreRecord,CancellationToken,Task<byte[]>> payload;
        internal ThemeStoreController(Func<RepositoryAccessMethod,CancellationToken,bool,Task<ThemeStoreCatalog>> metadata=null,
            Func<RepositoryAccessMethod,ThemeStoreCatalog,ThemeStoreRecord,CancellationToken,Task<byte[]>> payload=null)
        {this.metadata=metadata;this.payload=payload;}
        private Task<ThemeStoreCatalog> Read(RepositoryAccessMethod method,CancellationToken token,bool fresh=false)
            => metadata==null?ReadLive(method,token,fresh):metadata(method,token,fresh);
        private CancellationTokenSource running;
        private bool disposed,downloading;
        private ThemeStoreCatalog catalog;
        private ThemePackage ready;
        private string expected;
        private long revision;
        private string error;
        internal ThemeStoreCatalog Catalog {get {lock(gate)return catalog;}}
        internal long Revision {get {lock(gate)return revision;}}
        internal bool Busy {get {lock(gate)return running!=null;}}
        internal bool Installing {get {lock(gate)return running!=null && downloading;}}
        internal string Error {get {lock(gate)return error;}}
        internal ThemePackage TakeReady(out string expectedInstalled)
        {lock(gate) {expectedInstalled=expected;var value=ready;ready=null;return value;}}
        internal void Refresh(RepositoryAccessMethod method) => Start(async token=>
        {
            var value=await Read(method,token).ConfigureAwait(false);
            lock(gate) {token.ThrowIfCancellationRequested();catalog=value;revision++;}
        });
        internal void Download(RepositoryAccessMethod method,ThemeStoreCatalog snapshot,ThemeStoreRecord record,string expectedInstalled,string hostVersion)
        {
            if(snapshot==null || snapshot!=Catalog || record==null || !snapshot.Packages.Contains(record) || record.State!="active")
                throw new StoreValidationException("SnapshotChanged","Select a current active theme.");
            if(!record.Manifest.PhinixRange.Contains(Utils.Framework.ManagedExtensions.ManagedExtensionVersion.Parse(hostVersion)))
                throw new StoreValidationException("CandidateHostIncompatible","Update the main mod before installing this theme.");
            Start(async token=>
            {
                var endpoint=new RepositoryEndpoint(RepositoryProfile.Themes,method);
                byte[] bytes;
                if(payload!=null) bytes=await payload(method,snapshot,record,token).ConfigureAwait(false);
                else if(method==RepositoryAccessMethod.GitHub)
                    using(var access=new GitHubRepositoryAccess()) bytes=await access.DownloadTheme(endpoint,snapshot,record,token).ConfigureAwait(false);
                else using(var transport=new RepositoryTransport()) bytes=await transport.DownloadTheme(endpoint,snapshot,record,token).ConfigureAwait(false);
                var package=record.Verify(bytes,token).WithOrigin(new ThemePackageOrigin(snapshot.SourceId,endpoint.IdentityKey,snapshot.SnapshotId,snapshot.Sha256));
                var fresh=await Read(method,token,true).ConfigureAwait(false);
                if(fresh.Sha256!=snapshot.Sha256 || fresh.SnapshotId!=snapshot.SnapshotId)
                    throw new StoreValidationException("SnapshotChanged","Theme publication changed during transfer; review it again.");
                lock(gate) {token.ThrowIfCancellationRequested();ready=package;expected=expectedInstalled;revision++;}
            },true);
        }
        private static async Task<ThemeStoreCatalog> ReadLive(RepositoryAccessMethod method,CancellationToken token,bool fresh)
        {
            var endpoint=new RepositoryEndpoint(RepositoryProfile.Themes,method);
            using(var access=ManagedRepositoryAccess.Create(endpoint,new RepositoryDiagnostics()))
            {
                var bytes=await access.ReadMetadata(endpoint,null,token,fresh).ConfigureAwait(false);
                if(bytes.Stable.NotModified) throw new StoreValidationException("UnexpectedNotModified","Theme publication requires complete metadata.");
                var stable=RepositoryMetadata.ReadStableCore(bytes.Stable.Body,endpoint.SourceId,4);
                endpoint.Profile.VerifyPublished(bytes.Published);
                return RepositoryMetadata.VerifyTheme(stable,bytes.Published,bytes.Catalog);
            }
        }
        private void Start(Func<CancellationToken,Task> work,bool download=false)
        {
            lock(gate)
            {
                if(disposed) throw new ObjectDisposedException(nameof(ThemeStoreController));
                if(running!=null) throw new StoreValidationException("StoreBusy","Theme repository operation is running.");
                var source=new CancellationTokenSource();source.CancelAfter(150000);running=source;downloading=download;ready=null;error=null;revision++;
                _=Task.Run(async ()=>
                {
                    try {await work(source.Token).ConfigureAwait(false);}
                    catch(Exception ex)
                    {lock(gate) {if(!disposed) error=ex is OperationCanceledException?"RepositoryTimeout":(ex as StoreValidationException)?.Code??(ex as Utils.Framework.ManagedExtensions.ManagedExtensionValidationException)?.Code??"ThemeRepositoryFailed";}}
                    finally {lock(gate) {if(ReferenceEquals(running,source)) {running=null;revision++;} source.Dispose();}}
                });
            }
        }
        public void Dispose()
        {lock(gate) {if(disposed)return;disposed=true;ready=null;try {running?.Cancel();}catch(AggregateException) {}revision++;}}
    }
}
