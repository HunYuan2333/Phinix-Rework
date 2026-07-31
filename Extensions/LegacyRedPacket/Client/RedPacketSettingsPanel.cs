using PhinixClient.Framework;
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
            bool notifications = this.settings != null && this.settings.EnableNotifications;
            listing.CheckboxLabeled(
                "Phinix_legacyRedpacket_settingNotifications".Translate(),
                ref notifications,
                "Phinix_legacyRedpacket_settingNotificationsDesc".Translate()
            );
            if (notifications != (this.settings != null && this.settings.EnableNotifications))
            {
                this.settings?.Save(settingsContext, RedPacketSettings.KeyNotifications, notifications);
            }

            listing.Gap(6f);

            bool chatAnnouncement = this.settings != null && this.settings.EnableChatAnnouncement;
            listing.CheckboxLabeled(
                "Phinix_legacyRedpacket_settingChatAnnouncement".Translate(),
                ref chatAnnouncement,
                "Phinix_legacyRedpacket_settingChatAnnouncementDesc".Translate()
            );
            if (chatAnnouncement != (this.settings != null && this.settings.EnableChatAnnouncement))
            {
                this.settings?.Save(settingsContext, RedPacketSettings.KeyChatAnnouncement, chatAnnouncement);
            }

            listing.Gap(6f);

            bool suppressUnknownNotify = this.settings == null || this.settings.SuppressUnknownPacketNotification;
            listing.CheckboxLabeled(
                "Phinix_legacyRedpacket_settingSuppressUnknownNotify".Translate(),
                ref suppressUnknownNotify,
                "Phinix_legacyRedpacket_settingSuppressUnknownNotifyDesc".Translate()
            );
            if (suppressUnknownNotify != (this.settings == null || this.settings.SuppressUnknownPacketNotification))
            {
                this.settings?.Save(settingsContext, RedPacketSettings.KeySuppressUnknown, suppressUnknownNotify);
            }
        }
    }
}
