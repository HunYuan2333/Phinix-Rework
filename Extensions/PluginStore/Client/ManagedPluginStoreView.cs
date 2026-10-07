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
        private readonly IClientLocalizer localizer;
        private readonly IUiTheme theme;
        private readonly IClientLinkService links;
        private readonly StoreMaintainerRegistry maintainers;
        private readonly StoreBadgeIcons badgeIcons;
        private bool[] cardOfficial=new bool[0];
        private bool selectedOfficial;
        private float officialLabelWidth, managedLabelWidth, workshopLabelWidth;
        private string linkNotice;
        private readonly Rect[] toolbarRects = new Rect[3];
        private readonly float[] toolbarWidths = new float[3];
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
            IClientExtensionManagementWindowService management,IClientLocalizer localizer,IUiTheme theme,
            IClientLinkService links,StoreMaintainerRegistry maintainers,StoreBadgeIcons badgeIcons)
        {
            this.controller=controller; this.environment=environment; this.settings=settings; this.management=management; this.localizer=localizer; this.theme=theme;
            this.links=links; this.maintainers=maintainers; this.badgeIcons=badgeIcons;
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
                GUI.enabled=enabled && !snapshot.Busy;
                float clearWidth=string.IsNullOrEmpty(search)?0:Mathf.Min(28,searchWidth);
                string nextSearch = Widgets.TextField(new Rect(searchRect.x,searchRect.y,Mathf.Max(0,searchWidth-clearWidth),32), search, 128);
                if(clearWidth>0 && Widgets.ButtonText(new Rect(searchRect.xMax-clearWidth,searchRect.y,clearWidth,32),"×")) nextSearch="";
                if (string.IsNullOrEmpty(search)) {
                    GUI.color = theme.SecondaryText;
                    Text.WordWrap=false; Widgets.Label(new Rect(searchRect.x+8, searchRect.y+4, Mathf.Max(0, searchWidth-16), 24), T(searchWidth<320?"searchShort":"search")); Text.WordWrap=true;
                    GUI.color = Color.white;
                }
                TooltipHandler.TipRegion(searchRect, T("search"));
                GUI.enabled = enabled && !snapshot.Busy;
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
            }
            // One row on desktop, wrap/overflow on smaller windows using the host-neutral helper.
            var bar=ResponsiveToolbarLayout.Calculate(rect,toolbarWidths,3,1,32,6,2,40,toolbarRects);
            height=Mathf.Min(rect.height,bar.Height);
            GUI.enabled=enabled && !snapshot.Busy;
            for(int i=0;i<bar.VisibleActionCount;i++)
            {
                string text=T(i==0?"refresh":i==1?(accessMethod==RepositoryAccessMethod.GitHub?"accessGithub":"accessCloudflare"):"management");
                if(i==1) text+=" ▾";
                if(Widgets.ButtonText(toolbarRects[i],text)) ToolbarAction(i);
                TooltipHandler.TipRegion(toolbarRects[i],i==1?T("accessExplanation")+(accessMethod==RepositoryAccessMethod.GitHub?"\n"+T("githubAccessGuidance"):""):text);
            }
            if(bar.HasOverflow && Widgets.ButtonText(bar.OverflowButtonRect,"⋯"))
            {
                var options=new List<FloatMenuOption>();
                for(int i=bar.VisibleActionCount;i<3;i++) { int action=i; options.Add(new FloatMenuOption(T(i==0?"refresh":i==1?"accessCurrent":"management"),()=>ToolbarAction(action))); }
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
            else Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                new FloatMenuOption(T("accessGithub"),()=>SwitchAccess(RepositoryAccessMethod.GitHub)),
                new FloatMenuOption(T("accessCloudflare"),()=>SwitchAccess(RepositoryAccessMethod.Cloudflare)) }));
        }
        private float DrawStatus(Rect rect, ManagedStoreSnapshot snapshot, bool enabled)
        {
            bool diagnostic=error!=null || snapshot.State==ManagedStoreState.Failed;
            float actionWidth=diagnostic || snapshot.Busy?Mathf.Min(120,rect.width*.3f):0;
            string text=error==null?(linkNotice??status):T("failedFriendly");
            float textWidth=Mathf.Max(1,rect.width-actionWidth-24);
            if(measuredStatus!=text || statusWidth!=textWidth)
            { measuredStatus=text; statusWidth=textWidth; statusHeight=Text.CalcHeight(text,textWidth); }
            float height=Mathf.Min(rect.height,Mathf.Max(36,Mathf.Min(68,statusHeight+12)));
            Color accent=diagnostic?theme.Error:snapshot.State==ManagedStoreState.Installed?theme.Success:snapshot.Busy?theme.Warning:theme.SecondaryText;
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,rect.width,height),theme.Surface);
            Widgets.DrawBoxSolid(new Rect(rect.x,rect.y,3,height),accent);
            var messageRect=new Rect(rect.x+10,rect.y+6,textWidth,Mathf.Max(0,height-12));
            Label(messageRect,text,theme.PrimaryText);
            if(statusHeight>messageRect.height) TooltipHandler.TipRegion(messageRect,text);
            GUI.enabled=enabled;
            if(diagnostic && Widgets.ButtonText(new Rect(rect.xMax-actionWidth-4,rect.y+4,actionWidth,Mathf.Max(0,height-8)),T("errorDetails")))
                Find.WindowStack.Add(new Dialog_MessageBox(Clean(ErrorDetails(snapshot)),null));
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
        private void Rebuild(ManagedStoreSnapshot snapshot)
        {
            if(cached==snapshot && cachedSearch==search && cachedFilter==filter && ReferenceEquals(language,LanguageDatabase.activeLanguage) && locale==localizer.Locale) return;
            bool changed=cached?.Catalog!=snapshot.Catalog;
            bool contentChanged=cached==null || changed || cached.Inventory!=snapshot.Inventory || cached.Repository!=snapshot.Repository || cachedSearch!=search || cachedFilter!=filter || !ReferenceEquals(language,LanguageDatabase.activeLanguage) || locale!=localizer.Locale;
            cached=snapshot; cachedSearch=search; cachedFilter=filter; language=LanguageDatabase.activeLanguage; locale=localizer.Locale;
            if(changed && selected!=null)
            {
                selected=ManagedStoreListing.RestoreSelection(snapshot.Catalog,selected);
                if(selected==null) detailScroll=Vector2.zero;
            }
            if(contentChanged)
            {
                Text.Font=GameFont.Tiny;
                officialLabelWidth=Text.CalcSize(T("officialBadge")).x;
                managedLabelWidth=Text.CalcSize(T("managedBadge")).x;
                workshopLabelWidth=Text.CalcSize(T("workshop")).x;
                Text.Font=GameFont.Small;
                updateIds.Clear(); foreach(var p in controller.Updates) updateIds.Add(p.Id);
                var listing=ManagedStoreListing.Build(snapshot.Catalog,search,locale);
                rows=listing.Where(g=>filter==0 || filter==1 && snapshot.Inventory?.Packages.Any(p=>p.Package.PackageId==g.Preferred.Id)==true || filter==2 && updateIds.Contains(g.Preferred.Id)).ToArray();
                if(selected!=null && !rows.Any(g=>g.Versions.Contains(selected))) { selected=null; installIntent=null; detailScroll=Vector2.zero; }
                cardWidth=-1;
                selectedLocal=selected==null?null:snapshot.Inventory?.Packages.FirstOrDefault(p=>p.Package.PackageId==selected.Id);
                selectedOwned=selectedLocal!=null && Owns(snapshot,selectedLocal);
                selectedOfficial=maintainers.IsOfficial(selected,snapshot.Catalog,snapshot.Repository?.Endpoint);
                detail=BuildDetail(snapshot); detailHeight=0;
                resultCaption=string.Format(T("resultCount"),rows.Length);
            }
            string key="state_"+snapshot.State; status=T(key);
            if(snapshot.State==ManagedStoreState.Failed) status=FailureMessage(snapshot);
            if(snapshot.Code=="ManagedStateSaved") status=T("stateSaved");
            if(snapshot.Repository?.Offline==true || snapshot.Repository?.Stale==true) status+=" · "+T("offlineNotice");
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
            nameLine=Clean(selected.DisplayName(locale)); authorLine=Clean(selected.Author)+" · "+Clean(selected.License);
            sections.Add(new DetailSection { Title=T("about"), Body=Clean(selected.DisplaySummary(locale)) });
            if(!selected.IsWorkshop) sections.Add(new DetailSection { Title=T("changelog"), Body=Clean(selected.DisplayChangelog(locale)??T("noChangelog")) });
            var b=new StringBuilder();
            b.AppendLine("RimWorld "+Clean(string.Join(", ",selected.RimWorldVersions)));
            if(selected.IsWorkshop) b.AppendLine(T("workshopNotice"));
            else
            {
                b.AppendLine("Phinix "+Clean(selected.Manifest.Compatibility.PhinixRange.Text));
                foreach(var dependency in selected.Manifest.Dependencies) b.AppendLine(T("dependency")+" "+Clean(dependency.PackageId)+" "+dependency.VersionRange.Text+(dependency.Optional?" · "+T("optional"):""));
                foreach(var mod in selected.Manifest.ExternalMods) b.AppendLine(T("externalMod")+" "+Clean(mod.PackageId));
            }
            sections.Add(new DetailSection { Title=T("compatibility"), Body=b.ToString().TrimEnd() });
            if(selectedLocal!=null && !selected.IsWorkshop)
            {
                b.Clear();
                b.AppendLine(T("installedVersion")+" "+selectedLocal.Package.Version);
                b.AppendLine(T("installedState")+" "+T("desired_"+selectedLocal.Package.DesiredState)+(selectedLocal.RestartPending?" · "+T("restartRequired"):""));
                if(!selectedOwned) b.AppendLine(T("foreignInstallation"));
                else if(ManagedExtensionVersion.Parse(selectedLocal.Package.Version).CompareTo(selected.Manifest.Version)>0) b.AppendLine(T("downgradeUnavailable"));
                else if(selectedLocal.Package.DesiredState!=ManagedExtensionDesiredState.Enabled && ManagedExtensionVersion.Parse(selectedLocal.Package.Version).CompareTo(selected.Manifest.Version)<0) b.AppendLine(T("enableBeforeUpdate"));
                sections.Add(new DetailSection { Title=T("installation"), Body=b.ToString().TrimEnd() });
            }
            if(selected.State!="active") sections.Add(new DetailSection { Title=T("availability"), Body=T("withdrawn") });
            if(selected.Tags.Count>0) sections.Add(new DetailSection { Title=T("tags"), Body=Clean(string.Join(" · ",selected.Tags)) });
            return "";
        }
        private void Label(Rect rect, string text, Color color)
        {
            if(rect.width<=0 || rect.height<=0) return;
            GUI.color=color; Widgets.Label(rect,text); GUI.color=Color.white;
        }
        private void DrawBadges(Rect rect,bool official,bool workshop)
        {
            var layout=ManagedStoreBadgeLayout.Calculate(rect,official,officialLabelWidth,workshop?workshopLabelWidth:managedLabelWidth);
            if(official) DrawBadge(layout.Official,StoreBadgeKind.Official,layout.OfficialText,T("officialBadge"),T("officialBadgeTip"));
            DrawBadge(layout.Route,workshop?StoreBadgeKind.Workshop:StoreBadgeKind.Managed,layout.RouteText,T(workshop?"workshop":"managedBadge"),T(workshop?"workshopBadgeTip":"managedBadgeTip"));
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
                    cardOfficial[i]=maintainers.IsOfficial(row,snapshot.Catalog,snapshot.Repository?.Endpoint);
                    cardTips[i]=Clean(row.DisplayName(locale))+"\n"+Clean(row.DisplaySummary(locale))+"\n"+
                        (cardOfficial[i]?T("officialBadge")+" · ":"")+T(row.IsWorkshop?"workshop":"managedBadge")+"\n"+T(row.IsWorkshop?"workshopBadgeTip":"managedBadgeTip");
                    cardNames[i]=Shorten(Clean(row.DisplayName(locale)),Mathf.Max(0,width-24));
                    Text.Font=GameFont.Tiny;
                    cardMetadata[i]=Shorten((row.IsWorkshop?"":row.Manifest.Version+" · ")+Clean(row.Author),Mathf.Max(0,width-24));
                    var local=snapshot.Inventory?.Packages.FirstOrDefault(p=>p.Package.PackageId==row.Id);
                    cardStates[i]=Shorten(local?.RestartPending==true?T("restartRequired"):updateIds.Contains(row.Id)?T("updateBadge"):local!=null?T("desired_"+local.Package.DesiredState):row.State!="active"?T("withdrawn"):"",Mathf.Max(0,width-24));
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
                    if(active) Widgets.DrawBoxSolid(new Rect(0,card.y,3,card.height),theme.Warning);
                    Text.WordWrap=false;
                    Label(new Rect(12,card.y+7,Mathf.Max(0,width-24),26),cardNames[i],theme.PrimaryText);
                    Text.Font=GameFont.Tiny;
                    Label(new Rect(12,card.y+35,Mathf.Max(0,width-24),20),cardMetadata[i],theme.SecondaryText);
                    DrawBadges(new Rect(12,card.y+57,Mathf.Max(0,width-24),24),cardOfficial[i],row.IsWorkshop);
                    Label(new Rect(12,card.y+85,Mathf.Max(0,width-24),18),cardStates[i],updateIds.Contains(row.Id)?theme.Warning:theme.SecondaryText);
                    Text.Font=GameFont.Small; Text.WordWrap=true;
                    GUI.enabled=enabled && !snapshot.Busy;
                    if(Widgets.ButtonInvisible(card)) Select(active?selected:row,snapshot);
                    GUI.enabled=enabled;
                    TooltipHandler.TipRegion(card,cardTips[i]);
                }
                if(rows.Length==0) Label(new Rect(12,12,Mathf.Max(0,width-24),Mathf.Max(0,viewport.height-24)),T(snapshot.Catalog==null?"refreshFirst":snapshot.Catalog.Packages.Count==0?"catalogEmpty":"empty"),theme.SecondaryText);
            }
            finally { Widgets.EndScrollView(); }
        }
        private void DrawDetail(Rect rect,ManagedStoreSnapshot snapshot,bool enabled,bool compact)
        {
            if(rect.height<=0 || rect.width<=0) return;
            Widgets.DrawBoxSolid(rect,theme.Surface);
            var regions=ManagedStoreLayout.DetailRegions(rect,compact,selected!=null);
            GUI.enabled=enabled && !snapshot.Busy;
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
                        DrawBadges(new Rect(12,y,width,24),selectedOfficial,selected.IsWorkshop); y+=32;
                        GUI.enabled=enabled && !snapshot.Busy;
                        if(!selected.IsWorkshop && Widgets.ButtonText(new Rect(12,y,width,30),T("version")+" "+selected.Manifest.Version+" ▾")) ChooseVersion(snapshot);
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
            bool newer=local!=null && !selected.IsWorkshop && selected.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(local.Package.Version))>0;
            bool enable=local!=null && local.Package.DesiredState!=ManagedExtensionDesiredState.Enabled;
            bool canFetch=snapshot.Repository!=null && !snapshot.Repository.Offline && !snapshot.Repository.Stale && selected.State=="active";
            GUI.enabled=enabled && !snapshot.Busy;
            string caption;
            Action action;
            if(selected.IsWorkshop)
            { caption=T("openWorkshop"); action=OpenWorkshop; GUI.enabled=GUI.enabled && selected.State=="active"; }
            else if(local==null)
            { caption=T("install"); GUI.enabled=GUI.enabled && canFetch; action=()=>BeginInstall(snapshot,false); }
            else if(!selectedOwned)
            { caption=T("management"); action=()=>Act(()=>management.OpenExtensionManagerWindow()); }
            else if(newer)
            {
                caption=T("update")+" · "+selected.Manifest.Version;
                GUI.enabled=GUI.enabled && canFetch && local.Package.DesiredState==ManagedExtensionDesiredState.Enabled && local.Package.ContentState==ManagedExtensionContentState.ContentVerified && local.Package.DiagnosticCode==null;
                action=()=>BeginInstall(snapshot,true);
            }
            else
            { caption=T(enable?"enable":"disable"); GUI.enabled=GUI.enabled && (enable?local.EnableBlockCode:local.DisableBlockCode)==null; action=()=>SetEnabled(local,enable); }
            if(Widgets.ButtonText(primary,caption)) action();
            TooltipHandler.TipRegion(primary,caption);
            GUI.enabled=enabled && !snapshot.Busy;
            float y=primary.yMax+6;
            if(local!=null && selectedOwned && rect.yMax-y>=28)
            {
                float menuWidth=width>=360?width*.55f:width;
                if(Widgets.ButtonText(new Rect(rect.x+padding,y,menuWidth,28),T("manageInstalled"))) OpenInstalledMenu(local,enable);
                if(width>=360)
                {
                    GUI.enabled=enabled && !snapshot.Busy && local.RemovalBlockCode==null;
                    if(Widgets.ButtonText(new Rect(rect.x+padding+menuWidth+6,y,Mathf.Max(0,width-menuWidth-6),28),T("uninstall"))) ConfirmUninstall(local);
                }
                y+=34;
            }
            GUI.enabled=enabled;
            if(rect.yMax-y>=18)
            {
                Text.Font=GameFont.Tiny;
                Label(new Rect(rect.x+padding,y,width,rect.yMax-y),T(selected.IsWorkshop?"workshopFooter":local?.RestartPending==true?"restartRequired":"restartHint"),local?.RestartPending==true?theme.Warning:theme.SecondaryText);
                Text.Font=GameFont.Small;
            }
        }
        private void OpenInstalledMenu(ManagedExtensionManagementPackage local,bool enable)
        {
            var options=new List<FloatMenuOption> {
                new FloatMenuOption(T(enable?"enable":"disable"),(enable?local.EnableBlockCode:local.DisableBlockCode)==null?(Action)(()=>SetEnabled(local,enable)):null),
                new FloatMenuOption(T("uninstall"),local.RemovalBlockCode==null?(Action)(()=>ConfirmUninstall(local)):null),
                new FloatMenuOption(T("management"),()=>Act(()=>management.OpenExtensionManagerWindow())) };
            if(local.EnableBlockCode!=null || local.DisableBlockCode!=null || local.RemovalBlockCode!=null)
                options.Add(new FloatMenuOption(T("managementDetails"),()=>Find.WindowStack.Add(new Dialog_MessageBox(Clean(T("blockedFriendly")+"\n\n"+local.EnableBlockCode+"\n"+local.DisableBlockCode+"\n"+local.RemovalBlockCode),null))));
            Find.WindowStack.Add(new FloatMenu(options));
        }
        private void BeginInstall(ManagedStoreSnapshot snapshot, bool replace)
        {
            Act(()=> { controller.Plan(selected,environment.Capture(),snapshot.Catalog,replace); installIntent=new ManagedStoreInstallIntent(snapshot.Catalog,selected); });
        }
        private void SetEnabled(ManagedExtensionManagementPackage local,bool enable)
        { Act(()=>controller.Change(local.Package,enable?ManagedExtensionDesiredState.Enabled:ManagedExtensionDesiredState.Disabled,environment.Capture())); }
        private void ConfirmUninstall(ManagedExtensionManagementPackage local)
        {
            var expected=local.Package;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T("confirmUninstall"),()=>Act(()=>controller.Change(expected,ManagedExtensionDesiredState.PendingRemoval,environment.Capture()))));
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
        private void OpenWorkshop()
        {
            Act(()=> {
                var result=links.Open(selected.WorkshopUrl);
                if(result==ClientLinkOpenResult.Unavailable) error=T("linkUnavailable");
                else linkNotice=T(result==ClientLinkOpenResult.GameBrowserRequested?"gameBrowserRequested":"externalBrowserRequested");
            });
        }
        private void Act(Action action) { try { error=null; linkNotice=null; action(); } catch(Exception ex) { error=T("failed")+" "+((ex as StoreValidationException)?.Code??ex.GetType().Name); } }
        private bool Owns(ManagedStoreSnapshot snapshot,ManagedExtensionManagementPackage local)
        {
            return snapshot.Inventory.Packages.Count(p=>p.Package.PackageId==selected.Id)==1 &&
                local.Package.SourceId==snapshot.Catalog.SourceId && local.Package.RepositoryIdentitySha256==snapshot.Repository?.Endpoint.IdentityKey;
        }
        private void Select(ManagedStoreRecord row,ManagedStoreSnapshot snapshot)
        { installIntent=null; selected=row; cached=null; Rebuild(snapshot); detailScroll=Vector2.zero; error=null; linkNotice=null; }
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
