using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    internal sealed class ClientThemeWindowService : IClientThemeWindowService, IDisposable
    {
        private readonly IClientTheme theme;
        private readonly IClientThemeCatalog catalog;
        private ThemeSettingsWindow window;
        private ThemePresentationServices presentation;
        private readonly IClientThemePackageExporter exporter;
        private readonly IClientSettingsContext settings;
        internal ClientThemeWindowService(IClientTheme theme, IClientThemeCatalog catalog,IClientThemePackageExporter exporter,IClientSettingsContext settings) { this.theme = theme; this.catalog = catalog;this.exporter=exporter;this.settings=settings; }
        internal void ConfigurePresentation(IClientPresentationService renderer,ClientExtensionPointService points,ClientLifetimeService lifetime)
        { presentation=new ThemePresentationServices(renderer,points,lifetime); }
        public void Dispose()
        {try {if(window?.IsOpen==true)window.Close();window=null;}finally {presentation?.Dispose();presentation=null;}}
        public void OpenThemeWindow()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Theme window requires the game main thread.");
            if (window != null && window.IsOpen) return;
            if(presentation==null||!presentation.Active)throw new InvalidOperationException("ThemePresentationUnavailable");
            window = new ThemeSettingsWindow(presentation,theme, catalog,exporter,settings); Find.WindowStack.Add(window);
        }
    }
    internal sealed class ThemeSettingsPanel : IClientSettingsPanelProvider
    {
        private readonly IClientThemeWindowService windows;
        private readonly IClientTheme theme;
        internal ThemeSettingsPanel(IClientTheme theme, IClientThemeWindowService windows) { this.theme = theme; this.windows = windows; }
        public string SectionId => "Phinix_theme_title";
        public float Order => 40f;
        public bool IsVisible(IClientSettingsContext settings) => true;
        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        { if (ClientWidgets.Button(listing.GetRect(30f), "Phinix_theme_open".Translate(), theme.Capture())) windows.OpenThemeWindow(); }
    }
    internal sealed class ThemeSettingsWindow : ThemePresentationWindow
    {
        private sealed class EditSession
        {
            internal ClientThemeDraft Draft;internal string SelectedKey;
            internal readonly Dictionary<string,ThemeDraftInput> Inputs=new Dictionary<string,ThemeDraftInput>(StringComparer.Ordinal);
        }
        private sealed class Entry
        {
            internal string Key,Label,Description,Owner,OwnerLabel,Group,Fallback;
            internal ClientThemeFloat Parameter;internal bool IsFloat=>Parameter!=null;
        }
        private readonly IClientThemeCatalog catalog;
        private readonly IClientThemePackageExporter exporter;
        private readonly IClientSettingsContext settings;
        private readonly Dictionary<string,EditSession> sessions=new Dictionary<string,EditSession>(StringComparer.Ordinal);
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> keyRows=new Dictionary<string,string>(StringComparer.Ordinal);
        private EditSession session;
        private ThemeSelectorWindow selector;
        private ThemePackageExportWindow exportWindow;
        private string itemSearch="",filePath="",message="",lastExportPath;
        private ClientPresentationTone messageTone;
        private IReadOnlyList<ClientThemeInfo> seenThemes;
        private IReadOnlyList<ClientThemeColor> seenColors;
        private IReadOnlyList<ClientThemeFloat> seenParameters;
        internal ThemeSettingsWindow(ThemePresentationServices services,IClientTheme theme,IClientThemeCatalog catalog,IClientThemePackageExporter exporter,IClientSettingsContext settings)
            :base(services,theme)
        {this.catalog=catalog;this.exporter=exporter;this.settings=settings;Select(catalog.Themes.Any(t=>t.Id==catalog.SelectedThemeId)?catalog.SelectedThemeId:"builtin.default");}
        public override Vector2 InitialSize=>new Vector2(Mathf.Min(1040,UiScreenSafeArea.Current.width),Mathf.Min(800,UiScreenSafeArea.Current.height));
        protected override bool DataChanged=>!ReferenceEquals(seenThemes,catalog.Themes)||!ReferenceEquals(seenColors,catalog.Colors)||!ReferenceEquals(seenParameters,catalog.Parameters);
        protected override Func<bool> DataGuard
        {get {var themes=catalog.Themes;var colors=catalog.Colors;var parameters=catalog.Parameters;return ()=>ReferenceEquals(themes,catalog.Themes)&&ReferenceEquals(colors,catalog.Colors)&&ReferenceEquals(parameters,catalog.Parameters);}}
        private bool CanApply=>!session.Inputs.Values.Any(v=>v.Invalid)&&catalog.Themes.Any(t=>t.Id==session.Draft.ThemeId);
        private static string KeyId(string key)=>"key."+Utils.Framework.ManagedExtensions.ManagedExtensionPaths.Hash(System.Text.Encoding.UTF8.GetBytes(key));
        private void Select(string id)
        {if(!sessions.TryGetValue(id,out session))sessions.Add(id,session=new EditSession {Draft=catalog.Edit(id)});Invalidate();}
        private void RebuildEntries()
        {
            entries.Clear();
            foreach(var c in catalog.Colors)entries[c.Key]=new Entry {Key=c.Key,Label=c.Label,Description=c.Description,Owner=c.Owner,OwnerLabel=c.OwnerLabel,Group=c.Group,Fallback=c.FallbackKey};
            foreach(var f in catalog.Parameters)entries[f.Key]=new Entry {Key=f.Key,Label=f.Label,Description=f.Description,Owner=f.Owner,OwnerLabel=f.OwnerLabel,Group=f.Group,Parameter=f};
        }
        private ThemeDraftInput Buffer(Entry entry)
        {
            if(session.Inputs.TryGetValue(entry.Key,out var input)&&input.Dirty)return input;
            var c=entry.IsFloat?Color.clear:catalog.Preview(session.Draft,entry.Key);
            var values=entry.IsFloat?new[]{catalog.PreviewFloat(session.Draft,entry.Key).Value}:new[]{c.r,c.g,c.b,c.a};
            input=new ThemeDraftInput {IsFloat=entry.IsFloat,Values=values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)).ToArray()};session.Inputs[entry.Key]=input;return input;
        }
        protected override void Input(ClientPresentationInput input)
        {
            if(input.Id=="item-search")itemSearch=input.Value;
            else if(input.Id=="export-path")filePath=input.Value;
            else if(input.Id=="keys"&&keyRows.TryGetValue(input.Value,out var key))session.SelectedKey=key;
            else if(input.Id.StartsWith("value.",StringComparison.Ordinal)&&int.TryParse(input.Id.Substring(6),out var index)&&session.SelectedKey!=null&&entries.TryGetValue(session.SelectedKey,out var entry))
            {
                var buffer=Buffer(entry);
                ThemeDraftValues.Change(session.Draft,entry.Key,buffer,entry.IsFloat,entry.Parameter?.Minimum??0,entry.Parameter?.Maximum??1,index,input.Value);
            }
        }
        private string Origin(ClientThemeColorPreview preview)
        {
            string source=L("Phinix_theme_source_"+preview.Source);
            if(preview.Source==ClientThemeValueSource.Inherited)source+=" → "+preview.SourceKey+" ("+L("Phinix_theme_source_"+preview.ResolvedSource)+")";
            return source;
        }
        private void ClearOverride(string key,bool inherit)
        {ThemeDraftValues.Clear(session.Draft,session.Inputs,key,inherit);Invalidate();}
        private void Run(Action action)
        {try {action();}catch(Exception error){message=error.Message;messageTone=ClientPresentationTone.Error;}finally {Invalidate();}}
        private void ChooseTheme()
        {if(selector?.IsOpen==true)return;selector=new ThemeSelectorWindow(Services,Theme,catalog,()=>IsOpen,id=>Select(id),session.Draft.ThemeId);Find.WindowStack.Add(selector);}
        private void ExportTheme()
        {
            if(!CanApply||exportWindow?.IsOpen==true)return;
            exportWindow=new ThemePackageExportWindow(Services,Theme,catalog,exporter,settings,session.Draft,filePath,()=>IsOpen,destination=>
            {lastExportPath=destination;filePath=destination;OpenExportFolder();Invalidate();});Find.WindowStack.Add(exportWindow);
        }
        private void OpenExportFolder()
        {bool launched=LocalFileLocationAdapter.TryOpenDirectory(lastExportPath);message=L("Phinix_theme_exported")+"\n"+lastExportPath;messageTone=launched?ClientPresentationTone.Success:ClientPresentationTone.Warning;if(!launched)message+="\n"+L("Phinix_theme_openFolderFailed");Invalidate();}
        protected override void Project()
        {
            RebuildEntries();keyRows.Clear();string key=session.SelectedKey;Entry picked=null;bool registered=key!=null&&entries.TryGetValue(key,out picked);
            var defs=new List<ClientActionDefinition> {
                new ClientActionDefinition("choose-theme",L("Phinix_theme_chooseTheme"),()=>Run(ChooseTheme)),
                new ClientActionDefinition("refresh",L("Phinix_theme_refresh"),()=>Run(catalog.Refresh)),
                new ClientActionDefinition("reset",L("Phinix_theme_reset"),()=>{ThemeDraftValues.Reset(session.Draft,session.Inputs);message=L("Phinix_theme_resetDone");messageTone=ClientPresentationTone.Neutral;Invalidate();}),
                new ClientActionDefinition("apply",L("Phinix_theme_apply"),()=>Run(()=>{catalog.Apply(session.Draft);message=L("Phinix_theme_applied");messageTone=ClientPresentationTone.Success;}),CanApply?null:L("Phinix_theme_unfinishedInput")),
                new ClientActionDefinition("export",L("Phinix_theme_export"),()=>Run(ExportTheme),CanApply?null:L("Phinix_theme_unfinishedInput")),
                new ClientActionDefinition("close",L("Phinix_theme_cancel"),()=>Close())
            };
            if(lastExportPath!=null){defs.Add(new ClientActionDefinition("copy-path",L("Phinix_theme_copyPath"),()=>GUIUtility.systemCopyBuffer=lastExportPath));defs.Add(new ClientActionDefinition("open-folder",L("Phinix_theme_openFolder"),()=>Run(OpenExportFolder)));}
            if(key!=null)
            {
                defs.Add(new ClientActionDefinition("clear",L("Phinix_theme_clear"),()=>ClearOverride(key,false)));
                defs.Add(new ClientActionDefinition("inherit",L(registered&&!picked.IsFloat&&picked.Fallback!=null?"Phinix_theme_restoreInheritance":"Phinix_theme_restoreDefault"),()=>ClearOverride(key,true),registered?null:L("Phinix_theme_itemUnavailable")));
            }
            var rows=new List<ClientPresentationRow>();string selectedRow=null;
            foreach(var entry in entries.Values.OrderBy(e=>e.Owner,StringComparer.Ordinal).ThenBy(e=>e.Group,StringComparer.Ordinal).ThenBy(e=>e.Key,StringComparer.Ordinal))
            {
                string searchable=entry.Key+" "+Name(entry.Label)+" "+Name(entry.OwnerLabel)+" "+Name(entry.Group);
                if(itemSearch.Length>0&&searchable.IndexOf(itemSearch,StringComparison.OrdinalIgnoreCase)<0)continue;
                string id=KeyId(entry.Key);keyRows[id]=entry.Key;if(entry.Key==key)selectedRow=id;
                string origin=entry.IsFloat?L("Phinix_theme_source_"+catalog.PreviewFloat(session.Draft,entry.Key).Source):Origin(catalog.PreviewColor(session.Draft,entry.Key));
                rows.Add(new ClientPresentationRow(id,Name(entry.Label),Name(entry.OwnerLabel)+" / "+(string.IsNullOrEmpty(entry.Group)?L("Phinix_theme_group_general"):Name(entry.Group))+"\n"+entry.Key+" · "+origin));
            }
            var fields=new List<ClientPresentationTextField>();string limits="",source="";
            var swatches=new List<ClientPresentationSwatch>();
            if(registered)
            {
                var buffer=Buffer(picked);var c=picked.IsFloat?Color.clear:catalog.Preview(session.Draft,picked.Key);
                for(int i=0;i<buffer.Values.Length;i++)
                {
                    var field=new ClientPresentationTextField("value."+i,picked.IsFloat?Name(picked.Label):new[]{"R","G","B","A"}[i],buffer.Values[i],maxLength:256,validationText:buffer.Invalid?L("Phinix_theme_invalidInput"):null);
                    if(picked.IsFloat&&picked.Parameter.Minimum<picked.Parameter.Maximum)field=field.WithSlider(new ClientPresentationSlider(picked.Parameter.Minimum,picked.Parameter.Maximum,catalog.PreviewFloat(session.Draft,key).Value));
                    fields.Add(field);
                }
                limits=picked.IsFloat?string.Format(L("Phinix_theme_parameterRange"),picked.Parameter.Minimum.ToString("R",CultureInfo.InvariantCulture),picked.Parameter.Maximum.ToString("R",CultureInfo.InvariantCulture)):"R / G / B / A [0–1]";
                source=picked.IsFloat?L("Phinix_theme_source_"+catalog.PreviewFloat(session.Draft,key).Source):Origin(catalog.PreviewColor(session.Draft,key));
                if(session.Draft.InheritedKeys.Contains(key))source+=" · "+L("Phinix_theme_forcedInheritance");
                if(picked.IsFloat&&catalog.PreviewFloat(session.Draft,key).Clamped)source+=" · "+L("Phinix_theme_parameterClamped");
                if(!picked.IsFloat)swatches.Add(new ClientPresentationSwatch("selected",Name(picked.Label),c));
            }
            foreach(string role in new[]{"ui.surface","ui.text.primary","ui.success","ui.warning","ui.error","ui.hover"})swatches.Add(new ClientPresentationSwatch("preview."+swatches.Count,role,catalog.Preview(session.Draft,role)));
            string status=session.Inputs.Values.Any(v=>v.Invalid)?L("Phinix_theme_unfinishedInput"):!CanApply?L("Phinix_theme_missingDraftTheme"):message.Length>0?message:string.Join("\n",catalog.Diagnostics.Take(2));
            var info=catalog.Themes.FirstOrDefault(t=>t.Id==session.Draft.ThemeId);
            Publish(L("Phinix_theme_title"),ClientThemePresentationSurface.Editor,session.Draft.ThemeId,key,CanApply,defs,bindings=>new[] {
                new ClientPresentationSection("tools",info==null?session.Draft.ThemeId:Name(info.Name),Safe((info?.Source??"")+" · "+(info?.Author??""))+"\n"+string.Format(L("Phinix_theme_unusedCount"),catalog.CountUnused(session.Draft)),bindings.Where(b=>b.Action.OwnerId!=Services.Owner.ExtensionId||b.Action.Id!="clear"&&b.Action.Id!="inherit"),new ClientPresentationStatus(Safe(status),session.Inputs.Values.Any(v=>v.Invalid)||!CanApply?ClientPresentationTone.Warning:messageTone),fields:new[]{new ClientPresentationTextField("export-path",L("Phinix_theme_packagePath"),filePath,maxLength:4096)}).WithWrappedActions(),
                new ClientPresentationSection("key-tools","","",fields:new[]{new ClientPresentationTextField("item-search",L("Phinix_theme_searchItems"),itemSearch,maxLength:256)}),
                new ClientPresentationSection("key-list","","",list:new ClientPresentationList("keys",rows,L("Phinix_theme_selectItem"),selectedRow)),
                new ClientPresentationSection("selected",key==null?L("Phinix_theme_selectItem"):registered?Name(picked.Label):key,key==null?"":Safe(key)+"\n"+(registered?Name(picked.Description):L("Phinix_theme_itemUnavailable"))+"\n"+source,bindings.Where(b=>b.Action.OwnerId==Services.Owner.ExtensionId&&(b.Action.Id=="clear"||b.Action.Id=="inherit"))).WithWrappedActions(),
                new ClientPresentationSection("values","",limits,fields:fields),
                new ClientPresentationSection("preview",L("Phinix_theme_preview"),"").WithSwatches(swatches)
            },new ClientPresentationSplitView(3,L("Phinix_theme_searchItems"),L("Phinix_theme_preview"),.42f).WithFixedHeaders(1,1,1,KeyId(session.Draft.ThemeId+"\n"+key)));
            seenThemes=catalog.Themes;seenColors=catalog.Colors;seenParameters=catalog.Parameters;
        }
        public override void PostClose()
        {try {if(exportWindow?.IsOpen==true)exportWindow.Close();if(selector?.IsOpen==true)selector.Close();exportWindow=null;selector=null;}finally {base.PostClose();}}
    }
    internal sealed class ThemeSelectorWindow : ThemePresentationWindow
    {
        private readonly IClientThemeCatalog catalog;private readonly Func<bool> parent;private readonly Action<string> selected;private readonly string chosen;
        private readonly Dictionary<string,string> ids=new Dictionary<string,string>();private string search="";private IReadOnlyList<ClientThemeInfo> seen;
        internal ThemeSelectorWindow(ThemePresentationServices services,IClientTheme theme,IClientThemeCatalog catalog,Func<bool> parent,Action<string> selected,string chosen):base(services,theme)
        {this.catalog=catalog;this.parent=parent;this.selected=selected;this.chosen=chosen;}
        protected override bool Available=>base.Available&&parent();
        protected override bool DataChanged=>!ReferenceEquals(seen,catalog.Themes);
        protected override Func<bool> DataGuard {get {var choices=catalog.Themes;return ()=>ReferenceEquals(choices,catalog.Themes);}}
        public override Vector2 InitialSize=>new Vector2(Mathf.Min(640,UiScreenSafeArea.Current.width),Mathf.Min(560,UiScreenSafeArea.Current.height));
        protected override void Input(ClientPresentationInput input)
        {if(input.Id=="search")search=input.Value;else if(input.Id=="themes"&&ids.TryGetValue(input.Value,out var themeId)){selected(themeId);Close();}}
        protected override void Project()
        {
            ids.Clear();string selectedId=null;var rows=new List<ClientPresentationRow>();
            foreach(var theme in catalog.Themes)
            {if(search.Length>0&&(Name(theme.Name)+" "+theme.Source).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;string id="theme."+Utils.Framework.ManagedExtensions.ManagedExtensionPaths.Hash(System.Text.Encoding.UTF8.GetBytes(theme.Id));ids[id]=theme.Id;if(theme.Id==chosen)selectedId=id;rows.Add(new ClientPresentationRow(id,Name(theme.Name),Safe(theme.Source)+" · "+Safe(theme.Author)));}
            Publish(L("Phinix_theme_chooseTheme"),ClientThemePresentationSurface.Selector,chosen,null,false,new[]{new ClientActionDefinition("close",L("Phinix_theme_cancel"),()=>Close())},bindings=>new[]{
                new ClientPresentationSection("search","","",bindings,fields:new[]{new ClientPresentationTextField("search",L("Phinix_theme_searchThemes"),search,maxLength:256)}),
                new ClientPresentationSection("choices","","",list:new ClientPresentationList("themes",rows,L("Phinix_theme_noThemes"),selectedId))
            },fixedHeaders:1);seen=catalog.Themes;
        }
    }
}
