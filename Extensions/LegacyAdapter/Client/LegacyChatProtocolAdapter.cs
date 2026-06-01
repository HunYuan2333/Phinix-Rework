using System;
using Google.Protobuf;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.LegacyAdapter.Client
{
    /// <summary>
    /// Legacy Chat 协议翻译器 —— 在 Legacy 模式下，
    /// 将 Rework 出站文本转译为原版 ChatMessagePacket 发送，
    /// 并将原版入站 ChatMessagePacket 转译为 FrameworkDisplayMessage 注入消息管道。
    ///
    /// 错误隔离：所有 Protobuf 解析包裹 try-catch，单个损坏包不影响后续消息处理。
    /// </summary>
    internal sealed class LegacyChatProtocolAdapter
    {
        private readonly ILegacyModuleTransport legacyTransport;
        private readonly IDisplayMessageSink displaySink;
        private readonly IClientSessionContext sessionContext;
        private const string ChatModuleName = "Chat";
        private const string ChatNamespace = "Chat";

        // Protobuf message type names (from TypeUrl after stripping namespace)
        private const string ChatMessagePacketType = "ChatMessagePacket";
        private const string ChatHistoryPacketType = "ChatHistoryPacket";
        private const string ChatMessageResponsePacketType = "ChatMessageResponsePacket";

        // 入站消息队列上限 —— 与 PhinixFrameworkClient.MaxDisplayMessages(1000) 对齐
        private const int MaxPendingMessages = 200;
        private int pendingMessageCount;

        public LegacyChatProtocolAdapter(
            ILegacyModuleTransport legacyTransport,
            IDisplayMessageSink displaySink,
            IClientSessionContext sessionContext)
        {
            this.legacyTransport = legacyTransport;
            this.displaySink = displaySink;
            this.sessionContext = sessionContext;
        }

        public void RegisterHandlers()
        {
            pendingMessageCount = 0;
            legacyTransport.RegisterHandler(ChatModuleName, OnLegacyChatPacketReceived);
        }

        public void UnregisterHandlers()
        {
            legacyTransport.UnregisterHandler(ChatModuleName);
            pendingMessageCount = 0;
        }

        /// <summary>
        /// 出站：将文本消息打包为 legacy ChatMessagePacket → 发送到 "Chat" 模块
        /// </summary>
        public bool SendChatMessage(string rawMessage)
        {
            if (string.IsNullOrEmpty(rawMessage))
                return false;

            try
            {
                var chatPacket = new Chat.ChatMessagePacket
                {
                    SessionId = sessionContext.SessionId ?? string.Empty,
                    Uuid = sessionContext.Uuid ?? string.Empty,
                    MessageId = Guid.NewGuid().ToString(),
                    Message = rawMessage,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
                };

                var packed = ProtobufPacketHelper.Pack(chatPacket);
                legacyTransport.Send(ChatModuleName, packed.ToByteArray());
                return true;
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[LegacyAdapter] Failed to send legacy chat message: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 入站：从 legacy "Chat" 模块接收原始数据 →
        /// 解析并转换为 FrameworkDisplayMessage 注入管道。
        ///
        /// 错误隔离：单个 parse/handle 失败仅记录日志，不抛异常，不中断管线。
        /// </summary>
        private void OnLegacyChatPacketReceived(string module, string connectionId, byte[] data)
        {
            if (data == null || data.Length == 0) return;

            try
            {
                // 验证 + 解析 Protobuf Any 包
                if (!ProtobufPacketHelper.ValidatePacket(
                    ChatNamespace, ChatModuleName, module, data,
                    out var parsedMessage, out var typeUrl))
                {
                    return;
                }

                // 根据类型分发 —— 每种 unpacks/detach 被各自 try-catch 包裹
                switch (typeUrl.Type)
                {
                    case ChatMessagePacketType:
                        SafeUnpackAndHandle<Chat.ChatMessagePacket>(parsedMessage, HandleChatMessage);
                        break;
                    case ChatHistoryPacketType:
                        SafeUnpackAndHandle<Chat.ChatHistoryPacket>(parsedMessage, HandleChatHistory);
                        break;
                    case ChatMessageResponsePacketType:
                        SafeUnpackAndHandle<Chat.ChatMessageResponsePacket>(parsedMessage, HandleChatMessageResponse);
                        break;
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[LegacyAdapter] Error handling legacy chat packet: {ex.Message}");
            }
        }

        /// <summary>
        /// 泛型安全解包 + 处理 —— 将 Unpack try-catch 与 handler 逻辑分离，
        /// 确保单个消息解析失败不影响同帧内其他消息。
        /// </summary>
        private void SafeUnpackAndHandle<T>(Google.Protobuf.WellKnownTypes.Any parsedMessage, Action<T> handler)
            where T : class, Google.Protobuf.IMessage<T>, new()
        {
            T packet;
            try
            {
                packet = parsedMessage.Unpack<T>();
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[LegacyAdapter] Failed to unpack {typeof(T).Name}: {ex.Message}");
                return;
            }

            if (packet == null) return;

            try
            {
                handler(packet);
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[LegacyAdapter] Error in {typeof(T).Name} handler: {ex.Message}");
            }
        }

        private void HandleChatMessage(Chat.ChatMessagePacket packet)
        {
            if (packet == null) return;
            if (string.IsNullOrWhiteSpace(packet.Message)) return;

            // 入站反压：超出限制时丢弃最旧的消息（与 framework 1000 条上限对齐，
            // 但 legacy 入站直接注入，不受 framework 容量管控）
            if (pendingMessageCount++ > MaxPendingMessages)
            {
                Verse.Log.Warning("[LegacyAdapter] Too many pending legacy chat messages, resetting counter.");
                pendingMessageCount = MaxPendingMessages / 2;
            }

            var displayMsg = new FrameworkDisplayMessage
            {
                MessageId = packet.MessageId ?? Guid.NewGuid().ToString(),
                SenderUuid = packet.Uuid ?? string.Empty,
                Text = packet.Message,
                TimestampUtcTicks = packet.Timestamp != null
                    ? packet.Timestamp.Seconds * 10_000_000L + packet.Timestamp.Nanos / 100
                    : DateTime.UtcNow.Ticks,
                Source = "builtin_chat"
            };

            displaySink.Enqueue(displayMsg);
        }

        private void HandleChatHistory(Chat.ChatHistoryPacket packet)
        {
            if (packet?.ChatMessages == null) return;

            foreach (var chatMsg in packet.ChatMessages)
            {
                HandleChatMessage(chatMsg);
            }
        }

        private void HandleChatMessageResponse(Chat.ChatMessageResponsePacket packet)
        {
            if (packet == null) return;

            if (packet.Success)
            {
                Verse.Log.Message($"[LegacyAdapter] Chat message acked by server: {packet.OriginalMessageId} → {packet.NewMessageId}");
            }
            else
            {
                Verse.Log.Warning($"[LegacyAdapter] Chat message rejected by server: {packet.Message ?? "unknown reason"}");
                displaySink.Enqueue(new FrameworkDisplayMessage
                {
                    SenderUuid = FrameworkProtocol.SystemSenderUuid,
                    Source = "system",
                    Text = $"消息发送失败: {packet.Message ?? "未知原因"}"
                });
            }
        }
    }
}
