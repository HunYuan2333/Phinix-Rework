using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.TradeExtension.Client
{
    /// <summary>
    /// Trade 插件化设置面板。将 host 中原先硬编码的 Trade 设置项迁回插件自身，
    /// 通过 IClientSettingsPanelProvider 注册，host 只负责收集和绘制。
    /// 设计哲学 §1.3：host 只做通用服务；§2.3：减少硬编码。
    /// </summary>
    internal sealed class TradeSettingsPanelProvider : IClientSettingsPanelProvider, IClientLegacySettingsMigrator
    {
        private readonly string[] labels = new string[4];
        private readonly float[] heights = new float[4];
        private float cachedWidth = -1f;
        private object cachedLanguage;
        public string SectionId => "trade.general";

        public float Order => 120f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            RebuildLabels(listing.ColumnWidth);
            DrawCheckbox(listing, settings, 0, "trade.acceptingTrades", true);
            DrawCheckbox(listing, settings, 1, "trade.allItemsTradable", false);
            DrawCheckbox(listing, settings, 2, "trade.showBlockedTrades", false);
            DrawCheckbox(listing, settings, 3, "trade.dropCurrentMap", false);
        }

        private void RebuildLabels(float width)
        {
            width = Mathf.Max(1f, width);
            object language = LanguageDatabase.activeLanguage;
            if (Mathf.Approximately(cachedWidth, width) && ReferenceEquals(cachedLanguage, language)) return;
            cachedWidth = width;
            cachedLanguage = language;
            string[] keys = { "Phinix_modSettings_acceptingTradesTitle", "Phinix_modSettings_allItemsTradable",
                "Phinix_modSettings_showBlockedTrades", "Phinix_modSettings_dropCurrentMap" };
            for (int i = 0; i < keys.Length; i++)
            {
                labels[i] = keys[i].Translate();
                heights[i] = Mathf.Max(30f, Text.CalcHeight(labels[i], Mathf.Max(1f, width - 36f)));
            }
        }

        private void DrawCheckbox(Listing_Standard listing, IClientSettingsContext settings,
            int labelIndex, string key, bool defaultValue)
        {
            bool value = settings.Get(key, defaultValue);
            Rect rect = listing.GetRect(heights[labelIndex]);
            Widgets.CheckboxLabeled(rect, labels[labelIndex], ref value);
            TooltipHandler.TipRegion(rect, labels[labelIndex]);
            settings.Set(key, value);
        }

        public bool TryMigrateLegacySettings(IClientSettingsContext settings, System.Collections.Generic.IReadOnlyDictionary<string, string> legacyValues)
        {
            if (settings == null || legacyValues == null)
            {
                return false;
            }

            migrateBool(settings, legacyValues, "acceptingTrades", "trade.acceptingTrades", true);
            migrateBool(settings, legacyValues, "allItemsTradable", "trade.allItemsTradable", false);
            migrateBool(settings, legacyValues, "showBlockedTrades", "trade.showBlockedTrades", false);
            migrateBool(settings, legacyValues, "dropCurrentMap", "trade.dropCurrentMap", false);
            return true;
        }

        private static void migrateBool(IClientSettingsContext settings, System.Collections.Generic.IReadOnlyDictionary<string, string> legacyValues, string legacyKey, string targetKey, bool defaultValue)
        {
            if (legacyValues.TryGetValue(legacyKey, out string rawValue) && bool.TryParse(rawValue, out bool parsedValue))
            {
                settings.Set(targetKey, parsedValue);
                return;
            }

            settings.Set(targetKey, defaultValue);
        }
    }
}
