using PhinixClient.Framework;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// 插件设置（通过框架 IClientSettingsContext 持久化，键前缀 legacyTalentTrade.*
    /// 与作者未来新版隔离）。原 TalentTradeSettings(ModSettings) 由此替代。
    /// </summary>
    internal sealed class LegacyTalentTradeSettings
    {
        public const string KeyNotifications = "legacyTalentTrade.enableNotifications";
        public const string KeyDebugLog = "legacyTalentTrade.enableDebugLog";

        public bool EnableNotifications = true;
        public bool EnableDebugLog = false;

        public void Load(IClientSettingsContext context)
        {
            if (context == null) return;
            EnableNotifications = context.Get(KeyNotifications, true);
            EnableDebugLog = context.Get(KeyDebugLog, false);
        }

        public void Save(IClientSettingsContext context, string key, bool value)
        {
            if (context == null) return;
            context.Set(key, value);

            if (key == KeyNotifications) EnableNotifications = value;
            else if (key == KeyDebugLog) EnableDebugLog = value;
        }
    }
}
