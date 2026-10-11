using PhinixClient.Framework;

namespace Phinix.ChatExtension.Client
{
    // Persisted pre-plugin settings are normalized only at this input boundary.
    internal sealed class ChatSettingsInputAdapter : IClientLegacySettingsMigrator
    {
        public bool TryMigrateLegacySettings(IClientSettingsContext settings, System.Collections.Generic.IReadOnlyDictionary<string, string> legacyValues)
        {
            if (settings == null || legacyValues == null)
            {
                return false;
            }

            migrateBool(settings, legacyValues, "showNameFormatting", "chat.showNameFormatting", true);
            migrateBool(settings, legacyValues, "showChatFormatting", "chat.showChatFormatting", true);
            migrateBool(settings, legacyValues, "showUnreadMessageCount", "chat.showUnreadMessageCount", true);
            migrateBool(settings, legacyValues, "showBlockedUnreadMessageCount", "chat.showBlockedUnreadMessageCount", false);
            migrateBool(settings, legacyValues, "forceMessageFieldFocus", "chat.forceMessageFieldFocus", true);
            migrateBool(settings, legacyValues, "playNoiseOnMessageReceived", "chat.playNoiseOnMessageReceived", true);
            migrateInt(settings, legacyValues, "chatMessageLimit", "chat.messageLimit", 40);
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

        private static void migrateInt(IClientSettingsContext settings, System.Collections.Generic.IReadOnlyDictionary<string, string> legacyValues, string legacyKey, string targetKey, int defaultValue)
        {
            if (legacyValues.TryGetValue(legacyKey, out string rawValue) && int.TryParse(rawValue, out int parsedValue))
            {
                settings.Set(targetKey, parsedValue);
                return;
            }

            settings.Set(targetKey, defaultValue);
        }
    }
}
