using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Phinix.TradeExtension;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;

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
    ///
    /// 设计哲学 §3.7：实现 IClientOutgoingCommandHandler 接入出站命令管线，
    /// 在 Legacy 模式下抢在 Trade handler 前面拦截并翻译 FrameworkPacket → Legacy Proto。
    ///
    /// 设计哲学 §3.8：所有日志通过 ILoggable 回调上报，异常附带完整 Exception 对象。
    /// </summary>
    internal sealed class LegacyTradeProtocolAdapter : IClientOutgoingCommandHandler
    {
        private readonly ILegacyModuleTransport legacyTransport;
        private readonly IDisplayMessageSink displaySink;
        private readonly IClientSessionContext sessionContext;
        private readonly IFrameworkTradeClientApi tradeApi;
        private readonly IFrameworkLegacyTradeCompletionApi legacyCompletionApi;
        private readonly IFrameworkClientLifecycle lifecycle;
        private readonly System.Action<string, Utils.LogLevel> log;
        private const string TradingModuleName = "Trading";
        private const string TradingNamespace = "Trading";

        public LegacyTradeProtocolAdapter(
            ILegacyModuleTransport legacyTransport,
            IDisplayMessageSink displaySink,
            IClientSessionContext sessionContext,
            IFrameworkTradeClientApi tradeApi,
            IFrameworkClientLifecycle lifecycle,
            System.Action<string, Utils.LogLevel> log)
        {
            this.legacyTransport = legacyTransport;
            this.displaySink = displaySink;
            this.sessionContext = sessionContext;
            this.tradeApi = tradeApi;
            legacyCompletionApi = tradeApi as IFrameworkLegacyTradeCompletionApi;
            this.lifecycle = lifecycle;
            this.log = log;
        }

        int ICommandHandler.Priority => 500; // 高于 Trade handler (1100)，优先拦截

        public void RegisterHandlers()
        {
            legacyTransport.RegisterHandler(TradingModuleName, OnLegacyTradePacketReceived);
        }

        public void UnregisterHandlers()
        {
            legacyTransport.UnregisterHandler(TradingModuleName);
        }

        // ========== IClientOutgoingCommandHandler ==========

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            var currentMode = lifecycle?.CompatibilityMode ?? FrameworkCompatibilityMode.Unknown;
            bool canHandle = currentMode == FrameworkCompatibilityMode.Legacy
                && command?.MessageType?.StartsWith("trade.") == true;

            log?.Invoke(
                $"[LegacyAdapter] CanHandleOutgoingCommand: mode={currentMode}, msgType={command?.MessageType ?? "null"} → {canHandle}",
                LogLevel.DEBUG);

            return canHandle;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(
            FrameworkPacket command, ClientFrameworkContext context)
        {
            log?.Invoke(
                $"[LegacyAdapter] HandleOutgoingCommand: msgType={command?.MessageType ?? "null"}, mode={lifecycle?.CompatibilityMode}",
                LogLevel.INFO);

            try
            {
                SendLegacyPacket(command);
                log?.Invoke($"[LegacyAdapter] HandleOutgoingCommand: sent successfully", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[LegacyAdapter] HandleOutgoingCommand failed: {ex}", LogLevel.ERROR);
            }

            // 总是返回 Handled（Command=null），框架不再发送 FrameworkPacket。
            // Legacy 模式下 trade 包已通过 legacy transport 发出。
            return new ClientOutgoingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }

        private void SendLegacyPacket(FrameworkPacket packet)
        {
            log?.Invoke(
                $"[LegacyAdapter] SendLegacyPacket: msgType={packet?.MessageType ?? "null"}",
                LogLevel.DEBUG);

            switch (packet?.MessageType)
            {
                case FrameworkTradeProtocol.CreateRequestType:
                {
                    var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeCreateRequest>(packet.PayloadJson);
                    if (payload != null)
                    {
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: CreateRequest → {payload.OtherPartyUuid}", LogLevel.DEBUG);
                        SendCreateTrade(payload.OtherPartyUuid);
                    }
                    else
                    {
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: CreateRequest payload deserialization failed", LogLevel.WARNING);
                    }
                    break;
                }
                case FrameworkTradeProtocol.OfferUpdateRequestType:
                {
                    var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(packet.PayloadJson);
                    if (payload != null)
                    {
                        int rawCount = payload.Items?.Count ?? 0;
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: OfferUpdate tradeId={payload.TradeId}, rawItems={rawCount}", LogLevel.INFO);

                        var items = ConvertToProtoThings(payload.Items);
                        int convertedCount = items?.Count ?? 0;
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: OfferUpdate converted {rawCount} items → {convertedCount} proto things", LogLevel.INFO);

                        if (rawCount > 0 && convertedCount == 0)
                        {
                            string firstCodecId = payload.Items[0]?.CodecId ?? "null";
                            log?.Invoke(
                                $"[LegacyAdapter] ERROR: All {rawCount} items dropped during conversion! First item CodecId={firstCodecId}, PayloadBytes.Length={payload.Items[0]?.PayloadBytes?.Length ?? -1}, PayloadJson.Length={payload.Items[0]?.PayloadJson?.Length ?? -1}",
                                LogLevel.ERROR);
                        }

                        SendUpdateItems(payload.TradeId, items, packet.GetCorrelationId());
                        ApplyLocalOfferSnapshot(payload.TradeId, payload.Items, packet.GetCorrelationId());
                    }
                    else
                    {
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: OfferUpdate payload deserialization failed", LogLevel.WARNING);
                    }
                    break;
                }
                case FrameworkTradeProtocol.StatusUpdateRequestType:
                {
                    var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateRequest>(packet.PayloadJson);
                    if (payload != null)
                    {
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: StatusUpdate tradeId={payload.TradeId}, accepted={payload.Accepted}, cancelled={payload.Cancelled}", LogLevel.DEBUG);
                        SendUpdateStatus(payload.TradeId, payload.Accepted, payload.Cancelled);
                    }
                    else
                    {
                        log?.Invoke($"[LegacyAdapter] SendLegacyPacket: StatusUpdate payload deserialization failed", LogLevel.WARNING);
                    }
                    break;
                }
                default:
                    log?.Invoke($"[LegacyAdapter] SendLegacyPacket: unknown MessageType '{packet?.MessageType}'", LogLevel.WARNING);
                    break;
            }
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
                log?.Invoke($"[LegacyAdapter] Failed to send CreateTrade: {ex}", LogLevel.ERROR);
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

                int itemCount = packet.Items.Count;
                var packed = ProtobufPacketHelper.Pack(packet);
                byte[] packedBytes = packed.ToByteArray();
                log?.Invoke(
                    $"[LegacyAdapter] SendUpdateItems: tradeId={tradeId}, items={itemCount}, bytes={packedBytes.Length}, module=Trading",
                    LogLevel.INFO);
                legacyTransport.Send(TradingModuleName, packedBytes);
                log?.Invoke($"[LegacyAdapter] Sent UpdateTradeItems for {tradeId} ({itemCount} items)", LogLevel.DEBUG);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[LegacyAdapter] Failed to send UpdateTradeItems: {ex}", LogLevel.ERROR);
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
                log?.Invoke($"[LegacyAdapter] Failed to send UpdateTradeStatus: {ex}", LogLevel.ERROR);
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
                log?.Invoke($"[LegacyAdapter] Error handling legacy trade packet: {ex}", LogLevel.ERROR);
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

            var completionItems = DecodeProtoThings(packet.Items);
            legacyCompletionApi?.CompleteTrade(packet.TradeId, packet.Success, packet.OtherPartyUuid, completionItems);

            string verb = packet.Success ? "完成" : "取消";
            displaySink.Enqueue(new FrameworkDisplayMessage
            {
                SenderUuid = FrameworkProtocol.SystemSenderUuid,
                Source = "system",
                Text = $"交易已{verb} ID: {packet.TradeId}"
            });
            log?.Invoke($"[LegacyAdapter] Trade completed/cancelled: {packet.TradeId} success={packet.Success}", LogLevel.DEBUG);
        }

        private void ApplyLocalOfferSnapshot(string tradeId, List<FrameworkItemPayload> items, string token)
        {
            if (tradeApi == null || string.IsNullOrEmpty(tradeId))
                return;

            var target = tradeApi.GetRepositoryTrades()
                .FirstOrDefault(t => string.Equals(t.TradeId, tradeId, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                log?.Invoke(
                    $"[LegacyAdapter] ApplyLocalOfferSnapshot: trade {tradeId} not in repo; waiting for legacy echo",
                    LogLevel.WARNING);
                return;
            }

            if (target.Participants == null)
                target.Participants = new List<FrameworkTradeParticipantSnapshot>();

            var local = target.Participants.FirstOrDefault(p =>
                string.Equals(p.Uuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase));
            if (local == null)
            {
                log?.Invoke(
                    $"[LegacyAdapter] ApplyLocalOfferSnapshot: local participant '{sessionContext.Uuid}' not found for {tradeId}",
                    LogLevel.ERROR);
                return;
            }

            if (!string.IsNullOrEmpty(token))
                tradeApi.TrackPendingTradeUpdate(tradeId, token);

            local.ItemsOnOffer = CloneFrameworkItems(items);
            tradeApi.UpsertTrade(target);
            log?.Invoke(
                $"[LegacyAdapter] ApplyLocalOfferSnapshot: local offer updated for {tradeId}, items={local.ItemsOnOffer.Count}",
                LogLevel.DEBUG);
        }

        private void HandleUpdateItems(Trading.UpdateTradeItemsPacket packet)
        {
            if (packet == null || tradeApi == null) return;
            string participantUuid = ResolveLegacyPacketParticipantUuid(packet.Uuid, "HandleUpdateItems");

            // 直接从 repo 读取原始 FrameworkTradeStateSnapshot 进行原位更新，
            // 避免通过 ToTradeSnapshot（ClientTradeSnapshot）再重建造成的 UUID 精度损失
            // 和 "cannot resolve other party" 问题。
            var target = tradeApi.GetRepositoryTrades()
                .FirstOrDefault(t => string.Equals(t.TradeId, packet.TradeId, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                var existingUuids = string.Join(", ", (target.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                    .Select(p => $"'{p.Uuid}'"));
                log?.Invoke(
                    $"[LegacyAdapter] HandleUpdateItems: found existing trade {packet.TradeId} " +
                    $"with participants=[{existingUuids}], packet.Uuid='{packet.Uuid}', " +
                    $"effectiveUuid='{participantUuid}', sessionContext.Uuid='{sessionContext.Uuid}', " +
                    $"items={packet.Items.Count}, otherPartyItems={packet.OtherPartyItems.Count}",
                    LogLevel.WARNING);
            }

            if (target == null)
            {
                log?.Invoke(
                    $"[LegacyAdapter] HandleUpdateItems: trade {packet.TradeId} not in repo — creating from packet",
                    LogLevel.INFO);

                // 仓库中没有该 trade；从 packet 数据构造初始 snapshot。
                // 旧协议可能把当前客户端视角的 UUID 留空，所以这里使用归一化后的参与者 UUID。
                string otherUuid = string.Equals(participantUuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : participantUuid;
                if (string.IsNullOrEmpty(otherUuid))
                {
                    log?.Invoke(
                        $"[LegacyAdapter] HandleUpdateItems: cannot determine other party for {packet.TradeId} — dropping",
                        LogLevel.WARNING);
                    return;
                }

                target = new FrameworkTradeStateSnapshot
                {
                    TradeId = packet.TradeId,
                    Participants = new List<FrameworkTradeParticipantSnapshot>
                    {
                        new FrameworkTradeParticipantSnapshot { Uuid = sessionContext.Uuid ?? string.Empty },
                        new FrameworkTradeParticipantSnapshot { Uuid = otherUuid }
                    }
                };
            }

            // 确保参与者数正确（后续 update 在此基础上的 merge 不会引入重复）
            if (target.Participants == null) target.Participants = new List<FrameworkTradeParticipantSnapshot>();
            var currentUuids = string.Join(", ", target.Participants.Select(p => $"'{p.Uuid}'"));

            var local = target.Participants.FirstOrDefault(p =>
                string.Equals(p.Uuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase));
            var remote = target.Participants.FirstOrDefault(p =>
                !string.Equals(p.Uuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase));
            if (local == null || remote == null)
            {
                log?.Invoke(
                    $"[LegacyAdapter] HandleUpdateItems: cannot resolve local/remote participants in [{currentUuids}] — dropping update",
                    LogLevel.ERROR);
                return;
            }

            if (string.Equals(participantUuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase))
            {
                // Legacy update packets are client-perspective snapshots: Items=local offer,
                // OtherPartyItems=remote offer. This also covers server echo packets with empty Uuid.
                if (packet.Items.Count > 0 || (local.ItemsOnOffer == null || local.ItemsOnOffer.Count == 0))
                {
                    local.ItemsOnOffer = ConvertProtoThings(packet.Items);
                }
                else
                {
                    log?.Invoke(
                        $"[LegacyAdapter] HandleUpdateItems: preserving local offer for {packet.TradeId}; legacy echo Items is empty",
                        LogLevel.DEBUG);
                }
                remote.ItemsOnOffer = ConvertProtoThings(packet.OtherPartyItems);
            }
            else
            {
                remote.ItemsOnOffer = ConvertProtoThings(packet.Items);
                local.ItemsOnOffer = ConvertProtoThings(packet.OtherPartyItems);
            }

            if (!string.IsNullOrEmpty(packet.Token))
            {
                tradeApi.TrackPendingTradeUpdate(packet.TradeId, packet.Token);
            }

            tradeApi.UpsertTrade(target);
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
            string participantUuid = ResolveLegacyPacketParticipantUuid(packet.Uuid, "HandleUpdateStatus");

            // 直接从 repo 读取原始 snapshot 进行原位更新，避免重建造成的信息丢失。
            var target = tradeApi.GetRepositoryTrades()
                .FirstOrDefault(t => string.Equals(t.TradeId, packet.TradeId, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                var existingUuids = string.Join(", ", (target.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                    .Select(p => $"'{p.Uuid}'"));
                log?.Invoke(
                    $"[LegacyAdapter] HandleUpdateStatus: found existing trade {packet.TradeId} " +
                    $"with participants=[{existingUuids}], packet.Uuid='{packet.Uuid}', " +
                    $"effectiveUuid='{participantUuid}', sessionContext.Uuid='{sessionContext.Uuid}', accepted={packet.Accepted}, " +
                    $"otherAccepted={packet.OtherPartyAccepted}, cancelled={packet.Cancelled}",
                    LogLevel.WARNING);
            }

            if (target == null)
            {
                log?.Invoke(
                    $"[LegacyAdapter] HandleUpdateStatus: trade {packet.TradeId} not in repo — dropping",
                    LogLevel.WARNING);
                return;
            }

            var local = target.Participants.FirstOrDefault(p =>
                string.Equals(p.Uuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase));
            var remote = target.Participants.FirstOrDefault(p =>
                !string.Equals(p.Uuid, sessionContext.Uuid, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(packet.Uuid))
            {
                // Legacy status echoes with empty Uuid are client-perspective snapshots.
                // Treat both booleans as authoritative so false clears stale acceptance state.
                if (local == null || remote == null)
                {
                    var existingUuids = string.Join(", ", (target.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                        .Select(p => $"'{p.Uuid}'"));
                    log?.Invoke(
                        $"[LegacyAdapter] HandleUpdateStatus: cannot resolve local/remote participants in [{existingUuids}] — dropping status snapshot",
                        LogLevel.ERROR);
                    return;
                }

                local.Accepted = packet.Accepted;
                remote.Accepted = packet.OtherPartyAccepted;
                log?.Invoke(
                    $"[LegacyAdapter] HandleUpdateStatus: applied client-perspective status snapshot local={local.Accepted}, remote={remote.Accepted}",
                    LogLevel.DEBUG);
            }
            else
            {
                // 非空 Uuid 表示某个明确参与者的增量状态。
                var sender = target.Participants.FirstOrDefault(p =>
                    string.Equals(p.Uuid, participantUuid, StringComparison.OrdinalIgnoreCase));
                if (sender != null)
                {
                    sender.Accepted = packet.Accepted;
                }
                else
                {
                    var existingUuids = string.Join(", ", (target.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                        .Select(p => $"'{p.Uuid}'"));
                    log?.Invoke(
                        $"[LegacyAdapter] HandleUpdateStatus: participant '{participantUuid}' NOT FOUND in [{existingUuids}] — status update cannot be applied",
                        LogLevel.ERROR);
                }

                if (packet.OtherPartyAccepted)
                {
                    var other = target.Participants.FirstOrDefault(p =>
                        !string.Equals(p.Uuid, participantUuid, StringComparison.OrdinalIgnoreCase));
                    if (other != null)
                    {
                        other.Accepted = true;
                    }
                }
            }

            // 如果 trade 已被取消，从 repo 移除
            if (packet.Cancelled)
            {
                tradeApi.RemoveTrade(packet.TradeId);
            }
            else
            {
                tradeApi.UpsertTrade(target);
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

        private string ResolveLegacyPacketParticipantUuid(string packetUuid, string source)
        {
            if (!string.IsNullOrEmpty(packetUuid))
                return packetUuid;

            string localUuid = sessionContext?.Uuid ?? string.Empty;
            log?.Invoke(
                $"[LegacyAdapter] {source}: packet.Uuid is empty; using sessionContext.Uuid='{localUuid}' for legacy client-perspective update",
                LogLevel.WARNING);
            return localUuid;
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
                var itemData = ConvertProtoThingToVanillaItemData(protoThing);
                if (itemData == null)
                    continue;

                items.Add(new FrameworkItemPayload
                {
                    CodecId = "core.item.vanilla",
                    PayloadBytes = FrameworkSerialization.SerializeItemData(itemData)
                });
            }
            return items;
        }

        private static List<FrameworkItemPayload> CloneFrameworkItems(IEnumerable<FrameworkItemPayload> items)
        {
            return (items ?? Enumerable.Empty<FrameworkItemPayload>())
                .Where(item => item != null)
                .Select(item => new FrameworkItemPayload
                {
                    CodecId = item.CodecId,
                    PayloadJson = item.PayloadJson,
                    PayloadBytes = item.PayloadBytes?.ToArray() ?? Array.Empty<byte>(),
                    Metadata = (item.Metadata ?? new List<FrameworkMetadataEntry>())
                        .Select(entry => new FrameworkMetadataEntry
                        {
                            Key = entry?.Key,
                            Value = entry?.Value
                        })
                        .ToList()
                })
                .ToList();
        }

        private static TradeItemSnapshot[] DecodeProtoThings(
            Google.Protobuf.Collections.RepeatedField<Trading.ProtoThing> protoThings)
        {
            if (protoThings == null) return Array.Empty<TradeItemSnapshot>();

            return protoThings
                .Select(ConvertProtoThingToTradeItem)
                .Where(item => item != null)
                .ToArray();
        }

        private static TradeItemSnapshot ConvertProtoThingToTradeItem(Trading.ProtoThing protoThing)
        {
            if (protoThing == null) return null;

            return new TradeItemSnapshot(
                protoThing.DefName ?? string.Empty,
                protoThing.StackCount,
                protoThing.HitPoints,
                ConvertQualityToTrade(protoThing.Quality),
                protoThing.StuffDefName ?? string.Empty,
                ConvertProtoThingToTradeItem(protoThing.InnerProtoThing));
        }

        private static Phinix.Framework.FrameworkVanillaItemData ConvertProtoThingToVanillaItemData(
            Trading.ProtoThing protoThing)
        {
            if (protoThing == null) return null;

            return new Phinix.Framework.FrameworkVanillaItemData
            {
                DefName = protoThing.DefName ?? string.Empty,
                StackCount = protoThing.StackCount,
                StuffDefName = protoThing.StuffDefName ?? string.Empty,
                Quality = ConvertQualityToFramework(protoThing.Quality),
                HitPoints = protoThing.HitPoints,
                InnerItem = ConvertProtoThingToVanillaItemData(protoThing.InnerProtoThing)
            };
        }

        private static TradeItemQuality ConvertQualityToTrade(Trading.Quality quality)
        {
            switch (quality)
            {
                case Trading.Quality.Awful: return TradeItemQuality.Awful;
                case Trading.Quality.Poor: return TradeItemQuality.Poor;
                case Trading.Quality.Normal: return TradeItemQuality.Normal;
                case Trading.Quality.Good: return TradeItemQuality.Good;
                case Trading.Quality.Excellent: return TradeItemQuality.Excellent;
                case Trading.Quality.Masterwork: return TradeItemQuality.Masterwork;
                case Trading.Quality.Legendary: return TradeItemQuality.Legendary;
                case Trading.Quality.None: return TradeItemQuality.None;
                default: return TradeItemQuality.None;
            }
        }

        private static Phinix.Framework.FrameworkItemQuality ConvertQualityToFramework(Trading.Quality quality)
        {
            switch (quality)
            {
                case Trading.Quality.Awful: return Phinix.Framework.FrameworkItemQuality.Awful;
                case Trading.Quality.Poor: return Phinix.Framework.FrameworkItemQuality.Poor;
                case Trading.Quality.Normal: return Phinix.Framework.FrameworkItemQuality.Normal;
                case Trading.Quality.Good: return Phinix.Framework.FrameworkItemQuality.Good;
                case Trading.Quality.Excellent: return Phinix.Framework.FrameworkItemQuality.Excellent;
                case Trading.Quality.Masterwork: return Phinix.Framework.FrameworkItemQuality.Masterwork;
                case Trading.Quality.Legendary: return Phinix.Framework.FrameworkItemQuality.Legendary;
                case Trading.Quality.None: return Phinix.Framework.FrameworkItemQuality.None;
                default: return Phinix.Framework.FrameworkItemQuality.None;
            }
        }

        // ============ 出站方向：FrameworkItemPayload → Trading.ProtoThing ============

        /// <summary>
        /// 出站方向：FrameworkItemPayload → Trading.ProtoThing。
        /// FrameworkItemPayload 可能是：
        ///   1. "core.item.vanilla" + PayloadBytes (protobuf-encoded FrameworkVanillaItemData)
        ///   2. PayloadJson (protobuf JSON-encoded ProtoThing string)
        /// </summary>
        private List<Trading.ProtoThing> ConvertToProtoThings(List<FrameworkItemPayload> payloads)
        {
            if (payloads == null || payloads.Count == 0)
                return new List<Trading.ProtoThing>();

            var protoThings = new List<Trading.ProtoThing>(payloads.Count);
            for (int i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                var protoThing = ConvertToProtoThing(payload, i);
                if (protoThing != null)
                    protoThings.Add(protoThing);
            }
            return protoThings;
        }

        /// <summary>
        /// 转换单个 FrameworkItemPayload → Trading.ProtoThing。
        /// 设计哲学 §3.8：异常附带完整 Exception 对象，不裸 catch。
        /// </summary>
        private Trading.ProtoThing ConvertToProtoThing(FrameworkItemPayload payload, int index)
        {
            if (payload == null) return null;

            // 优先从 PayloadBytes 反序列化 FrameworkVanillaItemData（"core.item.vanilla" codec）
            if (payload.PayloadBytes != null && payload.PayloadBytes.Length > 0)
            {
                log?.Invoke(
                    $"[LegacyAdapter] ConvertToProtoThing: item[{index}] using PayloadBytes path, len={payload.PayloadBytes.Length}, CodecId={payload.CodecId ?? "null"}",
                    LogLevel.DEBUG);
                return ConvertFromFrameworkVanillaItemData(payload.PayloadBytes, index);
            }

            // 其次从 PayloadJson 反序列化
            if (!string.IsNullOrEmpty(payload.PayloadJson))
            {
                log?.Invoke(
                    $"[LegacyAdapter] ConvertToProtoThing: item[{index}] using PayloadJson path, len={payload.PayloadJson.Length}",
                    LogLevel.DEBUG);
                return ConvertFromPayloadJson(payload.PayloadJson, index);
            }

            log?.Invoke(
                $"[LegacyAdapter] ConvertToProtoThing: item[{index}] has no PayloadBytes and no PayloadJson — SKIPPED (CodecId={payload.CodecId ?? "null"})",
                LogLevel.WARNING);
            return null;
        }

        private Trading.ProtoThing ConvertFromFrameworkVanillaItemData(byte[] payloadBytes, int index)
        {
            try
            {
                var itemData = FrameworkSerialization.DeserializeItemData(payloadBytes);
                if (itemData == null)
                {
                    log?.Invoke(
                        $"[LegacyAdapter] ConvertFromFrameworkVanillaItemData: item[{index}] DeserializeItemData returned null",
                        LogLevel.WARNING);
                    return null;
                }
                return ConvertVanillaItemDataToProtoThing(itemData);
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    $"[LegacyAdapter] ConvertFromFrameworkVanillaItemData: item[{index}] failed: {ex}",
                    LogLevel.WARNING);
                return null;
            }
        }

        private Trading.ProtoThing ConvertFromPayloadJson(string payloadJson, int index)
        {
            try
            {
                // PayloadJson 存的是 ProtoThing protobuf JSON（protoThing.ToString()），
                // 直接用 protobuf JSON parser 反序列化。
                return Trading.ProtoThing.Parser.ParseJson(payloadJson);
            }
            catch (Exception ex)
            {
                log?.Invoke(
                    $"[LegacyAdapter] ConvertFromPayloadJson: item[{index}] ParseJson failed: {ex}",
                    LogLevel.WARNING);
                return null;
            }
        }

        private static Trading.ProtoThing ConvertVanillaItemDataToProtoThing(
            Phinix.Framework.FrameworkVanillaItemData itemData)
        {
            if (itemData == null) return null;

            var protoThing = new Trading.ProtoThing
            {
                DefName = itemData.DefName ?? string.Empty,
                StackCount = itemData.StackCount,
                StuffDefName = itemData.StuffDefName ?? string.Empty,
                Quality = ConvertQualityToTrading(itemData.Quality),
                HitPoints = itemData.HitPoints
            };

            if (itemData.InnerItem != null)
            {
                protoThing.InnerProtoThing = ConvertVanillaItemDataToProtoThing(itemData.InnerItem);
            }

            return protoThing;
        }

        private static Trading.Quality ConvertQualityToTrading(
            Phinix.Framework.FrameworkItemQuality quality)
        {
            switch (quality)
            {
                case Phinix.Framework.FrameworkItemQuality.Awful: return Trading.Quality.Awful;
                case Phinix.Framework.FrameworkItemQuality.Poor: return Trading.Quality.Poor;
                case Phinix.Framework.FrameworkItemQuality.Normal: return Trading.Quality.Normal;
                case Phinix.Framework.FrameworkItemQuality.Good: return Trading.Quality.Good;
                case Phinix.Framework.FrameworkItemQuality.Excellent: return Trading.Quality.Excellent;
                case Phinix.Framework.FrameworkItemQuality.Masterwork: return Trading.Quality.Masterwork;
                case Phinix.Framework.FrameworkItemQuality.Legendary: return Trading.Quality.Legendary;
                case Phinix.Framework.FrameworkItemQuality.None: return Trading.Quality.None;
                default: return Trading.Quality.None;
            }
        }

    }
}
