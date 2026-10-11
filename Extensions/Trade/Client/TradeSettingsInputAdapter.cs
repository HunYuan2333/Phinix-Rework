using PhinixClient.Framework;

namespace Phinix.TradeExtension.Client
{
    // Persisted pre-plugin settings are normalized only at this input boundary.
    internal sealed class TradeSettingsInputAdapter : IClientLegacySettingsMigrator
    {
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
