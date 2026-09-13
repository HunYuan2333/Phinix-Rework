using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Phinix.LegacyRedPacketExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Trade;
using RimWorld;
using UnityEngine;
using UserManagement;
using Utils;
using Verse;
using Thing = Verse.Thing;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包客户端状态机（老方案迁移：客户端确定性复算，与老 submod 行为一致）。
    ///
    /// 与老实现唯一的行为差异是依赖注入：
    /// - Client.Instance.Uuid / Online / TryGetDisplayName → IClientSessionContext / IClientUserDirectory
    /// - Trading.ProtoThing / TradingThingConverter → TradeItemSnapshot / TradeItemConverter
    /// - Client.Instance.DropPods → RedPacketDropHelper
    /// - 自建 MainThreadQueue → IClientMainThreadDispatcher（Tick 封送到主线程执行）
    ///
    /// 驱动：Timer 每 200ms 通过主线程调度器触发 Tick（中继轮询 + 状态机 + 过期结算）。
    /// 设计哲学 §3.5/§3.6/§3.8：异常隔离、有界集合、日志分级。
    /// </summary>
    internal sealed class RedPacketStateMachine : IDisposable
    {
        private static readonly object PacketsLock = new object();
        private static readonly Dictionary<string, RedPacket> Packets = new Dictionary<string, RedPacket>();
        private static readonly Dictionary<string, string> KnownDisplayNames = new Dictionary<string, string>();
        private static readonly Dictionary<string, DateTime> PendingClaims = new Dictionary<string, DateTime>();
        private static readonly object ProcessedLock = new object();
        private static readonly HashSet<string> ProcessedProtocolKeys = new HashSet<string>();
        /// <summary>RP-05: FIFO 淘汰顺序，与 ProcessedProtocolKeys 同步。</summary>
        private static readonly Queue<string> ProcessedProtocolKeyOrder = new Queue<string>();
        private static volatile bool testPingReceived;
        private static DateTime nextExpiryCheckUtc = DateTime.MinValue;
        private static DateTime nextClaimTimeoutCheckUtc = DateTime.MinValue;
        private static DateTime nextFinishedCleanupUtc = DateTime.MinValue;

        private static readonly TimeSpan ClaimTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan SenderFinishedRetention = TimeSpan.FromMinutes(1);
        private const int MaxDropPodCountPerDelivery = 100;
        private static readonly HashSet<string> SpecialPacketDefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wastepack",
            "GoldenCube"
        };
        private const int LuckyAlgorithmLegacy = 1;
        private const int LuckyAlgorithmDoubleMean = 2;

        private readonly IClientSessionContext session;
        private readonly IClientUserDirectory userDirectory;
        private readonly IClientMainThreadDispatcher dispatcher;
        private readonly IClientSettingsContext settingsContext;
        private readonly RedPacketSettings settings;
        private readonly RedPacketRelay relay;
        private readonly Action<string, LogLevel> log;

        private System.Timers.Timer driveTimer;
        // Timer callbacks run on a pool thread. Keep at most one relay Tick in
        // the host dispatcher so a stalled main thread cannot accumulate one
        // action every interval.
        private int tickPending;
        private bool initialized;
        private bool disposed;

        // 角标缓存版本（红包集合每次变更时递增，Badge 按版本缓存避免每帧分配）
        private int badgeVersion;
        private int claimableCount;

        public RedPacketStateMachine(
            IClientSessionContext session,
            IClientUserDirectory userDirectory,
            IClientMainThreadDispatcher dispatcher,
            IClientSettingsContext settingsContext,
            RedPacketSettings settings,
            RedPacketRelay relay,
            Action<string, LogLevel> log)
        {
            this.session = session;
            this.userDirectory = userDirectory;
            this.dispatcher = dispatcher;
            this.settingsContext = settingsContext;
            this.settings = settings;
            this.relay = relay;
            this.log = log;
        }

        public bool TestPingReceived => testPingReceived;

        public int BadgeVersion => badgeVersion;

        public int ClaimableCount => claimableCount;

        private bool IsOnline => session != null && session.Authenticated && session.LoggedIn;

        private string LocalUuid => session?.Uuid ?? string.Empty;

        public void Initialize()
        {
            if (initialized || disposed) return;
            initialized = true;

            driveTimer = new System.Timers.Timer
            {
                AutoReset = true,
                Interval = 200
            };
            driveTimer.Elapsed += OnDriveTick;
            driveTimer.Start();
        }

        public void Shutdown()
        {
            if (disposed) return;
            disposed = true;
            initialized = false;

            if (driveTimer != null)
            {
                driveTimer.Stop();
                driveTimer.Elapsed -= OnDriveTick;
                driveTimer.Dispose();
                driveTimer = null;
            }

            Clear();
        }

        /// <summary>
        /// RP-13: IDisposable 实现，幂等委托到 Shutdown。
        /// </summary>
        public void Dispose()
        {
            if (!disposed) Shutdown();
        }

        private void OnDriveTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (disposed) return;
            if (Interlocked.CompareExchange(ref tickPending, 1, 0) != 0) return;

            if (dispatcher == null)
            {
                Interlocked.Exchange(ref tickPending, 0);
                return;
            }

            dispatcher.Enqueue(() =>
            {
                try
                {
                    Tick();
                }
                finally
                {
                    Interlocked.Exchange(ref tickPending, 0);
                }
            });
        }

        private void Tick()
        {
            if (disposed) return;

            try
            {
                relay.Update();
            }
            catch (Exception ex)
            {
                log?.Invoke("[RedPacket] Relay update failed: " + ex, LogLevel.ERROR);
            }

            PollRelayBuffer();
            CheckClaimTimeouts();
            UpdateExpiry();
            CleanupFinishedPackets();
        }

        public RedPacket[] GetPacketsSnapshot()
        {
            lock (PacketsLock)
            {
                return Packets.Values
                    .OrderByDescending(packet => packet.CreatedAtUtc)
                    .ToArray();
            }
        }

        public bool ShouldDisplayInList(RedPacket packet, DateTime nowUtc, string viewerUuid)
        {
            if (packet == null) return false;

            DateTime? finishedAt = GetFinishedAtUtc(packet);
            if (!finishedAt.HasValue)
            {
                return !packet.Expired && packet.ExpiresAtUtc > nowUtc;
            }

            if (!packet.IsSender(viewerUuid)) return false;
            return nowUtc <= finishedAt.Value.Add(SenderFinishedRetention);
        }

        public bool TryGetPacketDetailSnapshot(string packetId, out RedPacketDetailSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(packetId)) return false;

            string senderUuid;
            string senderDisplayName;
            string itemLabel;
            string itemDefName;
            string stuffDefName;
            int totalCount;
            int totalPackets;
            int remainingCount;
            int remainingPackets;
            RedPacketType packetType;
            bool expired;
            DateTime createdAtUtc;
            DateTime expiresAtUtc;
            DateTime? completedAtUtc;
            Dictionary<string, int> claims;

            lock (PacketsLock)
            {
                if (!Packets.TryGetValue(packetId, out RedPacket packet)) return false;

                senderUuid = packet.SenderUuid;
                senderDisplayName = packet.SenderDisplayName;
                itemLabel = GetPacketItemLabel(packet);
                itemDefName = packet.Template != null ? packet.Template.DefName : string.Empty;
                stuffDefName = packet.Template != null ? packet.Template.StuffDefName : string.Empty;
                totalCount = packet.TotalCount;
                totalPackets = packet.TotalPackets;
                remainingCount = packet.RemainingCount;
                remainingPackets = packet.RemainingPackets;
                packetType = packet.Type;
                expired = packet.Expired;
                createdAtUtc = packet.CreatedAtUtc;
                expiresAtUtc = packet.ExpiresAtUtc;
                completedAtUtc = packet.CompletedAtUtc;
                claims = new Dictionary<string, int>(packet.ClaimedAmounts);
            }

            if (!completedAtUtc.HasValue && (expired || remainingPackets <= 0 || remainingCount <= 0))
            {
                completedAtUtc = expired ? expiresAtUtc : createdAtUtc;
            }

            List<RedPacketClaimSnapshot> claimEntries = claims
                .Select(entry => new RedPacketClaimSnapshot
                {
                    Uuid = entry.Key,
                    Name = GetDisplayName(entry.Key),
                    Amount = entry.Value
                })
                .OrderByDescending(entry => entry.Amount)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool fullyClaimed = remainingPackets <= 0 || remainingCount <= 0;
            RedPacketClaimSnapshot luckiest = null;
            if (fullyClaimed && claimEntries.Count > 0)
            {
                int maxAmount = claimEntries.Max(entry => entry.Amount);
                luckiest = claimEntries
                    .Where(entry => entry.Amount == maxAmount)
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }

            snapshot = new RedPacketDetailSnapshot
            {
                PacketId = packetId,
                ItemLabel = itemLabel,
                ItemDefName = itemDefName,
                StuffDefName = stuffDefName,
                TotalCount = totalCount,
                TotalPackets = totalPackets,
                RemainingCount = remainingCount,
                RemainingPackets = remainingPackets,
                Type = packetType,
                SenderName = !string.IsNullOrEmpty(senderDisplayName) ? TextHelper.StripRichText(senderDisplayName) : GetDisplayName(senderUuid),
                Expired = expired,
                CreatedAtUtc = createdAtUtc,
                CompletedAtUtc = completedAtUtc,
                Claims = claimEntries,
                IsFullyClaimed = fullyClaimed,
                LuckiestUuid = luckiest != null ? luckiest.Uuid : string.Empty,
                LuckiestName = luckiest != null ? luckiest.Name : string.Empty,
                LuckiestAmount = luckiest != null ? luckiest.Amount : 0
            };
            return true;
        }

        public void AddLocalPacket(RedPacket packet)
        {
            if (packet == null || string.IsNullOrEmpty(packet.Id)) return;

            lock (PacketsLock)
            {
                Packets[packet.Id] = packet;
            }
            MarkBadgeDirty();

            if (IsOnline)
            {
                BroadcastProtocolMessage(RedPacketProtocol.BuildCreate(ToCreateData(packet)));
            }
        }

        public void RequestClaim(RedPacket packet)
        {
            if (packet == null) return;
            if (!IsOnline) return;

            string localUuid = LocalUuid;
            if (string.IsNullOrEmpty(localUuid)) return;
            if (IsMinifiedTemplate(packet.Template))
            {
                Messages.Message(
                    "Phinix_legacyRedpacket_minifiedClaimReject".Translate(),
                    MessageTypeDefOf.RejectInput
                );
                return;
            }
            if (IsUnknownTemplate(packet.Template))
            {
                Messages.Message(
                    "Phinix_legacyRedpacket_unknownItemReject".Translate(GetPacketItemLabel(packet)),
                    MessageTypeDefOf.RejectInput
                );
                return;
            }

            lock (PacketsLock)
            {
                if (packet.Expired || packet.RemainingPackets <= 0 || packet.RemainingCount <= 0) return;
                if (packet.IsSender(localUuid)) return;
                if (packet.HasClaimed(localUuid)) return;
                if (PendingClaims.ContainsKey(packet.Id)) return;
                PendingClaims[packet.Id] = DateTime.UtcNow;
            }

            string localDisplayName = GetLocalPlayerDisplayName();
            BroadcastProtocolMessage(RedPacketProtocol.BuildClaim(packet.Id, localUuid, localDisplayName));
        }

        public bool IsPendingClaim(string packetId)
        {
            if (string.IsNullOrEmpty(packetId)) return false;

            lock (PacketsLock)
            {
                return PendingClaims.ContainsKey(packetId);
            }
        }

        public bool IsSpecialPacketItem(TradeItemSnapshot template)
        {
            if (template == null || string.IsNullOrEmpty(template.DefName)) return false;
            return SpecialPacketDefs.Contains(template.DefName);
        }

        public int GetLuckyAlgorithmVersionForType(RedPacketType packetType)
        {
            if (packetType != RedPacketType.Lucky) return 0;
            return LuckyAlgorithmDoubleMean;
        }

        public void Clear()
        {
            lock (PacketsLock)
            {
                Packets.Clear();
                PendingClaims.Clear();
                KnownDisplayNames.Clear();
            }

            lock (ProcessedLock)
            {
                ProcessedProtocolKeys.Clear();
                ProcessedProtocolKeyOrder.Clear();
            }
            relay?.Clear();
            MarkBadgeDirty();
        }

        private void MarkBadgeDirty()
        {
            badgeVersion++;
            RecomputeClaimableCount();
        }

        private void RecomputeClaimableCount()
        {
            string localUuid = LocalUuid;
            DateTime nowUtc = DateTime.UtcNow;
            int count = 0;
            lock (PacketsLock)
            {
                foreach (RedPacket packet in Packets.Values)
                {
                    if (packet.Expired || packet.ExpiresAtUtc <= nowUtc) continue;
                    if (packet.RemainingPackets <= 0 || packet.RemainingCount <= 0) continue;
                    if (packet.IsSender(localUuid)) continue;
                    if (packet.HasClaimed(localUuid)) continue;
                    if (PendingClaims.ContainsKey(packet.Id)) continue;
                    count++;
                }
            }
            claimableCount = count;
        }

        /// <summary>
        /// RP-00: Safe 包装——校验通过后注册去重，通知由 PollRelayBuffer 批次合并。
        /// </summary>
        private bool HandleCreateSafe(string[] parts, ref int newPacketCount, ref RedPacket lastNewPacket)
        {
            RedPacket newPacket = HandleCreateCore(parts);
            if (newPacket == null) return false;

            MarkProcessed(RedPacketMessageType.Create, parts);

            if (IsOnline && !newPacket.Expired)
            {
                string localUuid = LocalUuid;
                if (!string.IsNullOrEmpty(localUuid)
                    && !newPacket.IsSender(localUuid)
                    && ShouldNotifyNewPacket(newPacket))
                {
                    newPacketCount++;
                    lastNewPacket = newPacket;
                }
            }
            return true;
        }

        /// <summary>返回成功添加的 RedPacket；失败返回 null。</summary>
        private RedPacket HandleCreateCore(string[] parts)
        {
            if (parts.Length < 14) return null;

            string packetId = parts[3];
            string senderUuid = parts[4];
            string defName = parts[5];
            string stuffDefName = parts[6];
            if (string.IsNullOrEmpty(packetId) || packetId.Length > RedPacketLimits.MaxIdLength) return null;
            if (string.IsNullOrEmpty(senderUuid) || senderUuid.Length > RedPacketLimits.MaxIdLength) return null;
            if (string.IsNullOrEmpty(defName) || defName.Length > RedPacketLimits.MaxFieldLength) return null;
            if (!string.IsNullOrEmpty(stuffDefName) && stuffDefName.Length > RedPacketLimits.MaxFieldLength) return null;
            if (!int.TryParse(parts[7], out int qualityValue)) qualityValue = 0;
            if (!int.TryParse(parts[8], out int hitPoints)) hitPoints = 0;
            if (!int.TryParse(parts[9], out int totalCount)) return null;
            if (!int.TryParse(parts[10], out int totalPackets)) return null;
            if (totalCount <= 0 || totalPackets <= 0 || totalPackets > totalCount) return null;
            if (totalPackets > RedPacketLimits.MaxClaimDetailsPerPacket) return null;
            if (!int.TryParse(parts[11], out int typeValue)) typeValue = 0;
            RedPacketType packetType = (RedPacketType)typeValue;
            int luckyAlgorithmVersion = ResolveLuckyAlgorithmVersion(packetType, parts);
            if (!long.TryParse(parts[12], out long createdTicks)) createdTicks = DateTime.UtcNow.Ticks;
            if (!long.TryParse(parts[13], out long expiresTicks)) expiresTicks = DateTime.UtcNow.AddMinutes(10).Ticks;
            string senderDisplayName = string.Empty;
            if (parts.Length >= 15)
            {
                senderDisplayName = TextHelper.StripRichText(RedPacketProtocol.DecodeField(parts[14]));
            }
            RememberDisplayName(senderUuid, senderDisplayName);

            // RP-02: DateTime 安全构造
            if (createdTicks < 0 || createdTicks > DateTime.MaxValue.Ticks) return null;
            if (expiresTicks < 0 || expiresTicks > DateTime.MaxValue.Ticks) return null;
            DateTime createdAt = new DateTime(createdTicks, DateTimeKind.Utc);
            DateTime expiresAt = new DateTime(expiresTicks, DateTimeKind.Utc);
            if (expiresAt < createdAt
                || expiresAt - createdAt > TimeSpan.FromHours(RedPacketLimits.MaxPacketValidityHours)) return null;

            TradeItemSnapshot template = new TradeItemSnapshot(
                defName,
                totalCount,
                hitPoints,
                RedPacketProtocol.FromWireQuality(qualityValue),
                stuffDefName ?? string.Empty);

            RedPacket packet = new RedPacket
            {
                Id = packetId,
                SenderUuid = senderUuid,
                SenderDisplayName = senderDisplayName,
                Template = template,
                TotalCount = totalCount,
                RemainingCount = totalCount,
                TotalPackets = totalPackets,
                RemainingPackets = totalPackets,
                Type = packetType,
                LuckyAlgorithmVersion = luckyAlgorithmVersion,
                CreatedAtUtc = createdAt,
                ExpiresAtUtc = expiresAt,
                Expired = DateTime.UtcNow >= expiresAt
            };
            if (packet.Expired)
            {
                packet.CompletedAtUtc = expiresAt;
            }

            lock (PacketsLock)
            {
                if (Packets.ContainsKey(packetId)) return null;

                // RP-05: Packets 容量淘汰（优先清理已完成/已过期且无本地存储物品的）
                if (Packets.Count >= RedPacketLimits.MaxActivePackets)
                {
                    EvictOldestFinishedPackets();
                }
                if (Packets.Count >= RedPacketLimits.MaxActivePackets)
                {
                    log?.Invoke("[RedPacket] Packets dict at capacity, cannot add " + packetId, LogLevel.WARNING);
                    return null;
                }

                Packets[packetId] = packet;
            }

            return packet;
        }

        private bool HandleClaimSafe(string[] parts)
        {
            if (parts.Length < 5) return false;
            if (!IsOnline) return false;

            string packetId = parts[3];
            string claimerUuid = parts[4];
            if (string.IsNullOrEmpty(packetId) || string.IsNullOrEmpty(claimerUuid)) return false;
            string claimerDisplayName = string.Empty;
            if (parts.Length >= 6)
            {
                claimerDisplayName = TextHelper.StripRichText(RedPacketProtocol.DecodeField(parts[5]));
            }
            RememberDisplayName(claimerUuid, claimerDisplayName);

            RedPacket packet;
            int amount;
            string localUuid = LocalUuid;
            bool isLocalClaimer = false;
            bool completed = false;
            bool isSender = false;
            DateTime completedAtUtc = DateTime.UtcNow;
            Dictionary<string, int> claimsSnapshot = null;
            lock (PacketsLock)
            {
                if (!Packets.TryGetValue(packetId, out packet)) return false;
                if (packet.Expired || packet.RemainingPackets <= 0 || packet.RemainingCount <= 0) return false;
                if (packet.IsSender(claimerUuid)) return false;
                if (packet.HasClaimed(claimerUuid)) return false;
                if (packet.ClaimedUuids.Count >= RedPacketLimits.MaxClaimDetailsPerPacket) return false;

                amount = ComputeAmount(packet, claimerUuid);
                if (amount <= 0) return false;

                packet.RemainingPackets = Math.Max(0, packet.RemainingPackets - 1);
                packet.RemainingCount = Math.Max(0, packet.RemainingCount - amount);
                packet.ClaimedUuids.Add(claimerUuid);
                // RP-05: 单红包明细上限
                if (packet.ClaimedAmounts.Count < RedPacketLimits.MaxClaimDetailsPerPacket)
                {
                    packet.ClaimedAmounts[claimerUuid] = amount;
                }

                isLocalClaimer = !string.IsNullOrEmpty(localUuid) && claimerUuid == localUuid;
                if (isLocalClaimer)
                {
                    PendingClaims.Remove(packetId);
                }
                else if (packet.RemainingPackets <= 0 || packet.RemainingCount <= 0)
                {
                    PendingClaims.Remove(packetId);
                }

                completed = packet.RemainingPackets <= 0 || packet.RemainingCount <= 0;
                if (completed && !packet.CompletedAtUtc.HasValue)
                {
                    packet.CompletedAtUtc = DateTime.UtcNow;
                }
                if (completed)
                {
                    completedAtUtc = packet.CompletedAtUtc ?? DateTime.UtcNow;
                    claimsSnapshot = new Dictionary<string, int>(packet.ClaimedAmounts);
                }

                isSender = !string.IsNullOrEmpty(localUuid) && packet.IsSender(localUuid);
            }

            // RP-07: 校验应用成功后登记去重
            MarkProcessed(RedPacketMessageType.Claim, parts);

            if (isSender)
            {
                ConsumeStoredThings(packet, amount);
            }

            if (isLocalClaimer && amount > 0)
            {
                if (IsMinifiedTemplate(packet.Template))
                {
                    Messages.Message(
                        "Phinix_legacyRedpacket_minifiedClaimReject".Translate(),
                        MessageTypeDefOf.RejectInput
                    );
                }
                else if (IsUnknownTemplate(packet.Template))
                {
                    Messages.Message(
                        "Phinix_legacyRedpacket_unknownItemReject".Translate(GetPacketItemLabel(packet)),
                        MessageTypeDefOf.RejectInput
                    );
                }
                else
                {
                    SpawnReward(packet, amount);
                }
            }

            if (completed)
            {
                if (isSender)
                {
                    QueueSenderSummary(packet, claimsSnapshot, completedAtUtc, expired: false, returnedCount: 0);
                }
                MarkPacketFinished(packetId, completedAtUtc, expired: false);
            }

            return true;
        }

        private bool HandleAssignSafe(string[] parts)
        {
            if (parts.Length < 8) return false;

            string packetId = parts[3];
            string claimerUuid = parts[4];
            if (!int.TryParse(parts[5], out int amount)) amount = 0;
            if (!int.TryParse(parts[6], out int remainingPackets)) remainingPackets = 0;
            if (!int.TryParse(parts[7], out int remainingCount)) remainingCount = 0;
            if (parts.Length >= 9)
            {
                string claimerDisplayName = TextHelper.StripRichText(RedPacketProtocol.DecodeField(parts[8]));
                RememberDisplayName(claimerUuid, claimerDisplayName);
            }

            RedPacket packet;
            string localUuid = LocalUuid;
            bool alreadyClaimed = false;
            bool completed = false;
            bool isSender = false;
            DateTime completedAtUtc = DateTime.UtcNow;
            Dictionary<string, int> claimsSnapshot = null;
            lock (PacketsLock)
            {
                if (!Packets.TryGetValue(packetId, out packet)) return false;

                alreadyClaimed = !string.IsNullOrEmpty(claimerUuid) && packet.ClaimedUuids.Contains(claimerUuid);
                if (!alreadyClaimed
                    && !string.IsNullOrEmpty(claimerUuid)
                    && packet.ClaimedUuids.Count >= RedPacketLimits.MaxClaimDetailsPerPacket)
                    return false;
                packet.RemainingPackets = remainingPackets;
                packet.RemainingCount = remainingCount;
                if (!alreadyClaimed && !string.IsNullOrEmpty(claimerUuid))
                {
                    packet.ClaimedUuids.Add(claimerUuid);
                    // RP-05: 单红包明细上限
                    if (packet.ClaimedAmounts.Count < RedPacketLimits.MaxClaimDetailsPerPacket)
                    {
                        packet.ClaimedAmounts[claimerUuid] = amount;
                    }
                }

                if (!string.IsNullOrEmpty(localUuid) && claimerUuid == localUuid)
                {
                    PendingClaims.Remove(packetId);
                }
                else if (remainingPackets <= 0 || remainingCount <= 0)
                {
                    PendingClaims.Remove(packetId);
                }

                completed = remainingPackets <= 0 || remainingCount <= 0;
                if (completed && !packet.CompletedAtUtc.HasValue)
                {
                    packet.CompletedAtUtc = DateTime.UtcNow;
                }
                if (completed)
                {
                    completedAtUtc = packet.CompletedAtUtc ?? DateTime.UtcNow;
                    claimsSnapshot = new Dictionary<string, int>(packet.ClaimedAmounts);
                }
                isSender = !string.IsNullOrEmpty(localUuid) && packet.IsSender(localUuid);
            }

            // RP-07: 登记去重
            MarkProcessed(RedPacketMessageType.Assign, parts);

            if (claimerUuid == localUuid && amount > 0 && !alreadyClaimed)
            {
                if (IsMinifiedTemplate(packet.Template))
                {
                    Messages.Message(
                        "Phinix_legacyRedpacket_minifiedClaimReject".Translate(),
                        MessageTypeDefOf.RejectInput
                    );
                }
                else if (IsUnknownTemplate(packet.Template))
                {
                    Messages.Message(
                        "Phinix_legacyRedpacket_unknownItemReject".Translate(GetPacketItemLabel(packet)),
                        MessageTypeDefOf.RejectInput
                    );
                }
                else
                {
                    SpawnReward(packet, amount);
                }
            }

            if (completed)
            {
                if (isSender)
                {
                    QueueSenderSummary(packet, claimsSnapshot, completedAtUtc, expired: false, returnedCount: 0);
                }
                MarkPacketFinished(packetId, completedAtUtc, expired: false);
            }

            return true;
        }

        private bool HandleTimeoutSafe(string[] parts)
        {
            if (parts.Length < 4) return false;

            string packetId = parts[3];
            if (string.IsNullOrEmpty(packetId)) return false;

            RedPacket packet;
            bool isSender = false;
            DateTime completedAtUtc = DateTime.UtcNow;
            lock (PacketsLock)
            {
                if (!Packets.TryGetValue(packetId, out packet)) return false;
                string localUuid = LocalUuid;
                isSender = !string.IsNullOrEmpty(localUuid) && packet.IsSender(localUuid);
                packet.Expired = true;
                if (!packet.CompletedAtUtc.HasValue)
                {
                    packet.CompletedAtUtc = completedAtUtc;
                }
                PendingClaims.Remove(packetId);
            }

            // RP-07: 登记去重
            MarkProcessed(RedPacketMessageType.Timeout, parts);

            if (isSender)
            {
                ReturnRemainingTimeout(packet);
            }
            MarkPacketFinished(packetId, completedAtUtc, expired: true);

            return true;
        }

        private void PollRelayBuffer()
        {
            int processed = 0;
            bool stateChanged = false;
            int newPacketCount = 0;
            RedPacket lastNewPacket = null;
            long startTicks = Stopwatch.GetTimestamp();
            double freqMs = Stopwatch.Frequency / 1000.0;

            string message;
            while (processed < RedPacketLimits.MaxMessagesPerTick
                   && relay.TryDequeueIncoming(out message))
            {
                try
                {
                    // RP-08: 单消息异常隔离
                    bool changed = ProcessProtocolMessageSafe(message, ref newPacketCount, ref lastNewPacket);
                    if (changed) stateChanged = true;
                }
                catch (Exception ex)
                {
                    log?.Invoke("[RedPacket] Message processing failed, skipping: " + ex, LogLevel.WARNING);
                }
                processed++;

                // RP-00: 时间预算硬上限
                double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) / freqMs;
                if (elapsedMs >= RedPacketLimits.TickTimeBudgetMs)
                    break;
            }

            // RP-00: 批次结束后只更新一次 badge 和 UI 版本
            if (stateChanged)
                MarkBadgeDirty();

            // RP-00: 通知合并——同 Tick 多个新红包只显示一条汇总
            if (newPacketCount > 0)
                NotifyNewPacketsBatched(newPacketCount, lastNewPacket);
        }

        private void CheckClaimTimeouts()
        {
            if (!IsOnline) return;

            DateTime now = DateTime.UtcNow;
            if (now < nextClaimTimeoutCheckUtc) return;
            nextClaimTimeoutCheckUtc = now.AddSeconds(1);

            List<string> timedOut = new List<string>();
            lock (PacketsLock)
            {
                foreach (KeyValuePair<string, DateTime> entry in PendingClaims)
                {
                    if (now - entry.Value < ClaimTimeout) continue;
                    timedOut.Add(entry.Key);
                }

                foreach (string packetId in timedOut)
                {
                    PendingClaims.Remove(packetId);
                }
            }
            MarkBadgeDirty();

            if (timedOut.Count == 0) return;

            foreach (string packetId in timedOut)
            {
                Messages.Message("Phinix_legacyRedpacket_claimTimeoutMessage".Translate(), MessageTypeDefOf.RejectInput);
            }
        }

        /// <summary>
        /// RP-00: 合并 IsProtocolMessage + TryParse（只解码一次）。
        /// RP-07: 去重登记延迟到 Handler 成功后。
        /// RP-08: 异常不冒泡（由 PollRelayBuffer 外层 catch 兜底）。
        /// 返回 true 表示状态发生了变更（需要 badge 刷新）。
        /// </summary>
        private bool ProcessProtocolMessageSafe(string message, ref int newPacketCount, ref RedPacket lastNewPacket)
        {
            if (string.IsNullOrEmpty(message)) return false;

            // RP-06: 消息长度快速拒绝
            if (message.Length > RedPacketProtocol.MaxWireMessageChars) return false;

            // 测试 ping（不走协议）
            bool maybeTest = message.IndexOf("rptest", StringComparison.OrdinalIgnoreCase) >= 0;
            if (maybeTest)
            {
                string plainMessage = message.IndexOf('<') >= 0 ? TextHelper.StripRichText(message) : message;
                string trimmed = plainMessage.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.Equals("rptest", StringComparison.OrdinalIgnoreCase))
                {
                    testPingReceived = true;
                }
            }

            // RP-00: 直接 TryParse，不再先调 IsProtocolMessage 重复解码
            if (!RedPacketProtocol.TryParse(message, out RedPacketMessageType messageType, out string[] parts))
                return false;

            // RP-07: 先检查是否已处理（只读），不注册
            if (IsAlreadyProcessed(messageType, parts)) return false;

            // 调用语义 Handler（返回 true 表示状态变更 + 去重已注册）
            switch (messageType)
            {
                case RedPacketMessageType.Create:
                    return HandleCreateSafe(parts, ref newPacketCount, ref lastNewPacket);
                case RedPacketMessageType.Claim:
                    return HandleClaimSafe(parts);
                case RedPacketMessageType.Assign:
                    return HandleAssignSafe(parts);
                case RedPacketMessageType.Timeout:
                    return HandleTimeoutSafe(parts);
                default:
                    return false;
            }
        }

        private void BroadcastProtocolMessage(string protocolMessage)
        {
            if (string.IsNullOrEmpty(protocolMessage)) return;
            if (!IsOnline) return;

            string senderUuid = LocalUuid;
            relay.EnqueueProtocol(protocolMessage, senderUuid);
        }

        /// <summary>RP-07: 只读检查，不注册去重键。</summary>
        private bool IsAlreadyProcessed(RedPacketMessageType messageType, string[] parts)
        {
            string key = BuildProtocolKey(messageType, parts);
            if (string.IsNullOrEmpty(key)) return true; // 无法构建 key → 视为已处理（跳过）

            lock (ProcessedLock)
            {
                return ProcessedProtocolKeys.Contains(key);
            }
        }

        /// <summary>RP-05/RP-07: Handler 校验通过后注册去重键（FIFO 淘汰）。</summary>
        private void MarkProcessed(RedPacketMessageType messageType, string[] parts)
        {
            string key = BuildProtocolKey(messageType, parts);
            if (string.IsNullOrEmpty(key)) return;

            lock (ProcessedLock)
            {
                if (ProcessedProtocolKeys.Contains(key)) return;
                ProcessedProtocolKeys.Add(key);
                ProcessedProtocolKeyOrder.Enqueue(key);

                // RP-05: FIFO 淘汰最旧条目
                while (ProcessedProtocolKeyOrder.Count > RedPacketLimits.MaxProcessedKeys)
                {
                    string oldest = ProcessedProtocolKeyOrder.Dequeue();
                    ProcessedProtocolKeys.Remove(oldest);
                }
            }
        }

        private static string BuildProtocolKey(RedPacketMessageType messageType, string[] parts)
        {
            if (parts == null) return null;

            switch (messageType)
            {
                case RedPacketMessageType.Create:
                    if (parts.Length < 4) return null;
                    return string.Concat("create:", parts[3]);
                case RedPacketMessageType.Claim:
                    if (parts.Length < 5) return null;
                    return string.Concat("claim:", parts[3], ":", parts[4]);
                case RedPacketMessageType.Assign:
                    if (parts.Length < 5) return null;
                    return string.Concat("assign:", parts[3], ":", parts[4]);
                case RedPacketMessageType.Timeout:
                    if (parts.Length < 4) return null;
                    return string.Concat("timeout:", parts[3]);
                default:
                    return null;
            }
        }

        private static int ComputeAmount(RedPacket packet, string claimerUuid)
        {
            if (packet.RemainingPackets <= 1) return packet.RemainingCount;

            if (packet.Type == RedPacketType.Lucky)
            {
                if (packet.LuckyAlgorithmVersion >= LuckyAlgorithmDoubleMean)
                {
                    return ComputeLuckyDoubleMeanAmount(packet, claimerUuid);
                }

                return ComputeLuckyLegacyAmount(packet, claimerUuid);
            }

            return packet.RemainingCount / packet.RemainingPackets;
        }

        private static int ComputeLuckyLegacyAmount(RedPacket packet, string claimerUuid)
        {
            int avg = packet.RemainingCount / packet.RemainingPackets;
            int variance = Math.Max(1, avg / 2);
            int min = Math.Max(1, avg - variance);
            int max = Math.Min(packet.RemainingCount - (packet.RemainingPackets - 1), avg + variance);
            if (min > max)
            {
                min = max = Math.Max(1, packet.RemainingCount - (packet.RemainingPackets - 1));
            }

            return DeterministicRangeInclusive(min, max, packet.Id, claimerUuid, packet.RemainingCount, packet.RemainingPackets);
        }

        // 二倍均值法：x ~ U[1, min(2*avg, M-(n-1))]，最后一包直接拿剩余。
        private static int ComputeLuckyDoubleMeanAmount(RedPacket packet, string claimerUuid)
        {
            int min = 1;
            int avg = packet.RemainingCount / packet.RemainingPackets;
            long byAverage = (long)avg * 2L;
            long byReserve = (long)packet.RemainingCount - ((long)packet.RemainingPackets - 1L);
            long upperLong = Math.Min(byAverage, byReserve);
            if (upperLong < min) upperLong = min;
            if (upperLong > int.MaxValue) upperLong = int.MaxValue;
            int max = (int)upperLong;

            return DeterministicRangeInclusive(min, max, packet.Id, claimerUuid, packet.RemainingCount, packet.RemainingPackets);
        }

        private static int ResolveLuckyAlgorithmVersion(RedPacketType packetType, string[] parts)
        {
            if (packetType != RedPacketType.Lucky) return 0;
            if (parts != null && parts.Length >= 16 && int.TryParse(parts[15], out int parsed) && parsed > 0)
            {
                return parsed;
            }

            return LuckyAlgorithmLegacy;
        }

        // Lucky 红包在所有客户端使用同一输入计算金额，避免依赖本地随机数造成分叉。
        private static int DeterministicRangeInclusive(
            int min,
            int max,
            string packetId,
            string claimerUuid,
            int remainingCount,
            int remainingPackets)
        {
            if (min >= max) return min;

            uint seed = 2166136261u;
            MixSeed(ref seed, packetId);
            MixSeed(ref seed, claimerUuid);
            MixSeed(ref seed, remainingCount);
            MixSeed(ref seed, remainingPackets);

            int span = max - min + 1;
            if (span <= 1) return min;
            return min + (int)(seed % (uint)span);
        }

        private static void MixSeed(ref uint seed, string value)
        {
            unchecked
            {
                if (string.IsNullOrEmpty(value))
                {
                    seed ^= 0x9E3779B9u;
                    seed *= 16777619u;
                    return;
                }

                for (int i = 0; i < value.Length; i++)
                {
                    seed ^= value[i];
                    seed *= 16777619u;
                }
            }
        }

        private static void MixSeed(ref uint seed, int value)
        {
            unchecked
            {
                seed ^= (uint)value;
                seed *= 16777619u;
            }
        }

        private static bool IsUnknownTemplate(TradeItemSnapshot template)
        {
            if (template == null || string.IsNullOrEmpty(template.DefName)) return true;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(template.DefName);
            if (def == null) return true;

            return def.defName == "UnknownItem";
        }

        private static bool IsMinifiedTemplate(TradeItemSnapshot template)
        {
            if (template == null) return false;
            if (template.InnerItem != null) return true;
            return string.Equals(template.DefName, "MinifiedThing", StringComparison.Ordinal);
        }

        private void ConsumeStoredThings(RedPacket packet, int amount)
        {
            if (amount <= 0) return;

            List<Thing> toDestroy = new List<Thing>();

            lock (PacketsLock)
            {
                int remaining = amount;
                for (int i = 0; i < packet.StoredThings.Count && remaining > 0; i++)
                {
                    Thing thing = packet.StoredThings[i];
                    if (thing == null || thing.Destroyed) continue;

                    int take = Math.Min(thing.stackCount, remaining);
                    thing.stackCount -= take;
                    remaining -= take;

                    if (thing.stackCount <= 0)
                    {
                        toDestroy.Add(thing);
                        packet.StoredThings[i] = null;
                    }
                }

                packet.StoredThings.RemoveAll(item => item == null);
            }

            foreach (Thing thing in toDestroy)
            {
                if (!thing.Destroyed) thing.Destroy();
            }
        }

        private int ResolveSpecialExpiredPacket(RedPacket packet)
        {
            if (packet == null) return 0;

            float roll = Rand.Value;
            if (roll < 0.5f)
            {
                bool raidTriggered = TryTriggerSpecialRaid();
                DestroyStoredThings(packet, raidTriggered ? "Phinix_legacyRedpacket_specialRaidMessage" : "Phinix_legacyRedpacket_specialNoReturnMessage");
                return 0;
            }

            if (roll < 0.75f)
            {
                return ReturnRemaining(packet, 2, "Phinix_legacyRedpacket_specialReturnMessage");
            }

            DestroyStoredThings(packet, "Phinix_legacyRedpacket_specialNoReturnMessage");
            return 0;
        }

        private bool TryTriggerSpecialRaid()
        {
            if (Current.Game == null || IncidentDefOf.RaidEnemy == null || IncidentDefOf.RaidEnemy.Worker == null) return false;

            Map map = Find.CurrentMap;
            if (map == null)
            {
                List<Map> maps = Current.Game.Maps;
                if (maps == null || maps.Count == 0) return false;
                map = maps.FirstOrDefault(candidate => candidate != null && candidate.IsPlayerHome)
                    ?? maps.FirstOrDefault(candidate => candidate != null);
            }

            if (map == null) return false;

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.forced = true;
            return IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
        }

        private static List<Thing> TakeStoredThings(RedPacket packet)
        {
            if (packet == null) return new List<Thing>();

            lock (PacketsLock)
            {
                List<Thing> things = packet.StoredThings.Where(thing => thing != null && !thing.Destroyed).ToList();
                packet.StoredThings.Clear();
                return things;
            }
        }

        private int DestroyStoredThings(RedPacket packet, string messageKey)
        {
            List<Thing> things = TakeStoredThings(packet);
            if (!things.Any()) return 0;

            int totalCount = things.Sum(thing => thing.stackCount);
            foreach (Thing thing in things)
            {
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy();
                }
            }

            if (!string.IsNullOrEmpty(messageKey))
            {
                string itemLabel = GetPacketItemLabel(packet);
                Messages.Message(messageKey.Translate(itemLabel, totalCount), MessageTypeDefOf.NegativeEvent);
            }

            return totalCount;
        }

        private int ReturnRemaining(RedPacket packet, int multiplier, string messageKey)
        {
            List<Thing> things = TakeStoredThings(packet);
            if (!things.Any()) return 0;

            List<Thing> returnThings = things;
            if (multiplier > 1)
            {
                returnThings = new List<Thing>(things);
                foreach (Thing thing in things)
                {
                    Thing clone = CloneThing(thing);
                    if (clone != null)
                    {
                        returnThings.Add(clone);
                    }
                }
            }

            int totalCount = returnThings.Sum(thing => thing.stackCount);
            RedPacketDropHelper.DropPodsWithLimit(returnThings, DropCurrentMap);

            string itemLabel = GetPacketItemLabel(packet);
            Messages.Message(messageKey.Translate(itemLabel, totalCount), MessageTypeDefOf.PositiveEvent);
            return totalCount;
        }

        private void ReturnRemainingTimeout(RedPacket packet)
        {
            List<Thing> things = TakeStoredThings(packet);
            if (!things.Any()) return;

            int totalCount = things.Sum(thing => thing.stackCount);
            RedPacketDropHelper.DropPodsWithLimit(things, DropCurrentMap);

            string itemLabel = GetPacketItemLabel(packet);
            Messages.Message("Phinix_legacyRedpacket_timeoutReturnMessage".Translate(itemLabel, totalCount), MessageTypeDefOf.NeutralEvent);
        }

        private void SpawnReward(RedPacket packet, int amount)
        {
            if (Current.Game == null) return;
            if (packet.Template == null) return;

            List<Thing> things = CreateThingsFromTemplate(packet.Template, amount);
            if (!things.Any()) return;

            RedPacketDropHelper.DropPodsWithLimit(things, DropCurrentMap);

            string senderName = packet.SenderDisplayName;
            if (string.IsNullOrEmpty(senderName)) senderName = GetDisplayName(packet.SenderUuid);

            string itemLabel = GetPacketItemLabel(packet);
            Messages.Message("Phinix_legacyRedpacket_rewardMessage".Translate(itemLabel, amount, senderName), MessageTypeDefOf.PositiveEvent);
        }

        private static List<Thing> CreateThingsFromTemplate(TradeItemSnapshot template, int count)
        {
            List<Thing> things = new List<Thing>();
            if (template == null || count <= 0) return things;

            int stackLimit = 1;
            ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(template.DefName);
            if (thingDef != null) stackLimit = Math.Max(1, thingDef.stackLimit);

            int remaining = count;
            while (remaining > 0)
            {
                int stackCount = Math.Min(stackLimit, remaining);
                TradeItemSnapshot clone = CloneTemplate(template, stackCount);
                things.Add(TradeItemConverter.ConvertThingFromSnapshotOrUnknown(clone));
                remaining -= stackCount;
            }

            return things;
        }

        private static Thing CloneThing(Thing thing)
        {
            if (thing == null) return null;

            TradeItemSnapshot snapshot = TradeItemConverter.ConvertThingFromVerse(thing);
            return TradeItemConverter.ConvertThingFromSnapshotOrUnknown(snapshot);
        }

        private static TradeItemSnapshot CloneTemplate(TradeItemSnapshot template, int stackCount)
        {
            return new TradeItemSnapshot(
                template.DefName,
                stackCount,
                template.HitPoints,
                template.Quality,
                template.StuffDefName,
                template.InnerItem != null ? CloneTemplate(template.InnerItem, template.InnerItem.StackCount) : null);
        }

        private string GetPacketItemLabel(RedPacket packet)
        {
            if (packet.Template == null) return "???";

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(packet.Template.DefName);
            if (def != null) return def.LabelCap;

            return packet.Template.DefName;
        }

        private string GetDisplayName(string uuid)
        {
            if (!string.IsNullOrEmpty(uuid) && userDirectory != null && userDirectory.TryGetUser(uuid, out ImmutableUser user))
            {
                return TextHelper.StripRichText(user.DisplayName);
            }

            string remembered = GetRememberedDisplayName(uuid);
            if (!string.IsNullOrEmpty(remembered))
            {
                return remembered;
            }

            return "???";
        }

        private string GetLocalPlayerDisplayName()
        {
            string uuid = LocalUuid;
            if (string.IsNullOrEmpty(uuid)) return string.Empty;

            if (userDirectory != null && userDirectory.TryGetUser(uuid, out ImmutableUser user))
            {
                return TextHelper.StripRichText(user.DisplayName);
            }

            return GetRememberedDisplayName(uuid);
        }

        private static void RememberDisplayName(string uuid, string displayName)
        {
            if (string.IsNullOrEmpty(uuid)) return;
            if (string.IsNullOrEmpty(displayName)) return;

            string sanitized = TextHelper.StripRichText(displayName).Trim();
            if (string.IsNullOrEmpty(sanitized)) return;
            if (sanitized.Length > RedPacketLimits.MaxDisplayNameLength)
            {
                sanitized = sanitized.Substring(0, RedPacketLimits.MaxDisplayNameLength);
            }

            lock (PacketsLock)
            {
                // RP-05: 显示名缓存容量上限
                if (!KnownDisplayNames.ContainsKey(uuid) && KnownDisplayNames.Count >= RedPacketLimits.MaxDisplayNames)
                {
                    var enumerator = KnownDisplayNames.Keys.GetEnumerator();
                    if (enumerator.MoveNext())
                    {
                        KnownDisplayNames.Remove(enumerator.Current);
                    }
                }

                KnownDisplayNames[uuid] = sanitized;
            }
        }

        private static string GetRememberedDisplayName(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return string.Empty;

            lock (PacketsLock)
            {
                if (KnownDisplayNames.TryGetValue(uuid, out string value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private void NotifyNewPacket(RedPacket packet)
        {
            if (packet == null) return;
            if (!ShouldNotifyNewPacket(packet)) return;
            if (LanguageDatabase.activeLanguage == null) return;

            string senderName = packet.SenderDisplayName;
            if (string.IsNullOrEmpty(senderName)) senderName = GetDisplayName(packet.SenderUuid);

            string itemLabel = GetPacketItemLabel(packet);
            Messages.Message("Phinix_legacyRedpacket_newPacketMessage".Translate(senderName, itemLabel, packet.TotalCount), MessageTypeDefOf.PositiveEvent);
        }

        private bool ShouldNotifyNewPacket(RedPacket packet)
        {
            if (packet == null) return false;
            if (settings != null && !settings.EnableNotifications) return false;
            return settings == null
                || !settings.SuppressUnknownPacketNotification
                || !IsUnknownTemplate(packet.Template);
        }

        /// <summary>
        /// RP-00: 通知合并——同一个 Tick 收到多个新红包时，合并为一条提示，避免屏幕瞬间被消息占满。
        /// </summary>
        private void NotifyNewPacketsBatched(int count, RedPacket lastPacket)
        {
            if (count <= 0) return;
            if (count == 1 && lastPacket != null)
            {
                NotifyNewPacket(lastPacket);
                return;
            }

            if (settings != null && !settings.EnableNotifications) return;
            if (LanguageDatabase.activeLanguage == null) return;

            string msgKey = "Phinix_legacyRedpacket_newPacketsBatchedMessage";
            TaggedString translated = msgKey.Translate(count);
            if (!translated.RawText.Contains(msgKey))
            {
                Messages.Message(translated, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                // 若无此 key，则至少通知最后一个红包
                if (lastPacket != null)
                {
                    NotifyNewPacket(lastPacket);
                }
            }
        }

        /// <summary>
        /// RP-05: 活跃红包容量上限淘汰——优先淘汰已过期或已抢完且没有本地暂存物品的记录。
        /// </summary>
        private static void EvictOldestFinishedPackets()
        {
            List<string> candidates = new List<string>();
            foreach (KeyValuePair<string, RedPacket> kv in Packets)
            {
                RedPacket p = kv.Value;
                if ((p.Expired || p.CompletedAtUtc.HasValue) && (p.StoredThings == null || p.StoredThings.Count == 0))
                {
                    candidates.Add(kv.Key);
                }
            }

            if (candidates.Count > 0)
            {
                candidates.Sort((a, b) => Packets[a].CreatedAtUtc.CompareTo(Packets[b].CreatedAtUtc));
                int toRemove = Math.Min(candidates.Count, Math.Max(16, Packets.Count - RedPacketLimits.MaxActivePackets + 32));
                for (int i = 0; i < toRemove; i++)
                {
                    string id = candidates[i];
                    Packets.Remove(id);
                    PendingClaims.Remove(id);
                }
            }
        }

        private void QueueSenderSummary(RedPacket packet, Dictionary<string, int> claimsSnapshot, DateTime completedAtUtc, bool expired, int returnedCount)
        {
            if (packet == null) return;

            string localUuid = LocalUuid;
            if (string.IsNullOrEmpty(localUuid) || !packet.IsSender(localUuid)) return;

            string itemLabel = GetPacketItemLabel(packet);
            string duration = FormatDuration(completedAtUtc - packet.CreatedAtUtc);
            string claimsText = BuildClaimSummary(claimsSnapshot);

            string titleKey = expired ? "Phinix_legacyRedpacket_expiredLetterTitle" : "Phinix_legacyRedpacket_completeLetterTitle";
            string textKey = expired ? "Phinix_legacyRedpacket_expiredLetterText" : "Phinix_legacyRedpacket_completeLetterText";

            string text = expired
                ? textKey.Translate(itemLabel, packet.TotalCount, duration, claimsText, returnedCount)
                : textKey.Translate(itemLabel, packet.TotalCount, duration, claimsText);

            LetterDef letterDef = expired ? LetterDefOf.NegativeEvent : LetterDefOf.PositiveEvent;

            Find.LetterStack.ReceiveLetter(titleKey.Translate(), text, letterDef);
        }

        private string BuildClaimSummary(Dictionary<string, int> claimsSnapshot)
        {
            if (claimsSnapshot == null || claimsSnapshot.Count == 0)
            {
                return "Phinix_legacyRedpacket_summaryNone".Translate();
            }

            return string.Join("\n", claimsSnapshot.Select(entry =>
                "Phinix_legacyRedpacket_summaryLine".Translate(GetDisplayName(entry.Key), entry.Value)
            ));
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            if (duration.TotalHours >= 1)
            {
                return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)duration.TotalHours, duration.Minutes, duration.Seconds);
            }

            return string.Format("{0:D2}:{1:D2}", (int)duration.TotalMinutes, duration.Seconds);
        }

        private void MarkPacketFinished(string packetId, DateTime completedAtUtc, bool expired)
        {
            if (string.IsNullOrEmpty(packetId)) return;

            lock (PacketsLock)
            {
                if (!Packets.TryGetValue(packetId, out RedPacket packet))
                {
                    PendingClaims.Remove(packetId);
                    return;
                }

                if (expired)
                {
                    packet.Expired = true;
                }

                if (!packet.CompletedAtUtc.HasValue)
                {
                    packet.CompletedAtUtc = completedAtUtc;
                }

                PendingClaims.Remove(packetId);
            }
        }

        private static DateTime? GetFinishedAtUtc(RedPacket packet)
        {
            if (packet == null) return null;
            if (packet.CompletedAtUtc.HasValue) return packet.CompletedAtUtc.Value;
            if (packet.Expired) return packet.ExpiresAtUtc;
            if (packet.RemainingPackets <= 0 || packet.RemainingCount <= 0) return packet.CreatedAtUtc;
            return null;
        }

        private void UpdateExpiry()
        {
            if (!IsOnline) return;

            DateTime now = DateTime.UtcNow;
            if (now < nextExpiryCheckUtc) return;

            nextExpiryCheckUtc = now.AddSeconds(1);

            List<ExpirySettlement> settlements = new List<ExpirySettlement>();
            List<PacketSummary> summaries = new List<PacketSummary>();
            lock (PacketsLock)
            {
                foreach (KeyValuePair<string, RedPacket> entry in Packets)
                {
                    RedPacket packet = entry.Value;
                    if (packet.Expired || packet.CompletedAtUtc.HasValue || now < packet.ExpiresAtUtc) continue;

                    packet.Expired = true;
                    PendingClaims.Remove(packet.Id);
                    if (!packet.CompletedAtUtc.HasValue)
                    {
                        packet.CompletedAtUtc = now;
                    }

                    bool isSpecial = IsSpecialPacketItem(packet.Template);
                    bool isSender = packet.IsSender(LocalUuid);
                    if (isSender && packet.StoredThings.Any())
                    {
                        settlements.Add(new ExpirySettlement
                        {
                            Packet = packet,
                            IsSpecial = isSpecial,
                            HasRemaining = packet.RemainingCount > 0
                        });
                    }

                    if (isSender)
                    {
                        summaries.Add(new PacketSummary
                        {
                            Packet = packet,
                            Claims = new Dictionary<string, int>(packet.ClaimedAmounts),
                            CompletedAtUtc = packet.CompletedAtUtc ?? now
                        });
                    }
                }
            }

            Dictionary<string, int> returnedCounts = new Dictionary<string, int>();
            foreach (ExpirySettlement settlement in settlements)
            {
                int returnedCount = 0;
                if (settlement.IsSpecial && settlement.HasRemaining)
                {
                    returnedCount = ResolveSpecialExpiredPacket(settlement.Packet);
                }
                else if (!settlement.IsSpecial)
                {
                    returnedCount = ReturnRemaining(settlement.Packet, 1, "Phinix_legacyRedpacket_returnMessage");
                }
                else
                {
                    DestroyStoredThings(settlement.Packet, null);
                }

                returnedCounts[settlement.Packet.Id] = returnedCount;
            }

            foreach (PacketSummary summary in summaries)
            {
                int returnedCount = 0;
                returnedCounts.TryGetValue(summary.Packet.Id, out returnedCount);
                QueueSenderSummary(summary.Packet, summary.Claims, summary.CompletedAtUtc, expired: true, returnedCount: returnedCount);
            }
            MarkBadgeDirty();
        }

        private void CleanupFinishedPackets()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextFinishedCleanupUtc) return;
            nextFinishedCleanupUtc = now.AddSeconds(5);

            List<string> toRemove = new List<string>();
            lock (PacketsLock)
            {
                foreach (KeyValuePair<string, RedPacket> entry in Packets)
                {
                    RedPacket packet = entry.Value;
                    DateTime? finishedAt = GetFinishedAtUtc(packet);
                    if (!finishedAt.HasValue) continue;
                    if (now <= finishedAt.Value.Add(SenderFinishedRetention)) continue;
                    toRemove.Add(entry.Key);
                }

                foreach (string packetId in toRemove)
                {
                    Packets.Remove(packetId);
                    PendingClaims.Remove(packetId);
                }
            }

            if (toRemove.Count > 0)
            {
                MarkBadgeDirty();
            }
        }

        private bool DropCurrentMap => settingsContext != null && settingsContext.Get("trade.dropCurrentMap", false);

        private static RedPacketCreateData ToCreateData(RedPacket packet)
        {
            return new RedPacketCreateData
            {
                Id = packet.Id,
                SenderUuid = packet.SenderUuid,
                SenderDisplayName = packet.SenderDisplayName,
                DefName = packet.Template != null ? packet.Template.DefName : string.Empty,
                StuffDefName = packet.Template != null ? packet.Template.StuffDefName : string.Empty,
                Quality = packet.Template != null ? packet.Template.Quality : TradeItemQuality.None,
                HitPoints = packet.Template != null ? packet.Template.HitPoints : 0,
                TotalCount = packet.TotalCount,
                TotalPackets = packet.TotalPackets,
                Type = packet.Type,
                LuckyAlgorithmVersion = packet.LuckyAlgorithmVersion,
                CreatedAtUtc = packet.CreatedAtUtc,
                ExpiresAtUtc = packet.ExpiresAtUtc
            };
        }

        private struct ExpirySettlement
        {
            public RedPacket Packet;
            public bool IsSpecial;
            public bool HasRemaining;
        }

        private struct PacketSummary
        {
            public RedPacket Packet;
            public Dictionary<string, int> Claims;
            public DateTime CompletedAtUtc;
        }
    }
}
