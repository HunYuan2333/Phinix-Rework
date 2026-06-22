using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    internal static class ThemeLoader
    {
        private const string DefaultThemeRelativePath = "Themes/default.xml";

        public static void Load(UiTheme theme, string modRootDir)
        {
            if (string.IsNullOrEmpty(modRootDir)) return;
            try
            {
                string defaultPath = Path.Combine(modRootDir, DefaultThemeRelativePath);
                if (File.Exists(defaultPath))
                {
                    LoadFile(theme, defaultPath);
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[Phinix] Failed to load default theme: {ex.Message}");
            }
        }

        public static void LoadUserThemes(UiTheme theme)
        {
            string configThemeDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "Phinix", "Themes");
            if (!Directory.Exists(configThemeDir)) return;

            foreach (string path in Directory.GetFiles(configThemeDir, "*.xml").OrderBy(p => p))
            {
                try
                {
                    LoadFile(theme, path);
                }
                catch (Exception ex)
                {
                    Verse.Log.Warning($"[Phinix] Failed to load theme file '{path}': {ex.Message}");
                }
            }
        }

        public static void LoadThirdPartyThemes(UiTheme theme)
        {
            foreach (ModMetaData mod in ModLister.AllInstalledMods)
            {
                if (mod == null || !mod.Active) continue;
                if (string.Equals(mod.PackageId, Client.PackageId, StringComparison.OrdinalIgnoreCase)) continue;

                string themeDir = System.IO.Path.Combine(mod.RootDir?.ToString() ?? "", "Themes");
                if (!Directory.Exists(themeDir)) continue;

                foreach (string path in Directory.GetFiles(themeDir, "*.xml").OrderBy(p => p))
                {
                    try
                    {
                        LoadFile(theme, path);
                    }
                    catch (Exception ex)
                    {
                        Verse.Log.Warning($"[Phinix] Failed to load third-party theme file '{path}': {ex.Message}");
                    }
                }
            }
        }

        private static void LoadFile(UiTheme theme, string path)
        {
            if (!File.Exists(path)) return;

            XmlDocument doc = new XmlDocument();
            doc.Load(path);

            XmlNodeList colorNodes = doc.SelectNodes("/PhinixTheme/color");
            if (colorNodes == null) return;

            foreach (XmlNode node in colorNodes)
            {
                try
                {
                    string key = GetAttribute(node, "key");
                    if (string.IsNullOrEmpty(key)) continue;

                    float r = ParseFloat(GetAttribute(node, "r"), 1f);
                    float g = ParseFloat(GetAttribute(node, "g"), 1f);
                    float b = ParseFloat(GetAttribute(node, "b"), 1f);
                    float a = ParseFloat(GetAttribute(node, "a"), 1f);

                    r = Mathf.Clamp01(r);
                    g = Mathf.Clamp01(g);
                    b = Mathf.Clamp01(b);
                    a = Mathf.Clamp01(a);

                    theme.SetCustomColor(key, new Color(r, g, b, a));
                }
                catch (Exception ex)
                {
                    Verse.Log.Warning($"[Phinix] Failed to parse color node in '{path}': {ex.Message}");
                }
            }

            // 解析 <param key="..." value="..."/> 浮点参数
            XmlNodeList paramNodes = doc.SelectNodes("/PhinixTheme/param");
            if (paramNodes != null)
            {
                foreach (XmlNode node in paramNodes)
                {
                    try
                    {
                        string key = GetAttribute(node, "key");
                        if (string.IsNullOrEmpty(key)) continue;
                        float value = ParseFloat(GetAttribute(node, "value"), 0f);
                        theme.SetFloatParam(key, value);
                    }
                    catch (Exception ex)
                    {
                        Verse.Log.Warning($"[Phinix] Failed to parse param node in '{path}': {ex.Message}");
                    }
                }
            }
        }

        private static string GetAttribute(XmlNode node, string name)
        {
            if (node.Attributes?[name] != null)
                return node.Attributes[name].Value;
            return null;
        }

        private static float ParseFloat(string value, float defaultValue)
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
            return defaultValue;
        }
    }
}
