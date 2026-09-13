namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包插件统一资源边界常量。
    ///
    /// 所有限制由接收端客户端强制执行，不依赖发送端配合。
    /// UI、协议和状态机统一引用此类型，避免各自维护不一致的魔法数字。
    /// 设计哲学 §3.6：反压与资源边界。
    /// </summary>
    internal static class RedPacketLimits
    {
        // ── 主线程消息处理预算 ──

        /// <summary>每 Tick 软上限消息数（动态范围 4-64）。</summary>
        public const int MaxMessagesPerTick = 32;

        /// <summary>每 Tick 绝对时间预算（毫秒），超过立即停止处理。</summary>
        public const double TickTimeBudgetMs = 3.0;

        // ── 长期集合容量 ──

        /// <summary>活跃/保留红包字典上限。</summary>
        public const int MaxActivePackets = 512;

        /// <summary>报文去重 FIFO 容量。</summary>
        public const int MaxProcessedKeys = 8192;

        /// <summary>显示名缓存上限。</summary>
        public const int MaxDisplayNames = 2048;

        /// <summary>单红包最大领取明细条目。</summary>
        public const int MaxClaimDetailsPerPacket = 512;

        // ── 中继传输边界 ──

        /// <summary>中继 HTTP 响应体最大字节数（1 MiB）。</summary>
        public const int MaxResponseBytes = 1_048_576;

        // ── 协议字段长度 ──

        /// <summary>DefName / StuffDefName 最大字符数。</summary>
        public const int MaxFieldLength = 256;

        /// <summary>packet ID / UUID 最大字符数。</summary>
        public const int MaxIdLength = 128;

        /// <summary>解码后显示名最大字符数。</summary>
        public const int MaxDisplayNameLength = 128;

        /// <summary>红包最长有效期（小时）。兼容值，正常仍为 10 分钟。</summary>
        public const int MaxPacketValidityHours = 24;

    }
}
