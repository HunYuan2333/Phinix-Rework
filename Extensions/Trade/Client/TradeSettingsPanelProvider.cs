using System;
using PhinixClient.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    /// <summary>
    /// Trade 插件化设置面板。将 host 中原先硬编码的 Trade 设置项迁回插件自身，
    /// 通过 IClientSettingsPanelProvider 注册，host 只负责收集和绘制。
    /// 设计哲学 §1.3：host 只做通用服务；§2.3：减少硬编码。
    /// </summary>
    internal sealed class TradeSettingsPanelProvider : IClientSettingsPanelProvider
    {
        public string SectionId => "trade.general";

        public float Order => 120f;

        public bool IsVisible(IClientSettingsContext settings) => true;

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            bool acceptingTrades = settings.Get("trade.acceptingTrades", true);
            listing.CheckboxLabeled("Phinix_modSettings_acceptingTradesTitle".Translate(), ref acceptingTrades);
            settings.Set("trade.acceptingTrades", acceptingTrades);

            bool allItemsTradable = settings.Get("trade.allItemsTradable", false);
            listing.CheckboxLabeled("Phinix_modSettings_allItemsTradable".Translate(), ref allItemsTradable);
            settings.Set("trade.allItemsTradable", allItemsTradable);

            bool showBlockedTrades = settings.Get("trade.showBlockedTrades", false);
            listing.CheckboxLabeled("Phinix_modSettings_showBlockedTrades".Translate(), ref showBlockedTrades);
            settings.Set("trade.showBlockedTrades", showBlockedTrades);

            bool dropCurrentMap = settings.Get("trade.dropCurrentMap", false);
            listing.CheckboxLabeled("Phinix_modSettings_dropCurrentMap".Translate(), ref dropCurrentMap);
            settings.Set("trade.dropCurrentMap", dropCurrentMap);
        }
    }
}
