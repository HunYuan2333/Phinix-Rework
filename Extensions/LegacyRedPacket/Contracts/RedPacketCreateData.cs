using System;
using PhinixClient.Trade;

namespace Phinix.LegacyRedPacketExtension
{
    /// <summary>
    /// 创建红包的纯数据描述（运行时中立，可序列化）。
    /// 由客户端状态机从游戏内物品快照组装，供 v1 协议 BuildCreate 使用。
    /// </summary>
    public sealed class RedPacketCreateData
    {
        public string Id;
        public string SenderUuid;
        public string SenderDisplayName;

        /// <summary>物品定义名（ThingDef.defName）。</summary>
        public string DefName;
        /// <summary>材料定义名（可为空）。</summary>
        public string StuffDefName;
        public TradeItemQuality Quality;
        public int HitPoints;

        public int TotalCount;
        public int TotalPackets;
        public RedPacketType Type;
        public int LuckyAlgorithmVersion;

        public DateTime CreatedAtUtc;
        public DateTime ExpiresAtUtc;
    }
}
