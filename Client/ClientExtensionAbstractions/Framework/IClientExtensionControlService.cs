using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient.Framework
{
    /// <summary>Host-owned settings and package facts. Commands save next-start intent, never hot-toggle modules.</summary>
    public interface IClientExtensionControlService
    {
        ClientExtensionControlSnapshot Capture();
        ClientExtensionControlResult SetModuleEnabled(string moduleId, bool enabled, long expectedRevision);
    }

    public sealed class ClientExtensionControlResult
    {
        public ClientExtensionControlResult(bool succeeded, string code) { Succeeded=succeeded; Code=code; }
        public bool Succeeded { get; }
        public string Code { get; }
    }

    public sealed class ClientExtensionControlSnapshot
    {
        public ClientExtensionControlSnapshot(long revision, bool busy, bool inventoryKnown,
            ManagedExtensionManagementSnapshot inventory, IEnumerable<string> disabledModules,
            IEnumerable<string> activeModules, IEnumerable<string> failedModules)
        {
            Revision=revision; Busy=busy; InventoryKnown=inventoryKnown; Inventory=inventory;
            DisabledModuleIds=Freeze(disabledModules); ActiveModuleIds=Freeze(activeModules); FailedModuleIds=Freeze(failedModules);
            var effective=new HashSet<string>(DisabledModuleIds,StringComparer.OrdinalIgnoreCase);
            if(inventoryKnown && inventory!=null)
                foreach(var package in inventory.Packages.Where(p=>p.Package.DesiredState!=ManagedExtensionDesiredState.Enabled))
                    if(package.Package.Manifest!=null) foreach(var module in package.Package.Manifest.Modules) effective.Add(module.Id);
            EffectiveDisabledModuleIds=Freeze(effective);
        }
        private static ReadOnlyCollection<string> Freeze(IEnumerable<string> values)
            => Array.AsReadOnly((values??Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        public long Revision { get; }
        public bool Busy { get; }
        public bool InventoryKnown { get; }
        public ManagedExtensionManagementSnapshot Inventory { get; }
        public ReadOnlyCollection<string> DisabledModuleIds { get; }
        public ReadOnlyCollection<string> EffectiveDisabledModuleIds { get; }
        public ReadOnlyCollection<string> ActiveModuleIds { get; }
        public ReadOnlyCollection<string> FailedModuleIds { get; }
        public ManagedExtensionManagementPackage FindModuleOwner(string id)
            => Inventory?.Packages.FirstOrDefault(p=>p.Package.Manifest?.Modules.Any(m=>string.Equals(m.Id,id,StringComparison.OrdinalIgnoreCase))==true);
    }

    public enum ClientPackageNextState { Unknown, Enabled, PartiallyDisabled, Disabled, PendingRemoval }
    public enum ClientPackageCurrentState { Unknown, Inactive, Active, PartiallyActive, Failed }

    /// <summary>One projection consumed by both package UIs; assembly loading is not module activation.</summary>
    public sealed class ClientExtensionPackageState
    {
        public ClientExtensionPackageState(ManagedExtensionManagementPackage package, ClientExtensionControlSnapshot controls)
        {
            if(package==null) throw new ArgumentNullException(nameof(package));
            var modules=package.Package.Manifest?.Modules;
            ModuleCount=modules?.Count??0;
            if(controls==null || !controls.InventoryKnown || modules==null)
            { Next=ClientPackageNextState.Unknown; Current=ClientPackageCurrentState.Unknown; return; }
            EnabledModuleCount=modules.Count(m=>!controls.DisabledModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase));
            ActiveModuleCount=modules.Count(m=>controls.ActiveModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase));
            bool failed=modules.Any(m=>controls.FailedModuleIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase)) ||
                package.Package.DiagnosticCode!=null || package.Current?.DiagnosticCode!=null &&
                package.Current.DiagnosticCode!="CandidateNotEnabled" && package.Current.DiagnosticCode!="ManagedAllModulesDisabled";
            Current=failed?ClientPackageCurrentState.Failed:ActiveModuleCount==0?ClientPackageCurrentState.Inactive:
                ActiveModuleCount==ModuleCount?ClientPackageCurrentState.Active:ClientPackageCurrentState.PartiallyActive;
            Next=package.Package.DesiredState==ManagedExtensionDesiredState.PendingRemoval?ClientPackageNextState.PendingRemoval:
                package.Package.DesiredState==ManagedExtensionDesiredState.Unknown?ClientPackageNextState.Unknown:
                package.Package.DesiredState!=ManagedExtensionDesiredState.Enabled || EnabledModuleCount==0?ClientPackageNextState.Disabled:
                EnabledModuleCount==ModuleCount?ClientPackageNextState.Enabled:ClientPackageNextState.PartiallyDisabled;
            RestartPending=package.RestartPending || package.ModulesRestartPending ||
                (Next==ClientPackageNextState.Disabled && ActiveModuleCount>0) ||
                (!failed && (Current==ClientPackageCurrentState.Active || Current==ClientPackageCurrentState.PartiallyActive) &&
                    (Next==ClientPackageNextState.Enabled || Next==ClientPackageNextState.PartiallyDisabled) && ActiveModuleCount!=EnabledModuleCount);
            // Restoring one module in an already-enabled package changes only one settings store.
            CanRestoreSingleModule=ModuleCount==1 && EnabledModuleCount==0 &&
                package.Package.DesiredState==ManagedExtensionDesiredState.Enabled && package.ModuleSettingsBlockCode==null;
        }
        public ClientPackageNextState Next { get; }
        public ClientPackageCurrentState Current { get; }
        public int ModuleCount { get; }
        public int EnabledModuleCount { get; }
        public int ActiveModuleCount { get; }
        public bool RestartPending { get; }
        public bool CanRestoreSingleModule { get; }
    }
}
