using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static void ExtensionControlRegression()
    {
        var dll=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","Fixture.Plugin.dll"));
        var document=ManagedManifest(dll);
        var module=(Dictionary<string,object>)((object[])document["modules"])[0];
        document["modules"]=new object[] { module, With(With(module,"id","test.managed.second"),"entryType","Fixture.Plugin.Second") };
        var fake=new ControlBackend(ManagedExtensionManifestReader.Read(Serialize(document)));
        var disabled=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active=new HashSet<string>(fake.Manifest.Modules.Select(m=>m.Id),StringComparer.OrdinalIgnoreCase);
        var failed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int version=0, writes=0; bool writeFails=false;
        int mainThread=Thread.CurrentThread.ManagedThreadId;
        using(var service=new ClientExtensionControlService(fake,fake,()=>Thread.CurrentThread.ManagedThreadId==mainThread,
            ()=>version,()=>disabled,(id,value)=> { writes++; if(writeFails) throw new IOException("fixture write failed");
                if(value) disabled.Add(id); else disabled.Remove(id); version++; },()=>active,()=>failed))
        {
            service.Refresh(disabled,CancellationToken.None);
            var start=service.Capture();
            var initial=Project(start);
            Assert(initial.Current==ClientPackageCurrentState.Active && initial.Next==ClientPackageNextState.Enabled && !initial.RestartPending,"F6-M current activation and next intent agree at startup.");
            long stable=start.Revision;
            service.Refresh(disabled,CancellationToken.None);
            Assert(service.Capture().Revision==stable,"F6-M unchanged inventory does not trigger a refresh loop.");
            string first=fake.Manifest.Modules[0].Id;
            var save=service.SetModuleEnabled(first,false,stable);
            var partial=service.Capture();
            Assert(save.Succeeded && partial.Revision>stable && disabled.SetEquals(new[]{first}),"F6-M module commands save only the selected flag and invalidate both consumers.");
            Assert(Project(partial).Next==ClientPackageNextState.PartiallyDisabled && Project(partial).Current==ClientPackageCurrentState.Active && Project(partial).RestartPending,"F6-M saved module disable never pretends to hot-stop an active module.");
            Assert(service.SetModuleEnabled(first,false,partial.Revision).Succeeded && writes==1 && service.Capture().Revision==partial.Revision,"F6-M repeated module command is idempotent.");
            Assert(!service.SetModuleEnabled(first,true,stable).Succeeded && disabled.Contains(first),"F6-M stale menu revision cannot overwrite newer intent.");
            service.Refresh(disabled,CancellationToken.None);
            var row=service.Capture().Inventory.Packages.Single();
            Assert(service.ChangeDesiredState(row.Package,ManagedExtensionDesiredState.Disabled,disabled,CancellationToken.None).Succeeded,"F6-M package disable delegates the existing validated command.");
            Assert(!service.Capture().InventoryKnown && Project(service.Capture()).Next==ClientPackageNextState.Unknown,"F6-M mutation invalidates package facts until refreshed.");
            service.Refresh(disabled,CancellationToken.None);
            var wholeDisabled=service.Capture();
            Assert(wholeDisabled.EffectiveDisabledModuleIds.Count==2 && wholeDisabled.DisabledModuleIds.Count==1 && Project(wholeDisabled).Next==ClientPackageNextState.Disabled,"F6-M package disable gates all modules without erasing individual selections.");
            service.ChangeDesiredState(wholeDisabled.Inventory.Packages.Single().Package,ManagedExtensionDesiredState.Enabled,disabled,CancellationToken.None);
            service.Refresh(disabled,CancellationToken.None);
            Assert(Project(service.Capture()).Next==ClientPackageNextState.PartiallyDisabled && disabled.Contains(first),"F6-M package re-enable preserves partial module choices.");
            service.SetModuleEnabled(fake.Manifest.Modules[1].Id,false,service.Capture().Revision);
            Assert(!Project(service.Capture()).CanRestoreSingleModule && Project(service.Capture()).Next==ClientPackageNextState.Disabled,"F6-M all-disabled multi-module packages require an explicit module selection.");
            ManagedFailure("ManagedStateChanged",()=>service.Refresh(new string[0],CancellationToken.None));
            ManagedFailure("ManagedStateChanged",()=>service.ChangeDesiredState(fake.Row,ManagedExtensionDesiredState.Disabled,new string[0],CancellationToken.None));
            ManagedFailure("ManagedStateChanged",()=>service.Install(null,new string[0],CancellationToken.None));
            Assert(!service.Capture().Busy,"F6-M stale operation rejection releases the shared command guard.");
            service.Refresh(disabled,CancellationToken.None);
            writeFails=true;
            var writeResult=service.SetModuleEnabled(first,true,service.Capture().Revision);
            Assert(!writeResult.Succeeded && writeResult.Code=="ManagedModuleSettingsWriteFailed" && disabled.Contains(first) && !service.Capture().InventoryKnown,"F6-M failed settings save reports failure and invalidates facts without claiming success.");
            writeFails=false; service.Refresh(disabled,CancellationToken.None);
            fake.ReadFails=true;
            try { service.Refresh(disabled,CancellationToken.None); throw new Exception("Expected read failure"); } catch(IOException) { }
            Assert(Project(service.Capture()).Next==ClientPackageNextState.Unknown && !service.SetModuleEnabled(first,true,service.Capture().Revision).Succeeded,"F6-M failed inventory reads yield unknown state and reject module writes.");
            fake.ReadFails=false; service.Refresh(disabled,CancellationToken.None);
            fake.ModuleBlock="ManagedModuleIdentityConflict"; service.Refresh(disabled,CancellationToken.None);
            Assert(service.SetModuleEnabled(first,true,service.Capture().Revision).Code==fake.ModuleBlock && disabled.Contains(first),"F6-M shared commands preserve package ownership gates.");
            fake.ModuleBlock=null; service.Refresh(disabled,CancellationToken.None);
            Assert(service.SetModuleEnabled(first,true,service.Capture().Revision).Succeeded,"F6-M successful refresh recovers command availability.");
            service.Refresh(disabled,CancellationToken.None);
            fake.ChangeEntered=new ManualResetEventSlim(); fake.ChangeRelease=new ManualResetEventSlim();
            var expected=service.Capture().Inventory.Packages.Single().Package;
            var snapshotDisabled=disabled.ToArray();
            var task=Task.Run(()=>service.ChangeDesiredState(expected,ManagedExtensionDesiredState.Disabled,snapshotDisabled,CancellationToken.None));
            try
            {
                Assert(fake.ChangeEntered.Wait(5000),"F6-M package operation entered worker backend.");
                Assert(service.Capture().Busy && service.SetModuleEnabled(first,false,service.Capture().Revision).Code=="StoreBusy","F6-M concurrent module writes are rejected while a package operation runs.");
                ManagedFailure("StoreBusy",()=>service.Install(null,snapshotDisabled,CancellationToken.None));
            }
            finally { fake.ChangeRelease.Set(); }
            Assert(task.GetAwaiter().GetResult().Succeeded && !service.Capture().Busy,"F6-M background package completion releases the guard and preserves main-thread settings.");
            fake.ChangeEntered.Dispose(); fake.ChangeRelease.Dispose(); fake.ChangeEntered=null; fake.ChangeRelease=null;
            service.Refresh(disabled,CancellationToken.None);
            fake.OnRead=()=> { fake.OnRead=null; disabled.Add(first); version++; service.Capture(); };
            ManagedFailure("ManagedStateChanged",()=>service.Refresh(disabled,CancellationToken.None));
            service.Refresh(disabled,CancellationToken.None);
            Assert(service.Capture().DisabledModuleIds.Contains(first),"F6-M a read racing newer settings never publishes obsolete inventory.");
            active.Clear(); failed.Add(first); service.Capture();
            Assert(Project(service.Capture()).Current==ClientPackageCurrentState.Failed,"F6-M module load failure stays distinct from saved package intent.");
            failed.Clear(); service.Capture();
            Assert(Project(service.Capture()).Current==ClientPackageCurrentState.Inactive,"F6-M loaded assemblies alone never imply module activation.");
            ManagedFailureOnWorker(service);
        }
        var one=ManagedExtensionManifestReader.Read(Serialize(ManagedManifest(dll)));
        var single=new ControlBackend(one);
        var oneDisabled=new[]{one.Modules[0].Id};
        var singleInventory=single.Refresh(oneDisabled,CancellationToken.None);
        var singleState=new ClientExtensionControlSnapshot(1,false,true,singleInventory,oneDisabled,new string[0],new string[0]);
        Assert(Project(singleState).CanRestoreSingleModule,"F6-M Store can restore a single disabled module without a package write.");
        single.StartupDisabled=oneDisabled;
        singleState=new ClientExtensionControlSnapshot(2,false,true,single.Refresh(oneDisabled,CancellationToken.None),oneDisabled,new string[0],new string[0]);
        Assert(!Project(singleState).RestartPending && Project(singleState).Current==ClientPackageCurrentState.Inactive,"F6-M restart-applied module disable does not retain a pending marker.");
        single.Desired=ManagedExtensionDesiredState.Disabled;
        singleState=new ClientExtensionControlSnapshot(2,false,true,single.Refresh(oneDisabled,CancellationToken.None),oneDisabled,new string[0],new string[0]);
        Assert(!Project(singleState).CanRestoreSingleModule,"F6-M overlapping package and module disable requires separate explicit commands.");
    }
    private static ClientExtensionPackageState Project(ClientExtensionControlSnapshot state)
        =>new ClientExtensionPackageState(state.Inventory.Packages.Single(),state);
    private static void ManagedFailureOnWorker(ClientExtensionControlService service)
    {
        bool rejected=Task.Run(()=> { try { service.Capture(); return false; } catch(InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
        Assert(rejected,"F6-M game settings/runtime capture is restricted to the main thread.");
    }
    private sealed class ControlBackend : IManagedExtensionManagementService,IManagedExtensionInstallationService
    {
        internal ControlBackend(ManagedExtensionManifest manifest) { Manifest=manifest; Startup=MakeRow(); }
        internal readonly ManagedExtensionManifest Manifest;
        private readonly ManagedExtensionPackageSnapshot Startup;
        internal ManagedExtensionDesiredState Desired=ManagedExtensionDesiredState.Enabled;
        internal bool ReadFails;
        internal string ModuleBlock;
        internal string[] StartupDisabled=new string[0];
        internal Action OnRead;
        internal ManualResetEventSlim ChangeEntered,ChangeRelease;
        private int operation;
        internal ManagedExtensionPackageSnapshot Row=>MakeRow();
        private ManagedExtensionPackageSnapshot MakeRow()=>new ManagedExtensionPackageSnapshot("record","test.source",Hash,Manifest.PackageId,
            "1.0.0",Hash,"catalog",Hash,Hash,"transaction",operation.ToString(),Manifest,Desired,ManagedExtensionContentState.ContentVerified,null);
        public ManagedExtensionRuntimeSnapshot Snapshot=>new ManagedExtensionRuntimeSnapshot("startup",new[]{new ManagedExtensionRuntimePackage(Startup,null,true)},new string[0]);
        public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> flags,CancellationToken token)
        {
            if(ReadFails) throw new IOException("fixture read failed");
            var copied=flags.ToArray(); OnRead?.Invoke();
            return new ManagedExtensionManagementSnapshot(new[]{new ManagedExtensionManagementPackage(Row,Snapshot.Packages.Single(),
                Manifest.Modules.All(m=>copied.Contains(m.Id))?"ManagedAllModulesDisabled":null,null,null,ModuleBlock,Manifest.Modules.Any(m=>copied.Contains(m.Id)!=StartupDisabled.Contains(m.Id)))},new string[0]);
        }
        public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState desired,IEnumerable<string> flags,CancellationToken token)
        {
            ChangeEntered?.Set(); if(ChangeRelease!=null && !ChangeRelease.Wait(5000)) throw new TimeoutException();
            if(expected.StateOperationId!=operation.ToString()) return new ManagedExtensionStateChangeResult(false,"ManagedStateChanged",Row,null);
            Desired=desired; operation++; return new ManagedExtensionStateChangeResult(true,"ManagedStateSaved",Row,operation.ToString());
        }
        public ManagedExtensionInstallResult Install(ManagedExtensionInstallRequest request,IEnumerable<string> flags,CancellationToken token)=>throw new NotSupportedException();
    }
}
