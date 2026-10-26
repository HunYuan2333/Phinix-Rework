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
        internal ThemePresentationServices HostPresentation => presentation;
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
            internal readonly Dictionary<string,string> HexInputs=new Dictionary<string,string>(StringComparer.Ordinal);
            internal readonly HashSet<string> InvalidHex=new HashSet<string>(StringComparer.Ordinal);
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
        private bool commonOnly=true,advancedColors;
        // A beginner starting set, not a claim about measured usage. All registered items remain searchable.
        private static readonly string[] CommonKeys={"ui.background","ui.surface","ui.text.primary","ui.text.secondary","ui.input.background","ui.button.background","ui.accent","ui.action.primary","ui.navigation.selected","ui.separator"};
        private static int CommonOrder(string key)=>Array.IndexOf(CommonKeys,key);
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
        private bool InvalidInput=>session.InvalidHex.Count>0||session.Inputs.Values.Any(v=>v.Invalid);
        private bool CanApply=>!InvalidInput&&catalog.Themes.Any(t=>t.Id==session.Draft.ThemeId);
        private static string KeyId(string key)=>"key."+Utils.Framework.ManagedExtensions.ManagedExtensionPaths.Hash(System.Text.Encoding.UTF8.GetBytes(key));
        private void Select(string id)
        {if(!sessions.TryGetValue(id,out session))sessions.Add(id,session=new EditSession {Draft=catalog.Edit(id),SelectedKey="ui.background"});Invalidate();}
        private void RebuildEntries()
        {
            entries.Clear();
            foreach(var c in catalog.Colors)entries[c.Key]=new Entry {Key=c.Key,Label=c.Label,Description=c.Description,Owner=c.Owner,OwnerLabel=c.OwnerLabel,Group=c.Group,Fallback=c.FallbackKey};
            foreach(var f in catalog.Parameters)entries[f.Key]=new Entry {Key=f.Key,Label=f.Label,Description=f.Description,Owner=f.Owner,OwnerLabel=f.OwnerLabel,Group=f.Group,Parameter=f};
        }
        private ThemeDraftInput Buffer(Entry entry)
        {
            if(session.Inputs.TryGetValue(entry.Key,out var input)&&input.Dirty)return input;
            var c=entry.IsFloat?default(ClientRgba):catalog.Preview(session.Draft,entry.Key);
            var values=entry.IsFloat?new[]{catalog.PreviewFloat(session.Draft,entry.Key).Value}:new[]{c.Red,c.Green,c.Blue,c.Alpha};
            input=new ThemeDraftInput {IsFloat=entry.IsFloat,Values=values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)).ToArray()};session.Inputs[entry.Key]=input;return input;
        }
        private void SetColor(string key,ClientRgba value)
        {
            session.Draft.InheritedKeys.Remove(key);session.Draft.Floats.Remove(key);session.Draft.Colors[key]=value;
            session.Inputs.Remove(key);session.HexInputs.Remove(key);session.InvalidHex.Remove(key);
            message="";messageTone=ClientPresentationTone.Neutral;
        }
        protected override void Input(ClientPresentationInput input)
        {
            if(input.Id=="item-search")itemSearch=input.Value;
            else if((input.Id=="color-hex"||input.Id=="color-picker")&&session.SelectedKey!=null&&entries.TryGetValue(session.SelectedKey,out var selected)&&!selected.IsFloat)
            {
                if(input.Id=="color-hex")
                {
                    session.HexInputs[selected.Key]=input.Value;
                    if(ThemeDraftValues.TryHex(input.Value,catalog.Preview(session.Draft,selected.Key).Alpha,out var color)){SetColor(selected.Key,color);session.HexInputs[selected.Key]=input.Value;}
                    else session.InvalidHex.Add(selected.Key);
                }
                else if(ThemeDraftValues.TryPicker(input.Value,out var color))SetColor(selected.Key,color);
            }
            else if(input.Id.StartsWith("keys",StringComparison.Ordinal)&&keyRows.TryGetValue(input.Value,out var key))session.SelectedKey=key;
            else if(input.Id.StartsWith("value.",StringComparison.Ordinal)&&int.TryParse(input.Id.Substring(6),out var index)&&session.SelectedKey!=null&&entries.TryGetValue(session.SelectedKey,out var entry))
            {
                var buffer=Buffer(entry);message="";messageTone=ClientPresentationTone.Neutral;
                ThemeDraftValues.Change(session.Draft,entry.Key,buffer,entry.IsFloat,entry.Parameter?.Minimum??0,entry.Parameter?.Maximum??1,index,input.Value);
                if(!buffer.Invalid){session.HexInputs.Remove(entry.Key);session.InvalidHex.Remove(entry.Key);}
            }
        }
        private string Origin(ClientThemeColorPreview preview)
        {
            string source=L("Phinix_theme_source_"+preview.Source);
            if(preview.Source==ClientThemeValueSource.Inherited)source+=" → "+preview.SourceKey+" ("+L("Phinix_theme_source_"+preview.ResolvedSource)+")";
            return source;
        }
        private void ClearOverride(string key,bool inherit)
        {ThemeDraftValues.Clear(session.Draft,session.Inputs,key,inherit);session.HexInputs.Remove(key);session.InvalidHex.Remove(key);Invalidate();}
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
                new ClientActionDefinition("common",L("Phinix_theme_common"),()=>{commonOnly=true;itemSearch="";Invalidate();}),
                new ClientActionDefinition("all-items",L("Phinix_theme_allItems"),()=>{commonOnly=false;Invalidate();}),
                new ClientActionDefinition("refresh",L("Phinix_theme_refresh"),()=>Run(catalog.Refresh)),
                new ClientActionDefinition("reset",L("Phinix_theme_reset"),()=>{ThemeDraftValues.Reset(session.Draft,session.Inputs);session.HexInputs.Clear();session.InvalidHex.Clear();message=L("Phinix_theme_resetDone");messageTone=ClientPresentationTone.Neutral;Invalidate();}),
                new ClientActionDefinition("apply",L("Phinix_theme_apply"),()=>Run(()=>{catalog.Apply(session.Draft);message=L("Phinix_theme_applied");messageTone=ClientPresentationTone.Success;}),CanApply?null:L("Phinix_theme_unfinishedInput")),
                new ClientActionDefinition("export",L("Phinix_theme_export"),()=>Run(ExportTheme),CanApply?null:L("Phinix_theme_unfinishedInput")),
                new ClientActionDefinition("close",L("Phinix_theme_cancel"),()=>Close())
            };
            if(lastExportPath!=null){defs.Add(new ClientActionDefinition("copy-path",L("Phinix_theme_copyPath"),()=>GUIUtility.systemCopyBuffer=lastExportPath));defs.Add(new ClientActionDefinition("open-folder",L("Phinix_theme_openFolder"),()=>Run(OpenExportFolder)));}
            if(registered&&!picked.IsFloat)defs.Add(new ClientActionDefinition("advanced",L(advancedColors?"Phinix_theme_hideAdvanced":"Phinix_theme_advanced"),()=>{advancedColors=!advancedColors;Invalidate();}));
            if(key!=null)
            {
                defs.Add(new ClientActionDefinition("clear",L("Phinix_theme_clear"),()=>ClearOverride(key,false)));
                defs.Add(new ClientActionDefinition("inherit",L(registered&&!picked.IsFloat&&picked.Fallback!=null?"Phinix_theme_restoreInheritance":"Phinix_theme_restoreDefault"),()=>ClearOverride(key,true),registered?null:L("Phinix_theme_itemUnavailable")));
            }
            bool searching=!string.IsNullOrWhiteSpace(itemSearch);
            var visible=entries.Values.Where(entry=>searching
                ? (entry.Key+" "+Name(entry.Label)+" "+Name(entry.Description)+" "+Name(entry.OwnerLabel)+" "+Name(entry.Group)).IndexOf(itemSearch.Trim(),StringComparison.OrdinalIgnoreCase)>=0
                : !commonOnly||CommonOrder(entry.Key)>=0);
            var groups=visible.OrderBy(e=>commonOnly&&!searching?CommonOrder(e.Key):int.MaxValue)
                .ThenBy(e=>e.Owner,StringComparer.Ordinal).ThenBy(e=>e.Group,StringComparer.Ordinal).ThenBy(e=>e.Key,StringComparer.Ordinal)
                .GroupBy(e=>commonOnly&&!searching?"":Name(e.OwnerLabel)+" / "+(string.IsNullOrEmpty(e.Group)?L("Phinix_theme_group_general"):Name(e.Group)))
                .Select(g=>new {Title=g.Key,Items=g.ToArray()}).ToArray();
            // Public pages allow 16 sections. Overflow stays accessible in a final grouped list.
            var lists=new List<ClientPresentationSection>();
            for(int groupIndex=0;groupIndex<Math.Min(groups.Length,10);groupIndex++)
            {
                bool overflow=groupIndex==9&&groups.Length>10;
                var items=overflow?groups.Skip(9).SelectMany(g=>g.Items):groups[groupIndex].Items;
                var rows=new List<ClientPresentationRow>();string selectedRow=null;
                foreach(var entry in items)
                {
                    string id=KeyId(entry.Key);keyRows[id]=entry.Key;if(entry.Key==key)selectedRow=id;
                    string label=(CommonOrder(entry.Key)>=0?L("Phinix_theme_commonMark")+" ":"")+Name(entry.Label);
                    rows.Add(new ClientPresentationRow(id,label,Name(entry.OwnerLabel)+" / "+(string.IsNullOrEmpty(entry.Group)?L("Phinix_theme_group_general"):Name(entry.Group))));
                }
                string title=commonOnly&&!searching?L("Phinix_theme_common"):overflow?L("Phinix_theme_moreGroups"):groups[groupIndex].Title;
                lists.Add(new ClientPresentationSection("key-list."+groupIndex,title,"",list:new ClientPresentationList("keys."+groupIndex,rows,L("Phinix_theme_noItems"),selectedRow)));
            }
            if(lists.Count==0)lists.Add(new ClientPresentationSection("key-list.empty",L("Phinix_theme_allItems"),L("Phinix_theme_noItems")));
            var fields=new List<ClientPresentationTextField>();string limits="",source="";
            var swatches=new List<ClientPresentationSwatch>();
            if(registered)
            {
                var buffer=Buffer(picked);var c=picked.IsFloat?default(ClientRgba):catalog.Preview(session.Draft,picked.Key);
                if(!picked.IsFloat)
                {
                    fields.Add(new ClientPresentationTextField("color-picker",L("Phinix_theme_pickColor"),"").WithColorPicker(new ClientPresentationColorPicker(c,L("Phinix_theme_brightness"))));
                    if(advancedColors)fields.Add(new ClientPresentationTextField("color-hex",L("Phinix_theme_hex"),session.HexInputs.TryGetValue(key,out var rawHex)?rawHex:ThemeDraftValues.Hex(c),maxLength:32,validationText:session.InvalidHex.Contains(key)?L("Phinix_theme_invalidHex"):null));
                }
                for(int i=0;i<buffer.Values.Length;i++)
                {
                    if(!picked.IsFloat&&!advancedColors&&i<3)continue;
                    var field=new ClientPresentationTextField("value."+i,picked.IsFloat?Name(picked.Label):new[]{L("Phinix_theme_red"),L("Phinix_theme_green"),L("Phinix_theme_blue"),L("Phinix_theme_opacity")}[i],buffer.Values[i],maxLength:256,validationText:buffer.Invalid?L("Phinix_theme_invalidInput"):null);
                    if(picked.IsFloat&&picked.Parameter.Minimum<picked.Parameter.Maximum)field=field.WithSlider(new ClientPresentationSlider(picked.Parameter.Minimum,picked.Parameter.Maximum,catalog.PreviewFloat(session.Draft,key).Value));
                    if(!picked.IsFloat)field=field.WithSlider(new ClientPresentationSlider(0,1,new[]{c.Red,c.Green,c.Blue,c.Alpha}[i]));
                    fields.Add(field);
                }
                limits=picked.IsFloat?string.Format(L("Phinix_theme_parameterRange"),picked.Parameter.Minimum.ToString("R",CultureInfo.InvariantCulture),picked.Parameter.Maximum.ToString("R",CultureInfo.InvariantCulture)):L(advancedColors?"Phinix_theme_colorAdvancedHelp":"Phinix_theme_colorPickerHelp");
                source=picked.IsFloat?L("Phinix_theme_source_"+catalog.PreviewFloat(session.Draft,key).Source):Origin(catalog.PreviewColor(session.Draft,key));
                if(session.Draft.InheritedKeys.Contains(key))source+=" · "+L("Phinix_theme_forcedInheritance");
                if(picked.IsFloat&&catalog.PreviewFloat(session.Draft,key).Clamped)source+=" · "+L("Phinix_theme_parameterClamped");
                if(!picked.IsFloat)swatches.Add(new ClientPresentationSwatch("selected",Name(picked.Label),c));
            }
            foreach(string role in new[]{"ui.surface","ui.text.primary","ui.success","ui.warning","ui.error","ui.hover"})
            { var color=catalog.Preview(session.Draft,role);swatches.Add(new ClientPresentationSwatch("preview."+swatches.Count,entries.TryGetValue(role,out var previewEntry)?Name(previewEntry.Label):role,color)); }
            string status=InvalidInput?L("Phinix_theme_unfinishedInput"):!CanApply?L("Phinix_theme_missingDraftTheme"):message.Length>0?message:string.Join("\n",catalog.Diagnostics.Take(2));
            var info=catalog.Themes.FirstOrDefault(t=>t.Id==session.Draft.ThemeId);
            Publish(L("Phinix_theme_title"),ClientThemePresentationSurface.Editor,session.Draft.ThemeId,key,CanApply,defs,bindings=>
            {
                var tools=bindings.Where(b=>b.Action.OwnerId!=Services.Owner.ExtensionId||b.Action.Id!="clear"&&b.Action.Id!="inherit"&&b.Action.Id!="common"&&b.Action.Id!="all-items"&&b.Action.Id!="advanced").ToArray();
                var more=tools.Where(b=>b.Action.OwnerId==Services.Owner.ExtensionId&&b.Action.Id!="choose-theme"&&b.Action.Id!="apply"&&b.Action.Id!="close").Select(b=>b.Id).ToArray();
                var toolbar=new ClientPresentationSection("tools",info==null?session.Draft.ThemeId:Name(info.Name),Safe(info?.Author??""),tools,new ClientPresentationStatus(Safe(status),InvalidInput||!CanApply?ClientPresentationTone.Warning:messageTone)).WithWrappedActions();
                if(more.Length>0)toolbar=toolbar.WithActionMenus(new[]{new ClientPresentationActionMenu("more",L("Phinix_theme_moreTools"),more)});
                return new[] {
                toolbar,
                new ClientPresentationSection("key-tools",searching?L("Phinix_theme_searchResults"):commonOnly?L("Phinix_theme_common"):L("Phinix_theme_allItems"),L("Phinix_theme_browseHelp"),bindings.Where(b=>b.Action.OwnerId==Services.Owner.ExtensionId&&(b.Action.Id=="common"||b.Action.Id=="all-items")).Select(b=>b.WithSelection(b.Action.Id==(commonOnly?"common":"all-items"))),fields:new[]{new ClientPresentationTextField("item-search",L("Phinix_theme_searchItems"),itemSearch,maxLength:256)}).WithWrappedActions()
            }.Concat(lists).Concat(new[] {
                new ClientPresentationSection("selected",key==null?L("Phinix_theme_selectItem"):registered?Name(picked.Label):key,key==null?"":(registered?Name(picked.Description):L("Phinix_theme_itemUnavailable"))+"\n"+L("Phinix_theme_technicalDetails")+": "+Safe(key)+" · "+source,bindings.Where(b=>b.Action.OwnerId==Services.Owner.ExtensionId&&(b.Action.Id=="clear"||b.Action.Id=="inherit"||b.Action.Id=="advanced"))).WithWrappedActions().WithSwatches(swatches.Where(s=>s.Id=="selected")),
                new ClientPresentationSection("values","",limits,fields:fields),
                new ClientPresentationSection("preview",L("Phinix_theme_preview"),"").WithSwatches(swatches.Where(s=>s.Id!="selected"))
            });},new ClientPresentationSplitView(2+lists.Count,L("Phinix_theme_searchItems"),L("Phinix_theme_preview"),.42f).WithFixedHeaders(1,1,1,KeyId(session.Draft.ThemeId+"\n"+key)));
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
