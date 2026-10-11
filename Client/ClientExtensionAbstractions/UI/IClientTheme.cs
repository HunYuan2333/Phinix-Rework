using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Current client contract. File versions and legacy keys belong to input adapters.
    public interface IClientThemeReader
    {
        long Version { get; }
        Color PrimaryText { get; }
        Color SecondaryText { get; }
        Color Background { get; }
        Color Surface { get; }
        Color Separator { get; }
        Color HoverHighlight { get; }
        Color Pending { get; }
        Color Error { get; }
        Color Success { get; }
        Color Warning { get; }
        Color GetColor(string key);
        bool TryGetColor(string key, out Color color);
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
        void RegisterColor(string key, Color defaultColor, string label = null, string fallbackKey = null, string group = null, string description = null);
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
        public ClientThemeColor(string owner, string key, string label, Color defaultColor, string fallbackKey = null, string group = null, string description = null, string ownerLabel = null)
        { Owner = owner; Key = key; Label = label ?? key; DefaultColor = defaultColor; FallbackKey = fallbackKey; Group = group; Description = description; OwnerLabel = ownerLabel ?? owner; }
        public string Owner { get; }
        public string Key { get; }
        public string Label { get; }
        public Color DefaultColor { get; }
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
        public ClientThemeColorPreview(Color value, ClientThemeValueSource source, string sourceKey, ClientThemeValueSource resolvedSource)
        { Value = value; Source = source; SourceKey = sourceKey; ResolvedSource = resolvedSource; }
        public Color Value { get; }
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
        private readonly IReadOnlyDictionary<string, Color> colors;
        private readonly IReadOnlyDictionary<string, float> floats;
        public ClientThemeSnapshot(long version, IDictionary<string, Color> colors, IDictionary<string, float> floats)
        {
            Version = version;
            this.colors = new ReadOnlyDictionary<string, Color>(new Dictionary<string, Color>(colors, StringComparer.Ordinal));
            this.floats = new ReadOnlyDictionary<string, float>(new Dictionary<string, float>(floats, StringComparer.Ordinal));
        }
        public long Version { get; }
        public Color PrimaryText => GetColor("ui.text.primary");
        public Color SecondaryText => GetColor("ui.text.secondary");
        public Color Background => GetColor("ui.background");
        public Color Surface => GetColor("ui.surface");
        public Color Separator => GetColor("ui.separator");
        public Color HoverHighlight => GetColor("ui.hover");
        public Color Pending => GetColor("ui.pending");
        public Color Error => GetColor("ui.error");
        public Color Success => GetColor("ui.success");
        public Color Warning => GetColor("ui.warning");
        public Color GetColor(string key)
        { if (!TryGetColor(key, out Color value)) throw new KeyNotFoundException("Unregistered theme color: " + key); return value; }
        public bool TryGetColor(string key, out Color value) => colors.TryGetValue(key, out value);
        public float GetFloat(string key, float defaultValue = 0f) => floats.TryGetValue(key, out float value) ? value : defaultValue;
    }

    public sealed class ClientThemeDraft
    {
        public ClientThemeDraft(string themeId, IDictionary<string, Color> colors, IDictionary<string, float> floats = null, IEnumerable<string> inheritedKeys = null)
        { ThemeId = themeId; Colors = new Dictionary<string, Color>(colors ?? new Dictionary<string, Color>(), StringComparer.Ordinal);
          Floats = new Dictionary<string, float>(floats ?? new Dictionary<string, float>(), StringComparer.Ordinal);
          InheritedKeys = new HashSet<string>(inheritedKeys ?? new string[0], StringComparer.Ordinal); }
        public string ThemeId { get; }
        // Window-owned draft; applying takes a validated defensive copy.
        public Dictionary<string, Color> Colors { get; }
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
        Color Preview(ClientThemeDraft draft, string key);
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
