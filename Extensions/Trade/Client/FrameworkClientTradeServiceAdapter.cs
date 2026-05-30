using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.LegacyAdapter.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    internal sealed class FrameworkClientTradeServiceAdapter : IClientTradeService, ITradeRequestApi
    {
        private readonly IFrameworkTradeClientApi tradeService;
        private readonly IFrameworkClientTransport frameworkClient;
        private readonly IFrameworkClientLifecycle lifecycle;
        private readonly IClientSessionContext sessionContext;
        private readonly ILegacyModuleTransport legacyTransport;
        private readonly Action<string, LogLevel> log;

        public FrameworkClientTradeServiceAdapter(
            IFrameworkTradeClientApi tradeService,
            IFrameworkClientTransport frameworkClient,
            IFrameworkClientLifecycle lifecycle,
            IClientSessionContext sessionContext,
            ILegacyModuleTransport legacyTransport,
            Action<string, LogLevel> log)
        {
            this.tradeService = tradeService;
            this.frameworkClient = frameworkClient;
            this.lifecycle = lifecycle;
            this.sessionContext = sessionContext;
            this.legacyTransport = legacyTransport;
            this.log = log;
        }

        public event EventHandler<LogEventArgs> OnLogEntry
        {
            add { }
            remove { }
        }

        public event EventHandler<TradeCreationEventArgs> OnTradeCreationRequested;

        public event EventHandler<TradeCreationEventArgs> OnTradeCreationSuccess
        {
            add => tradeService.OnTradeCreationSuccess += value;
            remove => tradeService.OnTradeCreationSuccess -= value;
        }

        public event EventHandler<TradeCreationEventArgs> OnTradeCreationFailure
        {
            add => tradeService.OnTradeCreationFailure += value;
            remove => tradeService.OnTradeCreationFailure -= value;
        }

        public event EventHandler<TradeCompletionEventArgs> OnTradeCompleted
        {
            add => tradeService.OnTradeCompleted += value;
            remove => tradeService.OnTradeCompleted -= value;
        }

        public event EventHandler<TradeCompletionEventArgs> OnTradeCancelled
        {
            add => tradeService.OnTradeCancelled += value;
            remove => tradeService.OnTradeCancelled -= value;
        }

        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess
        {
            add => tradeService.OnTradeUpdateSuccess += value;
            remove => tradeService.OnTradeUpdateSuccess -= value;
        }

        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure
        {
            add => tradeService.OnTradeUpdateFailure += value;
            remove => tradeService.OnTradeUpdateFailure -= value;
        }

        public event EventHandler<TradesSyncedEventArgs> OnTradesSynced
        {
            add => tradeService.OnTradesSynced += value;
            remove => tradeService.OnTradesSynced -= value;
        }

        public void CreateTrade(string uuid)
        {
            OnTradeCreationRequested?.Invoke(this, new TradeCreationEventArgs(new ClientTradeSnapshot(string.Empty, new UserManagement.ImmutableUser(uuid))));
            SendTradePacket(tradeService.CreateTradeRequest(uuid, createContext()));
        }

        public void CancelTrade(string tradeId)
        {
            Verse.Log.Message($"[TradeAdapter] CancelTrade: tradeId={tradeId}");
            SendTradePacket(tradeService.CreateStatusUpdateRequest(tradeId, null, true, createContext()));
        }

        public string[] GetTradeIds() => tradeService.GetTradeIds();

        public ClientTradeSnapshot[] GetTrades() => tradeService.GetTrades();

        public ClientTradeSnapshot[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids)
        {
            HashSet<string> ignored = new HashSet<string>(otherPartyUuids ?? Enumerable.Empty<string>());
            return GetTrades().Where(trade => !ignored.Contains(trade.OtherPartyUuid)).ToArray();
        }

        public bool TryGetTrade(string tradeId, out ClientTradeSnapshot trade) => tradeService.TryGetTrade(tradeId, out trade);

        public bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid) => tradeService.TryGetOtherPartyUuid(tradeId, sessionContext.Uuid, out otherPartyUuid);

        public bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted) => tradeService.TryGetOtherPartyAccepted(tradeId, sessionContext.Uuid, out otherPartyAccepted);

        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted) => tradeService.TryGetPartyAccepted(tradeId, partyUuid, out accepted);

        public bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<TradeItemSnapshot> items) => tradeService.TryGetItemsOnOffer(tradeId, uuid, out items);

        public void UpdateTradeItems(string tradeId, IEnumerable<TradeItemSnapshot> items, string token = "")
        {
            FrameworkPacket packet = tradeService.CreateOfferUpdateRequest(tradeId, items, createContext());
            if (!string.IsNullOrEmpty(token))
            {
                packet.SetCorrelationId(token);
            }

            SendTradePacket(packet);
        }

        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null)
        {
            Verse.Log.Message($"[TradeAdapter] UpdateTradeStatus: tradeId={tradeId}, accepted={accepted}, cancelled={cancelled}");
            SendTradePacket(tradeService.CreateStatusUpdateRequest(tradeId, accepted, cancelled, createContext()));
        }

        /// <summary>
        /// 根据 CompatibilityMode 路由 trade 出站包：
        /// - FrameworkV2: 直接发 FrameworkPacket 到 "PhinixFramework" 模块
        /// - Legacy: 通过 ILegacyModuleTransport 发送 legacy proto 包。
        ///   设计哲学 §3.7：出站命令通过 CompatibilityMode 判断后决定路由策略。
        ///   设计哲学 §1.3：ILegacyModuleTransport 是 host 通用服务，任何插件可用。
        /// </summary>
        private void SendTradePacket(FrameworkPacket packet)
        {
            if (packet == null) return;

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
            {
                frameworkClient.SendFrameworkPacket(packet);
                return;
            }

            // Legacy 模式：Trade 插件自己通过 ILegacyModuleTransport 发送 legacy proto 包。
            // 出站命令不经过 IClientCommandHandler 管线（目前没有出站命令管线），
            // 由业务接口直接路由。符合 §3.7 "通过 CompatibilityMode 判断路由策略"。
            SendLegacyPacket(packet);
        }

        private void SendLegacyPacket(FrameworkPacket packet)
        {
            try
            {
                switch (packet?.MessageType)
                {
                    case FrameworkTradeProtocol.CreateRequestType:
                    {
                        var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeCreateRequest>(packet.PayloadJson);
                        if (payload != null)
                            legacyTransport.Send("Trading", PackProtobuf(new Trading.CreateTradePacket
                            {
                                SessionId = sessionContext.SessionId ?? "",
                                Uuid = sessionContext.Uuid ?? "",
                                OtherPartyUuid = payload.OtherPartyUuid ?? ""
                            }));
                        break;
                    }
                    case FrameworkTradeProtocol.OfferUpdateRequestType:
                    {
                        var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(packet.PayloadJson);
                        if (payload != null)
                            legacyTransport.Send("Trading", PackProtobuf(new Trading.UpdateTradeItemsPacket
                            {
                                SessionId = sessionContext.SessionId ?? "",
                                Uuid = sessionContext.Uuid ?? "",
                                TradeId = payload.TradeId ?? "",
                                Token = packet.GetCorrelationId() ?? ""
                            }));
                        break;
                    }
                    case FrameworkTradeProtocol.StatusUpdateRequestType:
                    {
                        var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateRequest>(packet.PayloadJson);
                        if (payload != null)
                            legacyTransport.Send("Trading", PackProtobuf(new Trading.UpdateTradeStatusPacket
                            {
                                SessionId = sessionContext.SessionId ?? "",
                                Uuid = sessionContext.Uuid ?? "",
                                TradeId = payload.TradeId ?? "",
                                Accepted = payload.Accepted ?? false,
                                Cancelled = payload.Cancelled ?? false
                            }));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[TradeAdapter] Failed to send legacy packet: {ex.Message}", LogLevel.ERROR);
            }
        }

        private static byte[] PackProtobuf(object packet) =>
            Google.Protobuf.WellKnownTypes.Any.Pack((Google.Protobuf.IMessage)packet).ToByteArray();

        private ClientFrameworkContext createContext()
        {
            return new ClientFrameworkContext
            {
                CompatibilityMode = lifecycle.CompatibilityMode,
                SenderUuid = sessionContext.Uuid,
                SessionId = sessionContext.SessionId,
                SendMessage = frameworkClient.SendFrameworkPacket,
                RemoteCapabilities = Array.Empty<string>(),
                HasRemoteCapability = frameworkClient.HasRemoteCapability,
                Log = (message, level) => log?.Invoke(message, level)
            };
        }
    }
}
