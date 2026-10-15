using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PhinixClient.Framework;
using Utils.Framework;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Utils.Framework.Themes;
using Utils.Framework.ManagedExtensions;

namespace PhinixClient
{
    internal sealed class UiTheme : IClientTheme, IClientThemeCatalog, IClientThemePackageExporter, IClientThemeRegistry, IExtensionModuleLifecycleObserver
    {
        private readonly string settingsFile;
        private readonly string userDirectory;
        private readonly Func<IEnumerable<ThemeSource>> sources;
        private readonly Action<string> diagnostic;
        private readonly Action requireMainThread;
        private Dictionary<string, ClientThemeColor> definitions = new Dictionary<string, ClientThemeColor>(StringComparer.Ordinal);
        private Dictionary<string, ClientThemeFloat> floatDefinitions = new Dictionary<string, ClientThemeFloat>(StringComparer.Ordinal);
        private Dictionary<string, ThemeDefinition> themes = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ClientThemeDraft> profiles = new Dictionary<string, ClientThemeDraft>(StringComparer.Ordinal);
        private ClientThemeSnapshot effective = new ClientThemeSnapshot(0, new Dictionary<string, ClientRgba>(), new Dictionary<string, float>());
        private readonly HashSet<Type> moduleTypes = new HashSet<Type>();
        private readonly Dictionary<IPhinixExtensionModule, string> activeModules = new Dictionary<IPhinixExtensionModule, string>(ModuleIdentity.Instance);
        private readonly Dictionary<IPhinixExtensionModule, Registration> registrations = new Dictionary<IPhinixExtensionModule, Registration>(ModuleIdentity.Instance);
        private readonly Dictionary<string, Registration> namespaces = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private IReadOnlyList<ClientThemeInfo> catalog = new ClientThemeInfo[0];
        private IReadOnlyList<ClientThemeColor> editable = new ClientThemeColor[0];
        private IReadOnlyList<ClientThemeFloat> parameters = new ClientThemeFloat[0];
        private IReadOnlyList<string> diagnostics = new string[0];
        private string selected = "builtin.default";
        public UiTheme(string settingsFile, string userDirectory, Func<IEnumerable<ThemeSource>> sources, Action<string> diagnostic, Action requireMainThread = null)
        {
            this.requireMainThread = requireMainThread ?? (() => { });
            this.requireMainThread();
            this.settingsFile = settingsFile; this.userDirectory = userDirectory; this.sources = sources; this.diagnostic = diagnostic;
            using (var defaults = new Registration(this, "builtin.host", null, null, "Phinix_theme_owner_host"))
            {
                // Host definitions have process lifetime, unlike plugin registration leases.
                defaults.RegisterColor("ui.text.primary", new ClientRgba(1f,.95f,.88f), label: "Phinix_theme_label_ui_text_primary", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_ui_text_primary");
                defaults.RegisterColor("ui.text.secondary", new ClientRgba(.55f,.52f,.48f,.8f), label: "Phinix_theme_label_ui_text_secondary", group: "Phinix_theme_group_text", description: "Phinix_theme_desc_ui_text_secondary");
                defaults.RegisterColor("ui.background", new ClientRgba(.08f,.08f,.06f,0f), label: "Phinix_theme_label_ui_background", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_ui_background");
                defaults.RegisterColor("ui.surface", new ClientRgba(1f,1f,1f,.04f), label: "Phinix_theme_label_ui_surface", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_ui_surface");
                defaults.RegisterColor("ui.separator", new ClientRgba(1f,1f,1f,.06f), label: "Phinix_theme_label_ui_separator", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_ui_separator");
                defaults.RegisterColor("ui.hover", new ClientRgba(1f,1f,1f,.08f), label: "Phinix_theme_label_ui_hover", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_ui_hover");
                defaults.RegisterColor("ui.pending", new ClientRgba(1f,1f,1f,.6f), label: "Phinix_theme_label_ui_pending", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_ui_pending");
                defaults.RegisterColor("ui.error", new ClientRgba(.85f,.3f,.25f), label: "Phinix_theme_label_ui_error", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_ui_error");
                defaults.RegisterColor("ui.success", new ClientRgba(.3f,.65f,.35f), label: "Phinix_theme_label_ui_success", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_ui_success");
                defaults.RegisterColor("ui.warning", new ClientRgba(.9f,.72f,.25f), label: "Phinix_theme_label_ui_warning", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_ui_warning");
                defaults.RegisterColor("ui.accent", new ClientRgba(.32f,.66f,.95f), label: "Phinix_theme_label_ui_accent", group: "Phinix_theme_group_surface", description: "Phinix_theme_desc_ui_accent");
                defaults.RegisterColor("ui.button.background", new ClientRgba(.223529f,.207843f,.176471f,1f), label: "Phinix_theme_label_ui_button_background", group: "Phinix_theme_group_controls", description: "Phinix_theme_desc_ui_button_background");
                defaults.RegisterColor("ui.button.hover", new ClientRgba(.286275f,.266667f,.227451f,1f), label: "Phinix_theme_label_ui_button_hover", fallbackKey: "ui.hover", group: "Phinix_theme_group_controls", description: "Phinix_theme_desc_ui_button_hover");
                defaults.RegisterColor("ui.button.pressed", new ClientRgba(.32f,.3f,.24f,1f), label: "Phinix_theme_label_ui_button_pressed", fallbackKey: "ui.accent", group: "Phinix_theme_group_controls", description: "Phinix_theme_desc_ui_button_pressed");
                defaults.RegisterColor("ui.control.disabled", new ClientRgba(.12f,.12f,.12f,.65f), label: "Phinix_theme_label_ui_control_disabled", fallbackKey: "ui.surface", group: "Phinix_theme_group_controls", description: "Phinix_theme_desc_ui_control_disabled");
                defaults.RegisterColor("ui.input.background", new ClientRgba(1,1,1), label: "Phinix_theme_label_ui_input_background", group: "Phinix_theme_group_controls", description: "Phinix_theme_desc_ui_input_background");
                defaults.RegisterColor("ui.input.focus", new ClientRgba(.32f,.66f,.95f), label: "Phinix_theme_label_ui_input_focus", fallbackKey: "ui.accent", group: "Phinix_theme_group_controls", description: "Phinix_theme_desc_ui_input_focus");
                defaults.RegisterColor("ui.scroll.track",new ClientRgba(.1f,.1f,.1f),fallbackKey:"ui.surface",label:"Phinix_theme_label_ui_scroll_track",group:"Phinix_theme_group_controls",description:"Phinix_theme_desc_ui_scroll_track");
                defaults.RegisterColor("ui.scroll.thumb",new ClientRgba(.6f,.6f,.6f),fallbackKey:"ui.text.secondary",label:"Phinix_theme_label_ui_scroll_thumb",group:"Phinix_theme_group_controls",description:"Phinix_theme_desc_ui_scroll_thumb");
                defaults.RegisterColor("ui.scroll.hover",new ClientRgba(.32f,.66f,.95f),fallbackKey:"ui.accent",label:"Phinix_theme_label_ui_scroll_hover",group:"Phinix_theme_group_controls",description:"Phinix_theme_desc_ui_scroll_hover");
                // No baseline XML values: old/custom palettes inherit their existing roles.
                defaults.RegisterColor("ui.action.primary", new ClientRgba(.32f,.66f,.95f), fallbackKey: "ui.accent", label: "Phinix_theme_label_ui_action_primary", group: "Phinix_theme_group_controls");
                defaults.RegisterColor("ui.navigation.selected", new ClientRgba(1f,1f,1f,.08f), fallbackKey: "ui.hover", label: "Phinix_theme_label_ui_navigation_selected", group: "Phinix_theme_group_controls");
                defaults.RegisterColor("ui.badge.maintained", new ClientRgba(.38f, .82f, .66f), label: "Phinix_theme_label_badge_maintained", group: "Phinix_theme_group_controls");
                defaults.RegisterColor("ui.badge.development", new ClientRgba(.95f, .68f, .36f), label: "Phinix_theme_label_badge_development", group: "Phinix_theme_group_controls");
                defaults.RegisterColor("ui.badge.external", new ClientRgba(.65f, .7f, .95f), label: "Phinix_theme_label_badge_external", group: "Phinix_theme_group_controls");
                defaults.RegisterColor("ui.badge.managed", new ClientRgba(.38f, .72f, .95f), label: "Phinix_theme_label_badge_managed", group: "Phinix_theme_group_controls");
                defaults.RegisterColor("ext.statusActive", new ClientRgba(.3f,.8f,.3f), label: "Phinix_theme_label_ext_statusActive", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_statusActive");
                defaults.RegisterColor("ext.statusFailed", new ClientRgba(1f,0f,0f), label: "Phinix_theme_label_ext_statusFailed", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_statusFailed");
                defaults.RegisterColor("ext.statusDisabled", new ClientRgba(.45f,.45f,.45f), label: "Phinix_theme_label_ext_statusDisabled", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_statusDisabled");
                defaults.RegisterColor("ext.statusDependencyDisabled", new ClientRgba(.95f,.75f,.25f), label: "Phinix_theme_label_ext_statusDependencyDisabled", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_statusDependencyDisabled");
                defaults.RegisterColor("ext.statusRegistered", new ClientRgba(.8f,.8f,.3f), label: "Phinix_theme_label_ext_statusRegistered", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_statusRegistered");
                defaults.RegisterColor("ext.statusDefault", new ClientRgba(.5f,.5f,.5f), label: "Phinix_theme_label_ext_statusDefault", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_statusDefault");
                defaults.RegisterColor("ext.logDiagnostic", new ClientRgba(.5f,.5f,.5f), label: "Phinix_theme_label_ext_logDiagnostic", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_logDiagnostic");
                defaults.RegisterColor("ext.logWarning", new ClientRgba(1f,0f,0f), label: "Phinix_theme_label_ext_logWarning", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_logWarning");
                defaults.RegisterColor("ext.headerText", new ClientRgba(.5f,.5f,.5f), label: "Phinix_theme_label_ext_headerText", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_headerText");
                defaults.RegisterColor("ext.summaryText", new ClientRgba(1f,1f,1f), label: "Phinix_theme_label_ext_summaryText", group: "Phinix_theme_group_extension", description: "Phinix_theme_desc_ext_summaryText");

                defaults.Commit();
                defaults.Keep = true;
            }
            ReadSettings(); Refresh();
        }
        public long Version => effective.Version;
        public ClientThemeSnapshot Capture() => effective;
        public event Action Changed;
        public ClientRgba PrimaryText => GetColor("ui.text.primary");
        public ClientRgba SecondaryText => GetColor("ui.text.secondary");
        public ClientRgba Background => GetColor("ui.background");
        public ClientRgba Surface => GetColor("ui.surface");
        public ClientRgba Separator => GetColor("ui.separator");
        public ClientRgba HoverHighlight => GetColor("ui.hover");
        public ClientRgba Pending => GetColor("ui.pending");
        public ClientRgba Error => GetColor("ui.error");
        public ClientRgba Success => GetColor("ui.success");
        public ClientRgba Warning => GetColor("ui.warning");
        public string SelectedThemeId => selected;
        public IReadOnlyList<ClientThemeInfo> Themes => catalog;
        public IReadOnlyList<ClientThemeColor> Colors => editable;
        public IReadOnlyList<ClientThemeFloat> Parameters => parameters;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public ClientRgba GetColor(string key)
        { if (!TryGetColor(key, out ClientRgba value)) throw new KeyNotFoundException("Unregistered theme color: " + key); return value; }
        public bool TryGetColor(string key, out ClientRgba value) => effective.TryGetColor(key, out value);
        public float GetFloat(string key, float defaultValue = 0f) => effective.GetFloat(key, defaultValue);
        internal void SetModuleTypes(IEnumerable<Type> types)
        { requireMainThread(); foreach (var type in types) moduleTypes.Add(type); }
        public void OnActivating(IPhinixExtensionModule module)
        {
            requireMainThread();
            if (module == null || !moduleTypes.Contains(module.GetType())) throw new InvalidOperationException("Theme module is not registered.");
            string identity = module.GetType().GetCustomAttributes(typeof(PhinixExtensionAttribute), true)
                .Cast<PhinixExtensionAttribute>().SingleOrDefault()?.ExtensionId ?? module.GetType().Name;
            if (!string.Equals(identity, module.ExtensionId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Theme module identity changed.");
            activeModules[module] = identity;
        }
        public IClientThemeRegistration RegisterModule(IPhinixExtensionModule module, string keyNamespace = null, string ownerLabel = null)
        {
            requireMainThread();
            if (module == null || !activeModules.TryGetValue(module, out string owner)) throw new InvalidOperationException("Theme module is not active.");
            if (registrations.ContainsKey(module)) throw new InvalidOperationException("Theme module already owns a registration.");
            if (ownerLabel != null && (ownerLabel.Length > 256 || ownerLabel.Any(char.IsControl))) throw new ArgumentException("Invalid theme owner label.");
            string prefix = keyNamespace ?? owner;
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 120 ||
                prefix.EndsWith(".", StringComparison.Ordinal) || prefix.Contains("..") ||
                prefix.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_')) ||
                prefix == "ui" || prefix.StartsWith("ui.", StringComparison.Ordinal) ||
                prefix == "ext" || prefix.StartsWith("ext.", StringComparison.Ordinal) ||
                prefix == "builtin.host" || prefix.StartsWith("builtin.host.", StringComparison.Ordinal))
                throw new ArgumentException("Invalid or reserved theme namespace.");
            if (namespaces.Keys.Any(n => n == prefix || n.StartsWith(prefix + ".", StringComparison.Ordinal) || prefix.StartsWith(n + ".", StringComparison.Ordinal)))
                throw new ArgumentException("Theme namespace already owned.");
            var lease = new Registration(this, owner, module, prefix, ownerLabel);
            registrations.Add(module, lease); namespaces.Add(prefix, lease); return lease;
        }
        public void OnStopped(IPhinixExtensionModule module)
        {
            requireMainThread();
            if (module == null) return;
            try { if (registrations.TryGetValue(module, out var owned)) owned.Dispose(); }
            finally { activeModules.Remove(module); }
        }
        public void Refresh()
        {
            requireMainThread();
            ThemeSource[] inputs;
            try { inputs = sources().Take(1024).ToArray(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { diagnostics = Array.AsReadOnly(new[] { "theme.discovery: " + error.Message }); diagnostic?.Invoke(diagnostics[0]); return; }
            var next = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal);
            next.Add("builtin.default", new ThemeDefinition { Info = new ClientThemeInfo("builtin.default", "Phinix_theme_default" , "Phinix Rework", "builtin") });
            var errors = new List<string>(); var imported = new ThemeDefinition { Info = new ClientThemeInfo("user.migrated", "Phinix_theme_migrated", "", "user") };
            bool hasPrevious = false;
            foreach (var source in inputs)
            {
                try
                {
                    var value = source.Document==null ? ThemeInputAdapter.Read(source.Path,source.Id,source.Source)
                        : ThemeInputAdapter.Read(ThemeInputAdapter.ReadXml(source.Document),source.Id,source.Source,source.Name);
                    if(source.Document!=null) value.Info=new ClientThemeInfo(source.Id,source.Name,source.Author,source.Source);
                    if (source.Id == "builtin.default") next[source.Id] = value;
                    else if (!next.ContainsKey(source.Id)) next.Add(source.Id, value);
                    else throw new InvalidDataException("Duplicate theme identity.");
                    if (source.Migrate)
                    {
                        ThemeInputAdapter.Overlay(imported, value);
                        hasPrevious = true;
                    }
                }
                catch (Exception error) when (error is InvalidDataException || error is IOException || error is System.Xml.XmlException || error is UnauthorizedAccessException || error is ArgumentException)
                { errors.Add(source.Id + ": " + error.Message); }
            }
            if (!File.Exists(settingsFile) && hasPrevious && !next.ContainsKey("user/migrated.xml"))
            {
                try
                {
                    ThemeInputAdapter.Save(Path.Combine(userDirectory, "migrated.xml"), imported);
                    imported.Info = new ClientThemeInfo("user/migrated.xml", "Phinix_theme_migrated", "", "user");
                    next[imported.Info.Id] = imported;
                    Persist(imported.Info.Id, profiles); selected = imported.Info.Id;
                }
                catch (Exception error) when (error is InvalidDataException || error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { errors.Add("theme.migration: " + error.Message); }
            }
            // Keep the active, previously verified theme when its replacement fails parsing.
            foreach (string id in new[] { "builtin.default", selected }.Distinct(StringComparer.Ordinal))
                if (inputs.Any(source => source.Id == id && File.Exists(source.Path)) && themes.TryGetValue(id, out ThemeDefinition previous) && errors.Any(e => e.StartsWith(id + ":", StringComparison.Ordinal))) next[id] = previous;
            themes = next; catalog = Array.AsReadOnly(next.Values.Select(v => v.Info).OrderBy(v => v.Source, StringComparer.Ordinal).ThenBy(v => v.Name, StringComparer.Ordinal).ToArray());
            if (!next.ContainsKey(selected)) errors.Add("theme.missing: " + selected);
            diagnostics = errors.AsReadOnly(); Rebuild();
        }
        private ClientThemeColorPreview ResolveColor(IDictionary<string, ClientThemeColor> entries,
            string themeId, ClientThemeDraft edits, string key)
        {
            themes.TryGetValue("builtin.default", out var baseline);
            themes.TryGetValue(themeId, out var active);
            for (int depth = 0; depth <= entries.Count; depth++)
            {
                var definition = entries[key];
                bool inherited = active != null && edits != null && edits.InheritedKeys.Contains(key);
                ClientRgba value = definition.DefaultColor;
                ClientThemeValueSource source = ClientThemeValueSource.Default;
                bool found = false;
                if (active != null && !inherited && edits != null && edits.Colors.TryGetValue(key, out value)) { source = ClientThemeValueSource.UserOverride; found = true; }
                else if (!inherited)
                {
                    if (active != null && active.Colors.TryGetValue(key, out value)) { source = ClientThemeValueSource.Theme; found = true; }
                    else
                    {
                        inherited = active != null && active.InheritedKeys.Contains(key);
                        if (!inherited && baseline != null && baseline.Colors.TryGetValue(key, out value)) { source = ClientThemeValueSource.BuiltInTheme; found = true; }
                    }
                }
                if (found) return new ClientThemeColorPreview(value, depth > 0 ? ClientThemeValueSource.Inherited : source, key, source);
                if (definition.FallbackKey == null) return new ClientThemeColorPreview(definition.DefaultColor,
                    depth > 0 ? ClientThemeValueSource.Inherited : ClientThemeValueSource.Default, key, ClientThemeValueSource.Default);
                key = definition.FallbackKey;
            }
            throw new InvalidOperationException("Theme fallback cycle.");
        }
        private Dictionary<string, ClientRgba> ResolveColors(IDictionary<string, ClientThemeColor> entries,
            string themeId, ClientThemeDraft edits)
        {
            var colors = new Dictionary<string, ClientRgba>(StringComparer.Ordinal);
            foreach (string key in entries.Keys) colors.Add(key, ResolveColor(entries, themeId, edits, key).Value);
            return colors;
        }
        private ClientThemeFloatPreview ResolveFloat(ClientThemeFloat definition, string themeId, ClientThemeDraft edits)
        {
            themes.TryGetValue("builtin.default", out var baseline);
            themes.TryGetValue(themeId, out var active);
            string key = definition.Key;
            bool inherited = active != null && edits != null && edits.InheritedKeys.Contains(key);
            float value = definition.DefaultValue;
            ClientThemeValueSource source = ClientThemeValueSource.Default;
            if (active != null && !inherited && edits != null && edits.Floats.TryGetValue(key, out value)) source = ClientThemeValueSource.UserOverride;
            else if (!inherited && active != null && active.Floats.TryGetValue(key, out value)) source = ClientThemeValueSource.Theme;
            else if (!inherited && !(active != null && active.InheritedKeys.Contains(key)) && baseline != null && baseline.Floats.TryGetValue(key, out value)) source = ClientThemeValueSource.BuiltInTheme;
            else value = definition.DefaultValue;
            float bounded = Math.Max(definition.Minimum, Math.Min(definition.Maximum, value));
            return new ClientThemeFloatPreview(bounded, source, bounded != value);
        }
        private static void ValidateDefinitions(IDictionary<string, ClientThemeColor> entries)
        {
            var complete = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            Action<string> visit = null;
            visit = key =>
            {
                if (complete.Contains(key)) return;
                if (!visiting.Add(key)) throw new ArgumentException("Theme fallback cycle: " + key);
                var definition = entries[key];
                if (definition.FallbackKey != null)
                {
                    if (!entries.TryGetValue(definition.FallbackKey, out var target)) throw new ArgumentException("Theme fallback is not registered: " + definition.FallbackKey);
                    if (target.Owner != "builtin.host" && target.Owner != definition.Owner) throw new ArgumentException("Theme fallback cannot depend on another plugin.");
                    visit(definition.FallbackKey);
                }
                visiting.Remove(key); complete.Add(key);
            };
            foreach (string key in entries.Keys) visit(key);
        }
        private ClientThemeSnapshot ResolveSnapshot(IDictionary<string, ClientThemeColor> colors,
            IDictionary<string, ClientThemeFloat> floatEntries, ClientThemeDraft draft = null)
        {
            string id = draft?.ThemeId ?? selected;
            if (draft == null) profiles.TryGetValue(id, out draft);
            var resolvedColors = ResolveColors(colors, id, draft);
            var floats = floatEntries.ToDictionary(d => d.Key, d => ResolveFloat(d.Value, id, draft).Value, StringComparer.Ordinal);
            return new ClientThemeSnapshot(Version + 1, resolvedColors, floats);
        }
        private void Publish(ClientThemeSnapshot next)
        {
            editable = Array.AsReadOnly(definitions.Values.OrderBy(d => d.Owner, StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal).ToArray());
            parameters = Array.AsReadOnly(floatDefinitions.Values.OrderBy(d => d.Owner, StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal).ToArray());
            effective = next;
            if (Changed != null) foreach (Action subscriber in Changed.GetInvocationList()) try { subscriber(); } catch (Exception e) { diagnostic?.Invoke("Theme observer failed: " + e.Message); }
        }
        private void Rebuild() { Publish(ResolveSnapshot(definitions, floatDefinitions)); }
        public ClientThemeDraft Edit(string themeId)
        {
            if (!themes.ContainsKey(themeId)) throw new ArgumentException("Theme unavailable.");
            profiles.TryGetValue(themeId, out var profile);
            return new ClientThemeDraft(themeId, profile?.Colors, profile?.Floats, profile?.InheritedKeys);
        }
        public ClientRgba Preview(ClientThemeDraft draft, string key) => PreviewColor(draft, key).Value;
        public ClientThemeColorPreview PreviewColor(ClientThemeDraft draft, string key)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!definitions.ContainsKey(key)) throw new KeyNotFoundException("Unregistered theme color: " + key);
            return ResolveColor(definitions, draft.ThemeId, draft, key);
        }
        public ClientThemeFloatPreview PreviewFloat(ClientThemeDraft draft, string key)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!floatDefinitions.TryGetValue(key, out var definition)) throw new KeyNotFoundException("Unregistered theme parameter: " + key);
            return ResolveFloat(definition, draft.ThemeId, draft);
        }
        public int CountUnused(ClientThemeDraft draft)
        {
            var unused = new HashSet<string>(draft.Colors.Keys.Where(key => !definitions.ContainsKey(key))
                .Concat(draft.Floats.Keys.Where(key => !floatDefinitions.ContainsKey(key)))
                .Concat(draft.InheritedKeys.Where(key => !definitions.ContainsKey(key) && !floatDefinitions.ContainsKey(key))), StringComparer.Ordinal);
            if (themes.TryGetValue(draft.ThemeId, out var input)) unused.UnionWith(input.Colors.Keys.Where(key => !definitions.ContainsKey(key))
                .Concat(input.Floats.Keys.Where(key => !floatDefinitions.ContainsKey(key)))
                .Concat(input.InheritedKeys.Where(key => !definitions.ContainsKey(key) && !floatDefinitions.ContainsKey(key))));
            return unused.Count;
        }
        private ClientThemeDraft ValidateDraft(ClientThemeDraft draft)
        {
            if (draft == null || !themes.ContainsKey(draft.ThemeId)) throw new ArgumentException("Theme unavailable.");
            if (draft.Colors.Count + draft.Floats.Count + draft.InheritedKeys.Count > 512) throw new ArgumentException("Theme edit entry limit.");
            var copy = new ClientThemeDraft(draft.ThemeId, draft.Colors, draft.Floats, draft.InheritedKeys);
            foreach (var item in copy.Colors)
            {
                ThemeInputAdapter.ValidateSettingKey(item.Key); ThemeInputAdapter.Validate(item.Value);
                if (copy.Floats.ContainsKey(item.Key) || copy.InheritedKeys.Contains(item.Key) || floatDefinitions.ContainsKey(item.Key)) throw new ArgumentException("Conflicting theme key: " + item.Key);
            }
            foreach (var item in copy.Floats)
            {
                ThemeInputAdapter.ValidateSettingKey(item.Key);
                if (!ThemeInputAdapter.Finite(item.Value) || definitions.ContainsKey(item.Key) || copy.InheritedKeys.Contains(item.Key)) throw new ArgumentException("Invalid theme parameter: " + item.Key);
                if (floatDefinitions.TryGetValue(item.Key, out var definition) && (item.Value < definition.Minimum || item.Value > definition.Maximum)) throw new ArgumentException("Theme parameter outside registered bounds: " + item.Key);
            }
            foreach (string key in copy.InheritedKeys) ThemeInputAdapter.ValidateSettingKey(key);
            return copy;
        }
        public void Apply(ClientThemeDraft draft)
        {
            requireMainThread();
            var copy = ValidateDraft(draft);
            var next = new Dictionary<string, ClientThemeDraft>(profiles, StringComparer.Ordinal) { [copy.ThemeId] = copy };
            var snapshot = ResolveSnapshot(definitions, floatDefinitions, copy);
            Persist(copy.ThemeId, next);
            profiles[copy.ThemeId] = copy; selected = copy.ThemeId; Publish(snapshot);
        }
        private void Persist(string selection, IDictionary<string, ClientThemeDraft> edits)
        { ThemeInputAdapter.SaveSettings(settingsFile, selection, edits.Values); }
        private void ReadSettings()
        {
            if (!File.Exists(settingsFile)) return;
            try
            {
                var input = ThemeInputAdapter.ReadSettings(settingsFile);
                selected = input.Selected;
                foreach (var profile in input.Profiles) profiles.Add(profile.Key, profile.Value);
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is System.Xml.XmlException || e is FormatException || e is ArgumentException || e is OverflowException)
            { diagnostic?.Invoke("Theme settings rejected: " + e.Message); }
        }
        public void Import(string path)
        {
            requireMainThread();
            var definition = ThemeInputAdapter.Read(path, "import", "user");
            string filename = "import-" + Guid.NewGuid().ToString("N") + ".xml";
            ThemeInputAdapter.Save(Path.Combine(userDirectory, filename), definition); Refresh();
        }
        public void Export(ClientThemeDraft draft, string path)
        {
            requireMainThread();
            ThemeInputAdapter.Save(path,ExportDefinition(draft));
        }
        private ThemeDefinition ExportDefinition(ClientThemeDraft draft)
        {
            var copy = ValidateDraft(draft);
            var source = themes[copy.ThemeId];
            var definition = new ThemeDefinition { Info = source.Info };
            foreach (var item in source.Colors) definition.Colors[item.Key] = item.Value;
            foreach (var item in source.Floats) definition.Floats[item.Key] = item.Value;
            definition.InheritedKeys.UnionWith(source.InheritedKeys);
            foreach (var item in copy.Colors) { definition.Colors[item.Key] = item.Value; definition.Floats.Remove(item.Key); definition.InheritedKeys.Remove(item.Key); }
            foreach (var item in copy.Floats) { definition.Floats[item.Key] = item.Value; definition.Colors.Remove(item.Key); definition.InheritedKeys.Remove(item.Key); }
            foreach (string key in copy.InheritedKeys) { definition.Colors.Remove(key); definition.Floats.Remove(key); definition.InheritedKeys.Add(key); }
            return definition;
        }
        public void ExportPackage(ClientThemeDraft draft,ClientThemeExportInfo info,string path)
        {
            requireMainThread(); if(info==null) throw new ArgumentNullException(nameof(info));
            var definition=ExportDefinition(draft); definition.Info=new ClientThemeInfo("export",info.Name,info.Author,"local");
            byte[] bytes;
            using(var memory=new MemoryStream()) {ThemeInputAdapter.Document(definition).Save(memory);bytes=memory.ToArray();}
            var package=ThemePackageBuilder.Build(info.PackageId,info.Name,info.Author,info.Version,">="+Client.CompatibilityVersion+" <1.0.0",new[]{new ThemePackageInput("main",info.Name,bytes)});
            ThemePackageBuilder.WriteNew(package,path);
        }
        internal string CapturePackageRemovalIntent(string packageId)
        {
            requireMainThread();
            return selected.StartsWith("package/"+packageId+"/",StringComparison.Ordinal) ? selected+"\n"+SettingsStamp() : "";
        }
        private string SettingsStamp()
        {
            if(!File.Exists(settingsFile)) return "missing";
            using(var file=File.OpenRead(settingsFile))
            {
                if(file.Length>ThemeInputAdapter.MaximumBytes) throw new InvalidDataException("Theme settings size limit.");
                using(var memory=new MemoryStream()) {file.CopyTo(memory);if(memory.Length>ThemeInputAdapter.MaximumBytes) throw new InvalidDataException("Theme settings size limit.");return ManagedExtensionPaths.Hash(memory.ToArray());}
            }
        }
        internal void CompletePackageRemoval(string intent)
        {
            requireMainThread(); if(string.IsNullOrEmpty(intent)) return;
            string[] parts=intent.Split('\n'); if(parts.Length!=2 || !parts[0].StartsWith("package/",StringComparison.Ordinal)) throw new InvalidDataException("Invalid removal selection intent.");
            // A later saved choice wins. Failed persistence leaves the durable removal intent available for replay.
            if(selected!=parts[0] || SettingsStamp()!=parts[1]) return;
            Persist("builtin.default",profiles);selected="builtin.default";Rebuild();
        }
        private sealed class ModuleIdentity : IEqualityComparer<IPhinixExtensionModule>
        {
            internal static readonly ModuleIdentity Instance = new ModuleIdentity();
            public bool Equals(IPhinixExtensionModule a, IPhinixExtensionModule b) => ReferenceEquals(a, b);
            public int GetHashCode(IPhinixExtensionModule module) => RuntimeHelpers.GetHashCode(module);
        }
        private sealed class Registration : IClientThemeRegistration
        {
            private UiTheme service;
            private readonly string owner, keyNamespace, ownerLabel;
            private readonly IPhinixExtensionModule module;
            private readonly Dictionary<string, ClientThemeColor> colors = new Dictionary<string, ClientThemeColor>(StringComparer.Ordinal);
            private readonly Dictionary<string, ClientThemeFloat> floats = new Dictionary<string, ClientThemeFloat>(StringComparer.Ordinal);
            private bool committed;
            internal bool Keep;
            internal Registration(UiTheme service, string owner, IPhinixExtensionModule module, string keyNamespace, string ownerLabel)
            { this.service = service; this.owner = owner; this.module = module; this.keyNamespace = keyNamespace; this.ownerLabel = ownerLabel; }
            private void Stage(string key)
            {
                if (service == null) throw new ObjectDisposedException(nameof(Registration));
                service.requireMainThread();
                if (committed) throw new InvalidOperationException("Theme registration already committed.");
                if (string.IsNullOrWhiteSpace(key) || key.Length > 160 || key.Any(char.IsControl)) throw new ArgumentException("Invalid theme key.");
                if (keyNamespace != null && !key.StartsWith(keyNamespace + ".", StringComparison.Ordinal)) throw new ArgumentException("Theme key is outside the module namespace.");
                if (colors.ContainsKey(key) || floats.ContainsKey(key)) throw new ArgumentException("Duplicate theme key: " + key);
                if (colors.Count + floats.Count >= 128) throw new ArgumentException("Theme module registration limit.");
            }
            private static void Metadata(string label, string group, string description)
            {
                if (label != null && label.Length > 256 || group != null && group.Length > 160 || description != null && description.Length > 2048)
                    throw new ArgumentException("Theme metadata limit.");
            }
            public void RegisterColor(string key, ClientRgba value, string label = null, string fallbackKey = null, string group = null, string description = null)
            {
                Stage(key); Metadata(label, group, description); ThemeInputAdapter.Validate(value);
                colors.Add(key, new ClientThemeColor(owner, key, label, value, fallbackKey, group, description, ownerLabel));
            }
            public void RegisterFloat(string key, float value, float minimum = 0f, float maximum = 1f, string label = null, string group = null, string description = null)
            {
                Stage(key); Metadata(label, group, description);
                if (!ThemeInputAdapter.Finite(value) || !ThemeInputAdapter.Finite(minimum) || !ThemeInputAdapter.Finite(maximum) || minimum > value || value > maximum) throw new ArgumentException("Invalid theme parameter bounds.");
                floats.Add(key, new ClientThemeFloat(owner, key, value, minimum, maximum, label, group, description, ownerLabel));
            }
            public void Commit()
            {
                if (service == null) throw new ObjectDisposedException(nameof(Registration));
                service.requireMainThread();
                if (committed) throw new InvalidOperationException("Theme registration already committed.");
                var nextColors = new Dictionary<string, ClientThemeColor>(service.definitions, StringComparer.Ordinal);
                var nextFloats = new Dictionary<string, ClientThemeFloat>(service.floatDefinitions, StringComparer.Ordinal);
                foreach (var entry in colors)
                { if (nextColors.ContainsKey(entry.Key) || nextFloats.ContainsKey(entry.Key)) throw new ArgumentException("Theme key already registered: " + entry.Key); nextColors.Add(entry.Key, entry.Value); }
                foreach (var entry in floats)
                { if (nextColors.ContainsKey(entry.Key) || nextFloats.ContainsKey(entry.Key)) throw new ArgumentException("Theme key already registered: " + entry.Key); nextFloats.Add(entry.Key, entry.Value); }
                if (nextColors.Count + nextFloats.Count > 4096) throw new ArgumentException("Theme registration limit.");
                ValidateDefinitions(nextColors);
                var snapshot = service.ResolveSnapshot(nextColors, nextFloats);
                service.definitions = nextColors; service.floatDefinitions = nextFloats; committed = true;
                service.Publish(snapshot);
            }
            public void Dispose()
            {
                var value = service; if (value == null || Keep) return;
                value.requireMainThread(); service = null;
                if (module != null) { value.registrations.Remove(module); value.namespaces.Remove(keyNamespace); }
                if (!committed) return;
                foreach (string key in colors.Keys) value.definitions.Remove(key);
                foreach (string key in floats.Keys) value.floatDefinitions.Remove(key);
                value.Rebuild();
            }
        }
    }
}
