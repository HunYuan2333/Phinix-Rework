using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Phinix.TradeExtension;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.LegacyAdapter.Client
{
    /// <summary>
    /// Legacy Trade 协议翻译器 —— 在 Legacy 模式下，
    /// 将 Rework Trade API 调用转译为原版 Trading 模块的 Protobuf 包，
    /// 并将原版入站 Trading 包转译为 FrameworkTradeStateSnapshot 写入 repo。
    ///
    /// 设计哲学 §3.3：插件间交互通过 API registry 解析接口，框架不充当中介。
    /// Adapter 通过 IFrameworkTradeClientApi.UpsertTrade/RemoveTrade 写 repository，
    /// Trade UI 自动通过 RepositoryChanged 事件感知变化。
    /// </summary>
    internal sealed class LegacyTradeProtocolAdapter
    {
        private readonly ILegacyModuleTransport legacyTransport;
        private readonly IDisplayMessageSink displaySink;
        private readonly IClientSessionContext sessionContext;
        private readonly IFrameworkTradeClientApi tradeApi;
        private readonly System.Action<string, Utils.LogLevel> log;
        private const string TradingModuleName = "Trading";
        private const string TradingNamespace = "Trading";

        public LegacyTradeProtocolAdapter(
            ILegacyModuleTransport legacyTransport,
            IDisplayMessageSink displaySink,
            IClientSessionContext sessionContext,
            IFrameworkTradeClientApi tradeApi,
            System.Action<string, Utils.LogLevel> log)
        {
            this.legacyTransport = legacyTransport;
            this.displaySink = displaySink;
            this.sessionContext = sessionContext;
            this.tradeApi = tradeApi;
            this.log = log;
        }

        public void RegisterHandlers()
        {
            legacyTransport.RegisterHandler(TradingModuleName, OnLegacyTradePacketReceived);
        }

        public void UnregisterHandlers()
        {
            legacyTransport.UnregisterHandler(TradingModuleName);
        }

        // ============ 出站：Rework API → Legacy Proto ============

        public void SendCreateTrade(string otherPartyUuid)
        {
            try
            {
                var packet = new Trading.CreateTradePacket
                {
                    SessionId = sessionContext.SessionId ?? string.Empty,
                    Uuid = sessionContext.Uuid ?? string.Empty,
                    OtherPartyUuid = otherPartyUuid ?? string.Empty
                };
                var packed = ProtobufPacketHelper.Pack(packet);
                legacyTransport.Send(TradingModuleName, packed.ToByteArray());
                log?.Invoke($"[LegacyAdapter] Sent CreateTrade to {otherPartyUuid}", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[LegacyAdapter] Failed to send CreateTrade: {ex.Message}", LogLevel.ERROR);
            }
        }

        public void SendUpdateItems(string tradeId, IEnumerable<Trading.ProtoThing> items, string token = "")
        {
            try
            {
                var packet = new Trading.UpdateTradeItemsPacket
                {
                    SessionId = sessionContext.SessionId ?? string.Empty,
                    Uuid = sessionContext.Uuid ?? string.Empty,
                    TradeId = tradeId ?? string.Empty,
                    Token = token ?? string.Empty
                };
                if (items != null)
                    packet.Items.AddRange(items);

                var packed = ProtobufPacketHelper.Pack(packet);
                legacyTransport.Send(TradingModuleName, packed.ToByteArray());
                log?.Invoke($"[LegacyAdapter] Sent UpdateTradeItems for {tradeId}", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[LegacyAdapter] Failed to send UpdateTradeItems: {ex.Message}", LogLevel.ERROR);
            }
        }

        public void SendUpdateStatus(string tradeId, bool? accepted, bool? cancelled)
        {
            try
            {
                var packet = new Trading.UpdateTradeStatusPacket
                {
                    SessionId = sessionContext.SessionId ?? string.Empty,
                    Uuid = sessionContext.Uuid ?? string.Empty,
                    TradeId = tradeId ?? string.Empty,
                    Accepted = accepted ?? false,
                    Cancelled = cancelled ?? false
                };
                var packed = ProtobufPacketHelper.Pack(packet);
                legacyTransport.Send(TradingModuleName, packed.ToByteArray());
                log?.Invoke($"[LegacyAdapter] Sent UpdateTradeStatus for {tradeId} (accepted={accepted}, cancelled={cancelled})", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[LegacyAdapter] Failed to send UpdateTradeStatus: {ex.Message}", LogLevel.ERROR);
            }
        }

        // ============ 入站：Legacy Proto → FrameworkTradeStateSnapshot → repo ============

        private void OnLegacyTradePacketReceived(string module, string connectionId, byte[] data)
        {
            try
            {
                if (!ProtobufPacketHelper.ValidatePacket(
                    TradingNamespace, TradingModuleName, module, data,
                    out var parsedMessage, out var typeUrl))
                {
                    return;
                }

                switch (typeUrl.Type)
                {
                    case "CreateTradeResponsePacket":
                        HandleCreateResponse(parsedMessage.Unpack<Trading.CreateTradeResponsePacket>());
                        break;
                    case "CompleteTradePacket":
                        HandleCompleteTrade(parsedMessage.Unpack<Trading.CompleteTradePacket>());
                        break;
                    case "UpdateTradeItemsPacket":
                        HandleUpdateItems(parsedMessage.Unpack<Trading.UpdateTradeItemsPacket>());
                        break;
                    case "UpdateTradeItemsResponsePacket":
                        HandleUpdateItemsResponse(parsedMessage.Unpack<Trading.UpdateTradeItemsResponsePacket>());
                        break;
                    case "UpdateTradeStatusPacket":
                        HandleUpdateStatus(parsedMessage.Unpack<Trading.UpdateTradeStatusPacket>());
                        break;
                    case "SyncTradesPacket":
                        HandleSyncTrades(parsedMessage.Unpack<Trading.SyncTradesPacket>());
                        break;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[LegacyAdapter] Error handling legacy trade packet: {ex.Message}", LogLevel.ERROR);
            }
        }

        // ---- 转换逻辑（设计哲学 §3.3：Adapter 负责全部协议转换，tradeApi 仅负责写入和通知） ----

        private void HandleCreateResponse(Trading.CreateTradeResponsePacket packet)
        {
            if (packet == null) return;

            if (packet.Success)
            {
                // 创建初始 snapshot 并注入 repo，后续 UpdateItems 才能正确合并
                var snapshot = new FrameworkTradeStateSnapshot
                {
                    TradeId = packet.TradeId,
                    Participants = new List<FrameworkTradeParticipantSnapshot>
                    {
                        new FrameworkTradeParticipantSnapshot { Uuid = sessionContext.Uuid },
                        new FrameworkTradeParticipantSnapshot { Uuid = packet.OtherPartyUuid ?? string.Empty }
                    }
                };

                tradeApi?.UpsertTrade(snapshot);

                log?.Invoke($"[LegacyAdapter] Trade created: {packet.TradeId} with {packet.OtherPartyUuid}", LogLevel.DEBUG);
                displaySink.Enqueue(new FrameworkDisplayMessage
                {
                    SenderUuid = FrameworkProtocol.SystemSenderUuid,
                    Source = "system",
                    Text = $"交易请求已发送至 {packet.OtherPartyUuid}"
                });
            }
            else
            {
                log?.Invoke($"[LegacyAdapter] Trade creation failed: {packet.FailureReason} - {packet.FailureMessage}", LogLevel.WARNING);
                displaySink.Enqueue(new FrameworkDisplayMessage
                {
                    SenderUuid = FrameworkProtocol.SystemSenderUuid,
                    Source = "system",
                    Text = $"交易请求失败: {packet.FailureMessage ?? "未知原因"}"
                });
            }
        }

        private void HandleCompleteTrade(Trading.CompleteTradePacket packet)
        {
            if (packet == null) return;

            tradeApi?.RemoveTrade(packet.TradeId);

            string verb = packet.Success ? "完成" : "取消";
            displaySink.Enqueue(new FrameworkDisplayMessage
            {
                SenderUuid = FrameworkProtocol.SystemSenderUuid,
                Source = "system",
                Text = $"交易已{verb} ID: {packet.TradeId}"
            });
            log?.Invoke($"[LegacyAdapter] Trade completed/cancelled: {packet.TradeId} success={packet.Success}", LogLevel.DEBUG);
        }

        private void HandleUpdateItems(Trading.UpdateTradeItemsPacket packet)
        {
            if (packet == null || tradeApi == null) return;

            // 从 repo 读已有 snapshot 做合并更新，而非凭空创建
            var snapshot = GetOrCreateSnapshot(packet.TradeId);

            // 查找 packet.Uuid 对应的参与者并更新其物品
            var participant = snapshot.Participants.FirstOrDefault(p =>
                string.Equals(p.Uuid, packet.Uuid, StringComparison.OrdinalIgnoreCase));
            if (participant == null)
            {
                participant = new FrameworkTradeParticipantSnapshot { Uuid = packet.Uuid };
                snapshot.Participants.Add(participant);
            }
            participant.ItemsOnOffer = ConvertProtoThings(packet.Items);

            // OtherPartyItems：更新对方（Uuid != packet.Uuid）的参与者
            var other = snapshot.Participants.FirstOrDefault(p =>
                !string.Equals(p.Uuid, packet.Uuid, StringComparison.OrdinalIgnoreCase));
            if (other != null)
            {
                other.ItemsOnOffer = ConvertProtoThings(packet.OtherPartyItems);
            }

            tradeApi.UpsertTrade(snapshot);
            log?.Invoke($"[LegacyAdapter] Trade items updated: {packet.TradeId}", LogLevel.DEBUG);
        }

        private void HandleUpdateItemsResponse(Trading.UpdateTradeItemsResponsePacket packet)
        {
            if (packet == null) return;

            if (!packet.Success)
            {
                displaySink.Enqueue(new FrameworkDisplayMessage
                {
                    SenderUuid = FrameworkProtocol.SystemSenderUuid,
                    Source = "system",
                    Text = $"物品更新失败: {packet.FailureMessage ?? "未知原因"}"
                });
            }
        }

        private void HandleUpdateStatus(Trading.UpdateTradeStatusPacket packet)
        {
            if (packet == null || tradeApi == null) return;

            // 从 repo 合并更新，而非凭空创建
            var snapshot = GetOrCreateSnapshot(packet.TradeId);

            // 更新 packet.Uuid 对应参与者的 Accepted 状态
            var participant = snapshot.Participants.FirstOrDefault(p =>
                string.Equals(p.Uuid, packet.Uuid, StringComparison.OrdinalIgnoreCase));
            if (participant == null)
            {
                participant = new FrameworkTradeParticipantSnapshot { Uuid = packet.Uuid };
                snapshot.Participants.Add(participant);
            }
            participant.Accepted = packet.Accepted;

            // 对方有 OtherPartyAccepted 字段时也更新
            if (packet.OtherPartyAccepted)
            {
                var other = snapshot.Participants.FirstOrDefault(p =>
                    !string.Equals(p.Uuid, packet.Uuid, StringComparison.OrdinalIgnoreCase));
                if (other != null)
                {
                    other.Accepted = true;
                }
            }

            // 如果 trade 已被取消，从 repo 移除
            if (packet.Cancelled)
            {
                tradeApi.RemoveTrade(packet.TradeId);
            }
            else
            {
                tradeApi.UpsertTrade(snapshot);
            }

            log?.Invoke($"[LegacyAdapter] Trade status updated: {packet.TradeId} accepted={packet.Accepted} cancelled={packet.Cancelled}", LogLevel.DEBUG);
        }

        private void HandleSyncTrades(Trading.SyncTradesPacket packet)
        {
            if (packet?.Trades == null || tradeApi == null) return;

            foreach (var legacyTrade in packet.Trades)
            {
                if (string.IsNullOrEmpty(legacyTrade.TradeId)) continue;

                var snapshot = ConvertTradeProto(legacyTrade);
                tradeApi.UpsertTrade(snapshot);
            }

            log?.Invoke($"[LegacyAdapter] Synced {packet.Trades.Count} active trade(s)", LogLevel.DEBUG);
        }

        // ---- Legacy Proto → FrameworkTypes 转换 ----

        private FrameworkTradeStateSnapshot ConvertTradeProto(Trading.TradeProto legacyTrade)
        {
            string localUuid = sessionContext.Uuid;

            // Items 字段：旧版不区分哪方是谁的，根据协议语义：Items=己方物品，OtherPartyItems=对方物品
            return new FrameworkTradeStateSnapshot
            {
                TradeId = legacyTrade.TradeId,
                Participants = new List<FrameworkTradeParticipantSnapshot>
                {
                    new FrameworkTradeParticipantSnapshot
                    {
                        Uuid = localUuid,
                        Accepted = legacyTrade.Accepted,
                        ItemsOnOffer = ConvertProtoThings(legacyTrade.Items)
                    },
                    new FrameworkTradeParticipantSnapshot
                    {
                        Uuid = legacyTrade.OtherPartyUuid,
                        Accepted = legacyTrade.OtherPartyAccepted,
                        ItemsOnOffer = ConvertProtoThings(legacyTrade.OtherPartyItems)
                    }
                }
            };
        }

        private static List<FrameworkItemPayload> ConvertProtoThings(
            Google.Protobuf.Collections.RepeatedField<Trading.ProtoThing> protoThings)
        {
            if (protoThings == null) return new List<FrameworkItemPayload>();

            var items = new List<FrameworkItemPayload>(protoThings.Count);
            foreach (var protoThing in protoThings)
            {
                // 用旧版 ProtoThing 编码为 FrameworkItemPayload ——
                // TradeItemPipeline 不识别旧版格式，作为 UnknownCodec 存储。
                // UI 在 Legacy 模式下通过 Adapter 直接读写 repo，不依赖 item codec 解码旧版物品。
                items.Add(new FrameworkItemPayload
                {
                    CodecId = "PhinixLegacy.ProtoThing",
                    PayloadJson = protoThing?.ToString() ?? string.Empty
                });
            }
            return items;
        }

        /// <summary>
        /// 从 repo 中查找已有 trade 的另一方 UUID。
        /// 如果 repo 中不存在该 trade，则无法确定对方，返回空字符串。
        /// </summary>
        private string ResolveOtherPartyUuid(string tradeId, string senderUuid)
        {
            if (tradeApi == null || string.IsNullOrEmpty(tradeId)) return string.Empty;

            if (!tradeApi.TryGetTrade(tradeId, out ClientTradeSnapshot existing))
                return string.Empty;

            return existing.OtherPartyUuid ?? string.Empty;
        }

        /// <summary>
        /// 从 repo 读取已有 snapshot，如果不存在则创建最小初始 snapshot。
        /// </summary>
        private FrameworkTradeStateSnapshot GetOrCreateSnapshot(string tradeId)
        {
            if (tradeApi != null && tradeApi.TryGetTrade(tradeId, out _))
            {
                // 通过 repo 内部接口获取原始 snapshot
                // TryGetTrade 返回 ClientTradeSnapshot（已转换），我们需要 FrameworkTradeStateSnapshot
                // 直接重新创建一个包含已有参与者的 snapshot 作为基础
                return BuildSnapshotFromClientTrade(tradeId);
            }

            return new FrameworkTradeStateSnapshot
            {
                TradeId = tradeId,
                Participants = new List<FrameworkTradeParticipantSnapshot>
                {
                    new FrameworkTradeParticipantSnapshot { Uuid = sessionContext.Uuid }
                }
            };
        }

        private FrameworkTradeStateSnapshot BuildSnapshotFromClientTrade(string tradeId)
        {
            // 用最保守的方式：已知本地 UUID，加上如果 repo 中有对方 UUID 则加入
            var snapshot = new FrameworkTradeStateSnapshot
            {
                TradeId = tradeId,
                Participants = new List<FrameworkTradeParticipantSnapshot>
                {
                    new FrameworkTradeParticipantSnapshot { Uuid = sessionContext.Uuid }
                }
            };

            string otherPartyUuid = ResolveOtherPartyUuid(tradeId, sessionContext.Uuid);
            if (!string.IsNullOrEmpty(otherPartyUuid))
            {
                snapshot.Participants.Add(new FrameworkTradeParticipantSnapshot { Uuid = otherPartyUuid });
            }

            return snapshot;
        }
    }
}
