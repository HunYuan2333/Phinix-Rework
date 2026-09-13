using System;
using System.Collections.Generic;
using PhinixClient;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    // Plugin-local presentation helpers; no protocol or trade state is owned here.
    internal static class TalentTradeUi
    {
        public static Rect Inset(Rect rect, float margin)
        {
            margin = Mathf.Min(margin, Mathf.Min(Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height)) / 2f);
            rect = rect.ContractedBy(margin);
            return new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
        }

        public static Rect Below(Rect rect, float height)
        {
            height = Mathf.Clamp(height, 0f, Mathf.Max(0f, rect.height));
            return new Rect(rect.x, rect.y + height, Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height - height));
        }

        public static void Label(Rect rect, string text)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            bool wrap = Text.WordWrap;
            Text.WordWrap = false;
            try { Widgets.Label(rect, text); }
            finally { Text.WordWrap = wrap; }
            TooltipHandler.TipRegion(rect, text ?? "");
        }

        public static bool Button(Rect rect, string text)
        {
            if (rect.width <= 0f || rect.height <= 0f) return false;
            TooltipHandler.TipRegion(rect, text);
            return Widgets.ButtonText(rect, text);
        }
    }

    internal sealed class TalentTabs
    {
        private readonly string[] keys;
        private readonly List<TabRecord> tabs = new List<TabRecord>();
        private object language;
        private float width = -1f;
        private float height;
        public int Selected;

        public TalentTabs(params string[] keys) { this.keys = keys; }

        public Rect Draw(Rect rect)
        {
            if (!ReferenceEquals(language, LanguageDatabase.activeLanguage) || tabs.Count == 0)
            {
                language = LanguageDatabase.activeLanguage;
                tabs.Clear();
                for (int i = 0; i < keys.Length; i++)
                {
                    int index = i;
                    tabs.Add(new TabRecord(keys[i].Translate(), () => Selected = index, () => Selected == index));
                }
                width = -1f;
            }
            if (rect.width <= 0f || rect.height <= 0f) return rect;
            if (!Mathf.Approximately(width, rect.width))
            {
                width = rect.width;
                height = TabDrawer.GetOverflowTabHeight(rect, tabs, 80f, 180f);
            }
            // Clip navigation only at physically unusable sizes.
            GUI.BeginGroup(rect);
            try { TabDrawer.DrawTabsOverflow(new Rect(0f, 0f, rect.width, rect.height), tabs, 80f, 180f); }
            finally { GUI.EndGroup(); }
            return TalentTradeUi.Below(rect, height);
        }
    }

    internal sealed class TalentToolbar
    {
        private readonly string[] labels;
        private readonly float[] widths;
        private readonly Rect[] buttons;
        private readonly string[] keys;
        private object language;
        private int cachedMode = int.MinValue;
        private readonly Action<int> activate;

        public TalentToolbar(Action<int> activate, params string[] keys)
        {
            this.activate = activate;
            this.keys = keys;
            labels = new string[keys.Length];
            widths = new float[keys.Length];
            buttons = new Rect[keys.Length];
        }

        public float Draw(Rect rect, int count, int mode = 0, string firstKey = null, int disabledIndex = -1)
        {
            if (rect.width <= 0f || rect.height <= 0f) return 0f;
            if (!ReferenceEquals(language, LanguageDatabase.activeLanguage) || cachedMode != mode)
            {
                language = LanguageDatabase.activeLanguage;
                cachedMode = mode;
                GameFont font = Text.Font;
                Text.Font = GameFont.Small;
                for (int i = 0; i < keys.Length; i++)
                {
                    labels[i] = (i == 0 && firstKey != null ? firstKey : keys[i]).Translate();
                    widths[i] = Mathf.Max(80f, Text.CalcSize(labels[i]).x + 24f);
                }
                Text.Font = font;
            }
            var layout = ResponsiveToolbarLayout.Calculate(rect, widths, count, 1,
                Mathf.Min(30f, rect.height), 6f, 1, 32f, buttons);
            for (int i = 0; i < layout.VisibleActionCount; i++)
            {
                TooltipHandler.TipRegion(buttons[i], labels[i]);
                if (Widgets.ButtonText(buttons[i], labels[i], active: i != disabledIndex)) activate(i);
            }
            if (layout.HasOverflow && Widgets.ButtonText(layout.OverflowButtonRect, "⋯"))
            {
                var options = new List<FloatMenuOption>();
                for (int i = layout.VisibleActionCount; i < count; i++)
                {
                    int index = i;
                    options.Add(new FloatMenuOption(labels[i], index == disabledIndex ? (Action)null : () => activate(index)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            return layout.Height;
        }
    }

    internal sealed class TalentCard
    {
        public string Text;
        public string Tooltip;
        public string ActionLabel;
        public float Height;
        private float textHeight;

        public TalentCard(string text, string tooltip, string actionKey, float width)
        {
            Text = text;
            Tooltip = text + "\n" + tooltip;
            ActionLabel = actionKey.Translate();
            GameFont font = Verse.Text.Font;
            bool wrap = Verse.Text.WordWrap;
            Verse.Text.Font = GameFont.Small;
            Verse.Text.WordWrap = true;
            try
            {
                textHeight = Verse.Text.CalcHeight(text, Mathf.Max(1f, width - 12f));
                float buttonHeight = Mathf.Max(30f, Verse.Text.CalcHeight(ActionLabel, Mathf.Max(1f, width - 24f)) + 8f);
                Height = 12f + textHeight + 6f + buttonHeight;
            }
            finally { Verse.Text.Font = font; Verse.Text.WordWrap = wrap; }
        }

        public bool Draw(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = TalentTradeUi.Inset(rect, 6f);
            var textRect = new Rect(inner.x, inner.y, inner.width, textHeight);
            GameFont font = Verse.Text.Font;
            bool wrap = Verse.Text.WordWrap;
            Verse.Text.Font = GameFont.Small;
            Verse.Text.WordWrap = true;
            try
            {
                Widgets.Label(textRect, Text);
                TooltipHandler.TipRegion(textRect, Tooltip);
                return Widgets.ButtonText(TalentTradeUi.Below(inner, textHeight + 6f), ActionLabel);
            }
            finally { Verse.Text.Font = font; Verse.Text.WordWrap = wrap; }
        }

        public static string Details(PawnSummary summary)
        {
            return summary == null ? "" : summary.SkillsSummary + "\n" + summary.TraitsSummary + "\n" + summary.HealthSummary;
        }

        public static string Description(PawnSummary summary)
        {
            if (summary == null) return "???";
            string race = summary.RaceDefName ?? "Human";
            string status = DefDatabase<ThingDef>.GetNamedSilentFail(race) != null ? "✓" : "✗";
            return summary.GetDisplayLabel() + "\n" + status + " " + race + " | " +
                summary.BiologicalAge + " " + "Phinix_legacyTalentTrade_ageUnit".Translate();
        }
    }

    // Full form scrolls, including its confirmation action. Geometry and pawn preview
    // are measured only when selection, language or available width changes.
    internal sealed class TalentForm
    {
        private readonly string[] keys;
        private readonly string[] labels;
        private readonly ResponsiveFormResult[] rows;
        private Vector2 scroll;
        private Pawn cachedPawn;
        private object language;
        private float width = -1f;
        private string pawnLabel;
        private string preview;
        private string confirmLabel;
        private float previewHeight;
        private float height;
        private Rect confirm;
        private float previewY;

        public TalentForm(params string[] keys)
        {
            this.keys = keys;
            labels = new string[keys.Length];
            rows = new ResponsiveFormResult[keys.Length];
        }

        public void Begin(Rect rect, Pawn pawn)
        {
            Widgets.DrawMenuSection(rect);
            rect = TalentTradeUi.Inset(rect, 6f);
            float available = Mathf.Max(1f, rect.width - 16f);
            if (width != available || !ReferenceEquals(cachedPawn, pawn) ||
                !ReferenceEquals(language, LanguageDatabase.activeLanguage))
            {
                width = available;
                cachedPawn = pawn;
                language = LanguageDatabase.activeLanguage;
                pawnLabel = pawn == null ? (string)"Phinix_legacyTalentTrade_select".Translate() : TradeablePawnUtility.GetLabel(pawn);
                PawnSummary summary = pawn == null ? null : PawnSummary.FromPawn(pawn);
                preview = summary == null ? "" : summary.GetDisplayLabel() + "\n" + TalentCard.Details(summary);
                confirmLabel = "Phinix_legacyTalentTrade_confirm".Translate();
                GameFont font = Text.Font;
                Text.Font = GameFont.Small;
                previewHeight = preview.Length == 0 ? 0f : Text.CalcHeight(preview, width);
                float y = 0f;
                for (int i = 0; i < keys.Length; i++)
                {
                    labels[i] = keys[i].Translate();
                    if (i > 0 && keys[i] != "Phinix_legacyTalentTrade_rentalMaxDays")
                        labels[i] += " (" + "Phinix_legacyTalentTrade_silver".Translate() + ")";
                    float labelWidth = Mathf.Min(200f, Text.CalcSize(labels[i]).x);
                    float rowHeight = Mathf.Max(30f, Text.CalcHeight(labels[i], Mathf.Max(1f, Mathf.Min(width, labelWidth))));
                    rows[i] = ResponsiveFormLayout.Calculate(new Rect(0f, y, width, 10000f), labelWidth,
                        140f, 0f, rowHeight, 6f, 0f);
                    y += rows[i].Height + 6f;
                    if (i == 0) { previewY = y; y += previewHeight + 6f; }
                }
                confirm = new Rect(0f, y, width, Mathf.Max(32f, Text.CalcHeight(confirmLabel, Mathf.Max(1f, width - 12f)) + 8f));
                height = confirm.yMax;
                Text.Font = font;
            }
            scroll.y = Mathf.Clamp(scroll.y, 0f, Mathf.Max(0f, height - rect.height));
            Widgets.BeginScrollView(rect, ref scroll, new Rect(0f, 0f, width, height));
            for (int i = 0; i < rows.Length; i++) Widgets.Label(rows[i].LabelRect, labels[i]);
            if (previewHeight > 0f) Widgets.Label(new Rect(0f, previewY, width, previewHeight), preview);
        }

        public bool PawnButton()
        {
            TooltipHandler.TipRegion(rows[0].InputRect, pawnLabel);
            return Widgets.ButtonText(rows[0].InputRect, pawnLabel);
        }

        public void Number(int row, ref string buffer, ref int value, int minimum)
        {
            buffer = Widgets.TextField(rows[row].InputRect, buffer);
            int.TryParse(buffer, out value);
            value = Math.Max(minimum, value);
        }

        public bool Confirm(bool enabled)
        {
            return Widgets.ButtonText(confirm, confirmLabel, active: enabled);
        }

        public void End() { Widgets.EndScrollView(); }
    }
}
