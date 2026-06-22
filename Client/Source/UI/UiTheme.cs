using System.Collections.Generic;
using UnityEngine;
using PhinixClient.Framework;

namespace PhinixClient
{
    internal sealed class UiTheme : IUiTheme
    {
        public Color PrimaryText { get; private set; } = new Color(1.0f, 0.95f, 0.88f, 1.0f);
        public Color SecondaryText { get; private set; } = new Color(0.55f, 0.52f, 0.48f, 0.8f);
        public Color Background { get; private set; } = new Color(0.08f, 0.08f, 0.06f, 0.0f);
        public Color Surface { get; private set; } = new Color(1.0f, 1.0f, 1.0f, 0.04f);
        public Color Separator { get; private set; } = new Color(1.0f, 1.0f, 1.0f, 0.06f);
        public Color HoverHighlight { get; private set; } = new Color(1.0f, 1.0f, 1.0f, 0.08f);
        public Color Pending { get; private set; } = new Color(1.0f, 1.0f, 1.0f, 0.6f);
        public Color Error { get; private set; } = new Color(0.85f, 0.3f, 0.25f, 1.0f);
        public Color Success { get; private set; } = new Color(0.3f, 0.65f, 0.35f, 1.0f);
        public Color Warning { get; private set; } = new Color(0.9f, 0.72f, 0.25f, 1.0f);

        private readonly Dictionary<string, Color> customColors = new Dictionary<string, Color>();
        private readonly Dictionary<string, Color> defaultColors = new Dictionary<string, Color>();
        private readonly Dictionary<string, float> customFloats = new Dictionary<string, float>();
        private readonly Dictionary<string, float> defaultFloats = new Dictionary<string, float>();
        private readonly string modRootDir;
        private bool loaded;

        public UiTheme(string modRootDir)
        {
            this.modRootDir = modRootDir;
        }

        public void RegisterColor(string key, Color defaultColor)
        {
            if (string.IsNullOrEmpty(key)) return;
            defaultColors[key] = defaultColor;
            if (!customColors.ContainsKey(key))
            {
                customColors[key] = defaultColor;
            }
        }

        public Color GetColor(string key)
        {
            if (TryGetColor(key, out Color color))
                return color;
            return default(Color);
        }

        public bool TryGetColor(string key, out Color color)
        {
            if (customColors.TryGetValue(key, out color))
                return true;
            if (defaultColors.TryGetValue(key, out color))
                return true;

            // 平台基础色映射——保留向后兼容，后续可移除此 switch
            switch (key)
            {
                case "primaryText": color = PrimaryText; return true;
                case "secondaryText": color = SecondaryText; return true;
                case "background": color = Background; return true;
                case "surface": color = Surface; return true;
                case "separator": color = Separator; return true;
                case "hoverHighlight": color = HoverHighlight; return true;
                case "pending": color = Pending; return true;
                case "error": color = Error; return true;
                case "success": color = Success; return true;
                case "warning": color = Warning; return true;
            }

            return false;
        }

        // ─── Themeable float parameters ──────────────────────────────

        public void RegisterFloat(string key, float defaultValue)
        {
            if (string.IsNullOrEmpty(key)) return;
            defaultFloats[key] = defaultValue;
            if (!customFloats.ContainsKey(key))
            {
                customFloats[key] = defaultValue;
            }
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (string.IsNullOrEmpty(key)) return defaultValue;
            if (customFloats.TryGetValue(key, out float value)) return value;
            if (defaultFloats.TryGetValue(key, out value)) return value;
            return defaultValue;
        }

        /// <summary>
        /// 由 ThemeLoader 调用来覆盖自定义浮点值。
        /// </summary>
        internal void SetFloatParam(string key, float value)
        {
            if (string.IsNullOrEmpty(key)) return;
            customFloats[key] = value;
        }

        // ─── Reload ──────────────────────────────────────────────────

        public void Reload()
        {
            if (!loaded)
            {
                loaded = true;
                LoadFromThemes();
            }
            else
            {
                customColors.Clear();
                foreach (var kvp in defaultColors)
                {
                    customColors[kvp.Key] = kvp.Value;
                }
                customFloats.Clear();
                foreach (var kvp in defaultFloats)
                {
                    customFloats[kvp.Key] = kvp.Value;
                }
                ReadAllThemeFiles();
            }
        }

        private void LoadFromThemes()
        {
            customColors.Clear();
            foreach (var kvp in defaultColors)
            {
                customColors[kvp.Key] = kvp.Value;
            }
            customFloats.Clear();
            foreach (var kvp in defaultFloats)
            {
                customFloats[kvp.Key] = kvp.Value;
            }
            ReadAllThemeFiles();
        }

        private void ReadAllThemeFiles()
        {
            ThemeLoader.Load(this, modRootDir);
            ThemeLoader.LoadThirdPartyThemes(this);
            ThemeLoader.LoadUserThemes(this);

            SetPlatformProperty("primaryText", v => PrimaryText = v);
            SetPlatformProperty("secondaryText", v => SecondaryText = v);
            SetPlatformProperty("background", v => Background = v);
            SetPlatformProperty("surface", v => Surface = v);
            SetPlatformProperty("separator", v => Separator = v);
            SetPlatformProperty("hoverHighlight", v => HoverHighlight = v);
            SetPlatformProperty("pending", v => Pending = v);
            SetPlatformProperty("error", v => Error = v);
            SetPlatformProperty("success", v => Success = v);
            SetPlatformProperty("warning", v => Warning = v);
        }

        internal void SetCustomColor(string key, Color color)
        {
            if (string.IsNullOrEmpty(key)) return;
            customColors[key] = color;
        }

        private void SetPlatformProperty(string key, System.Action<Color> setter)
        {
            if (customColors.TryGetValue(key, out Color color))
            {
                setter(color);
            }
        }
    }
}
