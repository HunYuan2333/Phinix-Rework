using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed partial class ManagedPluginStoreView
    {
        private readonly ManagedStoreController controller;
        private readonly IClientEnvironmentService environment;
        private readonly IClientSettingsContext settings;
        private readonly IClientExtensionManagementWindowService management;
        private readonly IClientExtensionControlService controls;
        private ClientExtensionControlSnapshot controlState;
        private long observedControlRevision=-1, renderedControlRevision=-1;
        private readonly IClientLocalizer localizer;
        private readonly IClientTheme themeSource;
        private readonly IClientLinkService links;
        private readonly StoreMaintainerRegistry maintainers;
        private readonly StoreBadgeIcons badgeIcons;
        private string linkNotice;
        private long observedLocalRevision=-1;
        private bool localImportActive;
        private readonly HashSet<string> updateIds = new HashSet<string>(StringComparer.Ordinal);
        private ManagedExtensionManagementPackage selectedLocal;
        private int filter;
        private int cachedFilter = -1;
        private string progressCaption="", progressBytes="", resultCaption="";
        private readonly List<DetailSection> sections = new List<DetailSection>();
        private sealed class DetailSection { internal string Title, Body; }
        private string locale;
        private string search="",error;
        private StoreFailureInfo localFailure;
        private RepositoryAccessMethod accessMethod;
        private ManagedStoreEntry selected;
        private ManagedStoreEntryGroup[] rows=new ManagedStoreEntryGroup[0];
        private ManagedStoreInstallIntent installIntent;
        private ManagedStoreSnapshot cached;
        private string cachedSearch,detail="",status="";
        private object language;
        private bool initialized;
        private bool renderedInventoryKnown;
        private bool InventoryKnown => controlState?.InventoryKnown??controller.InventoryKnown;
        private bool stopped;
        private LocalPackageFileWindow localWindow;
        private LocalThemePackagesWindow themeWindow;
        private readonly IClientThemePackageService themes;
        private readonly IClientThemeWindowService themeWindows;
        internal void Stop()
        {
            ClosePresentation(); stopped=true; installIntent=null;
            if(themeWindow?.IsOpen==true) themeWindow.Close();themeWindow=null;
            var closing=localWindow; localWindow=null;
            if(closing != null && closing.IsOpen) closing.Close();
        }
        internal ManagedPluginStoreView(ManagedStoreController controller,IClientEnvironmentService environment,IClientSettingsContext settings,
            IClientExtensionManagementWindowService management,IClientLocalizer localizer,IClientTheme theme,
            IClientLinkService links,StoreMaintainerRegistry maintainers,StoreBadgeIcons badgeIcons,IClientExtensionControlService controls=null,IClientThemePackageService themes=null,IClientThemeWindowService themeWindows=null)
        {
            this.themes=themes;this.themeWindows=themeWindows;
            this.controller=controller; this.environment=environment; this.settings=settings; this.management=management; this.localizer=localizer; this.themeSource=theme;
            this.links=links; this.badgeIcons=badgeIcons; this.maintainers=maintainers; this.controls=controls;
            accessMethod=settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub;
        }
        private static string T(string key) { return ("Phinix_store2_"+key).Translate(); }
        private static string Clean(string value) { return (value??"").Replace("<","‹").Replace(">","›"); }
        internal void Draw(Rect rect) { if (!stopped) presentation?.Draw(rect); }
        private ManagedStoreSnapshot AdvanceOwner()
        {
                var snapshot=controller.Snapshot;
                if(localImportActive && !Prefs.DevMode)
                { controller.Cancel(); controller.DiscardLocal(snapshot); localImportActive=false; snapshot=controller.Snapshot; }
                if(snapshot.LocalPackage!=null && observedLocalRevision!=snapshot.Revision)
                {
                    observedLocalRevision=snapshot.Revision;
                    if(localImportActive && Prefs.DevMode && controller.ClaimLocalConfirmation(snapshot)) ConfirmLocalInstall(snapshot);
                }
                if(!snapshot.Busy && (snapshot.State==ManagedStoreState.Failed || snapshot.State==ManagedStoreState.Canceled || snapshot.State==ManagedStoreState.Installed)) localImportActive=false;
                controlState=controls?.Capture();
                if(controlState!=null && !snapshot.Busy && snapshot.LocalPackage==null && !controlState.Busy && observedControlRevision!=controlState.Revision)
                {
                    observedControlRevision=controlState.Revision;
                    if(snapshot.Code!="ManagedStateSavedRefreshFailed" && snapshot.Code!="ManagedInstallSavedRefreshFailed")
                        Act(()=>controller.RefreshInventory(environment.Capture()));
                    snapshot=controller.Snapshot;
                }
                if(!initialized && !snapshot.Busy && snapshot.LocalPackage==null && controlState?.Busy!=true) { initialized=true; if(snapshot.Catalog==null) Refresh(false); snapshot=controller.Snapshot; }
                var ready=installIntent?.Take(snapshot,selected?.CatalogRecord);
                if(ready!=null)
                {
                    if(ManagedStoreInstallIntent.NeedsConfirmation(ready)) pendingPlan = ready;
                    else Act(()=>controller.Download(ready,environment.Capture(),true));
                    snapshot=controller.Snapshot;
                }
                return snapshot;
        }
        private static string Bytes(long bytes)
        { return bytes<1024?bytes+" B":bytes<1024*1024?(bytes/1024d).ToString("0.0")+" KiB":(bytes/(1024d*1024)).ToString("0.0")+" MiB"; }
        private void ToolbarAction(int action)
        {
            if(controller.Snapshot.Busy && action!=3) return;
            if(action==0) Refresh(false);
            else if(action==2) Act(()=>management.OpenExtensionManagerWindow());
            else if(action==3)
            {
                if(stopped || themes==null || themeWindows==null || themeWindow?.IsOpen==true) return;
                Act(()=> {themeWindow=new LocalThemePackagesWindow(presentationOwner,renderer,points,actionPoint,themes,themeWindows,()=>!stopped);Find.WindowStack.Add(themeWindow);});
            }
            else if(action==4)
            {
                if(!Prefs.DevMode || stopped) return;
                if(localWindow != null && localWindow.IsOpen) return;
                localWindow = new LocalPackageFileWindow(presentationOwner, renderer, points, actionPoint, () => !stopped, path=>Act(()=>
                {
                    if(stopped || !Prefs.DevMode) return;
                    installIntent=null; localImportActive=true;
                    controller.PrepareLocal(path,environment.Capture(),Prefs.DevMode);
                }));
                Find.WindowStack.Add(localWindow);
            }
            else Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                new FloatMenuOption(T("accessGithub"),()=>SwitchAccess(RepositoryAccessMethod.GitHub)),
                new FloatMenuOption(T("accessCloudflare"),()=>SwitchAccess(RepositoryAccessMethod.Cloudflare)) }));
        }
        private ManagedExtensionManagementSnapshot SharedInventory(ManagedStoreSnapshot snapshot)
            => controlState!=null?controlState.Inventory:snapshot.Inventory;
        private void Rebuild(ManagedStoreSnapshot snapshot)
        {
            if(renderedInventoryKnown==InventoryKnown && renderedControlRevision==(controlState?.Revision??-1) && cached==snapshot && cachedSearch==search && cachedFilter==filter && ReferenceEquals(language,LanguageDatabase.activeLanguage) && locale==localizer.Locale) return;
            bool changed=cached?.Catalog!=snapshot.Catalog;
            bool controlsChanged=renderedControlRevision!=(controlState?.Revision??-1); renderedControlRevision=controlState?.Revision??-1;
            bool contentChanged=renderedInventoryKnown!=InventoryKnown || controlsChanged || cached==null || changed || cached.Inventory!=snapshot.Inventory || cached.Repository!=snapshot.Repository || cachedSearch!=search || cachedFilter!=filter || !ReferenceEquals(language,LanguageDatabase.activeLanguage) || locale!=localizer.Locale;
            renderedInventoryKnown=InventoryKnown;
            cached=snapshot; cachedSearch=search; cachedFilter=filter; language=LanguageDatabase.activeLanguage; locale=localizer.Locale;
            if(contentChanged)
            {
                updateIds.Clear(); foreach(var p in controller.Updates) updateIds.Add(p.Id);
                var listing=ManagedStoreListing.BuildEntries(snapshot.Catalog,SharedInventory(snapshot),snapshot.Repository?.Endpoint,search,locale);
                rows=listing.Where(g=>filter==0 || filter==1 && g.Preferred.Installed!=null || filter==2 && !g.Preferred.IsInstalledOnly && updateIds.Contains(g.Preferred.Id)).ToArray();
                var previous=selected;
                selected=ManagedStoreListing.RestoreEntry(rows,previous);
                if(previous!=null && (selected==null || selected.CatalogRecord!=previous.CatalogRecord)) installIntent=null;
                selectedLocal=selected?.Installed;
                detail=BuildDetail(snapshot);
                resultCaption=string.Format(T("resultCount"),rows.Length);
            }
            string key="state_"+snapshot.State; status=T(key);
            if(snapshot.State==ManagedStoreState.Failed) status=FailureMessage(snapshot);
            if(snapshot.Code=="ManagedStateSaved") status=T("stateSaved");
            if(snapshot.Code=="ManagedStateSavedRefreshFailed" || snapshot.Code=="ManagedInstallSavedRefreshFailed") status=T("savedRefreshFailed");
            if(snapshot.Code=="LocalInstallSaved") status=T("localInstallSaved");
            if(snapshot.Code=="LocalPayloadVerified") status=T("localPayloadVerified");
            if(snapshot.Code=="ManagedStoreCanceled") status=T("state_Canceled");
            if(snapshot.Repository?.Offline==true || snapshot.Repository?.Stale==true) status+=" · "+T("offlineNotice");
            if(!InventoryKnown && SharedInventory(snapshot)!=null) status+=" · "+T("inventoryUnknown");
            if(snapshot.Progress!=null)
            {
                var p=snapshot.Progress;
                progressCaption=T("progress"+p.Stage);
                if(p.Package!=null) progressCaption+=" · "+Clean(p.Package.DisplayName(locale))+" · "+p.Index+"/"+p.Count;
                progressBytes=string.Format(T("progressBytes"),Bytes(p.Received),Bytes(p.Total));
                if(p.Total>0) progressBytes+=" · "+(p.Fraction*100).ToString("0")+"%";
            }
        }
        private string BuildDetail(ManagedStoreSnapshot snapshot)
        {
            sections.Clear();
            if(selected==null) return T("choose");
            sections.Add(new DetailSection { Title=T("about"), Body=Clean(selected.DisplaySummary(locale)) });
            if(!selected.IsWorkshop) sections.Add(new DetailSection { Title=T("changelog"), Body=Clean(selected.DisplayChangelog(locale)??T("noChangelog")) });
            var b=new StringBuilder();
            b.AppendLine("RimWorld "+Clean(string.Join(", ",selected.RimWorldVersions)));
            if(selected.IsWorkshop) b.AppendLine(T("workshopNotice"));
            else if(selected.Manifest!=null)
            {
                b.AppendLine("Phinix "+Clean(selected.Manifest.Compatibility.PhinixRange.Text));
                foreach(var dependency in selected.Manifest.Dependencies) b.AppendLine(T("dependency")+" "+Clean(dependency.PackageId)+" "+dependency.VersionRange.Text+(dependency.Optional?" · "+T("optional"):""));
                foreach(var mod in selected.Manifest.ExternalMods) b.AppendLine(T("externalMod")+" "+Clean(mod.PackageId));
            }
            sections.Add(new DetailSection { Title=T("compatibility"), Body=b.ToString().TrimEnd() });
            if(selected.IsInstalledOnly)
                sections.Add(new DetailSection { Title=T("installationSource"), Body=T(selected.IsLocalDevelopment?"localBadgeTip":"installedOnlyTip")+"\n"+Clean(selected.Id)+"\nSHA-256: "+Clean(selectedLocal.Package.ArtifactSha256) });
            if(selectedLocal!=null && !selected.IsWorkshop)
            {
                b.Clear();
                b.AppendLine(T("installedVersion")+" "+Clean(selectedLocal.Package.Version));
                var projected=controlState==null?null:new ClientExtensionPackageState(selectedLocal,controlState);
                b.AppendLine(T("installedState")+" "+(projected==null?T("desired_"+selectedLocal.Package.DesiredState):PackageStateText(projected)));
                if(projected!=null) b.AppendLine(T("currentState")+" "+T("current_"+projected.Current));
                if(!InventoryKnown) b.AppendLine(T("inventoryUnknown"));
                else if(!selected.IsInstalledOnly && ManagedExtensionVersion.Parse(selectedLocal.Package.Version).CompareTo(selected.Manifest.Version)>0) b.AppendLine(T("downgradeUnavailable"));
                else if(!selected.IsInstalledOnly && selectedLocal.Package.DesiredState!=ManagedExtensionDesiredState.Enabled && ManagedExtensionVersion.Parse(selectedLocal.Package.Version).CompareTo(selected.Manifest.Version)<0) b.AppendLine(T("enableBeforeUpdate"));
                if(selectedLocal.Package.DiagnosticCode!=null) b.AppendLine(Clean(selectedLocal.Package.DiagnosticCode));
                sections.Add(new DetailSection { Title=T("installation"), Body=b.ToString().TrimEnd() });
            }
            if(!selected.IsInstalledOnly && selected.State!="active") sections.Add(new DetailSection { Title=T("availability"), Body=T("withdrawn") });
            if(selected.Tags.Any()) sections.Add(new DetailSection { Title=T("tags"), Body=Clean(string.Join(" · ",selected.Tags)) });
            return "";
        }
        private string PackageStateText(ClientExtensionPackageState state)
        { return T("next_"+state.Next)+" · "+T("moduleChoices")+" "+state.EnabledModuleCount+"/"+state.ModuleCount+(state.RestartPending?" · "+T("restartRequired"):""); }
        private static string LocalDetail(LocalIdentityDiagnostic issue)
        { return T("localMod")+" "+Clean(issue.ModId)+"\n"+T("localFile")+" "+Clean(issue.RelativePath)+"\n"+T("localReason")+" "+Clean(issue.Reason); }
        private static string ReferenceDetail(ManagedExtensionAssemblyReferenceFailure issue)
        { return T("referencingAssembly")+" "+Clean(issue.ReferencingAssembly)+"\n"+T("requiredReference")+" "+Clean(issue.RequiredReference)+"\n"+T("availableReferences")+" "+(issue.AvailableReferences.Count==0?T("referenceMissing"):Clean(string.Join("; ",issue.AvailableReferences))); }
        private void BeginInstall(ManagedStoreSnapshot snapshot, bool replace)
        {
            if(selected?.CatalogRecord==null || !InventoryKnown) return;
            Act(()=> { controller.Plan(selected.CatalogRecord,environment.Capture(),snapshot.Catalog,replace); installIntent=new ManagedStoreInstallIntent(snapshot.Catalog,selected.CatalogRecord); });
        }
        private void SetEnabled(ManagedExtensionManagementPackage local,bool enable,long? expectedRevision=null)
        {
            var state=controls?.Capture();
            if(!(state?.InventoryKnown??controller.InventoryKnown)) return;
            if(state!=null && (expectedRevision??controlState?.Revision)!=state.Revision)
            { Act(()=> { throw new StoreValidationException("ManagedStateChanged","Module state changed; review again."); }); return; }
            var projected=state==null?null:new ClientExtensionPackageState(local,state);
            if(enable && projected?.CanRestoreSingleModule==true)
            {
                Act(()=>
                {
                    string id=local.Package.Manifest.Modules[0].Id;
                    var result=controls.SetModuleEnabled(id,true,state.Revision);
                    if(!result.Succeeded) throw new StoreValidationException(result.Code,"Module state refused.");
                    Log.Message("[Phinix] Managed single-module intent saved; restart required: "+id);
                });
                return;
            }
            Act(()=>controller.Change(local.Package,enable?ManagedExtensionDesiredState.Enabled:ManagedExtensionDesiredState.Disabled,environment.Capture()));
        }
        private void CancelRemoval(ManagedExtensionManagementPackage local)
        { if(InventoryKnown) Act(()=>controller.Change(local.Package,ManagedExtensionDesiredState.Disabled,environment.Capture())); }
        private void ConfirmLocalInstall(ManagedStoreSnapshot prepared)
        {
            // Explicit review stays in the public page. Closing a view does not discard staged state.
            pendingLocalConfirmation = prepared; presentationDirty = true;
        }
        private RepositoryEndpoint SelectedEndpoint()
        {
            return new RepositoryEndpoint(RepositoryProfile.Official,accessMethod);
        }
        private void SwitchAccess(RepositoryAccessMethod method)
        {
            if(controller.Snapshot.Busy || accessMethod==method) return;
            installIntent=null;
            accessMethod=method; Refresh(false);
        }
        private void Refresh(bool offline)
        {
            Act(()=> {
                var entry=SelectedEndpoint();
                installIntent=null;
                settings.Set("plugin-store.officialAccessMethod",entry.AccessMethod==RepositoryAccessMethod.GitHub?"github":"cloudflare");
                controller.Refresh(entry,environment.Capture(),offline);
            });
        }
        private void OpenWorkshop()
        {
            Act(()=> {
                var result=links.Open(selected.CatalogRecord.WorkshopUrl);
                if(result==ClientLinkOpenResult.Unavailable) { localFailure=null; error=T("linkUnavailable"); }
                else linkNotice=T(result==ClientLinkOpenResult.GameBrowserRequested?"gameBrowserRequested":"externalBrowserRequested");
            });
        }
        private void Act(Action action)
        {
            try { error=null; localFailure=null; linkNotice=null; action(); }
            catch(Exception ex) { localFailure=controller.ReportFailure(ex); error=localFailure.Diagnostic; }
        }
        private void Select(ManagedStoreEntry row,ManagedStoreSnapshot snapshot)
        { installIntent=null; selected=row; cached=null; Rebuild(snapshot); error=null; localFailure=null; linkNotice=null; }
        private static string FailureMessage(ManagedStoreSnapshot snapshot)
        {
            var failure=StoreFailureInfo.FromCode(snapshot.Code,snapshot.RequestId,snapshot.ContextReasons);
            return T((snapshot.ReferenceFailure!=null || snapshot.LocalIdentity!=null) && failure.Scope==StoreFailureScope.Unexpected?"compatibilityFriendly":failure.MessageKey);
        }
        private string ErrorDetails(ManagedStoreSnapshot snapshot)
        {
            var b=new StringBuilder(); b.AppendLine(error??StoreFailureInfo.FromCode(snapshot.Code,snapshot.RequestId,snapshot.ContextReasons).Diagnostic);
            var local=localFailure?.LocalIdentity??(error==null?snapshot.LocalIdentity:null);
            var reference=localFailure?.ReferenceFailure??(error==null?snapshot.ReferenceFailure:null);
            if(local!=null) b.AppendLine(LocalDetail(local));
            if(reference!=null) b.AppendLine(ReferenceDetail(reference));
            return b.ToString();
        }
    }

}
