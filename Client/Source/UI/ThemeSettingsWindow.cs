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
    internal sealed class ClientThemeWindowService : IClientThemeWindowService
    {
        private readonly IClientTheme theme;
        private readonly IClientThemeCatalog catalog;
        private ThemeSettingsWindow window;
        private readonly IClientThemePackageExporter exporter;
        private readonly IClientSettingsContext settings;
        internal ClientThemeWindowService(IClientTheme theme, IClientThemeCatalog catalog,IClientThemePackageExporter exporter,IClientSettingsContext settings) { this.theme = theme; this.catalog = catalog;this.exporter=exporter;this.settings=settings; }
        public void OpenThemeWindow()
        {
            if (!UnityData.IsInMainThread) throw new InvalidOperationException("Theme window requires the game main thread.");
            if (window != null && window.IsOpen) return;
            window = new ThemeSettingsWindow(theme, catalog,exporter,settings); Find.WindowStack.Add(window);
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
    internal sealed class ThemeSettingsWindow : Window
    {
        private sealed class InputBuffer
        {
            internal bool IsFloat, Invalid, Dirty;
            internal string[] Values;
        }
        private sealed class EditSession
        {
            internal ClientThemeDraft Draft;
            internal string SelectedKey;
            internal Vector2 ItemScroll, EditorScroll;
            internal readonly Dictionary<string, InputBuffer> Inputs = new Dictionary<string, InputBuffer>(StringComparer.Ordinal);
        }
        private sealed class Entry
        {
            internal string Key, Label, Description, Owner, OwnerLabel, Group, Fallback;
            internal ClientThemeFloat Parameter;
            internal bool Header;
            internal bool IsFloat => Parameter != null;
        }
        private readonly IClientTheme theme;
        private readonly IClientThemeCatalog catalog;
        private readonly IClientThemePackageExporter exporter;
        private readonly IClientSettingsContext settings;
        private ThemePackageExportWindow exportWindow;
        private readonly Dictionary<string, EditSession> sessions = new Dictionary<string, EditSession>(StringComparer.Ordinal);
        private EditSession session;
        private IClientThemeReader palette;
        private Vector2 themeScroll;
        private string themeSearch = "", itemSearch = "", filePath = "", message = "";
        private string lastExportPath;
        private readonly List<Entry> rows = new List<Entry>();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private IReadOnlyList<ClientThemeColor> shownColors;
        private IReadOnlyList<ClientThemeFloat> shownParameters;
        private object shownLanguage;
        private string shownSearch;
        private static readonly string[] roleKeys = { "ui.success", "ui.warning", "ui.error", "ui.hover" };
        internal ThemeSettingsWindow(IClientTheme theme, IClientThemeCatalog catalog,IClientThemePackageExporter exporter,IClientSettingsContext settings)
        {
            this.theme = theme; this.catalog = catalog;this.exporter=exporter;this.settings=settings;
            doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true; draggable = true; resizeable = true;
            Select(catalog.Themes.Any(t => t.Id == catalog.SelectedThemeId) ? catalog.SelectedThemeId : "builtin.default");
        }
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1040f, UiScreenSafeArea.Current.width), Mathf.Min(800f, UiScreenSafeArea.Current.height));
        public override void WindowUpdate()
        { base.WindowUpdate(); windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(360, 540)); }
        private static string L(string key) => key.Translate();
        private static string Name(string value) => string.IsNullOrEmpty(value) ? "" : Translator.CanTranslate(value) ? L(value) : value;
        private bool CanApply => !session.Inputs.Values.Any(v => v.Invalid) && catalog.Themes.Any(t => t.Id == session.Draft.ThemeId);
        private void Select(string id)
        {
            if (!sessions.TryGetValue(id, out session))
                sessions.Add(id, session = new EditSession { Draft = catalog.Edit(id) });
        }
        public override void DoWindowContents(Rect rect)
        {
            palette = theme.Capture();
            using (new ClientGuiScope(true))
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                ClientWidgets.Label(new Rect(0, 0, rect.width, 28), L("Phinix_theme_title"), palette);
                themeSearch = ClientWidgets.TextField(new Rect(0, 34, rect.width, 28), themeSearch, palette, controlName: "phinix.theme.search", placeholder: L("Phinix_theme_searchThemes"));
                TooltipHandler.TipRegion(new Rect(0, 34, rect.width, 28), L("Phinix_theme_searchThemes"));
                bool compact = rect.width < 700;
                float exportActionsHeight = lastExportPath == null ? 0 : 34;
                Rect body = new Rect(0, 70, rect.width, Mathf.Max(0, rect.height - (compact ? 228 : 194) - exportActionsHeight));
                float leftWidth = compact ? body.width : Mathf.Min(240, body.width * .27f);
                Rect list = new Rect(body.x, body.y, leftWidth, compact ? Mathf.Min(110, body.height * .25f) : body.height);
                Rect editor = compact ? new Rect(body.x, list.yMax + 8, body.width, Mathf.Max(0, body.yMax - list.yMax - 8))
                    : new Rect(list.xMax + 12, body.y, Mathf.Max(0, body.width - leftWidth - 12), body.height);
                DrawThemes(list); DrawEditor(editor);
                float toolbarHeight = compact ? 68 : 34;
                float y = rect.height - toolbarHeight - 78 - exportActionsHeight;
                filePath = ClientWidgets.TextField(new Rect(0, y, Mathf.Max(0, rect.width - 90), 28), filePath, palette, controlName: "phinix.theme.path");
                if (ClientWidgets.Button(new Rect(rect.width - 84, y, 84, 28), L("Phinix_theme_export"), palette, enabled: CanApply)) Run(ExportTheme);
                string status = session.Inputs.Values.Any(v => v.Invalid) ? L("Phinix_theme_unfinishedInput")
                    : !CanApply ? L("Phinix_theme_missingDraftTheme") : message.Length > 0 ? message : string.Join("\n", catalog.Diagnostics.Take(2));
                using (new ClientGuiScope(true))
                { Text.Font = GameFont.Tiny; ClientWidgets.Label(new Rect(0, y + 32, rect.width, 38), status, palette, true); }
                TooltipHandler.TipRegion(new Rect(0, y + 32, rect.width, 38), status);
                if (exportActionsHeight > 0)
                {
                    float width = (rect.width - 6) / 2;
                    if (ClientWidgets.Button(new Rect(0, y + 72, width, 28), L("Phinix_theme_copyPath"), palette)) GUIUtility.systemCopyBuffer = lastExportPath;
                    if (ClientWidgets.Button(new Rect(width + 6, y + 72, width, 28), L("Phinix_theme_openFolder"), palette)) OpenExportFolder();
                    TooltipHandler.TipRegion(new Rect(0, y + 72, rect.width, 28), lastExportPath);
                }
                int columns = compact ? 2 : 4;
                float buttonWidth = Mathf.Max(0, (rect.width - 6 * (columns - 1)) / columns);
                Func<int, Rect> buttonRect = i => new Rect((i % columns) * (buttonWidth + 6), rect.height - toolbarHeight + (i / columns) * 38, buttonWidth, 30);
                if (ClientWidgets.Button(buttonRect(0), L("Phinix_theme_refresh"), palette)) Run(catalog.Refresh);
                if (ClientWidgets.Button(buttonRect(1), L("Phinix_theme_reset"), palette))
                { session.Draft.Colors.Clear(); session.Draft.Floats.Clear(); session.Draft.InheritedKeys.Clear(); session.Inputs.Clear(); message = L("Phinix_theme_resetDone"); }
                if (ClientWidgets.Button(buttonRect(2), L("Phinix_theme_apply"), palette, ClientButtonKind.Primary, CanApply))
                    Run(() => { catalog.Apply(session.Draft); message = L("Phinix_theme_applied"); });
                if (ClientWidgets.Button(buttonRect(3), L("Phinix_theme_cancel"), palette)) Close();
            }
        }
        private void DrawThemes(Rect rect)
        {
            var choices = catalog.Themes.Where(t => themeSearch.Length == 0 || Name(t.Name).IndexOf(themeSearch, StringComparison.OrdinalIgnoreCase) >= 0 || t.Source.IndexOf(themeSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            Rect inner = rect.ContractedBy(4); Rect view = new Rect(0, 0, Mathf.Max(0, inner.width - 18), choices.Length * 54);
            Widgets.BeginScrollView(inner, ref themeScroll, view);
            try
            {
                var range = VirtualListLayout.GetFixedRange(choices.Length, 54, themeScroll.y, inner.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    var choice = choices[i]; Rect row = new Rect(0, i * 54, view.width, 50);
                    ClientWidgets.Surface(row, palette, choice.Id == session.Draft.ThemeId);
                    if (ClientWidgets.Button(new Rect(row.x + 4, row.y + 2, Mathf.Max(0, row.width - 8), 26), Name(choice.Name), palette)) Select(choice.Id);
                    ClientWidgets.LabelEllipses(new Rect(row.x + 6, row.y + 29, Mathf.Max(0, row.width - 12), 20), choice.Source + " · " + choice.Author, palette, true);
                }
            }
            finally { Widgets.EndScrollView(); }
        }
        private void RebuildRows()
        {
            if (ReferenceEquals(shownColors, catalog.Colors) && ReferenceEquals(shownParameters, catalog.Parameters)
                && ReferenceEquals(shownLanguage, LanguageDatabase.activeLanguage) && shownSearch == itemSearch) return;
            shownColors = catalog.Colors; shownParameters = catalog.Parameters; shownLanguage = LanguageDatabase.activeLanguage; shownSearch = itemSearch;
            entries.Clear(); rows.Clear();
            foreach (var item in shownColors) entries[item.Key] = new Entry { Key = item.Key, Label = item.Label, Description = item.Description, Owner = item.Owner, OwnerLabel = item.OwnerLabel, Group = item.Group, Fallback = item.FallbackKey };
            foreach (var item in shownParameters) entries[item.Key] = new Entry { Key = item.Key, Label = item.Label, Description = item.Description, Owner = item.Owner, OwnerLabel = item.OwnerLabel, Group = item.Group, Parameter = item };
            foreach (var owner in entries.Values.OrderBy(e => e.Owner, StringComparer.Ordinal).ThenBy(e => e.Group, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal)
                .Where(e => itemSearch.Length == 0 || (e.Key + " " + Name(e.Label) + " " + Name(e.OwnerLabel) + " " + Name(e.Group)).IndexOf(itemSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .GroupBy(e => e.Owner + "\n" + e.Group))
            {
                var first = owner.First();
                rows.Add(new Entry { Header = true, Label = Name(first.OwnerLabel) + " / " + (string.IsNullOrEmpty(first.Group) ? L("Phinix_theme_group_general") : Name(first.Group)) });
                rows.AddRange(owner);
            }
        }
        private string Origin(ClientThemeColorPreview value)
        {
            string source = L("Phinix_theme_source_" + value.Source);
            if (value.Source == ClientThemeValueSource.Inherited) source += " → " + value.SourceKey + " (" + L("Phinix_theme_source_" + value.ResolvedSource) + ")";
            return source;
        }
        private void DrawEditor(Rect rect)
        {
            if (rect.height < 10 || rect.width < 30) return;
            bool scrolling = rect.height < 450;
            Rect view = new Rect(0, 0, Mathf.Max(0, rect.width - (scrolling ? 18 : 0)), Mathf.Max(450, rect.height));
            Widgets.BeginScrollView(rect, ref session.EditorScroll, view);
            try { DrawEditorContents(view); } finally { Widgets.EndScrollView(); }
        }
        private void DrawEditorContents(Rect rect)
        {
            ClientWidgets.Surface(new Rect(0, 0, rect.width, 64), palette);
            ClientWidgets.Fill(new Rect(8, 8, Mathf.Max(0, rect.width - 16), 26), catalog.Preview(session.Draft, "ui.surface"));
            ClientWidgets.Label(new Rect(12, 8, Mathf.Max(0, rect.width - 24), 26), L("Phinix_theme_preview"), catalog.Preview(session.Draft, "ui.text.primary"));
            for (int i = 0; i < roleKeys.Length; i++) ClientWidgets.Fill(new Rect(8 + i * (rect.width - 16) / 4, 40, Mathf.Max(0, (rect.width - 24) / 4), 16), catalog.Preview(session.Draft, roleKeys[i]));
            itemSearch = ClientWidgets.TextField(new Rect(0, 70, rect.width, 26), itemSearch, palette, controlName: "phinix.theme.items", placeholder: L("Phinix_theme_searchItems"));
            TooltipHandler.TipRegion(new Rect(0, 70, rect.width, 26), L("Phinix_theme_searchItems"));
            int unused = catalog.CountUnused(session.Draft);
            ClientWidgets.Label(new Rect(0, 100, rect.width, 22), string.Format(L("Phinix_theme_unusedCount"), unused), palette, true);
            TooltipHandler.TipRegion(new Rect(0, 100, rect.width, 22), L("Phinix_theme_unusedTip"));
            RebuildRows();
            Rect area = new Rect(0, 126, rect.width, Mathf.Max(0, rect.height - 336));
            Rect content = new Rect(0, 0, Mathf.Max(0, area.width - 18), rows.Count * 40);
            Widgets.BeginScrollView(area, ref session.ItemScroll, content);
            try
            {
                var range = VirtualListLayout.GetFixedRange(rows.Count, 40, session.ItemScroll.y, area.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    var entry = rows[i]; Rect row = new Rect(0, i * 40, content.width, 38);
                    if (entry.Header) { ClientWidgets.LabelEllipses(row, entry.Label, palette, true); continue; }
                    ClientWidgets.Surface(row, palette, session.SelectedKey == entry.Key);
                    string origin;
                    if (entry.IsFloat)
                    {
                        var value = catalog.PreviewFloat(session.Draft, entry.Key);
                        ClientWidgets.Label(new Rect(2, row.y + 6, 26, 24), "#", palette);
                        origin = L("Phinix_theme_source_" + value.Source);
                    }
                    else
                    {
                        var value = catalog.PreviewColor(session.Draft, entry.Key);
                        ClientWidgets.Fill(new Rect(2, row.y + 6, 22, 22), value.Value); origin = Origin(value);
                    }
                    ClientWidgets.LabelEllipses(new Rect(30, row.y, Mathf.Max(0, row.width - 34), 21), Name(entry.Label), palette);
                    using (new ClientGuiScope(true))
                    { Text.Font = GameFont.Tiny; ClientWidgets.LabelEllipses(new Rect(30, row.y + 20, Mathf.Max(0, row.width - 34), 18), origin, palette, true); }
                    TooltipHandler.TipRegion(row, entry.Key + "\n" + Name(entry.Description) + "\n" + origin);
                    if (Widgets.ButtonInvisible(row)) session.SelectedKey = entry.Key;
                }
            }
            finally { Widgets.EndScrollView(); }
            DrawValueEditor(new Rect(0, rect.height - 202, rect.width, 202));
        }
        private InputBuffer Buffer(Entry entry)
        {
            if (session.Inputs.TryGetValue(entry.Key, out var input) && input.Dirty) return input;
            var values = entry.IsFloat ? new[] { catalog.PreviewFloat(session.Draft, entry.Key).Value }
                : new[] { catalog.Preview(session.Draft, entry.Key).r, catalog.Preview(session.Draft, entry.Key).g, catalog.Preview(session.Draft, entry.Key).b, catalog.Preview(session.Draft, entry.Key).a };
            input = new InputBuffer { IsFloat = entry.IsFloat, Values = values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)).ToArray() };
            session.Inputs[entry.Key] = input; return input;
        }
        private void ClearOverride(string key, bool inherit)
        {
            session.Draft.Colors.Remove(key); session.Draft.Floats.Remove(key); session.Draft.InheritedKeys.Remove(key);
            if (inherit) session.Draft.InheritedKeys.Add(key);
            session.Inputs.Remove(key);
        }
        private void DrawValueEditor(Rect rect)
        {
            string key = session.SelectedKey;
            if (key == null) { ClientWidgets.Label(rect, L("Phinix_theme_selectItem"), palette, true); return; }
            bool registered = entries.TryGetValue(key, out var entry);
            string caption = registered ? Name(entry.Label) : key;
            ClientWidgets.LabelEllipses(new Rect(rect.x, rect.y, rect.width, 24), caption, palette);
            string help = registered ? Name(entry.Description) : L("Phinix_theme_itemUnavailable");
            using (new ClientGuiScope(true))
            { Text.Font = GameFont.Tiny; ClientWidgets.Label(new Rect(rect.x, rect.y + 25, rect.width, 38), help, palette, true); }
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, 62), key + "\n" + help);
            if (registered)
            {
                var input = Buffer(entry);
                bool changed = false;
                bool valid = input.IsFloat == entry.IsFloat;
                float[] parsed = new float[input.Values.Length];
                float fieldWidth = Mathf.Max(0, (rect.width - 6 * (input.Values.Length - 1)) / input.Values.Length);
                bool sliderAvailable = entry.IsFloat && input.IsFloat && input.Values.Length == 1 && rect.width >= 220;
                if (sliderAvailable) fieldWidth = Mathf.Max(80, rect.width * .35f);
                for (int i = 0; i < input.Values.Length; i++)
                {
                    string value = ClientWidgets.TextField(new Rect(rect.x + i * (fieldWidth + 6), rect.y + 66, fieldWidth, 28), input.Values[i], palette, controlName: "phinix.theme.value." + session.Draft.ThemeId + "." + key + "." + i);
                    changed |= value != input.Values[i]; input.Values[i] = value;
                    valid &= float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]) && !float.IsNaN(parsed[i]) && !float.IsInfinity(parsed[i]);
                    valid &= entry.IsFloat ? parsed[i] >= entry.Parameter.Minimum && parsed[i] <= entry.Parameter.Maximum : parsed[i] >= 0 && parsed[i] <= 1;
                }
                if (sliderAvailable)
                {
                    float before = valid ? parsed[0] : catalog.PreviewFloat(session.Draft, key).Value;
                    float after = ClientWidgets.Slider(new Rect(rect.x + fieldWidth + 8, rect.y + 66,
                        Mathf.Max(0, rect.width - fieldWidth - 8), 28), before, entry.Parameter.Minimum, entry.Parameter.Maximum, palette);
                    if (after != before)
                    {
                        parsed[0] = after;
                        input.Values[0] = after.ToString("R", CultureInfo.InvariantCulture);
                        changed = true; valid = true;
                    }
                }
                input.Invalid = !valid;
                if (changed) input.Dirty = true;
                if (changed && valid)
                {
                    session.Draft.InheritedKeys.Remove(key);
                    if (entry.IsFloat) { session.Draft.Colors.Remove(key); session.Draft.Floats[key] = parsed[0]; }
                    else { session.Draft.Floats.Remove(key); session.Draft.Colors[key] = new Color(parsed[0], parsed[1], parsed[2], parsed[3]); }
                }
                string limits = entry.IsFloat ? string.Format(L("Phinix_theme_parameterRange"), entry.Parameter.Minimum.ToString("R", CultureInfo.InvariantCulture), entry.Parameter.Maximum.ToString("R", CultureInfo.InvariantCulture)) : "R / G / B / A [0–1]";
                if (!valid) limits = L("Phinix_theme_invalidInput") + " · " + limits;
                ClientWidgets.LabelEllipses(new Rect(rect.x, rect.y + 98, rect.width, 22), limits, palette, true);
                string source = entry.IsFloat ? L("Phinix_theme_source_" + catalog.PreviewFloat(session.Draft, key).Source) : Origin(catalog.PreviewColor(session.Draft, key));
                if (session.Draft.InheritedKeys.Contains(key)) source += " · " + L("Phinix_theme_forcedInheritance");
                if (entry.IsFloat && catalog.PreviewFloat(session.Draft, key).Clamped) source += " · " + L("Phinix_theme_parameterClamped");
                ClientWidgets.LabelEllipses(new Rect(rect.x, rect.y + 123, rect.width, 23), source, palette, true);
                TooltipHandler.TipRegion(new Rect(rect.x, rect.y + 123, rect.width, 23), source);
            }
            float buttonWidth = Mathf.Max(0, (rect.width - 6) / 2);
            if (ClientWidgets.Button(new Rect(rect.x, rect.y + 154, buttonWidth, 30), L("Phinix_theme_clear"), palette)) ClearOverride(key, false);
            string restore = registered && !entry.IsFloat && entry.Fallback != null ? "Phinix_theme_restoreInheritance" : "Phinix_theme_restoreDefault";
            if (ClientWidgets.Button(new Rect(rect.x + buttonWidth + 6, rect.y + 154, buttonWidth, 30), L(restore), palette, enabled: registered)) ClearOverride(key, true);
            TooltipHandler.TipRegion(new Rect(rect.x, rect.y + 154, buttonWidth, 30), L("Phinix_theme_clearTip"));
            TooltipHandler.TipRegion(new Rect(rect.x + buttonWidth + 6, rect.y + 154, buttonWidth, 30), L("Phinix_theme_restoreTip"));
        }
        private void Run(Action action)
        { try { action(); } catch (Exception error) { message = error.Message; } }

        private void ExportTheme()
        {
            if(exportWindow?.IsOpen==true) return;
            exportWindow=new ThemePackageExportWindow(theme,catalog,exporter,settings,session.Draft,filePath,destination=>
            {lastExportPath=destination;filePath=destination;OpenExportFolder();});
            Find.WindowStack.Add(exportWindow);
        }
        public override void PostClose() {base.PostClose();if(exportWindow?.IsOpen==true) exportWindow.Close();exportWindow=null;}
        private void OpenExportFolder()
        {
            bool launched = LocalFileLocationAdapter.TryOpenDirectory(lastExportPath);
            message = L("Phinix_theme_exported") + "\n" + lastExportPath;
            if (!launched) message += "\n" + L("Phinix_theme_openFolderFailed");
            // Keep the full path and any launch failure readable even in a narrow window.
        }
    }
}
