using System;
using PhinixClient.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    /// <summary>
    /// Chat 插件化设置面板。将 host 中原先硬编码的 Chat 设置项迁回插件自身，
    /// 通过 IClientSettingsPanelProvider 注册，host 只负责收集和绘制。
    /// 设计哲学 §1.3：host 只做通用服务；§2.3：减少硬编码。
    /// </summary>
    internal sealed class ChatSettingsPanelProvider : IClientSettingsPanelProvider, IClientLegacySettingsMigrator
    {
        public string SectionId => "chat.display";

        public float Order => 110f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            bool playNoiseOnMessageReceived = settings.Get("chat.playNoiseOnMessageReceived", true);
            listing.CheckboxLabeled("Phinix_modSettings_playNoiseOnMessageReceived".Translate(), ref playNoiseOnMessageReceived);
            settings.Set("chat.playNoiseOnMessageReceived", playNoiseOnMessageReceived);

            bool showNameFormatting = settings.Get("chat.showNameFormatting", true);
            listing.CheckboxLabeled("Phinix_modSettings_showNameFormatting".Translate(), ref showNameFormatting);
            settings.Set("chat.showNameFormatting", showNameFormatting);

            bool showChatFormatting = settings.Get("chat.showChatFormatting", true);
            listing.CheckboxLabeled("Phinix_modSettings_showChatFormatting".Translate(), ref showChatFormatting);
            settings.Set("chat.showChatFormatting", showChatFormatting);

            bool showUnreadMessageCount = settings.Get("chat.showUnreadMessageCount", true);
            listing.CheckboxLabeled("Phinix_modSettings_showUnreadMessageCount".Translate(), ref showUnreadMessageCount);
            settings.Set("chat.showUnreadMessageCount", showUnreadMessageCount);

            bool showBlockedUnreadMessageCount = settings.Get("chat.showBlockedUnreadMessageCount", false);
            listing.CheckboxLabeled("Phinix_modSettings_showBlockedUnreadMessageCount".Translate(), ref showBlockedUnreadMessageCount);
            settings.Set("chat.showBlockedUnreadMessageCount", showBlockedUnreadMessageCount);

            listing.Label("Phinix_modSettings_chatMessageLimit".Translate());
            string limitStr = settings.Get("chat.messageLimit", 40).ToString();
            limitStr = listing.TextEntry(limitStr);
            int.TryParse(limitStr, out int chatMessageLimit);
            settings.Set("chat.messageLimit", chatMessageLimit);

            bool forceMessageFieldFocus = settings.Get("chat.forceMessageFieldFocus", true);
            listing.CheckboxLabeled("Phinix_modSettings_forceMessageFieldFocus".Translate(), ref forceMessageFieldFocus);
            settings.Set("chat.forceMessageFieldFocus", forceMessageFieldFocus);
        }

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
