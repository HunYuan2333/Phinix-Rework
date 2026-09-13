using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatSettingsPanelProvider : IClientSettingsPanelProvider, IClientLegacySettingsMigrator
    {
        private readonly IUiTheme theme;
        private readonly string[] labels = new string[12];
        private readonly float[] labelHeights = new float[12];
        private float cachedWidth = -1f;
        private object cachedLanguage;

        public ChatSettingsPanelProvider(IUiTheme theme = null)
        {
            this.theme = theme;
        }

        public string SectionId => "chat.display";

        public float Order => 110f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            RebuildLabels(listing.ColumnWidth);
            DrawCheckbox(listing, settings, 0, "chat.playNoiseOnMessageReceived", true);
            DrawCheckbox(listing, settings, 1, "chat.showNameFormatting", true);
            DrawCheckbox(listing, settings, 2, "chat.showChatFormatting", true);
            DrawCheckbox(listing, settings, 3, "chat.showUnreadMessageCount", true);
            DrawCheckbox(listing, settings, 4, "chat.showBlockedUnreadMessageCount", false);

            DrawLabel(listing, 5);
            string limitStr = settings.Get("chat.messageLimit", 40).ToString();
            limitStr = listing.TextEntry(limitStr);
            int.TryParse(limitStr, out int chatMessageLimit);
            settings.Set("chat.messageLimit", chatMessageLimit);

            DrawCheckbox(listing, settings, 6, "chat.forceMessageFieldFocus", true);
            DrawCheckbox(listing, settings, 7, "chat.notice.enabled", true);

            DrawLabel(listing, 8);
            string noticeDurationStr = settings.Get("chat.notice.defaultDuration", 10).ToString();
            noticeDurationStr = listing.TextEntry(noticeDurationStr);
            int.TryParse(noticeDurationStr, out int noticeDuration);
            settings.Set("chat.notice.defaultDuration", noticeDuration);

            DrawCheckbox(listing, settings, 9, "chat.images.enabled", true);

            DrawLabel(listing, 10);
            string maxImageHeightStr = settings.Get("chat.images.maxHeight", 240f).ToString();
            maxImageHeightStr = listing.TextEntry(maxImageHeightStr);
            if (float.TryParse(maxImageHeightStr, out float maxImageHeight))
            {
                settings.Set("chat.images.maxHeight", maxImageHeight);
            }

            if (theme != null)
            {
                listing.Gap(4f);
                Rect buttonRect = listing.GetRect(Mathf.Max(30f, labelHeights[11]));
                if (Widgets.ButtonText(buttonRect, labels[11]))
                {
                    theme.Reload();
                    ChatTheme.Refresh(theme);
                }
            }
        }

        private void RebuildLabels(float width)
        {
            width = Mathf.Max(1f, width);
            object language = LanguageDatabase.activeLanguage;
            if (Mathf.Approximately(cachedWidth, width) && ReferenceEquals(cachedLanguage, language)) return;
            cachedWidth = width;
            cachedLanguage = language;
            string[] keys =
            {
                "Phinix_modSettings_playNoiseOnMessageReceived", "Phinix_modSettings_showNameFormatting",
                "Phinix_modSettings_showChatFormatting", "Phinix_modSettings_showUnreadMessageCount",
                "Phinix_modSettings_showBlockedUnreadMessageCount", "Phinix_modSettings_chatMessageLimit",
                "Phinix_modSettings_forceMessageFieldFocus", "Phinix_modSettings_noticeEnabled",
                "Phinix_modSettings_noticeDefaultDuration", "Phinix_modSettings_chatImagesEnabled",
                "Phinix_modSettings_chatImagesMaxHeight", "Phinix_modSettings_reloadTheme"
            };
            for (int i = 0; i < keys.Length; i++)
            {
                labels[i] = keys[i].Translate();
                float textWidth = i <= 4 || i == 6 || i == 7 || i == 9 ? width - 36f : width;
                labelHeights[i] = Mathf.Max(30f, Text.CalcHeight(labels[i], Mathf.Max(1f, textWidth)));
            }
        }

        private void DrawCheckbox(Listing_Standard listing, IClientSettingsContext settings,
            int labelIndex, string key, bool defaultValue)
        {
            bool value = settings.Get(key, defaultValue);
            Rect rect = listing.GetRect(labelHeights[labelIndex]);
            Widgets.CheckboxLabeled(rect, labels[labelIndex], ref value);
            TooltipHandler.TipRegion(rect, labels[labelIndex]);
            settings.Set(key, value);
        }

        private void DrawLabel(Listing_Standard listing, int labelIndex)
        {
            Rect rect = listing.GetRect(labelHeights[labelIndex]);
            Widgets.Label(rect, labels[labelIndex]);
            TooltipHandler.TipRegion(rect, labels[labelIndex]);
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
