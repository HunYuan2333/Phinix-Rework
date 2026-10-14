using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using PhinixClient.Framework;

namespace PhinixClient
{
    internal sealed class ThemeDefinition
    {
        internal ClientThemeInfo Info;
        internal readonly Dictionary<string, ClientRgba> Colors = new Dictionary<string, ClientRgba>(StringComparer.Ordinal);
        internal readonly Dictionary<string, float> Floats = new Dictionary<string, float>(StringComparer.Ordinal);
        internal readonly HashSet<string> InheritedKeys = new HashSet<string>(StringComparer.Ordinal);
    }

    internal sealed class ThemeSettingsData
    {
        internal string Selected = "builtin.default";
        internal readonly Dictionary<string, ClientThemeDraft> Profiles = new Dictionary<string, ClientThemeDraft>(StringComparer.Ordinal);
    }

    // The only place that understands historical theme files and platform aliases.
    internal static class ThemeInputAdapter
    {
        internal const int MaximumBytes = 1024 * 1024;
        private static readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"primaryText", "ui.text.primary"}, {"secondaryText", "ui.text.secondary"},
            {"background", "ui.background"}, {"surface", "ui.surface"}, {"separator", "ui.separator"},
            {"hoverHighlight", "ui.hover"}, {"pending", "ui.pending"}, {"error", "ui.error"},
            {"success", "ui.success"}, {"warning", "ui.warning"}
        };
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static void Validate(ClientRgba value)
        {
            foreach (float component in new[] { value.Red, value.Green, value.Blue, value.Alpha })
                if (!Finite(component) || component < 0f || component > 1f) throw new InvalidDataException("Invalid theme color.");
        }
        internal static XDocument ReadXml(string path)
        { using (var file = File.OpenRead(path)) return ReadXmlStream(file); }
        internal static XDocument ReadXml(byte[] document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            using (var stream = new MemoryStream(document, false)) return ReadXmlStream(stream);
        }
        private static XDocument ReadXmlStream(Stream stream)
        {
            if (stream.Length < 1 || stream.Length > MaximumBytes) throw new InvalidDataException("Theme file size limit.");
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes }))
            {
                var document = XDocument.Load(reader);
                if (document.Root == null) throw new InvalidDataException("Missing theme root.");
                foreach (var node in document.Descendants())
                {
                    int depth = 0; for (var parent = node.Parent; parent != null; parent = parent.Parent) depth++;
                    if (depth > 8) throw new InvalidDataException("Theme XML depth limit.");
                }
                return document;
            }
        }
        internal static ThemeDefinition Read(string path, string identity, string source)
        {
            return Read(ReadXml(path),identity,source,Path.GetFileNameWithoutExtension(path));
        }
        internal static ThemeDefinition Read(XDocument document,string identity,string source,string defaultName)
        {
            if (string.IsNullOrWhiteSpace(identity) || identity.Length > 240) throw new InvalidDataException("Invalid theme identity.");
            var root = document.Root;
            if (root.Name != "PhinixTheme") throw new InvalidDataException("Unsupported theme root.");
            string version = (string)root.Attribute("version") ?? "1";
            if (version != "1" && version != "2" && version != "3") throw new InvalidDataException("Unsupported theme version.");
            var result = new ThemeDefinition { Info = new ClientThemeInfo(identity, (string)root.Attribute("name") ?? defaultName, (string)root.Attribute("author") ?? "", source) };
            int count = 0; var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in root.Elements())
            {
                if (++count > 512) throw new InvalidDataException("Theme entry limit.");
                string key = (string)node.Attribute("key");
                ValidateSettingKey(key);
                if (version == "1" && aliases.TryGetValue(key, out string canonical)) key = canonical;
                if (key == "nameColor.saturation") key = "chat.nameColor.saturation";
                else if (key == "nameColor.value") key = "chat.nameColor.value";
                if (!seen.Add(key)) throw new InvalidDataException("Duplicate theme key: " + key);
                if (node.Name == "color")
                {
                    if (result.Colors.ContainsKey(key)) throw new InvalidDataException("Duplicate color: " + key);
                    ClientRgba color = new ClientRgba(Number(node, "r", version, 1f), Number(node, "g", version, 1f), Number(node, "b", version, 1f), Number(node, "a", version, 1f));
                    Validate(color); result.Colors.Add(key, color);
                }
                else if (node.Name == "param")
                {
                    // Historical nickname parameter names were global even in v2 files.
                    if (key == "nameColor.saturation") key = "chat.nameColor.saturation";
                    else if (key == "nameColor.value") key = "chat.nameColor.value";
                    if (result.Floats.ContainsKey(key)) throw new InvalidDataException("Duplicate parameter: " + key);
                    result.Floats.Add(key, Number(node, "value", version, 0f));
                }
                else if (node.Name == "inherit" && version == "3") result.InheritedKeys.Add(key);
                else throw new InvalidDataException("Unsupported theme element.");
            }
            return result;
        }
        private static float Number(XElement node, string attribute, string version, float fallback)
        {
            string raw = (string)node.Attribute(attribute);
            if (raw == null && version == "1") return fallback;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !Finite(value))
                throw new InvalidDataException("Invalid theme number: " + attribute);
            return node.Name == "color" && version == "1" ? Math.Max(0f, Math.Min(1f, value)) : value;
        }
        internal static void Overlay(ThemeDefinition target, ThemeDefinition layer)
        {
            foreach (var item in layer.Colors)
            { target.Colors[item.Key] = item.Value; target.Floats.Remove(item.Key); target.InheritedKeys.Remove(item.Key); }
            foreach (var item in layer.Floats)
            { target.Floats[item.Key] = item.Value; target.Colors.Remove(item.Key); target.InheritedKeys.Remove(item.Key); }
            foreach (string key in layer.InheritedKeys)
            { target.Colors.Remove(key); target.Floats.Remove(key); target.InheritedKeys.Add(key); }
        }
        internal static void Save(string path, ThemeDefinition definition)
        {
            AtomicSave(path,Document(definition));
        }
        internal static XDocument Document(ThemeDefinition definition)
        {
            if (definition.Colors.Count + definition.Floats.Count + definition.InheritedKeys.Count > 512) throw new InvalidDataException("Theme entry limit.");
            foreach (var item in definition.Colors) { ValidateSettingKey(item.Key); Validate(item.Value); }
            foreach (var item in definition.Floats) { ValidateSettingKey(item.Key); if (!Finite(item.Value)) throw new InvalidDataException("Invalid theme parameter."); }
            foreach (var key in definition.InheritedKeys) ValidateSettingKey(key);
            var root = new XElement("PhinixTheme", new XAttribute("version", definition.InheritedKeys.Count > 0 ? "3" : "2"), new XAttribute("name", definition.Info.Name), new XAttribute("author", definition.Info.Author));
            foreach (var item in definition.Colors)
                root.Add(new XElement("color", new XAttribute("key", item.Key), Attr("r", item.Value.Red), Attr("g", item.Value.Green), Attr("b", item.Value.Blue), Attr("a", item.Value.Alpha)));
            foreach (var item in definition.Floats) root.Add(new XElement("param", new XAttribute("key", item.Key), Attr("value", item.Value)));
            foreach (string key in definition.InheritedKeys) root.Add(new XElement("inherit", new XAttribute("key", key)));
            return new XDocument(root);
        }
        internal static ThemeSettingsData ReadSettings(string path)
        {
            var root = ReadXml(path).Root;
            string version = (string)root.Attribute("version");
            if (root.Name != "ThemeSettings" || version != "1" && version != "2") throw new InvalidDataException("Unsupported theme settings.");
            var result = new ThemeSettingsData { Selected = (string)root.Attribute("selected") ?? "builtin.default" };
            if (string.IsNullOrWhiteSpace(result.Selected) || result.Selected.Length > 240) throw new InvalidDataException("Invalid selected theme.");
            int count = 0;
            foreach (var node in root.Elements())
            {
                if (++count > 4096) throw new InvalidDataException("Theme settings entry limit.");
                string id = (string)node.Attribute("theme"), key = (string)node.Attribute("key");
                ValidateSettingKey(key);
                if (string.IsNullOrWhiteSpace(id) || id.Length > 240) throw new InvalidDataException("Invalid theme settings identity.");
                if (!result.Profiles.TryGetValue(id, out var profile)) result.Profiles.Add(id, profile = new ClientThemeDraft(id, null));
                if (profile.Colors.ContainsKey(key) || profile.Floats.ContainsKey(key) || profile.InheritedKeys.Contains(key)) throw new InvalidDataException("Duplicate theme setting.");
                if (node.Name == "color")
                {
                    var color = new ClientRgba(Number(node, "r", "2", 0), Number(node, "g", "2", 0), Number(node, "b", "2", 0), Number(node, "a", "2", 0));
                    Validate(color); profile.Colors.Add(key, color);
                }
                else if (node.Name == "param" && version == "2") profile.Floats.Add(key, Number(node, "value", "2", 0));
                else if (node.Name == "inherit" && version == "2") profile.InheritedKeys.Add(key);
                else throw new InvalidDataException("Unsupported theme setting.");
            }
            return result;
        }
        internal static void SaveSettings(string path, string selection, IEnumerable<ClientThemeDraft> profiles)
        {
            var root = new XElement("ThemeSettings", new XAttribute("version", "2"), new XAttribute("selected", selection));
            int count = 0;
            foreach (var profile in profiles)
            {
                if (profile.ThemeId.Length > 240) throw new InvalidDataException("Invalid theme settings identity.");
                foreach (var item in profile.Colors)
                {
                    ValidateSettingKey(item.Key); Validate(item.Value);
                    root.Add(new XElement("color", new XAttribute("theme", profile.ThemeId), new XAttribute("key", item.Key), Attr("r", item.Value.Red), Attr("g", item.Value.Green), Attr("b", item.Value.Blue), Attr("a", item.Value.Alpha)));
                }
                foreach (var item in profile.Floats)
                {
                    ValidateSettingKey(item.Key);
                    if (!Finite(item.Value)) throw new InvalidDataException("Invalid theme parameter.");
                    root.Add(new XElement("param", new XAttribute("theme", profile.ThemeId), new XAttribute("key", item.Key), Attr("value", item.Value)));
                }
                foreach (var key in profile.InheritedKeys)
                { ValidateSettingKey(key); root.Add(new XElement("inherit", new XAttribute("theme", profile.ThemeId), new XAttribute("key", key))); }
                count += profile.Colors.Count + profile.Floats.Count + profile.InheritedKeys.Count;
                if (count > 4096) throw new InvalidDataException("Theme settings entry limit.");
            }
            AtomicSave(path, new XDocument(root), backupExisting: true);
        }
        internal static void ValidateSettingKey(string key)
        { if (string.IsNullOrWhiteSpace(key) || key.Length > 160 || key.Any(char.IsControl)) throw new InvalidDataException("Invalid theme key."); }
        internal static XAttribute Attr(string name, float value) => new XAttribute(name, value.ToString("R", CultureInfo.InvariantCulture));
        internal static void AtomicSave(string path, XDocument document, bool backupExisting = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { document.Save(stream); if (stream.Length > MaximumBytes) throw new InvalidDataException("Theme file size limit."); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, backupExisting ? path + ".previous" : null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
