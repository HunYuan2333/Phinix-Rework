using PhinixClient.Framework;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    internal sealed class LegacyTalentTradeSettingsPanel : IClientSettingsPanelProvider
    {
        public string SectionId => "legacyTalentTrade.general";

        public float Order => 140f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            bool notifications = LegacyTalentTradeRuntime.Settings.EnableNotifications;
            listing.CheckboxLabeled(
                "Phinix_legacyTalentTrade_settingNotifications".Translate(),
                ref notifications,
                "Phinix_legacyTalentTrade_settingNotificationsDesc".Translate()
            );
            if (notifications != LegacyTalentTradeRuntime.Settings.EnableNotifications)
            {
                LegacyTalentTradeRuntime.Settings.Save(settings, LegacyTalentTradeSettings.KeyNotifications, notifications);
            }

            listing.Gap(6f);

            bool debugLog = LegacyTalentTradeRuntime.Settings.EnableDebugLog;
            listing.CheckboxLabeled(
                "Phinix_legacyTalentTrade_settingDebugLog".Translate(),
                ref debugLog,
                "Phinix_legacyTalentTrade_settingDebugLogDesc".Translate()
            );
            if (debugLog != LegacyTalentTradeRuntime.Settings.EnableDebugLog)
            {
                LegacyTalentTradeRuntime.Settings.Save(settings, LegacyTalentTradeSettings.KeyDebugLog, debugLog);
            }
        }
    }
}
