using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    internal sealed class LegacyTalentTradeSettingsPanel : IClientSettingsPanelProvider
    {
        private float cachedWidth = -1f;
        private object cachedLanguage;
        private string notificationsLabel;
        private string notificationsTip;
        private string debugLabel;
        private string debugTip;
        private float notificationsHeight;
        private float debugHeight;
        public string SectionId => "legacyTalentTrade.general";

        public float Order => 140f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            float width = Mathf.Max(1f, listing.ColumnWidth);
            if (cachedWidth != width || !ReferenceEquals(cachedLanguage, LanguageDatabase.activeLanguage))
            {
                cachedWidth = width;
                cachedLanguage = LanguageDatabase.activeLanguage;
                notificationsLabel = "Phinix_legacyTalentTrade_settingNotifications".Translate();
                notificationsTip = "Phinix_legacyTalentTrade_settingNotificationsDesc".Translate();
                debugLabel = "Phinix_legacyTalentTrade_settingDebugLog".Translate();
                debugTip = "Phinix_legacyTalentTrade_settingDebugLogDesc".Translate();
                notificationsHeight = Mathf.Max(30f, Text.CalcHeight(notificationsLabel, Mathf.Max(1f, width - 36f)));
                debugHeight = Mathf.Max(30f, Text.CalcHeight(debugLabel, Mathf.Max(1f, width - 36f)));
            }
            bool notifications = LegacyTalentTradeRuntime.Settings.EnableNotifications;
            Rect notificationsRect = listing.GetRect(notificationsHeight);
            Widgets.CheckboxLabeled(notificationsRect, notificationsLabel, ref notifications);
            TooltipHandler.TipRegion(notificationsRect, notificationsTip);
            if (notifications != LegacyTalentTradeRuntime.Settings.EnableNotifications)
            {
                LegacyTalentTradeRuntime.Settings.Save(settings, LegacyTalentTradeSettings.KeyNotifications, notifications);
            }

            listing.Gap(6f);

            bool debugLog = LegacyTalentTradeRuntime.Settings.EnableDebugLog;
            Rect debugRect = listing.GetRect(debugHeight);
            Widgets.CheckboxLabeled(debugRect, debugLabel, ref debugLog);
            TooltipHandler.TipRegion(debugRect, debugTip);
            if (debugLog != LegacyTalentTradeRuntime.Settings.EnableDebugLog)
            {
                LegacyTalentTradeRuntime.Settings.Save(settings, LegacyTalentTradeSettings.KeyDebugLog, debugLog);
            }
        }
    }
}
