using System.Collections.Generic;
using Phinix.LegacyRedPacketExtension;

namespace Phinix.LegacyRedPacketExtension.Client
{
    public sealed class RedPacketClaimSnapshot
    {
        public string Uuid;
        public string Name;
        public int Amount;
    }

    public sealed class RedPacketDetailSnapshot
    {
        public string PacketId;
        public string ItemLabel;
        public string ItemDefName;
        public string StuffDefName;
        public int TotalCount;
        public int TotalPackets;
        public int RemainingCount;
        public int RemainingPackets;
        public RedPacketType Type;
        public string SenderName;
        public bool Expired;
        public System.DateTime CreatedAtUtc;
        public System.DateTime? CompletedAtUtc;
        public List<RedPacketClaimSnapshot> Claims = new List<RedPacketClaimSnapshot>();
        public bool IsFullyClaimed;
        public string LuckiestUuid;
        public string LuckiestName;
        public int LuckiestAmount;
    }
}
