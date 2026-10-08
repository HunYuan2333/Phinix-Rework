using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient.Framework
{
    /// <summary>Single host-owned command boundary. Borrowed runtime services are never disposed here.</summary>
    internal sealed class ClientExtensionControlService : IClientExtensionControlService,
        IManagedExtensionManagementService, IManagedExtensionInstallationService, IDisposable
    {
        private readonly object gate=new object();
        private readonly IManagedExtensionManagementService management;
        private readonly IManagedExtensionInstallationService installation;
        private readonly Func<bool> isMainThread;
        private readonly Func<int> settingsVersion;
        private readonly Func<IEnumerable<string>> readDisabled, readActive, readFailed;
        private readonly Action<string,bool> saveModule;
        private readonly Action<Exception> diagnostic;
        private string[] disabled,active=new string[0],failed=new string[0];
        private int settingsSeen, busy;
        private long revision;
        private bool known,disposed;
        private ManagedExtensionManagementSnapshot inventory;
        private string inventorySignature;
        private ClientExtensionControlSnapshot cached;

        internal ClientExtensionControlService(IManagedExtensionManagementService management,
            IManagedExtensionInstallationService installation, Func<bool> isMainThread, Func<int> settingsVersion,
            Func<IEnumerable<string>> readDisabled, Action<string,bool> saveModule,
            Func<IEnumerable<string>> readActive, Func<IEnumerable<string>> readFailed, Action<Exception> diagnostic=null)
        {
            this.management=management??throw new ArgumentNullException(nameof(management));
            this.installation=installation??throw new ArgumentNullException(nameof(installation));
            this.isMainThread=isMainThread; this.settingsVersion=settingsVersion; this.readDisabled=readDisabled;
            this.saveModule=saveModule; this.readActive=readActive; this.readFailed=readFailed; this.diagnostic=diagnostic;
            RequireMainThread(); settingsSeen=settingsVersion(); disabled=Copy(readDisabled());
        }
        public ManagedExtensionRuntimeSnapshot Snapshot => management.Snapshot;
        private static string[] Copy(IEnumerable<string> ids) => (ids??Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        private static bool Same(IEnumerable<string> a,IEnumerable<string> b) => new HashSet<string>(a,StringComparer.OrdinalIgnoreCase).SetEquals(b);
        private void RequireMainThread() { if(!isMainThread()) throw new InvalidOperationException("Extension controls require the main thread."); }
        private void Changed() { revision++; cached=null; }
        public ClientExtensionControlSnapshot Capture()
        {
            RequireMainThread();
            int version=settingsVersion();
            var running=Copy(readActive()); var errors=Copy(readFailed());
            lock(gate)
            {
                if(version!=settingsSeen) { settingsSeen=version; disabled=Copy(readDisabled()); Changed(); }
                if(!Same(active,running) || !Same(failed,errors)) { active=running; failed=errors; Changed(); }
                if(cached==null || cached.Busy!=(Volatile.Read(ref busy)!=0))
                    cached=new ClientExtensionControlSnapshot(revision,Volatile.Read(ref busy)!=0,known,inventory,disabled,active,failed);
                return cached;
            }
        }
        public ClientExtensionControlResult SetModuleEnabled(string id,bool enabled,long expectedRevision)
        {
            RequireMainThread(); var state=Capture();
            if(disposed) return new ClientExtensionControlResult(false,"ManagedManagementUnavailable");
            if(string.IsNullOrWhiteSpace(id)) return new ClientExtensionControlResult(false,"InvalidModuleId");
            if(state.Revision!=expectedRevision) return new ClientExtensionControlResult(false,"ManagedStateChanged");
            if(Interlocked.CompareExchange(ref busy,1,0)!=0) return new ClientExtensionControlResult(false,"StoreBusy");
            try
            {
                lock(gate)
                {
                    if(disposed) return new ClientExtensionControlResult(false,"ManagedManagementUnavailable");
                    if(revision!=expectedRevision) return new ClientExtensionControlResult(false,"ManagedStateChanged");
                }
                if(!state.InventoryKnown) return new ClientExtensionControlResult(false,"ManagedInventoryUnavailable");
                var owner=state.FindModuleOwner(id);
                if(owner!=null && owner.ModuleSettingsBlockCode!=null) return new ClientExtensionControlResult(false,owner.ModuleSettingsBlockCode);
                if(state.DisabledModuleIds.Contains(id,StringComparer.OrdinalIgnoreCase)==!enabled)
                    return new ClientExtensionControlResult(true,"ManagedStateUnchanged");
                saveModule(id,!enabled);
                Capture();
                return new ClientExtensionControlResult(true,"ManagedStateSaved");
            }
            catch(Exception error)
            {
                lock(gate) { known=false; Changed(); }
                try { diagnostic?.Invoke(error); } catch { }
                return new ClientExtensionControlResult(false,"ManagedModuleSettingsWriteFailed");
            }
            finally { lock(gate) { Volatile.Write(ref busy,0); cached=null; } }
        }
        private string[] Input(IEnumerable<string> expected,out long capturedRevision)
        {
            lock(gate)
            {
                if(disposed) throw new ManagedExtensionValidationException("ManagedManagementUnavailable");
                if(!Same(disabled,expected??Enumerable.Empty<string>())) throw new ManagedExtensionValidationException("ManagedStateChanged");
                capturedRevision=revision; return (string[])disabled.Clone();
            }
        }
        public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> expected,CancellationToken token)
        {
            long captured; var input=Input(expected,out captured);
            try
            {
                var result=management.Refresh(input,token);
                string signature=string.Join("|",result.Packages.OrderBy(p=>p.Package.RecordKey,StringComparer.Ordinal).Select(p=>
                    p.Package.RecordKey+":"+p.Package.Version+":"+p.Package.ManifestSha256+":"+p.Package.InstallationTransactionId+":"+
                    p.Package.RepositoryIdentitySha256+":"+p.Package.ContentState+":"+p.Package.StateOperationId+":"+p.Package.DesiredState+":"+p.Package.DiagnosticCode+":"+
                    p.Current?.DiagnosticCode+":"+p.Current?.AssembliesLoaded+":"+p.EnableBlockCode+":"+p.DisableBlockCode+":"+
                    p.RemovalBlockCode+":"+p.ModuleSettingsBlockCode+":"+p.ModulesRestartPending)) + "|"+string.Join(",",result.Diagnostics);
                lock(gate)
                {
                    if(captured!=revision) throw new ManagedExtensionValidationException("ManagedStateChanged");
                    inventory=result;
                    if(!known || signature!=inventorySignature) { inventorySignature=signature; known=true; Changed(); }
                    else cached=null;
                }
                return result;
            }
            catch(OperationCanceledException) { throw; }
            catch(ManagedExtensionValidationException error) when(error.Code=="ManagedStateChanged") { throw; }
            catch
            {
                lock(gate) { if(known) { known=false; Changed(); } }
                throw;
            }
        }
        private void Begin(IEnumerable<string> expected,out string[] input)
        {
            if(Interlocked.CompareExchange(ref busy,1,0)!=0) throw new ManagedExtensionValidationException("StoreBusy");
            try { long ignored; input=Input(expected,out ignored); }
            catch { Volatile.Write(ref busy,0); throw; }
        }
        private void End()
        { lock(gate) { known=false; Changed(); Volatile.Write(ref busy,0); } }
        public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,
            ManagedExtensionDesiredState desired,IEnumerable<string> disabledModules,CancellationToken token)
        {
            string[] input; Begin(disabledModules,out input);
            try { return management.ChangeDesiredState(expected,desired,input,token); }
            finally { End(); }
        }
        public ManagedExtensionInstallResult Install(ManagedExtensionInstallRequest request,IEnumerable<string> disabledModules,CancellationToken token)
        {
            string[] input; Begin(disabledModules,out input);
            try { return installation.Install(request,input,token); }
            finally { End(); }
        }
        public void Dispose() { lock(gate) { disposed=true; known=false; Changed(); } }
    }
}
