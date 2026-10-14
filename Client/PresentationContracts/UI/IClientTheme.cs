using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Current client contract. File versions and legacy keys belong to input adapters.
    public interface IClientThemeReader
    {
        long Version { get; }
        ClientRgba PrimaryText { get; }
        ClientRgba SecondaryText { get; }
        ClientRgba Background { get; }
        ClientRgba Surface { get; }
        ClientRgba Separator { get; }
        ClientRgba HoverHighlight { get; }
        ClientRgba Pending { get; }
        ClientRgba Error { get; }
        ClientRgba Success { get; }
        ClientRgba Warning { get; }
        ClientRgba GetColor(string key);
        bool TryGetColor(string key, out ClientRgba color);
        float GetFloat(string key, float defaultValue = 0f);
    }

    public interface IClientTheme : IClientThemeReader
    {
        event Action Changed;
        ClientThemeSnapshot Capture();
    }

    public interface IClientThemeRegistry
    {
        // Called during activation. The host binds the owner to this exact module instance.
        IClientThemeRegistration RegisterModule(IPhinixExtensionModule module, string keyNamespace = null, string ownerLabel = null);
    }

    public interface IClientThemeRegistration : IDisposable
    {
        void RegisterColor(string key, ClientRgba defaultColor, string label = null, string fallbackKey = null, string group = null, string description = null);
        void RegisterFloat(string key, float defaultValue, float minimum = 0f, float maximum = 1f, string label = null, string group = null, string description = null);
        // Validates and publishes the entire batch once; disposal revokes a committed batch.
        void Commit();
    }

    public sealed class ClientThemeInfo
    {
        public ClientThemeInfo(string id, string name, string author, string source)
        { Id = id; Name = name; Author = author; Source = source; }
        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
        public string Source { get; }
    }

    public sealed class ClientThemeColor
    {
        public ClientThemeColor(string owner, string key, string label, ClientRgba defaultColor, string fallbackKey = null, string group = null, string description = null, string ownerLabel = null)
        { Owner = owner; Key = key; Label = label ?? key; DefaultColor = defaultColor; FallbackKey = fallbackKey; Group = group; Description = description; OwnerLabel = ownerLabel ?? owner; }
        public string Owner { get; }
        public string Key { get; }
        public string Label { get; }
        public ClientRgba DefaultColor { get; }
        public string FallbackKey { get; }
        public string Group { get; }
        public string Description { get; }
        public string OwnerLabel { get; }
    }

    public sealed class ClientThemeFloat
    {
        public ClientThemeFloat(string owner, string key, float defaultValue, float minimum, float maximum,
            string label = null, string group = null, string description = null, string ownerLabel = null)
        { Owner = owner; Key = key; DefaultValue = defaultValue; Minimum = minimum; Maximum = maximum; Label = label ?? key; Group = group; Description = description; OwnerLabel = ownerLabel ?? owner; }
        public string Owner { get; }
        public string Key { get; }
        public string Label { get; }
        public string Group { get; }
        public string Description { get; }
        public string OwnerLabel { get; }
        public float DefaultValue { get; }
        public float Minimum { get; }
        public float Maximum { get; }
    }

    public enum ClientThemeValueSource { UserOverride, Theme, BuiltInTheme, Inherited, Default }
    public sealed class ClientThemeColorPreview
    {
        public ClientThemeColorPreview(ClientRgba value, ClientThemeValueSource source, string sourceKey, ClientThemeValueSource resolvedSource)
        { Value = value; Source = source; SourceKey = sourceKey; ResolvedSource = resolvedSource; }
        public ClientRgba Value { get; }
        public ClientThemeValueSource Source { get; }
        public string SourceKey { get; }
        public ClientThemeValueSource ResolvedSource { get; }
    }
    public sealed class ClientThemeFloatPreview
    {
        public ClientThemeFloatPreview(float value, ClientThemeValueSource source, bool clamped)
        { Value = value; Source = source; Clamped = clamped; }
        public float Value { get; }
        public ClientThemeValueSource Source { get; }
        public bool Clamped { get; }
    }

    public sealed class ClientThemeSnapshot : IClientThemeReader
    {
        private readonly IReadOnlyDictionary<string, ClientRgba> colors;
        private readonly IReadOnlyDictionary<string, float> floats;
        public ClientThemeSnapshot(long version, IDictionary<string, ClientRgba> colors, IDictionary<string, float> floats)
        {
            Version = version;
            this.colors = new ReadOnlyDictionary<string, ClientRgba>(new Dictionary<string, ClientRgba>(colors, StringComparer.Ordinal));
            this.floats = new ReadOnlyDictionary<string, float>(new Dictionary<string, float>(floats, StringComparer.Ordinal));
        }
        public long Version { get; }
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
        public ClientRgba GetColor(string key)
        { if (!TryGetColor(key, out ClientRgba value)) throw new KeyNotFoundException("Unregistered theme color: " + key); return value; }
        public bool TryGetColor(string key, out ClientRgba value) => colors.TryGetValue(key, out value);
        public float GetFloat(string key, float defaultValue = 0f) => floats.TryGetValue(key, out float value) ? value : defaultValue;
    }

    public sealed class ClientThemeDraft
    {
        public ClientThemeDraft(string themeId, IDictionary<string, ClientRgba> colors, IDictionary<string, float> floats = null, IEnumerable<string> inheritedKeys = null)
        { ThemeId = themeId; Colors = new Dictionary<string, ClientRgba>(colors ?? new Dictionary<string, ClientRgba>(), StringComparer.Ordinal);
          Floats = new Dictionary<string, float>(floats ?? new Dictionary<string, float>(), StringComparer.Ordinal);
          InheritedKeys = new HashSet<string>(inheritedKeys ?? new string[0], StringComparer.Ordinal); }
        public string ThemeId { get; }
        // Window-owned draft; applying takes a validated defensive copy.
        public Dictionary<string, ClientRgba> Colors { get; }
        public Dictionary<string, float> Floats { get; }
        public HashSet<string> InheritedKeys { get; }
    }

    public interface IClientThemeCatalog
    {
        string SelectedThemeId { get; }
        IReadOnlyList<ClientThemeInfo> Themes { get; }
        IReadOnlyList<ClientThemeColor> Colors { get; }
        IReadOnlyList<ClientThemeFloat> Parameters { get; }
        IReadOnlyList<string> Diagnostics { get; }
        ClientThemeDraft Edit(string themeId);
        ClientRgba Preview(ClientThemeDraft draft, string key);
        ClientThemeColorPreview PreviewColor(ClientThemeDraft draft, string key);
        ClientThemeFloatPreview PreviewFloat(ClientThemeDraft draft, string key);
        int CountUnused(ClientThemeDraft draft);
        void Apply(ClientThemeDraft draft);
        void Refresh();
        void Import(string path);
        void Export(ClientThemeDraft draft, string path);
    }

    public interface IClientThemeWindowService
    {
        void OpenThemeWindow();
    }
}
