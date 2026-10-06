using System;
using System.Linq;
using System.Text;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class ManagedPluginStoreView
    {
        private readonly ManagedStoreController controller;
        private readonly IClientEnvironmentService environment;
        private readonly IClientSettingsContext settings;
        private readonly IClientExtensionManagementWindowService management;
        private readonly IClientLocalizer localizer;
        private string locale;
        private string search="",error;
        private RepositoryAccessMethod accessMethod;
        private ManagedStoreRecord selected;
        private ManagedStoreGroup[] rows=new ManagedStoreGroup[0];
        private ManagedStoreInstallIntent installIntent;
        private ManagedStoreSnapshot cached;
        private string cachedSearch,detail="",status="";
        private object language;
        private Vector2 listScroll,detailScroll;
        private float detailHeight;
        private float measuredWidth=-1;
        private bool initialized;
        internal ManagedPluginStoreView(ManagedStoreController controller,IClientEnvironmentService environment,IClientSettingsContext settings,
            IClientExtensionManagementWindowService management,IClientLocalizer localizer)
        {
            this.controller=controller; this.environment=environment; this.settings=settings; this.management=management; this.localizer=localizer;
            accessMethod=settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub;
        }
        private static string T(string key) { return ("Phinix_store2_"+key).Translate(); }
        private static string Clean(string value) { return (value??"").Replace("<","‹").Replace(">","›"); }
        internal void Draw(Rect rect)
        {
            var font=Text.Font; var anchor=Text.Anchor; var wrap=Text.WordWrap; var color=GUI.color; bool enabled=GUI.enabled;
            try
            {
                Text.Font=GameFont.Small; Text.Anchor=TextAnchor.UpperLeft; Text.WordWrap=true; GUI.color=Color.white;
                var snapshot=controller.Snapshot;
                if(!initialized && !snapshot.Busy) { initialized=true; if(snapshot.Catalog==null) Refresh(false); snapshot=controller.Snapshot; }
                var ready=installIntent?.Take(snapshot,selected);
                if(ready!=null)
                {
                    if(ManagedStoreInstallIntent.NeedsConfirmation(ready)) ConfirmInstall(ready);
                    else Act(()=>controller.Download(ready,environment.Capture(),true));
                    snapshot=controller.Snapshot;
                }
                Rebuild(snapshot);
                float width=Mathf.Max(0f,rect.width),height=Mathf.Max(0f,rect.height);
                var top=new Rect(rect.x,rect.y,width,Mathf.Min(32f,height));
                float button=Mathf.Max(0,(width-8)/3f);
                GUI.enabled=enabled && !snapshot.Busy;
                if(Widgets.ButtonText(new Rect(top.x,top.y,button,top.height),T("refresh"))) Refresh(false);
                if(Widgets.ButtonText(new Rect(top.x+button+4,top.y,button,top.height),T("management"))) Act(()=>management.OpenExtensionManagerWindow());
                if(Widgets.ButtonText(new Rect(top.x+2*(button+4),top.y,button,top.height),T(accessMethod==RepositoryAccessMethod.GitHub?"accessGithub":"accessCloudflare")))
                    Find.WindowStack.Add(new FloatMenu(new System.Collections.Generic.List<FloatMenuOption> {
                        new FloatMenuOption(T("accessGithub"),()=>SwitchAccess(RepositoryAccessMethod.GitHub)),
                        new FloatMenuOption(T("accessCloudflare"),()=>SwitchAccess(RepositoryAccessMethod.Cloudflare)) }));
                GUI.enabled=enabled;
                float y=rect.y+top.height+6;
                float available=Mathf.Max(0,rect.yMax-y);
                float statusHeight=Mathf.Min(50,available);
                bool diagnostic=error!=null || snapshot.State==ManagedStoreState.Failed;
                float diagnosticWidth=diagnostic?Mathf.Min(110,width/3):0;
                var statusRect=new Rect(rect.x,y,Mathf.Max(0,width-diagnosticWidth),statusHeight); Widgets.Label(statusRect,Clean(error==null?status:T("failedFriendly"))); TooltipHandler.TipRegion(statusRect,Clean(error==null?status:T("failedFriendly")));
                if(diagnostic && Widgets.ButtonText(new Rect(rect.xMax-diagnosticWidth,y,diagnosticWidth,Mathf.Min(30,statusHeight)),T("errorDetails")))
                    Find.WindowStack.Add(new Dialog_MessageBox(Clean(ErrorDetails(snapshot)),null));
                y+=statusHeight+4;
                if(snapshot.Busy && snapshot.Progress!=null && rect.yMax-y>=52)
                {
                    var p=snapshot.Progress;
                    string caption=T("progress"+p.Stage);
                    if(p.Package!=null) caption+=" · "+Clean(p.Package.DisplayName(locale))+" "+p.Package.Manifest.Version;
                    caption+="\n"+string.Format(T("progressBytes"),Bytes(p.Received),Bytes(p.Total))+" · "+p.Index+"/"+p.Count;
                    var textRect=new Rect(rect.x,y,width,42);
                    Widgets.Label(textRect,caption); TooltipHandler.TipRegion(textRect,caption);
                    Widgets.DrawBoxSolid(new Rect(rect.x,y+44,width,6),new Color(.22f,.22f,.22f));
                    Widgets.DrawBoxSolid(new Rect(rect.x,y+44,width*p.Fraction,6),new Color(.55f,.65f,.35f));
                    y+=56;
                }
                if(snapshot.Busy && rect.yMax-y>=30 && Widgets.ButtonText(new Rect(rect.x,y,width,28),T("cancel"))) { installIntent=null; controller.Cancel(); }
                if(snapshot.Busy) y+=32;
                if(rect.yMax-y<45) return;
                float searchCaption=Mathf.Min(80,width*.25f);
                Widgets.Label(new Rect(rect.x,y,searchCaption,30),T("searchShort"));
                search=Widgets.TextField(new Rect(rect.x+searchCaption,y,Mathf.Max(0,width-searchCaption),30),search,128); TooltipHandler.TipRegion(new Rect(rect.x,y,width,30),T("search")); y+=36;
                Rect body=new Rect(rect.x,y,width,Mathf.Max(0,rect.yMax-y));
                var layout=ManagedStoreLayout.Calculate(body);
                DrawList(layout.List,snapshot,enabled);
                DrawDetail(layout.Detail,snapshot,enabled);
            }
            finally { Text.Font=font; Text.Anchor=anchor; Text.WordWrap=wrap; GUI.color=color; GUI.enabled=enabled; }
        }
        private static string Bytes(long bytes)
        { return bytes<1024?bytes+" B":bytes<1024*1024?(bytes/1024d).ToString("0.0")+" KiB":(bytes/(1024d*1024)).ToString("0.0")+" MiB"; }
        private void Rebuild(ManagedStoreSnapshot snapshot)
        {
            if(cached==snapshot && cachedSearch==search && ReferenceEquals(language,LanguageDatabase.activeLanguage) && locale==localizer.Locale) return;
            bool changed=cached?.Catalog!=snapshot.Catalog;
            cached=snapshot; cachedSearch=search; language=LanguageDatabase.activeLanguage; locale=localizer.Locale;
            if(changed) { selected=null; detailScroll=Vector2.zero; }
            rows=ManagedStoreListing.Build(snapshot.Catalog,search,locale);
            string key="state_"+snapshot.State; status=T(key);
            if(snapshot.State==ManagedStoreState.Failed) status=FailureMessage(snapshot);
            if(snapshot.Repository!=null) status+=" · "+T(snapshot.Repository.Endpoint.AccessMethod==RepositoryAccessMethod.GitHub?"accessGithub":"accessCloudflare");
            if(snapshot.Code=="RepositoryRateLimited" || snapshot.Code=="RepositoryUnavailable" || snapshot.Code=="RepositoryTimeout") status+="\n"+T("accessHint");
            if(snapshot.Repository?.Offline==true) status+=" · "+T("offlineNotice");
            detail=BuildDetail(snapshot); detailHeight=0;
        }
        private string BuildDetail(ManagedStoreSnapshot snapshot)
        {
            if(selected==null) return T("choose");
            var b=new StringBuilder(); b.AppendLine(Clean(selected.DisplayName(locale))); b.AppendLine(Clean(selected.Author)+" · "+Clean(selected.License));
            b.AppendLine(Clean(string.Join(" · ",selected.Tags))); b.AppendLine(); b.AppendLine(Clean(selected.DisplaySummary(locale))); b.AppendLine();
            if(selected.IsWorkshop) { b.AppendLine(T("workshopNotice")); b.AppendLine("RimWorld "+string.Join(", ",selected.RimWorldVersions)); }
            else
            {
                b.AppendLine(selected.Manifest.Version+" · "+T("managedRoute"));
                if(controller.Updates.Any(p=>p.Id==selected.Id)) b.AppendLine(T("updateAvailable"));
                b.AppendLine(T("changelog")); b.AppendLine(Clean(selected.DisplayChangelog(locale)??T("noChangelog"))); b.AppendLine();
                b.AppendLine("RimWorld "+string.Join(", ",selected.RimWorldVersions));
                b.AppendLine("Phinix "+selected.Manifest.Compatibility.PhinixRange.Text);
                var local=snapshot.Inventory?.Packages.FirstOrDefault(p=>p.Package.PackageId==selected.Id);
                if(local!=null)
                {
                    b.AppendLine(T("installedVersion")+" "+local.Package.Version);
                    b.AppendLine(T("installedState")+" "+T("desired_"+local.Package.DesiredState)+(local.RestartPending?" · "+T("restartRequired"):""));
                    if(!Owns(snapshot,local)) b.AppendLine(T("foreignInstallation"));
                    else if(ManagedExtensionVersion.Parse(local.Package.Version).CompareTo(selected.Manifest.Version)>0) b.AppendLine(T("downgradeUnavailable"));
                    else if(local.Package.DesiredState!=ManagedExtensionDesiredState.Enabled && ManagedExtensionVersion.Parse(local.Package.Version).CompareTo(selected.Manifest.Version)<0) b.AppendLine(T("enableBeforeUpdate"));
                }
                if(selected.State!="active") b.AppendLine(T("withdrawn"));
            }
            return b.ToString();
        }
        private static string LocalDetail(LocalIdentityDiagnostic issue)
        { return T("localMod")+" "+Clean(issue.ModId)+"\n"+T("localFile")+" "+Clean(issue.RelativePath)+"\n"+T("localReason")+" "+Clean(issue.Reason); }
        private static string ReferenceDetail(ManagedExtensionAssemblyReferenceFailure issue)
        { return T("referencingAssembly")+" "+Clean(issue.ReferencingAssembly)+"\n"+T("requiredReference")+" "+Clean(issue.RequiredReference)+"\n"+T("availableReferences")+" "+(issue.AvailableReferences.Count==0?T("referenceMissing"):Clean(string.Join("; ",issue.AvailableReferences))); }
        private void DrawList(Rect rect,ManagedStoreSnapshot snapshot,bool enabled)
        {
            if(rect.height<=0 || rect.width<=0) return;
            const float stride=66; float width=Mathf.Max(0,rect.width-16),height=Mathf.Max(rect.height,rows.Length*stride);
            listScroll.y=Mathf.Clamp(listScroll.y,0,Mathf.Max(0,height-rect.height));
            Widgets.BeginScrollView(rect,ref listScroll,new Rect(0,0,width,height));
            try
            {
                var range=VirtualListLayout.GetFixedRange(rows.Length,stride,listScroll.y,rect.height,1);
                for(int i=range.FirstIndex;i<range.EndIndexExclusive;i++)
                {
                    var group=rows[i]; var row=group.Preferred; Rect card=new Rect(0,i*stride,width,60);
                    if(group.Versions.Contains(selected)) Widgets.DrawBoxSolid(card,new Color(.32f,.32f,.2f,.7f));
                    else Widgets.DrawBoxSolid(card,new Color(.16f,.16f,.16f,.5f));
                    Text.WordWrap=false; Widgets.Label(new Rect(8,card.y+4,Mathf.Max(0,width-16),26),Clean(row.DisplayName(locale)));
                    Text.Font=GameFont.Tiny; Widgets.Label(new Rect(8,card.y+32,Mathf.Max(0,width-16),22),(row.IsWorkshop?T("workshop"):row.Manifest.Version.ToString())+" · "+Clean(row.Author)); Text.Font=GameFont.Small; Text.WordWrap=true;
                    GUI.enabled=enabled && !snapshot.Busy;
                    if(Widgets.ButtonInvisible(card)) Select(group.Versions.Contains(selected)?selected:row,snapshot);
                    GUI.enabled=enabled;
                    TooltipHandler.TipRegion(card,Clean(row.DisplaySummary(locale)));
                }
                if(rows.Length==0) Widgets.Label(new Rect(4,4,width,100),T(snapshot.Catalog==null?"refreshFirst":snapshot.Catalog.Packages.Count==0?"catalogEmpty":"empty"));
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawDetail(Rect rect,ManagedStoreSnapshot snapshot,bool enabled)
        {
            if(rect.height<=0 || rect.width<=0) return;
            float width=Mathf.Max(0,rect.width-16);
            if(detailHeight==0 || measuredWidth!=width) { detailHeight=Text.CalcHeight(detail,Mathf.Max(1,width)); measuredWidth=width; }
            float total=detailHeight+220;
            Widgets.BeginScrollView(rect,ref detailScroll,new Rect(0,0,width,Mathf.Max(rect.height,total)));
            try
            {
                float contentY=0;
                if(selected!=null && !selected.IsWorkshop)
                {
                    GUI.enabled=enabled && !snapshot.Busy;
                    if(Widgets.ButtonText(new Rect(0,0,width,30),T("version")+" "+selected.Manifest.Version)) ChooseVersion(snapshot);
                    contentY=38;
                }
                Widgets.Label(new Rect(0,contentY,width,detailHeight),detail); float y=contentY+detailHeight+8;
                GUI.enabled=enabled && !snapshot.Busy && selected!=null && selected.State=="active";
                if(selected?.IsWorkshop==true)
                { if(Widgets.ButtonText(new Rect(0,y,width,30),T("openWorkshop"))) Application.OpenURL(selected.WorkshopUrl); }
                else if(selected!=null)
                {
                    var local=snapshot.Inventory?.Packages.FirstOrDefault(p=>p.Package.PackageId==selected.Id);
                    if(local==null)
                    {
                        GUI.enabled=GUI.enabled && snapshot.Repository!=null && !snapshot.Repository.Offline && !snapshot.Repository.Stale;
                        if(Widgets.ButtonText(new Rect(0,y,width,30),T("install"))) Act(()=> {
                            controller.Plan(selected,environment.Capture(),snapshot.Catalog);
                            installIntent=new ManagedStoreInstallIntent(snapshot.Catalog,selected);
                        }); y+=34;
                    }
                    else if(Owns(snapshot,local))
                    {
                        if(selected.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(local.Package.Version))>0)
                        {
                            GUI.enabled=enabled && !snapshot.Busy && selected.State=="active" && local.Package.DesiredState==ManagedExtensionDesiredState.Enabled &&
                                local.Package.ContentState==ManagedExtensionContentState.ContentVerified && local.Package.DiagnosticCode==null && snapshot.Repository!=null && !snapshot.Repository.Offline && !snapshot.Repository.Stale;
                            if(Widgets.ButtonText(new Rect(0,y,width,30),T("update"))) Act(()=>
                            {
                                controller.Plan(selected,environment.Capture(),snapshot.Catalog,true);
                                installIntent=new ManagedStoreInstallIntent(snapshot.Catalog,selected);
                            }); y+=34;
                        }
                        bool enable=local.Package.DesiredState!=ManagedExtensionDesiredState.Enabled;
                        GUI.enabled=enabled && !snapshot.Busy && (enable?local.EnableBlockCode:local.DisableBlockCode)==null;
                        if(Widgets.ButtonText(new Rect(0,y,width,30),T(enable?"enable":"disable"))) Act(()=>controller.Change(local.Package,enable?ManagedExtensionDesiredState.Enabled:ManagedExtensionDesiredState.Disabled,environment.Capture())); y+=34;
                        GUI.enabled=enabled && !snapshot.Busy && local.RemovalBlockCode==null;
                        if(Widgets.ButtonText(new Rect(0,y,width,30),T("uninstall")))
                        {
                            var expected=local.Package;
                            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T("confirmUninstall"),()=>Act(()=>controller.Change(expected,ManagedExtensionDesiredState.PendingRemoval,environment.Capture()))));
                        }
                        y+=34;
                        if(local.RemovalBlockCode!=null || (enable?local.EnableBlockCode:local.DisableBlockCode)!=null)
                        {
                            GUI.enabled=enabled;
                            if(Widgets.ButtonText(new Rect(0,y,width,30),T("managementDetails")))
                                Find.WindowStack.Add(new Dialog_MessageBox(Clean(T("blockedFriendly")+"\n\n"+local.EnableBlockCode+"\n"+local.DisableBlockCode+"\n"+local.RemovalBlockCode),null));
                        }
                    }
                }
            }
            finally { Widgets.EndScrollView(); GUI.enabled=enabled; }
        }
        private void ConfirmInstall(ManagedStorePlan plan)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T(plan.ReplacesPackages?"updateConfirmation":"dependencyConfirmation")+"\n\n"+string.Join("\n",plan.Items.Select(i=>Clean(i.Package.DisplayName(locale))+" "+i.Package.Manifest.Version+" · "+T(i.Installed==null?"newPackage":i.RequiresDownload?"updatedPackage":"existingPackage")))+"\n\n"+T("downloadSize")+" "+(plan.DownloadBytes/1024d/1024d).ToString("F2")+" MiB",()=>
            { if(controller.Snapshot.Plan!=plan || controller.Snapshot.Busy || selected!=plan.Root) { error=T("reviewAgain"); return; } Act(()=>controller.Download(plan,environment.Capture(),true)); }));
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
        private void Act(Action action) { try { error=null; action(); } catch(Exception ex) { error=T("failed")+" "+((ex as StoreValidationException)?.Code??ex.GetType().Name); } }
        private bool Owns(ManagedStoreSnapshot snapshot,ManagedExtensionManagementPackage local)
        {
            return snapshot.Inventory.Packages.Count(p=>p.Package.PackageId==selected.Id)==1 &&
                local.Package.SourceId==snapshot.Catalog.SourceId && local.Package.RepositoryIdentitySha256==snapshot.Repository?.Endpoint.IdentityKey;
        }
        private void Select(ManagedStoreRecord row,ManagedStoreSnapshot snapshot)
        { installIntent=null; selected=row; detail=BuildDetail(snapshot); detailHeight=0; detailScroll=Vector2.zero; error=null; }
        private void ChooseVersion(ManagedStoreSnapshot snapshot)
        {
            var versions=snapshot.Catalog.Packages.Where(p=>p.Id==selected.Id).OrderByDescending(p=>p.Manifest.Version);
            Find.WindowStack.Add(new FloatMenu(versions.Select(p=>new FloatMenuOption(p.Manifest.Version+ (p.State=="active"?"":" · "+T("withdrawn")),()=> {
                if(controller.Snapshot.Catalog==snapshot.Catalog && !controller.Snapshot.Busy) Select(p,controller.Snapshot);
            })).ToList()));
        }
        private static string FailureMessage(ManagedStoreSnapshot snapshot)
        {
            if(snapshot.Code=="RepositoryRateLimited") return T("rateLimitedFriendly");
            if(snapshot.Code=="RepositoryUnavailable" || snapshot.Code=="RepositoryTimeout") return T("networkFriendly");
            if(snapshot.ReferenceFailure!=null || snapshot.LocalIdentity!=null) return T("compatibilityFriendly");
            return T("failedFriendly");
        }
        private string ErrorDetails(ManagedStoreSnapshot snapshot)
        {
            var b=new StringBuilder(); b.AppendLine(error??snapshot.Code);
            if(snapshot.LocalIdentity!=null) b.AppendLine(LocalDetail(snapshot.LocalIdentity));
            if(snapshot.ReferenceFailure!=null) b.AppendLine(ReferenceDetail(snapshot.ReferenceFailure));
            if(snapshot.RequestId!=null) b.AppendLine(T("request")+" "+snapshot.RequestId);
            return b.ToString();
        }
    }

}
