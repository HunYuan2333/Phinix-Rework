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
    internal sealed class ManagedPluginStoreView
    {
        private readonly ManagedStoreController controller;
        private readonly IClientEnvironmentService environment;
        private readonly IClientSettingsContext settings;
        private readonly IClientExtensionManagementWindowService management;
        private readonly IClientExtensionControlService controls;
        private ClientExtensionControlSnapshot controlState;
        private long observedControlRevision=-1, renderedControlRevision=-1;
        private ClientExtensionPackageState selectedState;
        private readonly IClientLocalizer localizer;
        private readonly IUiTheme theme;
        private readonly IClientLinkService links;
        private readonly StoreMaintainerRegistry maintainers;
        private readonly StoreBadgeIcons badgeIcons;
        private bool[] cardOfficial=new bool[0];
        private bool selectedOfficial;
        private float officialLabelWidth, managedLabelWidth, workshopLabelWidth, localLabelWidth;
        private string linkNotice;
        private readonly Rect[] toolbarRects = new Rect[4];
        private readonly float[] toolbarWidths = new float[4];
        private long observedLocalRevision=-1;
        private bool localImportActive;
        private readonly HashSet<string> updateIds = new HashSet<string>(StringComparer.Ordinal);
        private ManagedExtensionManagementPackage selectedLocal;
        private bool selectedOwned;
        private int filter;
        private int cachedFilter = -1;
        private string[] cardNames = new string[0], cardMetadata = new string[0], cardStates = new string[0], cardTips = new string[0];
        private string progressCaption="", progressBytes="", resultCaption="", authorShort="";
        private readonly List<DetailSection> sections = new List<DetailSection>();
        private sealed class DetailSection { internal string Title, Body; internal float Height; }
        private float cardWidth = -1, toolbarWidth = -1, statusWidth = -1, statusHeight;
        private float guidanceWidth=-1, guidanceHeight;
        private string measuredGuidance;
        private string measuredStatus, nameLine, authorLine;
        private float nameHeight;
        private object toolbarLanguage;
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
        private Vector2 listScroll,detailScroll;
        private float detailHeight;
        private float measuredWidth=-1;
        private bool initialized;
        private bool renderedInventoryKnown;
        private bool InventoryKnown => controlState?.InventoryKnown??controller.InventoryKnown;
        private bool stopped;
        internal void Stop() { stopped=true; installIntent=null; }
        internal ManagedPluginStoreView(ManagedStoreController controller,IClientEnvironmentService environment,IClientSettingsContext settings,
            IClientExtensionManagementWindowService management,IClientLocalizer localizer,IUiTheme theme,
            IClientLinkService links,StoreMaintainerRegistry maintainers,StoreBadgeIcons badgeIcons,IClientExtensionControlService controls=null)
        {
            this.controller=controller; this.environment=environment; this.settings=settings; this.management=management; this.localizer=localizer; this.theme=theme;
            this.links=links; this.maintainers=maintainers; this.badgeIcons=badgeIcons; this.controls=controls;
            accessMethod=settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub;
        }
        private static string T(string key) { return ("Phinix_store2_"+key).Translate(); }
        private static string Clean(string value) { return (value??"").Replace("<","‹").Replace(">","›"); }
        internal void Draw(Rect rect)
        {
            if(stopped) return;
            var font=Text.Font; var anchor=Text.Anchor; var wrap=Text.WordWrap; var color=GUI.color; bool enabled=GUI.enabled;
            try
            {
                Text.Font=GameFont.Small; Text.Anchor=TextAnchor.UpperLeft; Text.WordWrap=true; GUI.color=Color.white;
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
                    if(ManagedStoreInstallIntent.NeedsConfirmation(ready)) ConfirmInstall(ready);
                    else Act(()=>controller.Download(ready,environment.Capture(),true));
                    snapshot=controller.Snapshot;
                }
                Rebuild(snapshot);
                float width = Mathf.Max(0, rect.width), y = rect.y;
                if (width <= 0 || rect.height < 32) return;
                DrawToolbar(new Rect(rect.x, y, width, Mathf.Max(0, rect.yMax-y)), snapshot, enabled, out float toolbarHeight);
                y += toolbarHeight + 8;
                y += DrawAccessGuidance(new Rect(rect.x,y,width,Mathf.Max(0,rect.yMax-y)));
                if (rect.yMax-y < 24) return;
                y += DrawStatus(new Rect(rect.x, y, width, Mathf.Max(0, rect.yMax-y)), snapshot, enabled) + 8;
                if (rect.yMax-y < 32) return;
                float filterWidth = Mathf.Min(190, width * .4f), gap = Mathf.Min(8, width);
                float searchWidth = Mathf.Max(0, width-filterWidth-gap);
                var searchRect = new Rect(rect.x, y, searchWidth, 32);
                GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
                float clearWidth=string.IsNullOrEmpty(search)?0:Mathf.Min(28,searchWidth);
                string nextSearch = Widgets.TextField(new Rect(searchRect.x,searchRect.y,Mathf.Max(0,searchWidth-clearWidth),32), search, 128);
                if(clearWidth>0 && Widgets.ButtonText(new Rect(searchRect.xMax-clearWidth,searchRect.y,clearWidth,32),"×")) nextSearch="";
                if (string.IsNullOrEmpty(search)) {
                    GUI.color = theme.SecondaryText;
                    Text.WordWrap=false; Widgets.Label(new Rect(searchRect.x+8, searchRect.y+4, Mathf.Max(0, searchWidth-16), 24), T(searchWidth<320?"searchShort":"search")); Text.WordWrap=true;
                    GUI.color = Color.white;
                }
                TooltipHandler.TipRegion(searchRect, T("search"));
                GUI.enabled = enabled && !snapshot.Busy && controlState?.Busy!=true;
                if (Widgets.ButtonText(new Rect(rect.xMax-filterWidth, y, filterWidth, 32), T("filter"+filter))) ChooseFilter();
                GUI.enabled = enabled;
                if (nextSearch != search) { search=nextSearch; listScroll=Vector2.zero; }
                Rebuild(snapshot); // Search results update in the same GUI event.
                y += 40;
                var layout = ManagedStoreLayout.Calculate(new Rect(rect.x, y, width, Mathf.Max(0, rect.yMax-y)), selected!=null);
                DrawList(layout.List, snapshot, enabled);
                DrawDetail(layout.Detail, snapshot, enabled, layout.Compact);
            }
            finally { Text.Font=font; Text.Anchor=anchor; Text.WordWrap=wrap; GUI.color=color; GUI.enabled=enabled; }
        }
        private static string Bytes(long bytes)
        { return bytes<1024?bytes+" B":bytes<1024*1024?(bytes/1024d).ToString("0.0")+" KiB":(bytes/(1024d*1024)).ToString("0.0")+" MiB"; }
        private void DrawToolbar(Rect rect, ManagedStoreSnapshot snapshot, bool enabled, out float height)
        {
            if (toolbarWidth!=rect.width || !ReferenceEquals(toolbarLanguage, LanguageDatabase.activeLanguage))
            {
                toolbarWidth=rect.width; toolbarLanguage=LanguageDatabase.activeLanguage;
                toolbarWidths[0]=Mathf.Max(120,Text.CalcSize(T("refresh")).x+24);
                toolbarWidths[1]=Mathf.Max(160,Mathf.Max(Text.CalcSize(T("accessGithub")).x,Text.CalcSize(T("accessCloudflare")).x)+32);
                toolbarWidths[2]=Mathf.Max(140,Text.CalcSize(T("management")).x+24);
                toolbarWidths[3]=Mathf.Max(180,Text.CalcSize(T("localImport")).x+24);
            }
            // One row on desktop, wrap/overflow on smaller windows using the host-neutral helper.
            int count=Prefs.DevMode?4:3;
            var bar=ResponsiveToolbarLayout.Calculate(rect,toolbarWidths,count,1,32,6,2,40,toolbarRects);
            height=Mathf.Min(rect.height,bar.Height);
            GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
            for(int i=0;i<bar.VisibleActionCount;i++)
            {
                string text=T(i==0?"refresh":i==1?(accessMethod==RepositoryAccessMethod.GitHub?"accessGithub":"accessCloudflare"):i==2?"management":"localImport");
                if(i==1) text+=" ▾";
                if(Widgets.ButtonText(toolbarRects[i],text)) ToolbarAction(i);
                TooltipHandler.TipRegion(toolbarRects[i],i==1?T("accessExplanation")+(accessMethod==RepositoryAccessMethod.GitHub?"\n"+T("githubAccessGuidance"):""):text);
            }
            if(bar.HasOverflow && Widgets.ButtonText(bar.OverflowButtonRect,"⋯"))
            {
                var options=new List<FloatMenuOption>();
                for(int i=bar.VisibleActionCount;i<count;i++) { int action=i; options.Add(new FloatMenuOption(T(i==0?"refresh":i==1?"accessCurrent":i==2?"management":"localImport"),()=>ToolbarAction(action))); }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            GUI.enabled=enabled;
        }
        private float DrawAccessGuidance(Rect rect)
        {
            if(accessMethod!=RepositoryAccessMethod.GitHub || rect.width<=16 || rect.height<=168) return 0;
            var font=Text.Font;
            try
            {
                Text.Font=GameFont.Tiny;
                string text=T("githubAccessGuidance");
                float width=rect.width-16;
                if(guidanceWidth!=width || measuredGuidance!=text)
                { guidanceWidth=width; measuredGuidance=text; guidanceHeight=Text.CalcHeight(text,width); }
                // Keep room for status, search and plugin actions in short windows.
                if(guidanceHeight+8>rect.height-168) return 0;
                var label=new Rect(rect.x+8,rect.y,width,guidanceHeight);
                Label(label,text,theme.SecondaryText); TooltipHandler.TipRegion(label,text);
                return guidanceHeight+8;
            }
            finally { Text.Font=font; }
        }
        private void ToolbarAction(int action)
        {
            if(controller.Snapshot.Busy) return;
            if(action==0) Refresh(false);
            else if(action==2) Act(()=>management.OpenExtensionManagerWindow());
            else if(action==3)
            {
                if(!Prefs.DevMode || stopped) return;
                Find.WindowStack.Add(new LocalPackageFileWindow(path=>Act(()=>
                {
                    if(stopped || !Prefs.DevMode) return;
                    installIntent=null; localImportActive=true;
                    controller.PrepareLocal(path,environment.Capture(),Prefs.DevMode);
                })));
            }
            else Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                new FloatMenuOption(T("accessGithub"),()=>SwitchAccess(RepositoryAccessMethod.GitHub)),
                new FloatMenuOption(T("accessCloudflare"),()=>SwitchAccess(RepositoryAccessMethod.Cloudflare)) }));
        }
        private float DrawStatus(Rect rect, ManagedStoreSnapshot snapshot, bool enabled)
        {
            bool diagnostic=error!=null || snapshot.State==ManagedStoreState.Failed;
            float actionWidth=diagnostic || snapshot.Busy?Mathf.Min(120,rect.width*.3f):0;
            string text=error==null?(linkNotice??status):(localFailure!=null?T(localFailure.MessageKey):(snapshot.State==ManagedStoreState.Failed?status:error));
            float textWidth=Mathf.Max(1,rect.width-actionWidth-24);
            if(measuredStatus!=text || statusWidth!=textWidth)
            { measuredStatus=text; statusWidth=textWidth; statusHeight=Text.CalcHeight(text,textWidth); }
            float height=Mathf.Min(rect.height,Mathf.Max(36,Mathf.Min(68,statusHeight+12)));
            bool refreshFailed=snapshot.Code=="ManagedStateSavedRefreshFailed" || snapshot.Code=="ManagedInstallSavedRefreshFailed";
            Color accent=diagnostic?theme.Error:refreshFailed?theme.Warning:snapshot.State==ManagedStoreState.Installed?theme.Success:snapshot.Busy?theme.Warning:theme.SecondaryText;
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width,height),theme.Surface);
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,3,height),accent);
            var messageRect=new Rect(rect.x+10,rect.y+6,textWidth,Mathf.Max(0,height-12));
            Label(messageRect,text,theme.PrimaryText);
            if(statusHeight>messageRect.height) TooltipHandler.TipRegion(messageRect,text);
            GUI.enabled=enabled;
            if(diagnostic && Widgets.ButtonText(new Rect(rect.xMax-actionWidth-4,rect.y+4,actionWidth,Mathf.Max(0,height-8)),T("errorDetails")))
            {
                string summary=ErrorDetails(snapshot);
                Find.WindowStack.Add(new Dialog_MessageBox(Clean(summary),T("copySummary"),()=>GUIUtility.systemCopyBuffer=summary));
            }
            else if(snapshot.Busy && Widgets.ButtonText(new Rect(rect.xMax-actionWidth-4,rect.y+4,actionWidth,Mathf.Max(0,height-8)),T("cancel")))
            { installIntent=null; controller.Cancel(); }
            if(snapshot.Busy && snapshot.Progress!=null && rect.height-height>=50)
            {
                var p=snapshot.Progress;
                var labelRect=new Rect(rect.x+8,rect.y+height+4,Mathf.Max(0,rect.width-16),22);
                Text.WordWrap=false; Label(labelRect,progressCaption,theme.PrimaryText); TooltipHandler.TipRegion(labelRect,progressCaption); Text.WordWrap=true;
                Text.Font=GameFont.Tiny; Label(new Rect(rect.x+8,rect.y+height+26,Mathf.Max(0,rect.width-16),18),progressBytes,theme.SecondaryText); Text.Font=GameFont.Small;
                Widgets.DrawBoxSolid(new Rect(rect.x,rect.y+height+46,rect.width,4),theme.Separator);
                Widgets.DrawBoxSolid(new Rect(rect.x,rect.y+height+46,rect.width*p.Fraction,4),theme.Warning);
                height+=50;
            }
            return height;
        }
        private void ChooseFilter()
        {
            Find.WindowStack.Add(new FloatMenu(Enumerable.Range(0,3).Select(i=>new FloatMenuOption(T("filter"+i),()=> {
                if(controller.Snapshot.Busy) return;
                filter=i; listScroll=Vector2.zero;
            })).ToList()));
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
                Text.Font=GameFont.Tiny;
                officialLabelWidth=Text.CalcSize(T("officialBadge")).x;
                managedLabelWidth=Text.CalcSize(T("managedBadge")).x;
                workshopLabelWidth=Text.CalcSize(T("workshop")).x;
                localLabelWidth=Text.CalcSize(T("localBadge")).x;
                Text.Font=GameFont.Small;
                updateIds.Clear(); foreach(var p in controller.Updates) updateIds.Add(p.Id);
                var listing=ManagedStoreListing.BuildEntries(snapshot.Catalog,SharedInventory(snapshot),snapshot.Repository?.Endpoint,search,locale);
                rows=listing.Where(g=>filter==0 || filter==1 && g.Preferred.Installed!=null || filter==2 && !g.Preferred.IsInstalledOnly && updateIds.Contains(g.Preferred.Id)).ToArray();
                var previous=selected;
                selected=ManagedStoreListing.RestoreEntry(rows,previous);
                if(previous!=null && (selected==null || selected.CatalogRecord!=previous.CatalogRecord)) installIntent=null;
                if(previous!=null && selected==null) detailScroll=Vector2.zero;
                cardWidth=-1;
                selectedLocal=selected?.Installed;
                selectedOwned=selectedLocal!=null && InventoryKnown;
                selectedOfficial=maintainers.IsOfficial(selected?.CatalogRecord,snapshot.Catalog,snapshot.Repository?.Endpoint);
                detail=BuildDetail(snapshot); detailHeight=0;
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
            nameLine=Clean(selected.DisplayName(locale)); authorLine=Clean(selected.Author)+(selected.License==null?"":" · "+Clean(selected.License));
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
        private void Label(Rect rect, string text, Color color)
        {
            if(rect.width<=0 || rect.height<=0) return;
            GUI.color=color; Widgets.Label(rect,text); GUI.color=Color.white;
        }
        private void DrawBadges(Rect rect,bool official,bool workshop,bool local=false)
        {
            var layout=ManagedStoreBadgeLayout.Calculate(rect,official,officialLabelWidth,local?localLabelWidth:workshop?workshopLabelWidth:managedLabelWidth);
            if(official) DrawBadge(layout.Official,StoreBadgeKind.Official,layout.OfficialText,T("officialBadge"),T("officialBadgeTip"));
            DrawBadge(layout.Route,local?StoreBadgeKind.Local:workshop?StoreBadgeKind.Workshop:StoreBadgeKind.Managed,layout.RouteText,T(local?"localBadge":workshop?"workshop":"managedBadge"),T(local?"localBadgeTip":workshop?"workshopBadgeTip":"managedBadgeTip"));
        }
        private void DrawBadge(Rect rect,StoreBadgeKind kind,bool showText,string caption,string tip)
        {
            if(rect.width<=0 || rect.height<=0) return;
            var font=Text.Font; var anchor=Text.Anchor; var wrap=Text.WordWrap; var color=GUI.color;
            try
            {
                Color accent=theme.GetColor("plugin-store.badge."+kind.ToString().ToLowerInvariant());
                var surface=accent; surface.a=.12f; Widgets.DrawBoxSolid(rect,surface);
                float size=Mathf.Min(16,Mathf.Min(rect.width,rect.height));
                var icon=badgeIcons.Get(kind);
                GUI.color=accent;
                if(icon!=null) GUI.DrawTexture(new Rect(rect.x+(showText?6:(rect.width-size)/2),rect.y+(rect.height-size)/2,size,size),icon,ScaleMode.ScaleToFit);
                Text.Font=GameFont.Tiny; Text.Anchor=TextAnchor.MiddleLeft; Text.WordWrap=false;
                if(showText) Widgets.Label(new Rect(rect.x+28,rect.y,Mathf.Max(0,rect.width-32),rect.height),caption);
                else if(icon==null) { Text.Anchor=TextAnchor.MiddleCenter; Widgets.Label(rect,"·"); }
                TooltipHandler.TipRegion(rect,caption+"\n"+tip);
            }
            finally { Text.Font=font; Text.Anchor=anchor; Text.WordWrap=wrap; GUI.color=color; }
        }
        private static string Shorten(string text, float width)
        {
            text=(text??"").Replace("\n"," ").Replace("\r"," ");
            if(width<=0) return "";
            if(Text.CalcSize(text).x<=width) return text;
            if(Text.CalcSize("…").x>width) return "";
            int low=0,high=text.Length;
            while(low<high) { int mid=(low+high+1)/2; if(Text.CalcSize(text.Substring(0,mid)+"…").x<=width) low=mid; else high=mid-1; }
            // Do not split a UTF-16 surrogate pair at the truncation boundary.
            if(low>0 && char.IsHighSurrogate(text[low-1])) low--;
            return text.Substring(0,low)+"…";
        }
        private static string LocalDetail(LocalIdentityDiagnostic issue)
        { return T("localMod")+" "+Clean(issue.ModId)+"\n"+T("localFile")+" "+Clean(issue.RelativePath)+"\n"+T("localReason")+" "+Clean(issue.Reason); }
        private static string ReferenceDetail(ManagedExtensionAssemblyReferenceFailure issue)
        { return T("referencingAssembly")+" "+Clean(issue.ReferencingAssembly)+"\n"+T("requiredReference")+" "+Clean(issue.RequiredReference)+"\n"+T("availableReferences")+" "+(issue.AvailableReferences.Count==0?T("referenceMissing"):Clean(string.Join("; ",issue.AvailableReferences))); }
        private void DrawList(Rect rect,ManagedStoreSnapshot snapshot,bool enabled)
        {
            if(rect.height<=0 || rect.width<=0) return;
            Widgets.DrawBoxSolid(rect,theme.Surface);
            Text.Font=GameFont.Tiny;
            Label(new Rect(rect.x+10,rect.y+6,Mathf.Max(0,rect.width-20),Mathf.Min(22,rect.height)),resultCaption,theme.SecondaryText);
            Text.Font=GameFont.Small;
            var viewport=new Rect(rect.x,rect.y+Mathf.Min(32,rect.height),rect.width,Mathf.Max(0,rect.height-32));
            const float stride=114;
            float width=Mathf.Max(0,rect.width-16),height=Mathf.Max(viewport.height,rows.Length*stride);
            if(cardWidth!=width)
            {
                cardWidth=width; cardNames=new string[rows.Length]; cardMetadata=new string[rows.Length]; cardStates=new string[rows.Length]; cardTips=new string[rows.Length];
                cardOfficial=new bool[rows.Length];
                for(int i=0;i<rows.Length;i++)
                {
                    var row=rows[i].Preferred;
                    cardOfficial[i]=maintainers.IsOfficial(row.CatalogRecord,snapshot.Catalog,snapshot.Repository?.Endpoint);
                    cardTips[i]=Clean(row.DisplayName(locale))+"\n"+Clean(row.DisplaySummary(locale))+"\n"+
                        (cardOfficial[i]?T("officialBadge")+" · ":"")+T(row.IsLocalDevelopment?"localBadge":row.IsWorkshop?"workshop":"managedBadge")+"\n"+T(row.IsLocalDevelopment?"localBadgeTip":row.IsWorkshop?"workshopBadgeTip":"managedBadgeTip");
                    cardNames[i]=Shorten(Clean(row.DisplayName(locale)),Mathf.Max(0,width-24));
                    Text.Font=GameFont.Tiny;
                    cardMetadata[i]=Shorten((row.IsWorkshop?"":row.Version+" · ")+Clean(row.IsInstalledOnly?row.Id:row.Author),Mathf.Max(0,width-24));
                    var local=row.Installed;
                    var projected=local==null || controlState==null?null:new ClientExtensionPackageState(local,controlState);
                    cardStates[i]=Shorten(local!=null && !InventoryKnown?T("inventoryUnknown"):projected!=null?PackageStateText(projected):(local?.RestartPending==true || local?.ModulesRestartPending==true)?T("restartRequired"):!row.IsInstalledOnly && updateIds.Contains(row.Id)?T("updateBadge"):local!=null?T("desired_"+local.Package.DesiredState):row.State!="active"?T("withdrawn"):"",Mathf.Max(0,width-24));
                    Text.Font=GameFont.Small;
                }
            }
            listScroll.y=Mathf.Clamp(listScroll.y,0,Mathf.Max(0,height-viewport.height));
            Widgets.BeginScrollView(viewport,ref listScroll,new Rect(0,0,width,height));
            try
            {
                var range=VirtualListLayout.GetFixedRange(rows.Length,stride,listScroll.y,viewport.height,1);
                for(int i=range.FirstIndex;i<range.EndIndexExclusive;i++)
                {
                    var group=rows[i]; var row=group.Preferred; Rect card=new Rect(0,i*stride,width,108);
                    bool active=group.Versions.Contains(selected);
                    Widgets.DrawBoxSolid(card,active?theme.HoverHighlight:theme.Surface);
                    Widgets.DrawHighlightIfMouseover(card);
                    if(active || row.IsLocalDevelopment) Widgets.DrawBoxSolid(new Rect(0,card.y,3,card.height),row.IsLocalDevelopment?theme.GetColor("plugin-store.badge.local"):theme.Warning);
                    Text.WordWrap=false;
                    Label(new Rect(12,card.y+7,Mathf.Max(0,width-24),26),cardNames[i],theme.PrimaryText);
                    Text.Font=GameFont.Tiny;
                    Label(new Rect(12,card.y+35,Mathf.Max(0,width-24),20),cardMetadata[i],theme.SecondaryText);
                    DrawBadges(new Rect(12,card.y+57,Mathf.Max(0,width-24),24),cardOfficial[i],row.IsWorkshop,row.IsLocalDevelopment);
                    Label(new Rect(12,card.y+85,Mathf.Max(0,width-24),18),cardStates[i],!row.IsInstalledOnly && updateIds.Contains(row.Id)?theme.Warning:theme.SecondaryText);
                    Text.Font=GameFont.Small; Text.WordWrap=true;
                    GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
                    if(Widgets.ButtonInvisible(card)) Select(active?selected:row,snapshot);
                    GUI.enabled=enabled;
                    TooltipHandler.TipRegion(card,cardTips[i]);
                }
                if(rows.Length==0) Label(new Rect(12,12,Mathf.Max(0,width-24),Mathf.Max(0,viewport.height-24)),T(snapshot.Catalog==null && SharedInventory(snapshot)?.Packages.Count==0?"refreshFirst":"empty"),theme.SecondaryText);
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawDetail(Rect rect,ManagedStoreSnapshot snapshot,bool enabled,bool compact)
        {
            if(rect.height<=0 || rect.width<=0) return;
            Widgets.DrawBoxSolid(rect,theme.Surface);
            var regions=ManagedStoreLayout.DetailRegions(rect,compact,selected!=null);
            GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
            if(regions.Back.height>0 && Widgets.ButtonText(regions.Back,T("backToList")))
            { selected=null; installIntent=null; cached=null; GUI.enabled=enabled; return; }
            GUI.enabled=enabled;
            if(regions.Content.height>0)
            {
                float width=Mathf.Max(0,regions.Content.width-40);
                if(detailHeight==0 || measuredWidth!=width)
                {
                    detailHeight=selected==null?Text.CalcHeight(detail,Mathf.Max(1,width)):0; measuredWidth=width;
                    foreach(var section in sections) { section.Height=Text.CalcHeight(section.Body,Mathf.Max(1,width)); detailHeight+=section.Height+42; }
                    Text.Font=GameFont.Tiny; authorShort=Shorten(authorLine,width); Text.Font=GameFont.Small;
                    Text.Font=GameFont.Medium; nameHeight=selected==null?0:Text.CalcHeight(nameLine,Mathf.Max(1,width)); Text.Font=GameFont.Small;
                }
                float header=selected==null?0:nameHeight+110;
                float total=detailHeight+header+24;
                detailScroll.y=Mathf.Clamp(detailScroll.y,0,Mathf.Max(0,total-regions.Content.height));
                Widgets.BeginScrollView(regions.Content,ref detailScroll,new Rect(0,0,Mathf.Max(0,regions.Content.width-16),Mathf.Max(regions.Content.height,total)));
                try
                {
                    float y=12;
                    if(selected!=null)
                    {
                        Text.Font=GameFont.Medium; Label(new Rect(12,y,width,nameHeight),nameLine,theme.PrimaryText); Text.Font=GameFont.Small; y+=nameHeight+6;
                        Text.Font=GameFont.Tiny; Label(new Rect(12,y,width,22),authorShort,theme.SecondaryText); TooltipHandler.TipRegion(new Rect(12,y,width,22),authorLine); Text.Font=GameFont.Small; y+=28;
                        DrawBadges(new Rect(12,y,width,24),selectedOfficial,selected.IsWorkshop,selected.IsLocalDevelopment); y+=32;
                        GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
                        if(selected.IsInstalledOnly) Label(new Rect(12,y,width,30),T("version")+" "+Clean(selected.Version),theme.SecondaryText);
                        else if(!selected.IsWorkshop && Widgets.ButtonText(new Rect(12,y,width,30),T("version")+" "+selected.Version+" ▾")) ChooseVersion(snapshot);
                        else if(selected.IsWorkshop) Label(new Rect(12,y,width,30),T("workshop"),theme.SecondaryText);
                        GUI.enabled=enabled; y+=44;
                    }
                    if(selected==null) Label(new Rect(12,y,width,detailHeight),detail,theme.SecondaryText);
                    else foreach(var section in sections)
                    {
                        Widgets.DrawBoxSolid(new Rect(12,y,width,1),theme.Separator); y+=8;
                        Text.Font=GameFont.Tiny; Label(new Rect(12,y,width,22),section.Title,theme.SecondaryText); Text.Font=GameFont.Small; y+=26;
                        Label(new Rect(12,y,width,section.Height),section.Body,theme.PrimaryText); y+=section.Height+8;
                    }
                }
                finally { Widgets.EndScrollView(); }
            }
            if(selected!=null) DrawActions(regions.Actions,snapshot,enabled);
        }
        private void DrawActions(Rect rect, ManagedStoreSnapshot snapshot, bool enabled)
        {
            if(rect.height<=0 || rect.width<=0) return;
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width,1),theme.Separator);
            float padding=Mathf.Min(10,rect.width/4), width=Mathf.Max(0,rect.width-2*padding);
            float primaryHeight=Mathf.Min(32,rect.height);
            var primary=new Rect(rect.x+padding,rect.y+Mathf.Min(6,Mathf.Max(0,rect.height-primaryHeight)),width,primaryHeight);
            var local=selectedLocal;
            bool newer=local!=null && !selected.IsWorkshop && !selected.IsInstalledOnly && selected.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(local.Package.Version))>0;
            bool undo=local?.Package.DesiredState==ManagedExtensionDesiredState.PendingRemoval;
            selectedState=local==null || controlState==null?null:new ClientExtensionPackageState(local,controlState);
            bool enable=local!=null && (local.Package.DesiredState!=ManagedExtensionDesiredState.Enabled || selectedState?.CanRestoreSingleModule==true);
            bool modulesBlocked=selectedState!=null && selectedState.ModuleCount>0 && selectedState.EnabledModuleCount==0 && !selectedState.CanRestoreSingleModule;
            bool canFetch=InventoryKnown && !selected.IsInstalledOnly && snapshot.Repository!=null && !snapshot.Repository.Offline && !snapshot.Repository.Stale && selected.State=="active";
            GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
            string caption;
            Action action;
            if(selected.IsWorkshop)
            { caption=T("openWorkshop"); action=OpenWorkshop; GUI.enabled=GUI.enabled && selected.State=="active"; }
            else if(local==null)
            { caption=T("install"); GUI.enabled=GUI.enabled && canFetch; action=()=>BeginInstall(snapshot,false); }
            else if(!selectedOwned)
            { caption=T("management"); action=()=>Act(()=>management.OpenExtensionManagerWindow()); }
            else if(undo)
            { caption=T("cancelRemoval"); GUI.enabled=GUI.enabled && local.DisableBlockCode==null; action=()=>CancelRemoval(local); }
            else if(newer)
            {
                caption=T("update")+" · "+selected.Manifest.Version;
                GUI.enabled=GUI.enabled && canFetch && local.Package.DesiredState==ManagedExtensionDesiredState.Enabled && local.Package.ContentState==ManagedExtensionContentState.ContentVerified && local.Package.DiagnosticCode==null;
                action=()=>BeginInstall(snapshot,true);
            }
            else if(modulesBlocked)
            { caption=T("management"); action=()=>Act(()=>management.OpenExtensionManagerWindow()); }
            else
            { caption=T(enable?"enable":"disable"); GUI.enabled=GUI.enabled && (selectedState?.CanRestoreSingleModule==true || (enable?local.EnableBlockCode:local.DisableBlockCode)==null); action=()=>SetEnabled(local,enable); }
            if(Widgets.ButtonText(primary,caption)) action();
            TooltipHandler.TipRegion(primary,selectedOwned && undo?T("cancelRemovalTip"):caption);
            GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true;
            float y=primary.yMax+6;
            if(local!=null && selectedOwned && rect.yMax-y>=28)
            {
                float menuWidth=width>=360?width*.55f:width;
                if(Widgets.ButtonText(new Rect(rect.x+padding,y,menuWidth,28),T("manageInstalled"))) OpenInstalledMenu(local,enable);
                if(width>=360)
                {
                    GUI.enabled=enabled && !snapshot.Busy && controlState?.Busy!=true && (undo?local.DisableBlockCode:local.RemovalBlockCode)==null;
                    var removalRect=new Rect(rect.x+padding+menuWidth+6,y,Mathf.Max(0,width-menuWidth-6),28);
                    if(Widgets.ButtonText(removalRect,T(undo?"cancelRemoval":"uninstall"))) { if(undo) CancelRemoval(local); else ConfirmUninstall(local); }
                    if(undo) TooltipHandler.TipRegion(removalRect,T("cancelRemovalTip"));
                }
                y+=34;
            }
            GUI.enabled=enabled;
            if(rect.yMax-y>=18)
            {
                Text.Font=GameFont.Tiny;
                Label(new Rect(rect.x+padding,y,width,rect.yMax-y),T(selected.IsWorkshop?"workshopFooter":(local?.RestartPending==true || local?.ModulesRestartPending==true)?"restartRequired":"restartHint"),(local?.RestartPending==true || local?.ModulesRestartPending==true)?theme.Warning:theme.SecondaryText);
                Text.Font=GameFont.Small;
            }
        }
        private void OpenInstalledMenu(ManagedExtensionManagementPackage local,bool enable)
        {
            if(!InventoryKnown) return;
            bool undo=local.Package.DesiredState==ManagedExtensionDesiredState.PendingRemoval;
            long? expectedRevision=controlState?.Revision;
            var options=new List<FloatMenuOption> {
                new FloatMenuOption(T(undo?"cancelRemoval":enable?"enable":"disable"),(undo?local.DisableBlockCode==null:selectedState?.CanRestoreSingleModule==true || (enable?local.EnableBlockCode:local.DisableBlockCode)==null)?(Action)(()=>{ if(undo) CancelRemoval(local); else SetEnabled(local,enable,expectedRevision); }):null),
                new FloatMenuOption(T("uninstall"),!undo && local.RemovalBlockCode==null?(Action)(()=>ConfirmUninstall(local)):null),
                new FloatMenuOption(T("management"),()=>Act(()=>management.OpenExtensionManagerWindow())) };
            if(local.EnableBlockCode!=null || local.DisableBlockCode!=null || local.RemovalBlockCode!=null)
                options.Add(new FloatMenuOption(T("managementDetails"),()=>Find.WindowStack.Add(new Dialog_MessageBox(Clean(T("blockedFriendly")+"\n\n"+local.EnableBlockCode+"\n"+local.DisableBlockCode+"\n"+local.RemovalBlockCode),null))));
            Find.WindowStack.Add(new FloatMenu(options));
        }
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
        private void ConfirmUninstall(ManagedExtensionManagementPackage local)
        {
            var expected=local.Package;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T("confirmUninstall"),()=>Act(()=>
            {
                if(!(controls?.Capture().InventoryKnown??controller.InventoryKnown)) throw new StoreValidationException("ManagedInventoryUnavailable","Refresh package facts before removal.");
                controller.Change(expected,ManagedExtensionDesiredState.PendingRemoval,environment.Capture());
            })));
        }
        private void CancelRemoval(ManagedExtensionManagementPackage local)
        { if(InventoryKnown) Act(()=>controller.Change(local.Package,ManagedExtensionDesiredState.Disabled,environment.Capture())); }
        private void ConfirmInstall(ManagedStorePlan plan)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T(plan.ReplacesPackages?"updateConfirmation":"dependencyConfirmation")+"\n\n"+string.Join("\n",plan.Items.Select(i=>Clean(i.Package.DisplayName(locale))+" "+i.Package.Manifest.Version+" · "+T(i.Installed==null?"newPackage":i.RequiresDownload?"updatedPackage":"existingPackage")))+"\n\n"+T("downloadSize")+" "+(plan.DownloadBytes/1024d/1024d).ToString("F2")+" MiB",()=>
            { if(controller.Snapshot.Plan!=plan || controller.Snapshot.Busy || selected?.CatalogRecord!=plan.Root) { localFailure=null; error=T("reviewAgain"); return; } Act(()=>controller.Download(plan,environment.Capture(),true)); }));
        }
        private void ConfirmLocalInstall(ManagedStoreSnapshot prepared)
        {
            var zip=prepared.LocalPackage;
            string summary=zip.Manifest.PackageId+"\n"+zip.Manifest.Version+"\nSHA-256: "+zip.Sha256;
            Find.WindowStack.Add(new Dialog_MessageBox(T("localConfirmation")+"\n\n"+summary,T("install"),()=>Act(()=>
            {
                if(stopped || !localImportActive || !Prefs.DevMode) return;
                controller.InstallLocal(prepared,environment.Capture(),Prefs.DevMode);
            }),T("cancel"),()=>{ localImportActive=false; controller.DiscardLocal(prepared); }));
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
        { installIntent=null; selected=row; cached=null; Rebuild(snapshot); detailScroll=Vector2.zero; error=null; localFailure=null; linkNotice=null; }
        private void ChooseVersion(ManagedStoreSnapshot snapshot)
        {
            var versions=snapshot.Catalog.Packages.Where(p=>p.Id==selected.Id).OrderByDescending(p=>p.Manifest.Version);
            Find.WindowStack.Add(new FloatMenu(versions.Select(p=>new FloatMenuOption(p.Manifest.Version+ (p.State=="active"?"":" · "+T("withdrawn")),()=> {
                if(controller.Snapshot.Catalog==snapshot.Catalog && !controller.Snapshot.Busy)
                {
                    var entries=ManagedStoreListing.BuildEntries(snapshot.Catalog,SharedInventory(snapshot),snapshot.Repository?.Endpoint,"",locale);
                    var entry=entries.SelectMany(g=>g.Versions).FirstOrDefault(e=>e.CatalogRecord==p);
                    if(entry!=null) Select(entry,controller.Snapshot);
                }
            })).ToList()));
        }
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
