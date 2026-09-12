using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包设置面板（Rework UI 新流程：IClientSettingsPanelProvider）。
    /// </summary>
    internal sealed class RedPacketSettingsPanel : IClientSettingsPanelProvider
    {
        private readonly IClientSettingsContext settingsContext;
        private readonly RedPacketSettings settings;
        private readonly string[] labels = new string[3];
        private readonly string[] tips = new string[3];
        private readonly float[] heights = new float[3];
        private float cachedWidth = -1f;
        private object cachedLanguage;

        public RedPacketSettingsPanel(IClientSettingsContext settingsContext, RedPacketSettings settings)
        {
            this.settingsContext = settingsContext;
            this.settings = settings;
        }

        public string SectionId => "redpacket.general";

        public float Order => 130f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            RebuildLabels(listing.ColumnWidth);
            bool notifications = this.settings != null && this.settings.EnableNotifications;
            DrawCheckbox(listing, 0, ref notifications);
            if (notifications != (this.settings != null && this.settings.EnableNotifications))
            {
                this.settings?.Save(settingsContext, RedPacketSettings.KeyNotifications, notifications);
            }

            listing.Gap(6f);

            bool chatAnnouncement = this.settings != null && this.settings.EnableChatAnnouncement;
            DrawCheckbox(listing, 1, ref chatAnnouncement);
            if (chatAnnouncement != (this.settings != null && this.settings.EnableChatAnnouncement))
            {
                this.settings?.Save(settingsContext, RedPacketSettings.KeyChatAnnouncement, chatAnnouncement);
            }

            listing.Gap(6f);

            bool suppressUnknownNotify = this.settings == null || this.settings.SuppressUnknownPacketNotification;
            DrawCheckbox(listing, 2, ref suppressUnknownNotify);
            if (suppressUnknownNotify != (this.settings == null || this.settings.SuppressUnknownPacketNotification))
            {
                this.settings?.Save(settingsContext, RedPacketSettings.KeySuppressUnknown, suppressUnknownNotify);
            }
        }

        private void RebuildLabels(float width)
        {
            width = Mathf.Max(1f, width);
            object language = LanguageDatabase.activeLanguage;
            if (Mathf.Approximately(cachedWidth, width) && ReferenceEquals(cachedLanguage, language)) return;
            cachedWidth = width;
            cachedLanguage = language;
            string[] labelKeys = { "Phinix_legacyRedpacket_settingNotifications",
                "Phinix_legacyRedpacket_settingChatAnnouncement",
                "Phinix_legacyRedpacket_settingSuppressUnknownNotify" };
            string[] tipKeys = { "Phinix_legacyRedpacket_settingNotificationsDesc",
                "Phinix_legacyRedpacket_settingChatAnnouncementDesc",
                "Phinix_legacyRedpacket_settingSuppressUnknownNotifyDesc" };
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = labelKeys[i].Translate();
                tips[i] = tipKeys[i].Translate();
                heights[i] = Mathf.Max(30f, Text.CalcHeight(labels[i], Mathf.Max(1f, width - 36f)));
            }
        }

        private void DrawCheckbox(Listing_Standard listing, int index, ref bool value)
        {
            Rect rect = listing.GetRect(heights[index]);
            Widgets.CheckboxLabeled(rect, labels[index], ref value);
            TooltipHandler.TipRegion(rect, tips[index]);
        }
    }
}
