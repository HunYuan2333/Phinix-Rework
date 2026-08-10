using System;
using PhinixClient.Framework;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatSettingsPanelProvider : IClientSettingsPanelProvider, IClientLegacySettingsMigrator
    {
        private readonly IUiTheme theme;

        public ChatSettingsPanelProvider(IUiTheme theme = null)
        {
            this.theme = theme;
        }

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

            bool noticeEnabled = settings.Get("chat.notice.enabled", true);
            listing.CheckboxLabeled("Phinix_modSettings_noticeEnabled".Translate(), ref noticeEnabled);
            settings.Set("chat.notice.enabled", noticeEnabled);

            listing.Label("Phinix_modSettings_noticeDefaultDuration".Translate());
            string noticeDurationStr = settings.Get("chat.notice.defaultDuration", 10).ToString();
            noticeDurationStr = listing.TextEntry(noticeDurationStr);
            int.TryParse(noticeDurationStr, out int noticeDuration);
            settings.Set("chat.notice.defaultDuration", noticeDuration);

            bool chatImagesEnabled = settings.Get("chat.images.enabled", true);
            listing.CheckboxLabeled("Phinix_modSettings_chatImagesEnabled".Translate(), ref chatImagesEnabled);
            settings.Set("chat.images.enabled", chatImagesEnabled);

            listing.Label("Phinix_modSettings_chatImagesMaxHeight".Translate());
            string maxImageHeightStr = settings.Get("chat.images.maxHeight", 240f).ToString();
            maxImageHeightStr = listing.TextEntry(maxImageHeightStr);
            if (float.TryParse(maxImageHeightStr, out float maxImageHeight))
            {
                settings.Set("chat.images.maxHeight", maxImageHeight);
            }

            if (theme != null)
            {
                listing.Gap(4f);
                if (listing.ButtonText("Phinix_modSettings_reloadTheme".Translate()))
                {
                    theme.Reload();
                    ChatTheme.Refresh(theme);
                }
            }
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
