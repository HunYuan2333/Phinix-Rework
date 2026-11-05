using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    internal sealed class FrameworkClientTradeServiceAdapter : IClientTradeService, ITradeRequestApi, IObservedTradeConsentService, IDisposable
    {
        private readonly IFrameworkTradeClientApi tradeService;
        private volatile bool stopped;
        private IFrameworkClientTransport frameworkClient;
        private IFrameworkClientCommandTransport commandTransport;
        private IFrameworkClientLifecycle lifecycle;
        private IClientSessionContext sessionContext;
        private Action<string, LogLevel> log;
        private TradeIntentJournal intentJournal;
        internal Action<TradeIntentRecord> IntentPrepared;
        private Func<string> intentEndpoint;
        private TradeOperationQueryClient operationQueries;
        private Func<Phinix.InventoryExtension.InventoryStatus> saveReadiness;
        internal void ConfigureSaveReadiness(Func<Phinix.InventoryExtension.InventoryStatus> readStatus)
        {saveReadiness=readStatus??throw new ArgumentNullException(nameof(readStatus));}
        private void RequireSavedRecipient()
        {
            string reason=TradeSaveReadiness.BlockReason(saveReadiness?.Invoke());
            if(reason!=null)throw new InvalidOperationException(reason);
        }
        internal void ConfigureIntentJournal(TradeIntentJournal journal, Func<string> endpoint)
        {
            intentJournal = journal; intentEndpoint = endpoint;
            operationQueries = new TradeOperationQueryClient(journal, endpoint, () => sessionContext?.Uuid, () => sessionContext?.SessionId,
                () => sessionContext?.Authenticated == true && sessionContext?.LoggedIn == true &&
                    frameworkClient?.HasRemoteCapability(TradeOperationQueryProtocol.Request) == true && frameworkClient?.HasRemoteCapability(TradeOperationQueryProtocol.Response) == true,
                packet => commandTransport != null && commandTransport.TryHandleOutgoingCommand(packet));
        }
        internal bool CanQueryOperation => operationQueries?.Available == true;
        internal void QueryOperation(string operationId) => operationQueries.Query(operationId);
        internal void HandleOperationQueryResponse(FrameworkPacket packet) => operationQueries?.Handle(packet);
        internal bool RecordCommandResult(FrameworkPacket packet)
        {
            return intentJournal == null || intentJournal.RecordResponse(intentEndpoint?.Invoke(), sessionContext?.Uuid, packet);
        }


        public FrameworkClientTradeServiceAdapter(
            IFrameworkTradeClientApi tradeService)
        {
            this.tradeService = tradeService;
        }

        internal void Initialize(
            IFrameworkClientTransport frameworkClient,
            IFrameworkClientCommandTransport commandTransport,
            IFrameworkClientLifecycle lifecycle,
            IClientSessionContext sessionContext,
            Action<string, LogLevel> log)
        {
            this.frameworkClient = frameworkClient;
            this.commandTransport = commandTransport;
            this.lifecycle = lifecycle;
            this.sessionContext = sessionContext;
            this.log = log;
        }

        public void Dispose()
        {
            stopped = true;
            frameworkClient = null;
            commandTransport = null;
            lifecycle = null;
            sessionContext = null;
            log = null;
            intentJournal = null; intentEndpoint = null;
            operationQueries = null;
            saveReadiness = null;
        }

        private void EnsureActive()
        {
            if (stopped) throw new InvalidOperationException("Trade has stopped.");
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
            EnsureActive();
            OnTradeCreationRequested?.Invoke(this, new TradeCreationEventArgs(new ClientTradeSnapshot(string.Empty, new UserManagement.ImmutableUser(uuid))));
            SendTradePacket(tradeService.CreateTradeRequest(uuid, createContext()));
        }

        public void CancelTrade(string tradeId)
        {
            EnsureActive();
            GuardOfferOperation(tradeId,null,false);

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

        public bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid) => tradeService.TryGetOtherPartyUuid(tradeId, sessionContext?.Uuid, out otherPartyUuid);

        public bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted) => tradeService.TryGetOtherPartyAccepted(tradeId, sessionContext?.Uuid, out otherPartyAccepted);

        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted) => tradeService.TryGetPartyAccepted(tradeId, partyUuid, out accepted);

        public bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<TradeItemSnapshot> items) => tradeService.TryGetItemsOnOffer(tradeId, uuid, out items);

        private Func<string,string,bool,bool> offerSubmissionGuard;
        internal void SetOfferSubmissionGuard(Func<string,string,bool,bool> guard){offerSubmissionGuard=guard;}
        private void GuardOfferOperation(string tradeId,string token,bool offer)
        {if(offerSubmissionGuard!=null&&!offerSubmissionGuard(tradeId,token,offer))throw new InvalidOperationException("TradeOfferAlreadyPending");}

        public void UpdateTradeItems(string tradeId, IEnumerable<TradeItemSnapshot> items, string token = "")
        {
            EnsureActive();
            RequireSavedRecipient();
            GuardOfferOperation(tradeId,token,true);
            var tradeItems = items.ToList();

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.Legacy)
            {
                FrameworkPacket packet = tradeService.CreateOfferUpdateRequest(
                    tradeId, tradeItems, createContext());
                if (!string.IsNullOrEmpty(token))
                    packet.SetCorrelationId(token);
                SendTradePacket(packet);
                return;
            }

            FrameworkItemPayload[] payloads = tradeService.EncodeTradeItems(tradeItems);

            var itemPackets = payloads.Select(payload =>
            {
                var item = FrameworkSerialization.BuildItemPacket(payload, sessionContext.SessionId, sessionContext.Uuid);
                item.MessageType = FrameworkTradeProtocol.Capability;
                return item;
            }).ToArray();
            var command = tradeService.CreateOfferUpdateRequest(tradeId, itemPackets.Select(i => i.MessageId), createContext());
            if (!string.IsNullOrEmpty(token)) command.SetCorrelationId(token);
            // Preserve the complete batch and exact command identity, then mark the
            // uncertain transport boundary before any item or command can leave.
            PrepareIntent(command, itemPackets);
            for (int i = 0; i < payloads.Length; i++)
                if (!frameworkClient.TryHandleOutgoingItem(payloads[i]))
                    frameworkClient.SendFrameworkPacket(itemPackets[i]);
            DispatchTradePacket(command);
        }

        public void AcceptObservedTrade(ClientTradeSnapshot snapshot)
        {
            EnsureActive();
            RequireSavedRecipient();
            long version = TradeSnapshotObservation.Require(snapshot);
            GuardOfferOperation(snapshot.TradeId, null, false);
            FrameworkPacket packet = tradeService.CreateStatusUpdateRequest(snapshot.TradeId, true, null, createContext());
            packet.SetSnapshotVersion(version);
            SendTradePacket(packet);
        }

        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null)
        {
            EnsureActive();
            if (accepted == true && cancelled != true)
                throw new InvalidOperationException("Confirmation requires the displayed trade quote.");
            GuardOfferOperation(tradeId,null,false);

            SendTradePacket(tradeService.CreateStatusUpdateRequest(tradeId, accepted, cancelled, createContext()));
        }

        /// <summary>
        /// 统一通过出站命令管线路由所有 trade 出站包。
        /// 设计哲学 §3.7：所有通信必须通过 handler 管线，不得直连传输层。
        /// - V2 模式：Trade handler (P=1100) 原样返回 FrameworkPacket → sendPacket → NetClient
        /// - Legacy 模式：LegacyAdapter (P=500) 抢先拦截 → 翻译为 Legacy Proto → ILegacyModuleTransport.Send("Trading")
        /// </summary>
        private void SendTradePacket(FrameworkPacket packet)
        {
            if (packet == null) return;
            PrepareIntent(packet, Array.Empty<FrameworkPacket>());
            DispatchTradePacket(packet);
        }

        private void PrepareIntent(FrameworkPacket packet, IEnumerable<FrameworkPacket> items)
        {
            if (intentJournal == null || (packet.MessageType != FrameworkTradeProtocol.CreateRequestType &&
                packet.MessageType != FrameworkTradeProtocol.OfferUpdateRequestType && packet.MessageType != FrameworkTradeProtocol.StatusUpdateRequestType)) return;
            var record = intentJournal.Prepare(intentEndpoint?.Invoke(), sessionContext?.Uuid, packet, items);
            IntentPrepared?.Invoke(record);
            intentJournal.BeginAttempt(record);
        }

        private void DispatchTradePacket(FrameworkPacket packet)
        {


            if (commandTransport == null || !commandTransport.TryHandleOutgoingCommand(packet))
            {
                log?.Invoke($"[TradeAdapter] No handler for outgoing command {packet.MessageType}", LogLevel.WARNING);
            }
            else
            {

            }
        }

        private ClientFrameworkContext createContext()
        {
            return new ClientFrameworkContext
            {
                CompatibilityMode = lifecycle.CompatibilityMode,
                SenderUuid = sessionContext.Uuid,
                SessionId = sessionContext.SessionId,
                SendMessage = SendTradePacket,
                RemoteCapabilities = Array.Empty<string>(),
                HasRemoteCapability = frameworkClient.HasRemoteCapability,
                Log = (message, level) => log?.Invoke(message, level)
            };
        }
    }
}
