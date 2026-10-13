using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.ChatExtension.Client
{
    internal sealed class ChatSettingsPanelProvider : IClientSettingsPanelProvider
    {
        private readonly string[] labels = new string[14];
        private readonly float[] labelHeights = new float[14];
        private bool timestampStacked;
        private float cachedWidth = -1f;
        private object cachedLanguage;

        public ChatSettingsPanelProvider() { }

        public string SectionId => "chat.display";

        public float Order => 110f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            using (ChatTheme.BeginDraw())
            using (new ClientGuiScope(true))
            {
                UnityEngine.GUI.color = ChatTheme.Current.PrimaryText;
                DrawSettingsContents(listing, settings);
            }
        }

        private void DrawSettingsContents(Listing_Standard listing, IClientSettingsContext settings)
        {
            RebuildLabels(listing.ColumnWidth);
            DrawCheckbox(listing, settings, 0, "chat.playNoiseOnMessageReceived", true);
            DrawCheckbox(listing, settings, 1, "chat.showNameFormatting", true);
            DrawCheckbox(listing, settings, 2, "chat.showChatFormatting", true);
            DrawLabel(listing, 11);
            bool onRight = settings.Get("chat.timestampOnRight", true);
            Rect timestampRect = listing.GetRect(timestampStacked ? 2 * ClientWidgets.ControlHeight + 6 : ClientWidgets.ControlHeight);
            float choiceWidth = timestampStacked ? timestampRect.width : Mathf.Max(1, (timestampRect.width - 8) / 2);
            Rect leftChoice = new Rect(timestampRect.x, timestampRect.y, choiceWidth, ClientWidgets.ControlHeight);
            Rect rightChoice = new Rect(timestampStacked ? timestampRect.x : timestampRect.x + choiceWidth + 8,
                timestampStacked ? timestampRect.y + ClientWidgets.ControlHeight + 6 : timestampRect.y, choiceWidth, ClientWidgets.ControlHeight);
            if (ClientWidgets.NavigationPill(leftChoice, labels[12], !onRight, ChatTheme.Current)) settings.Set("chat.timestampOnRight", false);
            if (ClientWidgets.NavigationPill(rightChoice, labels[13], onRight, ChatTheme.Current)) settings.Set("chat.timestampOnRight", true);
            DrawCheckbox(listing, settings, 3, "chat.showUnreadMessageCount", true);
            DrawCheckbox(listing, settings, 4, "chat.showBlockedUnreadMessageCount", false);

            DrawLabel(listing, 5);
            string limitStr = settings.Get("chat.messageLimit", 40).ToString();
            limitStr = ClientWidgets.TextField(listing.GetRect(30f), limitStr, ChatTheme.Current, controlName: "phinix.chat.settings.limitStr");
            int.TryParse(limitStr, out int chatMessageLimit);
            settings.Set("chat.messageLimit", chatMessageLimit);

            DrawCheckbox(listing, settings, 6, "chat.forceMessageFieldFocus", true);
            DrawCheckbox(listing, settings, 7, "chat.notice.enabled", true);

            DrawLabel(listing, 8);
            string noticeDurationStr = settings.Get("chat.notice.defaultDuration", 10).ToString();
            noticeDurationStr = ClientWidgets.TextField(listing.GetRect(30f), noticeDurationStr, ChatTheme.Current, controlName: "phinix.chat.settings.noticeDurationStr");
            int.TryParse(noticeDurationStr, out int noticeDuration);
            settings.Set("chat.notice.defaultDuration", noticeDuration);

            DrawCheckbox(listing, settings, 9, "chat.images.enabled", true);

            DrawLabel(listing, 10);
            string maxImageHeightStr = settings.Get("chat.images.maxHeight", 240f).ToString();
            maxImageHeightStr = ClientWidgets.TextField(listing.GetRect(30f), maxImageHeightStr, ChatTheme.Current, controlName: "phinix.chat.settings.maxImageHeightStr");
            if (float.TryParse(maxImageHeightStr, out float maxImageHeight))
            {
                settings.Set("chat.images.maxHeight", maxImageHeight);
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
                "Phinix_modSettings_chatImagesMaxHeight", "Phinix_modSettings_timestampPosition",
                "Phinix_modSettings_timestampLeft", "Phinix_modSettings_timestampRight"
            };
            for (int i = 0; i < keys.Length; i++)
            {
                labels[i] = keys[i].Translate();
                float textWidth = i <= 4 || i == 6 || i == 7 || i == 9 ? width - 36f : width;
                labelHeights[i] = Mathf.Max(30f, Text.CalcHeight(labels[i], Mathf.Max(1f, textWidth)));
            }
            timestampStacked = Text.CalcSize(labels[12]).x + Text.CalcSize(labels[13]).x + 48 > width;
        }

        private void DrawCheckbox(Listing_Standard listing, IClientSettingsContext settings,
            int labelIndex, string key, bool defaultValue)
        {
            bool value = settings.Get(key, defaultValue);
            Rect rect = listing.GetRect(labelHeights[labelIndex]);
            ClientWidgets.Checkbox(rect, labels[labelIndex], ref value, ChatTheme.Current);
            TooltipHandler.TipRegion(rect, labels[labelIndex]);
            settings.Set(key, value);
        }

        private void DrawLabel(Listing_Standard listing, int labelIndex)
        {
            Rect rect = listing.GetRect(labelHeights[labelIndex]);
            ClientWidgets.Label(rect, labels[labelIndex], ChatTheme.Current);
            TooltipHandler.TipRegion(rect, labels[labelIndex]);
        }

    }
}
