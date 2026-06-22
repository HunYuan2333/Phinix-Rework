using PhinixClient;
using PhinixClient.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包未读角标（Rework UI 新流程：IBadgeProvider）。
    /// 显示当前可领取的红包数量；按状态机版本缓存，避免每帧分配。
    /// </summary>
    internal sealed class RedPacketUnreadBadge : IBadgeProvider
    {
        private readonly IClientSessionContext session;
        private readonly RedPacketStateMachine stateMachine;
        private int cachedVersion = -1;
        private string cachedText;

        public RedPacketUnreadBadge(IClientSessionContext session, RedPacketStateMachine stateMachine)
        {
            this.session = session;
            this.stateMachine = stateMachine;
        }

        public string BadgeText
        {
            get
            {
                if (session == null || !session.Authenticated || !session.LoggedIn)
                {
                    return null;
                }

                if (cachedVersion != stateMachine.BadgeVersion)
                {
                    cachedVersion = stateMachine.BadgeVersion;
                    int count = stateMachine.ClaimableCount;
                    cachedText = count > 0 ? count.ToString() : null;
                }

                return cachedText;
            }
        }
    }
}
