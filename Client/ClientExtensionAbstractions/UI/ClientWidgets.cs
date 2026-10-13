using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PhinixClient.Framework
{
    // Per-call state, including nested and throwing controls. No global skin changes.
    public struct ClientGuiScope : IDisposable
    {
        private readonly Color color, background, content;
        private readonly bool enabled, wrap;
        private readonly GameFont font;
        private readonly TextAnchor anchor;
        public ClientGuiScope(bool capture)
        {
            color = UnityEngine.GUI.color; background = UnityEngine.GUI.backgroundColor; content = UnityEngine.GUI.contentColor;
            enabled = UnityEngine.GUI.enabled; wrap = Text.WordWrap; font = Text.Font; anchor = Text.Anchor;
        }
        public void Dispose()
        {
            UnityEngine.GUI.color = color; UnityEngine.GUI.backgroundColor = background; UnityEngine.GUI.contentColor = content;
            UnityEngine.GUI.enabled = enabled; Text.WordWrap = wrap; Text.Font = font; Text.Anchor = anchor;
        }
    }

    public enum ClientButtonKind { Normal, Primary, Danger }

    public static class ClientWidgets
    {
        public static void Surface(Rect rect, IClientThemeReader theme, bool selected = false)
        { Fill(rect, selected ? theme.HoverHighlight : theme.Surface); }
        public static void Fill(Rect rect, Color color)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = Color.white; Widgets.DrawBoxSolid(rect, color); } }
        public static void Border(Rect rect, IClientThemeReader theme, int thickness = 1)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = theme.Separator; Widgets.DrawBox(rect, thickness); } }
        public static void Label(Rect rect, string text, IClientThemeReader theme, bool secondary = false)
        { Label(rect, text, secondary ? theme.SecondaryText : theme.PrimaryText); }
        public static void Label(Rect rect, string text, Color color)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = color; Widgets.Label(rect, text); } }
        public static void LabelEllipses(Rect rect, string text, IClientThemeReader theme, bool secondary = false)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = secondary ? theme.SecondaryText : theme.PrimaryText; Widgets.LabelEllipses(rect, text); } }
        public static bool RadioButton(Rect rect, string text, bool selected, IClientThemeReader theme)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = theme.PrimaryText; return Widgets.RadioButtonLabeled(rect, text, selected); } }
        public static bool RadioOption(Rect rect, string title, string description, bool selected,
            IClientThemeReader theme, Color? selectedBackground = null)
        {
            using (new ClientGuiScope(true))
            {
                if (selected) Fill(rect, selectedBackground ?? theme.HoverHighlight);
                else if (Mouse.IsOver(rect)) Fill(rect, theme.HoverHighlight);
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                var titleRect = new Rect(rect.x + 6f, rect.y + 4f, Mathf.Max(0f, rect.width - 12f), 30f);
                bool clicked = RadioButton(titleRect, title, selected, theme);
                Text.Font = GameFont.Tiny;
                var descriptionRect = new Rect(rect.x + 34f, titleRect.yMax - 2f,
                    Mathf.Max(0f, rect.width - 42f), Mathf.Max(0f, rect.yMax - titleRect.yMax));
                Label(descriptionRect, description, theme, secondary: true);
                TooltipHandler.TipRegion(rect, description);
                return clicked || (Widgets.ButtonInvisible(rect) && !selected);
            }
        }
        // Fixed layout metrics; colors still use the one theme registry and captured reader.
        public const float ControlHeight = 32f;
        public const float Gap = 8f;
        public const float CardPadding = 12f;
        public static Color Role(IClientThemeReader theme, string key, string fallback)
        { return theme.TryGetColor(key, out var value) ? value : theme.GetColor(fallback); }
        // Unity's existing GUI material draws smooth, scale-aware corners in one call.
        // White texture/material lifetime belongs to Unity; no per-control resource allocation.
        public static void RoundedSurface(Rect rect, Color color, float requestedRadius = 12f)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Max(0, Mathf.Min(requestedRadius, Mathf.Min(rect.width, rect.height) / 2f));
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.color = Color.white;
                UnityEngine.GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color,
                    Vector4.zero, new Vector4(radius, radius, radius, radius));
            }
        }
        public static void RoundedBorder(Rect rect, Color color, float radius = 12f, float thickness = 1f)
        {
            if (rect.width <= 0 || rect.height <= 0 || thickness <= 0) return;
            radius = Mathf.Max(0, Mathf.Min(radius, Mathf.Min(rect.width, rect.height) / 2f));
            thickness = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) / 2f);
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.color = Color.white;
                UnityEngine.GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color,
                    new Vector4(thickness, thickness, thickness, thickness), new Vector4(radius, radius, radius, radius));
            }
        }
        private static Color ButtonFill(IClientThemeReader theme, ClientButtonKind kind, bool active, bool hover)
        {
            if (!active) return theme.GetColor("ui.control.disabled");
            if (kind != ClientButtonKind.Primary) return theme.GetColor(hover && Input.GetMouseButton(0) ? "ui.button.pressed" : hover ? "ui.button.hover" : "ui.button.background");
            var accent = Role(theme, "ui.action.primary", "ui.accent");
            // Keep the primary role during hover/press; explicit themes retain their hue and alpha.
            float delta = hover ? (Input.GetMouseButton(0) ? -.07f : .045f) : 0;
            return new Color(Mathf.Clamp01(accent.r + delta), Mathf.Clamp01(accent.g + delta), Mathf.Clamp01(accent.b + delta), accent.a);
        }
        private static Color ButtonTextColor(IClientThemeReader theme, ClientButtonKind kind, bool active, Color? textColor)
        { return !active ? theme.SecondaryText : textColor ?? (kind == ClientButtonKind.Danger ? theme.Error : kind == ClientButtonKind.Primary ? new Color(theme.Background.r, theme.Background.g, theme.Background.b, 1) : theme.PrimaryText); }
        // Clip in a local group: native ellipsis/button helpers may reset the text anchor.
        // Full rich text stays intact; tooltips expose content that exceeds the available width.
        public static void ClippedLabel(Rect rect, string text, Color color, TextAnchor anchor, bool wrap = false)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            using (new ClientGuiScope(true))
            {
                Text.Anchor = anchor; Text.WordWrap = wrap;
                UnityEngine.GUI.BeginGroup(rect);
                try { Label(new Rect(0, 0, rect.width, rect.height), text, color); }
                finally { UnityEngine.GUI.EndGroup(); }
            }
        }
        public static bool NavigationPill(Rect rect, string label, bool selected, IClientThemeReader theme)
        {
            if (rect.width <= 0 || rect.height <= 0) return false;
            RoundedSurface(rect, selected ? Role(theme, "ui.navigation.selected", "ui.hover") : theme.GetColor("ui.button.background"));
            if (selected)
            {
                RoundedBorder(rect, Role(theme, "ui.accent", "ui.input.focus"), thickness: 2);
                RoundedSurface(new Rect(rect.x + 12, rect.yMax - 4, Mathf.Max(0, rect.width - 24), 2), Role(theme, "ui.accent", "ui.input.focus"), 1);
            }
            else
            {
                if (UnityEngine.GUI.enabled && Mouse.IsOver(rect)) RoundedSurface(rect, theme.GetColor("ui.button.hover"));
                RoundedBorder(rect, theme.Separator);
            }
            TooltipHandler.TipRegion(rect, label);
            using (new ClientGuiScope(true))
            {
                Text.Font = GameFont.Small;
                ClippedLabel(new Rect(rect.x + 8, rect.y + 2, Mathf.Max(0, rect.width - 16), Mathf.Max(0, rect.height - 4)), label, selected ? Role(theme, "ui.accent", "ui.input.focus") : theme.PrimaryText, TextAnchor.MiddleCenter);
                return UnityEngine.GUI.enabled && Widgets.ButtonInvisible(rect);
            }
        }
        // Same font, padding and arrow reserve for measurement and rendering. No fixed pixel cap.
        public static Vector2 MeasureButton(string label, float availableWidth, bool dropdown = false, float minimumWidth = 96)
        {
            using (new ClientGuiScope(true))
            {
                Text.Font = GameFont.Small; Text.WordWrap = false;
                float reserve = 20 + (dropdown ? 24 : 0);
                float width = Mathf.Min(Mathf.Max(1, availableWidth), Mathf.Max(minimumWidth, Text.CalcSize(label ?? "").x + reserve + 4));
                Text.WordWrap = true;
                float height = Mathf.Max(ControlHeight, Text.CalcHeight(label ?? "", Mathf.Max(1, width - reserve)) + 8);
                return new Vector2(width, height);
            }
        }
        public static bool Button(Rect rect, string text, IClientThemeReader theme, ClientButtonKind kind = ClientButtonKind.Normal, bool enabled = true, Color? textColor = null, float cornerRadius = 12, bool wrap = false, bool dropdown = false)
        {
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && enabled;
                bool active = UnityEngine.GUI.enabled;
                bool hover = active && Mouse.IsOver(rect);
                RoundedSurface(rect, ButtonFill(theme, kind, active, hover), cornerRadius);
                if (kind != ClientButtonKind.Primary || !active) RoundedBorder(rect, theme.Separator, cornerRadius);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; Text.WordWrap = false;
                UnityEngine.GUI.color = ButtonTextColor(theme, kind, active, textColor);
                if (dropdown) ClippedLabel(new Rect(rect.xMax - 28, rect.y + 4, 18, Mathf.Max(0, rect.height - 8)), "▾",
                    ButtonTextColor(theme, kind, active, textColor), TextAnchor.MiddleCenter);
                ClippedLabel(new Rect(rect.x + 10, rect.y + 4, Mathf.Max(0, rect.width - 20 - (dropdown ? 24 : 0)), Mathf.Max(0, rect.height - 8)), text,
                    ButtonTextColor(theme, kind, active, textColor), TextAnchor.MiddleCenter, wrap);
                TooltipHandler.TipRegion(rect, text);
                return Widgets.ButtonText(rect, "", drawBackground: false, active: active) && active;
            }
        }
        // Borrowed icon and native dimensions. Full surface is interactive; text/icon are centered as a group.
        public static bool IconButton(Rect rect, string text, Texture2D icon, Vector2 iconSize, IClientThemeReader theme,
            ClientButtonKind kind = ClientButtonKind.Normal, bool enabled = true, bool wrap = false)
        {
            if (icon == null) return Button(rect, text, theme, kind, enabled, wrap: wrap);
            if (iconSize.x <= 0 || iconSize.y <= 0 || float.IsNaN(iconSize.x) || float.IsNaN(iconSize.y) || float.IsInfinity(iconSize.x) || float.IsInfinity(iconSize.y)) throw new ArgumentOutOfRangeException(nameof(iconSize));
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && enabled;
                bool active = UnityEngine.GUI.enabled;
                RoundedSurface(rect, ButtonFill(theme, kind, active, active && Mouse.IsOver(rect)));
                if (kind != ClientButtonKind.Primary || !active) RoundedBorder(rect, theme.Separator);
                Text.Anchor = TextAnchor.MiddleCenter; Text.WordWrap = false;
                float textWidth = Mathf.Min(Text.CalcSize(text ?? "").x, Mathf.Max(0, rect.width - iconSize.x - 6 - 16));
                float groupWidth = textWidth + 6 + iconSize.x;
                float x = rect.x + (rect.width - groupWidth) / 2f;
                ClippedLabel(new Rect(x, rect.y + 4, textWidth, Mathf.Max(0, rect.height - 8)), text, ButtonTextColor(theme, kind, active, null), TextAnchor.MiddleCenter, wrap);
                UnityEngine.GUI.color = active ? Color.white : theme.SecondaryText;
                UnityEngine.GUI.DrawTexture(new Rect(x + textWidth + 6, rect.y + (rect.height - iconSize.y) / 2f, iconSize.x, iconSize.y), icon);
                return Widgets.ButtonInvisible(rect) && active;
            }
        }
        public static void LabelEllipses(Rect rect, string text, Color color)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = color; Widgets.LabelEllipses(rect, text); } }
        public static string TextField(Rect rect, string value, IClientThemeReader theme,
            string controlName = null, string placeholder = null, int maxLength = int.MaxValue,
            Regex inputValidator = null, Color? textColor = null, Color? placeholderColor = null)
        { return TextFieldCore(rect, value, theme, controlName, placeholder, maxLength, inputValidator, textColor, placeholderColor, true); }
        // For a field inside an already framed composer/card. Same Verse/Unity input, validators and IME patch path.
        public static string EmbeddedTextField(Rect rect, string value, IClientThemeReader theme,
            string controlName = null, string placeholder = null, int maxLength = int.MaxValue,
            Regex inputValidator = null, Color? textColor = null, Color? placeholderColor = null)
        { return TextFieldCore(rect, value, theme, controlName, placeholder, maxLength, inputValidator, textColor, placeholderColor, false); }
        private static string TextFieldCore(Rect rect, string value, IClientThemeReader theme, string controlName,
            string placeholder, int maxLength, Regex inputValidator, Color? textColor, Color? placeholderColor, bool framed)
        {
            using (new ClientGuiScope(true))
            {
                bool focused = controlName != null && UnityEngine.GUI.GetNameOfFocusedControl() == controlName;
                if (framed)
                {
                    RoundedSurface(rect, theme.GetColor(UnityEngine.GUI.enabled ? "ui.input.background" : "ui.control.disabled"), 8);
                    RoundedBorder(rect, focused && UnityEngine.GUI.enabled ? theme.GetColor("ui.input.focus") : theme.Separator, 8, focused && UnityEngine.GUI.enabled ? 2 : 1);
                }
                if (controlName != null) UnityEngine.GUI.SetNextControlName(controlName);
                UnityEngine.GUI.color = UnityEngine.GUI.enabled ? textColor ?? theme.PrimaryText : theme.SecondaryText;
                UnityEngine.GUI.backgroundColor = Color.clear;
                string result = Widgets.TextField(rect, value ?? "", maxLength, inputValidator);
                focused = controlName != null && UnityEngine.GUI.GetNameOfFocusedControl() == controlName;
                if (string.IsNullOrEmpty(result) && !focused && !string.IsNullOrEmpty(placeholder))
                    ClippedLabel(rect.ContractedBy(4), placeholder, placeholderColor ?? theme.SecondaryText, TextAnchor.MiddleLeft);
                return result;
            }
        }
        public static void Checkbox(Rect rect, string label, ref bool value, IClientThemeReader theme, bool enabled = true)
        {
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && enabled;
                UnityEngine.GUI.color = UnityEngine.GUI.enabled ? theme.PrimaryText : theme.SecondaryText;
                Widgets.CheckboxLabeled(rect, label, ref value, disabled: !UnityEngine.GUI.enabled);
            }
        }
        public static void Progress(Rect rect, float fraction, IClientThemeReader theme, Color? fill = null)
        {
            fraction = float.IsNaN(fraction) || float.IsInfinity(fraction) ? 0 : Mathf.Clamp01(fraction);
            Fill(rect, theme.Separator);
            Fill(new Rect(rect.x, rect.y, rect.width * fraction, rect.height), fill ?? theme.GetColor("ui.accent"));
        }
        public static float Slider(Rect rect, float value, float minimum, float maximum, IClientThemeReader theme, bool enabled = true)
        {
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && enabled;
                bool active = UnityEngine.GUI.enabled;
                UnityEngine.GUI.color = active ? theme.GetColor("ui.accent") : theme.SecondaryText;
                float result = Widgets.HorizontalSlider(rect, value, minimum, maximum);
                return active ? result : value;
            }
        }
        public static bool Link(Rect rect, string text, IClientThemeReader theme, bool enabled = true)
        {
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && enabled;
                bool active = UnityEngine.GUI.enabled;
                if (active && Mouse.IsOver(rect)) Fill(rect, theme.HoverHighlight);
                Label(rect, text, active ? theme.GetColor("ui.accent") : theme.SecondaryText);
                return Widgets.ButtonInvisible(rect) && active;
            }
        }
        public static void Tabs(Rect rect, List<TabRecord> tabs, float minimumWidth, float maximumWidth, IClientThemeReader theme)
        {
            using (new ClientGuiScope(true))
            {
                Fill(new Rect(rect.x, rect.y, rect.width, Mathf.Min(TabDrawer.TabHeight, rect.height)), theme.Surface);
                UnityEngine.GUI.color = theme.PrimaryText;
                TabDrawer.DrawTabsOverflow(rect, tabs, minimumWidth, maximumWidth);
            }
        }
        public static void Separator(Rect rect, IClientThemeReader theme)
        { using (new ClientGuiScope(true)) { UnityEngine.GUI.color = Color.white; Widgets.DrawBoxSolid(rect, theme.Separator); } }
    }
}
