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
        public static bool Button(Rect rect, string text, IClientThemeReader theme, ClientButtonKind kind = ClientButtonKind.Normal, bool enabled = true, Color? textColor = null)
        {
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && enabled;
                bool active = UnityEngine.GUI.enabled;
                bool hover = active && Mouse.IsOver(rect);
                Color background = theme.GetColor(!active ? "ui.control.disabled" : hover && Input.GetMouseButton(0)
                    ? "ui.button.pressed" : hover ? "ui.button.hover" : "ui.button.background");
                Fill(rect, theme.GetColor("ui.button.background"));
                if (!active || hover) Fill(rect, background);
                Border(rect, theme);
                UnityEngine.GUI.color = !active ? theme.SecondaryText : textColor ?? (kind == ClientButtonKind.Danger ? theme.Error : kind == ClientButtonKind.Primary ? theme.Success : theme.PrimaryText);
                return Widgets.ButtonText(rect, text, drawBackground: false, active: active) && active;
            }
        }
        public static string TextField(Rect rect, string value, IClientThemeReader theme,
            string controlName = null, string placeholder = null, int maxLength = int.MaxValue,
            Regex inputValidator = null, Color? textColor = null, Color? placeholderColor = null)
        {
            using (new ClientGuiScope(true))
            {
                if (controlName != null) UnityEngine.GUI.SetNextControlName(controlName);
                UnityEngine.GUI.color = UnityEngine.GUI.enabled ? textColor ?? theme.PrimaryText : theme.SecondaryText;
                UnityEngine.GUI.backgroundColor = theme.GetColor(UnityEngine.GUI.enabled ? "ui.input.background" : "ui.control.disabled");
                string result = Widgets.TextField(rect, value ?? "", maxLength, inputValidator);
                bool focused = controlName != null && UnityEngine.GUI.GetNameOfFocusedControl() == controlName;
                if (focused && UnityEngine.GUI.enabled)
                {
                    UnityEngine.GUI.color = theme.GetColor("ui.input.focus");
                    Widgets.DrawBox(rect);
                }
                else Border(rect, theme);
                if (string.IsNullOrEmpty(result) && !focused && !string.IsNullOrEmpty(placeholder))
                    Label(rect.ContractedBy(4), placeholder, placeholderColor ?? theme.SecondaryText);
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
