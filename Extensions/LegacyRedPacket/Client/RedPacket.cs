using System;
using System.Collections.Generic;
using Phinix.LegacyRedPacketExtension;
using PhinixClient.Trade;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包客户端模型。物品使用 Rework 的 TradeItemSnapshot（新流程），
    /// StoredThings 为发送者本地持有的真实 Thing 引用。
    /// </summary>
    public sealed class RedPacket
    {
        public string Id;
        public string SenderUuid;
        public string SenderDisplayName;
        public TradeItemSnapshot Template;
        public int TotalCount;
        public int RemainingCount;
        public int TotalPackets;
        public int RemainingPackets;
        public RedPacketType Type;
        public int LuckyAlgorithmVersion = 1;
        public DateTime CreatedAtUtc;
        public DateTime ExpiresAtUtc;
        public DateTime? CompletedAtUtc;
        public bool Expired;

        public readonly HashSet<string> ClaimedUuids = new HashSet<string>();
        public readonly Dictionary<string, int> ClaimedAmounts = new Dictionary<string, int>();
        public readonly List<Thing> StoredThings = new List<Thing>();

        public bool IsSender(string uuid)
        {
            return !string.IsNullOrEmpty(uuid) && SenderUuid == uuid;
        }

        public bool HasClaimed(string uuid)
        {
            return !string.IsNullOrEmpty(uuid) && ClaimedUuids.Contains(uuid);
        }
    }
}
