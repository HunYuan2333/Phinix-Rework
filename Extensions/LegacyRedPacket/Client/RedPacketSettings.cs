using PhinixClient.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包插件设置。通过框架的 IClientSettingsContext（宿主扩展设置存储）持久化，
    /// 与 Mod Settings 面板（RedPacketSettingsPanel）共用。
    /// </summary>
    internal sealed class RedPacketSettings
    {
        public const string KeyNotifications = "legacyRedpacket.enableNotifications";
        public const string KeyChatAnnouncement = "legacyRedpacket.enableChatAnnouncement";
        public const string KeySuppressUnknown = "legacyRedpacket.suppressUnknownNotify";

        public bool EnableNotifications = true;
        public bool EnableChatAnnouncement = false;
        public bool SuppressUnknownPacketNotification = true;

        public void Load(IClientSettingsContext context)
        {
            if (context == null) return;
            EnableNotifications = context.Get(KeyNotifications, true);
            EnableChatAnnouncement = context.Get(KeyChatAnnouncement, false);
            SuppressUnknownPacketNotification = context.Get(KeySuppressUnknown, true);
        }

        public void Save(IClientSettingsContext context, string key, bool value)
        {
            if (context == null) return;
            context.Set(key, value);

            if (key == KeyNotifications) EnableNotifications = value;
            else if (key == KeyChatAnnouncement) EnableChatAnnouncement = value;
            else if (key == KeySuppressUnknown) SuppressUnknownPacketNotification = value;
        }
    }
}
