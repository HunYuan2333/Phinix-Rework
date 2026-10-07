using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace PhinixClient.Framework
{
    internal sealed class ManagedExtensionManagerView : IDisposable
    {
        private sealed class Row
        { internal ManagedExtensionManagementPackage Model; internal string Title, State, Modules, Diagnostic, Tooltip, Toggle, Removal; }
        private readonly ManagedExtensionManagerController controller=new ManagedExtensionManagerController(
            error=>{ if(Prefs.DevMode) Log.Warning("[Phinix] Managed manager internal diagnostic: "+error); });
        private readonly List<Row> rows=new List<Row>();
        private Vector2 scroll;
        private int cachedVersion=-1, settingsVersion=-1;
        private float cachedWidth=-1f;
        private object language;
        private bool initialized, disposed;
        private string refreshLabel, status, shortStatus, empty, confirm, confirmTitle, moduleLabel;
        internal bool Busy => controller.Busy;
        internal void Update() { controller.Poll(); }
        public void Draw(Rect rect)
        {
            var service=Client.Instance?.ManagedExtensionManagement;
            var settings=Client.Instance?.Settings;
            if(!initialized) { initialized=true; settingsVersion=settings?.SettingsVersion??0; controller.Refresh(service,settings?.DisabledExtensions); }
            Update();
            int version=settings?.SettingsVersion??0;
            if(!controller.Busy && version!=settingsVersion) { settingsVersion=version; controller.Refresh(service,settings?.DisabledExtensions); }
            float width=Mathf.Max(0f,rect.width-16f);
            if(cachedVersion!=controller.Version || cachedWidth!=width || !ReferenceEquals(language,LanguageDatabase.activeLanguage))
            { Rebuild(width); cachedVersion=controller.Version; cachedWidth=width; language=LanguageDatabase.activeLanguage; }
            bool oldEnabled=UnityEngine.GUI.enabled, oldWrap=Text.WordWrap; GameFont oldFont=Text.Font; Color oldColor=UnityEngine.GUI.color;
            try
            {
                Text.WordWrap=false; Text.Font=GameFont.Small;
                float top=Mathf.Min(32f,Mathf.Max(0f,rect.height));
                UnityEngine.GUI.enabled=oldEnabled && !controller.Busy;
                if(Widgets.ButtonText(new Rect(rect.x,rect.y,Mathf.Min(110f,Mathf.Max(0f,rect.width)),top),refreshLabel)) controller.Refresh(service,settings?.DisabledExtensions);
                UnityEngine.GUI.enabled=oldEnabled;
                Rect statusRect=new Rect(rect.x+Mathf.Min(116f,rect.width),rect.y,Mathf.Max(0f,rect.width-116f),top);
                Widgets.Label(statusRect,shortStatus); TooltipHandler.TipRegion(statusRect,status);
                Rect viewport=new Rect(rect.x,rect.y+top+6f,Mathf.Max(0f,rect.width),Mathf.Max(0f,rect.height-top-6f));
                if(viewport.height<=0f) return;
                if(rows.Count==0)
                { Text.WordWrap=true; Widgets.Label(viewport,empty); return; }
                const float stride=ManagedExtensionManagerLayout.CardHeight+6f;
                float height=rows.Count*stride;
                float contentWidth=Mathf.Max(0f,viewport.width-16f);
                scroll.y=Mathf.Clamp(scroll.y,0f,Mathf.Max(0f,height-viewport.height));
                Widgets.BeginScrollView(viewport,ref scroll,new Rect(0f,0f,contentWidth,Mathf.Max(height,viewport.height)));
                try
                {
                    var range=VirtualListLayout.GetFixedRange(rows.Count,stride,scroll.y,viewport.height,1);
                    for(int i=range.FirstIndex;i<range.EndIndexExclusive;i++) DrawRow(new Rect(0f,i*stride,contentWidth,ManagedExtensionManagerLayout.CardHeight),rows[i],service,settings);
                }
                finally { Widgets.EndScrollView(); }
            }
            finally { UnityEngine.GUI.enabled=oldEnabled; Text.WordWrap=oldWrap; Text.Font=oldFont; UnityEngine.GUI.color=oldColor; }
        }
        private void DrawRow(Rect rect,Row row,IManagedExtensionManagementService service,Settings settings)
        {
            Widgets.DrawBoxSolid(rect,new Color(0.12f,0.12f,0.12f,0.5f));
            var layout=ManagedExtensionManagerLayout.Card(rect);
            Widgets.Label(layout.Title,row.Title); Widgets.Label(layout.State,row.State);
            Text.Font=GameFont.Tiny; Widgets.Label(layout.Modules,row.Modules); Widgets.Label(layout.Diagnostic,row.Diagnostic); Text.Font=GameFont.Small;
            TooltipHandler.TipRegion(rect,row.Tooltip);
            bool oldEnabled=UnityEngine.GUI.enabled;
            try
            {
                var model=row.Model;
                UnityEngine.GUI.enabled=oldEnabled && !controller.Busy && model.ModuleSettingsBlockCode==null;
                if(Widgets.ButtonText(layout.ModuleButton,moduleLabel))
                {
                    var options=new List<FloatMenuOption>();
                    foreach(var module in model.Package.Manifest.Modules)
                    {
                        string id=module.Id; bool disabled=settings?.IsExtensionDisabled(id)==true;
                        options.Add(new FloatMenuOption((disabled?"Phinix_managed_enable":"Phinix_managed_disable").Translate()+" · "+id,()=>
                        {
                            if(disposed || controller.Busy || settings==null) return;
                            settings.SetExtensionDisabled(id,!disabled); settings.AcceptChanges();
                            Log.Message("[Phinix] Managed module intent saved; restart required: "+id);
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                if(model.ModuleSettingsBlockCode!=null) TooltipHandler.TipRegion(layout.ModuleButton,Reason(model.ModuleSettingsBlockCode));
                bool enable=model.Package.DesiredState!=ManagedExtensionDesiredState.Enabled;
                string block=enable?model.EnableBlockCode:model.DisableBlockCode;
                UnityEngine.GUI.enabled=oldEnabled && !controller.Busy && block==null;
                if(Widgets.ButtonText(layout.Toggle,row.Toggle)) controller.Change(service,model.Package,enable?ManagedExtensionDesiredState.Enabled:ManagedExtensionDesiredState.Disabled,settings?.DisabledExtensions);
                bool undo=model.Package.DesiredState==ManagedExtensionDesiredState.PendingRemoval;
                UnityEngine.GUI.enabled=oldEnabled && !controller.Busy && (undo?model.DisableBlockCode:model.RemovalBlockCode)==null;
                if(Widgets.ButtonText(layout.Removal,row.Removal))
                {
                    if(undo) controller.Change(service,model.Package,ManagedExtensionDesiredState.Disabled,settings?.DisabledExtensions);
                    else Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirm.Translate(Safe(model.Package.Manifest?.Name??model.Package.PackageId)),
                        ()=>{ if(!disposed) controller.Change(service,model.Package,ManagedExtensionDesiredState.PendingRemoval,Client.Instance?.Settings?.DisabledExtensions); },false,confirmTitle));
                }
                if(block!=null) TooltipHandler.TipRegion(layout.Toggle,Reason(block));
                if(!undo && model.RemovalBlockCode!=null) TooltipHandler.TipRegion(layout.Removal,Reason(model.RemovalBlockCode));
            }
            finally { UnityEngine.GUI.enabled=oldEnabled; }
        }
        private void Rebuild(float width)
        {
            var settings=Client.Instance?.Settings;
            refreshLabel="Phinix_managed_refresh".Translate(); empty="Phinix_managed_empty".Translate();
            moduleLabel="Phinix_managed_moduleActions".Translate();
            confirm="Phinix_managed_confirmRemoval"; confirmTitle="Phinix_managed_remove".Translate();
            status=Reason(controller.MessageCode??"ManagedInventoryReady");
            var snapshot=controller.Snapshot;
            if(snapshot?.Diagnostics.Count>0) status+=" · "+string.Join(", ",snapshot.Diagnostics);
            shortStatus=Short(status,Mathf.Max(0f,width-100f));
            rows.Clear();
            if(snapshot==null) return;
            var sorted=snapshot.Packages.OrderBy(p=>p.Package.PackageId,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.Package.SourceId,StringComparer.Ordinal);
            foreach(var model in sorted)
            {
                var p=model.Package;
                string title=Safe(p.Manifest?.Name??p.PackageId??p.RecordKey??"?")+" · "+Safe(p.Version??"?")+" · "+Safe(p.SourceId??"?");
                string current=model.Current?.AssembliesLoaded==true?"Phinix_managed_loaded".Translate().ToString():"Phinix_managed_unloaded".Translate().ToString();
                string state="Phinix_managed_state".Translate(current,Desired(p.DesiredState)).ToString()+(model.RestartPending || model.ModulesRestartPending?" · "+"Phinix_managed_restartPending".Translate().ToString():"");
                int disabledCount=p.Manifest?.Modules.Count(m=>settings?.IsExtensionDisabled(m.Id)==true)??0;
                state+=" · "+"Phinix_managed_moduleIntent".Translate(disabledCount,p.Manifest?.Modules.Count??0);
                string modules=p.Manifest==null?"":string.Join(", ",p.Manifest.Modules.Select(m=>m.Id));
                string diagnostic=p.DiagnosticCode??model.Current?.DiagnosticCode;
                string diagnosticText=diagnostic==null?"Phinix_managed_verified".Translate().ToString():Reason(diagnostic);
                string tooltip=title+"\n"+Safe(p.PackageId)+"\n"+state+"\n"+modules+"\n"+diagnosticText+"\n"+"Phinix_managed_restartExplanation".Translate();
                if(model.EnableBlockCode!=null) tooltip+="\n"+"Phinix_managed_enable".Translate()+": "+Reason(model.EnableBlockCode);
                if(model.DisableBlockCode!=null) tooltip+="\n"+"Phinix_managed_disable".Translate()+": "+Reason(model.DisableBlockCode);
                if(model.RemovalBlockCode!=null) tooltip+="\n"+"Phinix_managed_remove".Translate()+": "+Reason(model.RemovalBlockCode);
                rows.Add(new Row {Model=model,Title=Short(title,width-12f),State=Short(state,width-12f),Modules=Short(modules,width-86f),Diagnostic=Short(diagnosticText,width-12f),Tooltip=tooltip,
                    Toggle=(p.DesiredState==ManagedExtensionDesiredState.Enabled?"Phinix_managed_disable":"Phinix_managed_enable").Translate(),
                    Removal=(p.DesiredState==ManagedExtensionDesiredState.PendingRemoval?"Phinix_managed_cancelRemoval":"Phinix_managed_remove").Translate()});
            }
        }
        private static string Desired(ManagedExtensionDesiredState value)
        { return ("Phinix_managed_desired_"+value).Translate(); }
        private static string Reason(string code)
        {
            switch(code)
            {
                case "ManagedOperationRunning": return "Phinix_managed_working".Translate();
                case "ManagedInventoryReady": return "Phinix_managed_ready".Translate();
                case "ManagedStateSaved": case "ManagedStateUnchanged": return "Phinix_managed_saved".Translate();
                case "ManagedStateSavedRefreshFailed": return "Phinix_managed_savedRefreshFailed".Translate();
                case "CandidateNotEnabled": return "Phinix_managed_startupDisabled".Translate();
                case "ManagedAllModulesDisabled": return "Phinix_managed_allModulesDisabled".Translate()+" ("+code+")";
                case "CandidatePackageDependencyUnavailable": case "ManagedModuleDependencyUnavailable": case "ManagedDependencyRejected": return "Phinix_managed_missingDependency".Translate()+" ("+code+")";
                case "CandidateHostIncompatible": return "Phinix_managed_incompatible".Translate()+" ("+code+")";
                case "ManagedStateWriteUncertain": return "Phinix_managed_writeUncertain".Translate();
                case "ManagedHasDependents": case "ManagedHostHasDependents": case "ManagedHostAssemblyDependent": return "Phinix_managed_dependents".Translate()+" ("+code+")";
                case "ManagedStateChanged": return "Phinix_managed_stale".Translate();
                case "ManagedTransactionPending": return "Phinix_managed_transaction".Translate();
                case "ManagedOperationCanceled": return "Phinix_managed_canceled".Translate();
                default: return "Phinix_managed_diagnostic".Translate(code);
            }
        }
        private static string Safe(string text) { return (text??"").Replace('<','＜').Replace('>','＞'); }
        private static string Short(string text,float width)
        {
            if(width<=0f) return ""; if(Text.CalcSize(text).x<=width) return text;
            int low=0,high=text.Length;
            while(low<high) { int mid=low+(high-low+1)/2; if(Text.CalcSize(text.Substring(0,mid)+"…").x<=width) low=mid; else high=mid-1; }
            return text.Substring(0,low)+"…";
        }
        public void Dispose() { disposed=true; controller.Dispose(); rows.Clear(); }
    }
}
