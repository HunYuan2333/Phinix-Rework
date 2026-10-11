using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UserManagement;
using Utils;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    public sealed class PhinixFrameworkTradeClientService : IFrameworkTradeClientApi, IFrameworkTradeUpdateResultApi, ITradeStateInput
    {
        private readonly PhinixFrameworkTradeClientRepository repository;
        private readonly ITradeItemPayloadEncoder itemPipeline;
        private IClientUserDirectory userDirectory;
        private readonly Action<LogEventArgs> log;
        private readonly Dictionary<string, string> pendingTradeCreationByTradeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> pendingTradeUpdateTokensByTradeId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> unconfirmedTradeUpdates = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly object tradeUpdateLock = new object();

        public event EventHandler RepositoryChanged;
        public event EventHandler<TradeCreationEventArgs> OnTradeCreationSuccess;
        public event EventHandler<TradeCreationEventArgs> OnTradeCreationFailure;
        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess;
        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure;
        public event EventHandler<TradesSyncedEventArgs> OnTradesSynced;
        public event EventHandler<TradeCompletionEventArgs> OnTradeCompleted;
        public event EventHandler<TradeCompletionEventArgs> OnTradeCancelled;

        public PhinixFrameworkTradeClientService(ITradeItemPayloadEncoder itemPipeline, Action<LogEventArgs> log)
            : this(itemPipeline, null, log)
        {
        }

        public PhinixFrameworkTradeClientService(ITradeItemPayloadEncoder itemPipeline, IClientUserDirectory userDirectory, Action<LogEventArgs> log)
        {
            repository = new PhinixFrameworkTradeClientRepository();
            this.itemPipeline = itemPipeline;
            this.userDirectory = userDirectory;
            this.log = log;
        }

        internal void InitializeUserDirectory(IClientUserDirectory value)
        {
            userDirectory = value;
        }

        public FrameworkTradeStateSnapshot[] GetRepositoryTrades() => repository.GetAll();

        public string[] GetTradeIds() => repository.GetAll().Select(snapshot => snapshot.TradeId).ToArray();

        public ClientTradeSnapshot[] GetTrades()
        {
            return repository.GetAll()
                .Select(ToTradeSnapshot)
                .Where(trade => trade != null && !string.IsNullOrEmpty(trade.TradeId))
                .ToArray();
        }

        public bool TryGetTrade(string tradeId, out ClientTradeSnapshot trade)
        {
            if (!repository.TryGet(tradeId, out FrameworkTradeStateSnapshot snapshot))
            {
                trade = null;
                return false;
            }

            trade = ToTradeSnapshot(snapshot);
            return trade != null;
        }

        public bool TryGetOtherPartyUuid(string tradeId, string localUuid, out string otherPartyUuid)
        {
            otherPartyUuid = null;
            if (!repository.TryGet(tradeId, out FrameworkTradeStateSnapshot snapshot))
            {
                return false;
            }

            FrameworkTradeParticipantSnapshot otherParty = (snapshot.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                .SingleOrDefault(participant => participant.Uuid != localUuid);
            if (otherParty == null)
            {
                return false;
            }

            otherPartyUuid = otherParty.Uuid;
            return true;
        }

        public bool TryGetOtherPartyAccepted(string tradeId, string localUuid, out bool otherPartyAccepted)
        {
            otherPartyAccepted = false;
            if (!repository.TryGet(tradeId, out FrameworkTradeStateSnapshot snapshot))
            {
                return false;
            }

            FrameworkTradeParticipantSnapshot otherParty = (snapshot.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                .SingleOrDefault(participant => participant.Uuid != localUuid);
            if (otherParty == null)
            {
                return false;
            }

            otherPartyAccepted = otherParty.Accepted;
            return true;
        }

        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted)
        {
            accepted = false;
            if (!repository.TryGet(tradeId, out FrameworkTradeStateSnapshot snapshot))
            {
                return false;
            }

            FrameworkTradeParticipantSnapshot participant = (snapshot.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                .SingleOrDefault(candidate => candidate.Uuid == partyUuid);
            if (participant == null)
            {
                return false;
            }

            accepted = participant.Accepted;
            return true;
        }

        public bool TryGetItemsOnOffer(string tradeId, string partyUuid, out IEnumerable<TradeItemSnapshot> items)
        {
            items = Array.Empty<TradeItemSnapshot>();
            if (!repository.TryGet(tradeId, out FrameworkTradeStateSnapshot snapshot))
            {
                return false;
            }

            FrameworkTradeParticipantSnapshot participant = (snapshot.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                .SingleOrDefault(candidate => candidate.Uuid == partyUuid);
            if (participant == null)
            {
                return false;
            }

            items = DecodeTradeItems(participant.ItemsOnOffer);
            return true;
        }

        public FrameworkPacket CreateSnapshotRequestPacket(string sessionId, string senderUuid)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(senderUuid))
            {
                return null;
            }

            FrameworkPacket packet = new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.SnapshotType,
                SessionId = sessionId,
                SenderUuid = senderUuid,
                PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkTradeStateCollectionSnapshot())
            };
            packet.SetStateKind(FrameworkMetadataStateKinds.Snapshot);
            return packet;
        }

        public FrameworkPacket CreateTradeRequest(string otherPartyUuid, ClientFrameworkContext context)
        {
            FrameworkTradeCreateRequest payload = new FrameworkTradeCreateRequest
            {
                OtherPartyUuid = otherPartyUuid
            };

            return new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.CreateRequestType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = context.SessionId,
                SenderUuid = context.SenderUuid,
                PayloadJson = FrameworkSerialization.SerializePayload(payload)
            };
        }

        public FrameworkItemPayload[] EncodeTradeItems(IEnumerable<TradeItemSnapshot> tradeItems)
        {
            return itemPipeline.EncodeTradeItems(tradeItems) ?? Array.Empty<FrameworkItemPayload>();
        }

        public FrameworkPacket CreateOfferUpdateRequest(string tradeId, IEnumerable<TradeItemSnapshot> tradeItems, ClientFrameworkContext context)
        {
            FrameworkTradeOfferUpdateRequest payload = new FrameworkTradeOfferUpdateRequest
            {
                TradeId = tradeId,
                Items = (itemPipeline.EncodeTradeItems(tradeItems) ?? Array.Empty<FrameworkItemPayload>()).ToList()
            };

            return new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.OfferUpdateRequestType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = context.SessionId,
                SenderUuid = context.SenderUuid,
                PayloadJson = FrameworkSerialization.SerializePayload(payload)
            };
        }

        public FrameworkPacket CreateOfferUpdateRequest(string tradeId, IEnumerable<string> itemPacketRefs, ClientFrameworkContext context)
        {
            FrameworkTradeOfferUpdateRequest payload = new FrameworkTradeOfferUpdateRequest
            {
                TradeId = tradeId,
                ItemPacketRefs = (itemPacketRefs ?? Array.Empty<string>()).ToList()
            };

            return new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.OfferUpdateRequestType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = context.SessionId,
                SenderUuid = context.SenderUuid,
                PayloadJson = FrameworkSerialization.SerializePayload(payload)
            };
        }

        public FrameworkPacket CreateStatusUpdateRequest(string tradeId, bool? accepted, bool? cancelled, ClientFrameworkContext context)
        {
            FrameworkTradeStatusUpdateRequest payload = new FrameworkTradeStatusUpdateRequest
            {
                TradeId = tradeId,
                Accepted = accepted,
                Cancelled = cancelled
            };

            return new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.StatusUpdateRequestType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = context.SessionId,
                SenderUuid = context.SenderUuid,
                PayloadJson = FrameworkSerialization.SerializePayload(payload)
            };
        }

        public void BeginTradeUpdate(string tradeId, string token)
        {
            if (string.IsNullOrEmpty(tradeId))
            {
                throw new ArgumentException("A trade ID is required.", nameof(tradeId));
            }

            lock (tradeUpdateLock)
            {
                if (!unconfirmedTradeUpdates.TryGetValue(tradeId, out HashSet<string> tokens))
                {
                    tokens = new HashSet<string>(StringComparer.Ordinal);
                    unconfirmedTradeUpdates.Add(tradeId, tokens);
                }

                if (!tokens.Add(token ?? string.Empty))
                {
                    throw new InvalidOperationException("This trade update is already awaiting a result.");
                }
            }
        }

        public bool IsTradeUpdatePending(string tradeId, string token)
        {
            if (string.IsNullOrEmpty(tradeId)) return false;
            lock (tradeUpdateLock)
            {
                return unconfirmedTradeUpdates.TryGetValue(tradeId, out HashSet<string> tokens) &&
                    tokens.Contains(token ?? string.Empty);
            }
        }

        public bool CompleteTradeUpdate(string tradeId, string token, TradeFailureReason failureReason, string failureMessage)
        {
            if (string.IsNullOrEmpty(tradeId)) return false;

            ClientTradeSnapshot trade = GetTradeOrPlaceholder(tradeId);
            lock (tradeUpdateLock)
            {
                if (!unconfirmedTradeUpdates.TryGetValue(tradeId, out HashSet<string> tokens) ||
                    !tokens.Remove(token ?? string.Empty))
                {
                    return false;
                }

                if (tokens.Count == 0) unconfirmedTradeUpdates.Remove(tradeId);
            }

            TradeUpdateEventArgs args = new TradeUpdateEventArgs(trade, failureReason, failureMessage, token);
            if (failureReason == TradeFailureReason.None)
            {
                OnTradeUpdateSuccess?.Invoke(this, args);
            }
            else
            {
                OnTradeUpdateFailure?.Invoke(this, args);
            }
            return true;
        }

        public void TrackPendingTradeUpdate(string tradeId, string token)
        {
            if (string.IsNullOrEmpty(tradeId))
            {
                return;
            }

            if (!pendingTradeUpdateTokensByTradeId.TryGetValue(tradeId, out List<string> tokens))
            {
                tokens = new List<string>();
                pendingTradeUpdateTokensByTradeId[tradeId] = tokens;
            }

            tokens.Add(token ?? string.Empty);
        }

        public void HandleSnapshot(FrameworkPacket packet)
        {
            FrameworkTradeStateCollectionSnapshot payload = FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(packet.PayloadJson);
            bool isFullSnapshot = packet.TryGetStateKind(out string stateKind) &&
                                  string.Equals(stateKind, FrameworkMetadataStateKinds.Snapshot, StringComparison.OrdinalIgnoreCase);

            if (isFullSnapshot)
            {
                repository.ReplaceAll(payload?.Trades);
                foreach (FrameworkTradeStateSnapshot trade in payload?.Trades ?? Enumerable.Empty<FrameworkTradeStateSnapshot>())
                {
                    FlushPendingEventsForTrade(trade.TradeId, false);
                }
            }
            else
            {
                foreach (FrameworkTradeStateSnapshot trade in payload?.Trades ?? Enumerable.Empty<FrameworkTradeStateSnapshot>())
                {
                    repository.Upsert(trade);
                    bool emittedPendingEvent = FlushPendingEventsForTrade(trade.TradeId, true);
                    if (!emittedPendingEvent && TryGetTrade(trade.TradeId, out ClientTradeSnapshot updatedTrade))
                    {
                        log?.Invoke(new LogEventArgs($"[TradeService] HandleSnapshot: firing trade update for tradeId={updatedTrade.TradeId}, accepted={updatedTrade.Accepted}, otherAccepted={updatedTrade.OtherPartyAccepted}", LogLevel.DEBUG));
                        OnTradeUpdateSuccess?.Invoke(this, new TradeUpdateEventArgs(updatedTrade));
                    }
                    else
                    {
                        log?.Invoke(new LogEventArgs($"[TradeService] HandleSnapshot: skipped update for tradeId={trade.TradeId}, emittedPending={emittedPendingEvent}", LogLevel.DEBUG));
                    }
                }
            }

            RepositoryChanged?.Invoke(this, EventArgs.Empty);
            if (isFullSnapshot)
            {
                OnTradesSynced?.Invoke(this, new TradesSyncedEventArgs(GetTrades()));
            }

            log?.Invoke(new LogEventArgs(
                $"Framework trade repository {(isFullSnapshot ? "replaced" : "updated")} with {payload?.Trades?.Count ?? 0} trade snapshot(s).",
                LogLevel.DEBUG));
        }

        public void HandleCreateResponse(FrameworkPacket packet)
        {
            FrameworkTradeCreateResponse payload = FrameworkSerialization.DeserializePayload<FrameworkTradeCreateResponse>(packet.PayloadJson);
            if (payload == null)
            {
                return;
            }

            if (payload.Success)
            {
                pendingTradeCreationByTradeId[payload.TradeId] = payload.OtherPartyUuid;
                return;
            }

            OnTradeCreationFailure?.Invoke(this, new TradeCreationEventArgs(
                payload.OtherPartyUuid,
                ToClientFailureReason(payload.FailureReason),
                payload.FailureMessage));

            if (!string.IsNullOrEmpty(payload.FailureMessage))
            {
                log?.Invoke(new LogEventArgs($"Framework trade creation failed: {payload.FailureReason} - {payload.FailureMessage}", LogLevel.WARNING));
            }
        }

        public void HandleOfferUpdateResponse(FrameworkPacket packet)
        {
            FrameworkTradeOfferUpdateResponse payload = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateResponse>(packet.PayloadJson);
            if (payload == null)
            {
                log?.Invoke(new LogEventArgs("[TradeService] HandleOfferUpdateResponse: payload is null", LogLevel.DEBUG));
                return;
            }

            log?.Invoke(new LogEventArgs($"[TradeService] HandleOfferUpdateResponse: tradeId={payload.TradeId}, success={payload.Success}, failureReason={payload.FailureReason}", LogLevel.DEBUG));
            string token = packet.GetCorrelationId();
            if (payload.Success)
            {
                TrackPendingTradeUpdate(payload.TradeId, token);
                return;
            }

            OnTradeUpdateFailure?.Invoke(this, new TradeUpdateEventArgs(
                GetTradeOrPlaceholder(payload.TradeId),
                ToClientFailureReason(payload.FailureReason),
                payload.FailureMessage,
                token));

            if (!string.IsNullOrEmpty(payload.FailureMessage))
            {
                log?.Invoke(new LogEventArgs($"Framework trade offer update failed for trade '{payload.TradeId}': {payload.FailureReason} - {payload.FailureMessage}", LogLevel.WARNING));
            }
        }

        public void HandleStatusUpdateResponse(FrameworkPacket packet)
        {
            FrameworkTradeStatusUpdateResponse payload = FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateResponse>(packet.PayloadJson);
            if (payload == null)
            {
                log?.Invoke(new LogEventArgs("[TradeService] HandleStatusUpdateResponse: payload is null", LogLevel.DEBUG));
                return;
            }

            log?.Invoke(new LogEventArgs($"[TradeService] HandleStatusUpdateResponse: tradeId={payload.TradeId}, success={payload.Success}, failureReason={payload.FailureReason}, failureMessage={payload.FailureMessage ?? "null"}", LogLevel.DEBUG));
            string token = packet.GetCorrelationId();
            if (payload.Success)
            {
                TrackPendingTradeUpdate(payload.TradeId, string.Equals(token, payload.TradeId, StringComparison.OrdinalIgnoreCase) ? string.Empty : token);
                return;
            }

            OnTradeUpdateFailure?.Invoke(this, new TradeUpdateEventArgs(
                GetTradeOrPlaceholder(payload.TradeId),
                ToClientFailureReason(payload.FailureReason),
                payload.FailureMessage,
                token));

            if (!string.IsNullOrEmpty(payload.FailureMessage))
            {
                log?.Invoke(new LogEventArgs($"Framework trade status update failed for trade '{payload.TradeId}': {payload.FailureReason} - {payload.FailureMessage}", LogLevel.WARNING));
            }
        }

        public void HandleCompletedEvent(FrameworkPacket packet)
        {
            FrameworkTradeCompletionEvent payload = FrameworkSerialization.DeserializePayload<FrameworkTradeCompletionEvent>(packet.PayloadJson);
            if (payload == null)
            {
                return;
            }

            CompleteTrade(payload.TradeId, true, payload.OtherPartyUuid, payload.Items);
        }

        public void HandleCancelledEvent(FrameworkPacket packet)
        {
            FrameworkTradeCompletionEvent payload = FrameworkSerialization.DeserializePayload<FrameworkTradeCompletionEvent>(packet.PayloadJson);
            if (payload == null)
            {
                return;
            }

            CompleteTrade(payload.TradeId, false, payload.OtherPartyUuid, payload.Items);
        }

        /// <summary>
        /// 应用已经转换和校验的权威交易状态，触发 UI 刷新。
        /// 所有协议转换逻辑在 Adapter 中完成，此处仅负责写入和通知。
        /// </summary>
        public void UpsertTrade(FrameworkTradeStateSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrEmpty(snapshot.TradeId)) return;

            // 参与者必须刚好 2 人 —— Adapter 数据合并有 bug 时这里拦住，不让坏数据进 repo 导致 UI 炸
            if (snapshot.Participants == null || snapshot.Participants.Count != 2)
            {
                log?.Invoke(new LogEventArgs(
                    $"State input supplied trade '{snapshot.TradeId}' with {snapshot.Participants?.Count ?? 0} participant(s); expected 2. Dropping.",
                    LogLevel.WARNING));
                return;
            }

            // 参与者 UUID 不能为空或重复
            if (string.IsNullOrEmpty(snapshot.Participants[0].Uuid) ||
                string.IsNullOrEmpty(snapshot.Participants[1].Uuid) ||
                string.Equals(snapshot.Participants[0].Uuid, snapshot.Participants[1].Uuid, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke(new LogEventArgs(
                    $"State input supplied trade '{snapshot.TradeId}' with invalid participants [{snapshot.Participants[0].Uuid}, {snapshot.Participants[1].Uuid}]. Dropping.",
                    LogLevel.WARNING));
                return;
            }

            bool existed = repository.TryGet(snapshot.TradeId, out _);
            repository.Upsert(snapshot);
            RepositoryChanged?.Invoke(this, EventArgs.Empty);

            if (!existed && TryGetTrade(snapshot.TradeId, out ClientTradeSnapshot trade))
            {
                OnTradeCreationSuccess?.Invoke(this, new TradeCreationEventArgs(trade));
            }
            else if (existed)
            {
                if (TryGetTrade(snapshot.TradeId, out ClientTradeSnapshot updatedTrade))
                {
                    log?.Invoke(new LogEventArgs(
                        $"Trade state update event emitted for '{snapshot.TradeId}'.",
                        LogLevel.DEBUG));
                    OnTradeUpdateSuccess?.Invoke(this, new TradeUpdateEventArgs(updatedTrade));
                }
            }
        }

        /// <summary>
        /// 移除交易状态并通知观察者。
        /// </summary>
        public void RemoveTrade(string tradeId)
        {
            if (string.IsNullOrEmpty(tradeId)) return;

            bool hadTrade = repository.TryGet(tradeId, out _);
            repository.Remove(tradeId);
            if (hadTrade)
            {
                RepositoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void CompleteTrade(string tradeId, bool success, string otherPartyUuid,
            IEnumerable<FrameworkItemPayload> items)
        {
            FrameworkItemPayload[] payloads = (items ?? Array.Empty<FrameworkItemPayload>()).ToArray();
            TradeItemSnapshot[] snapshots = DecodeTradeItems(payloads);
            if (string.IsNullOrEmpty(tradeId)) return;

            bool hadTrade = repository.TryGet(tradeId, out _);
            repository.Remove(tradeId);
            if (hadTrade) RepositoryChanged?.Invoke(this, EventArgs.Empty);

            TradeCompletionEventArgs args = new TradeCompletionEventArgs(
                tradeId, success, otherPartyUuid ?? string.Empty, snapshots, null, payloads);
            if (success) OnTradeCompleted?.Invoke(this, args);
            else OnTradeCancelled?.Invoke(this, args);
        }

        private bool FlushPendingEventsForTrade(string tradeId, bool emitUpdateSuccessWhenPendingCleared)
        {
            if (string.IsNullOrEmpty(tradeId))
            {
                return false;
            }

            bool emitted = false;
            if (pendingTradeCreationByTradeId.TryGetValue(tradeId, out string otherPartyUuid))
            {
                pendingTradeCreationByTradeId.Remove(tradeId);
                OnTradeCreationSuccess?.Invoke(this, new TradeCreationEventArgs(GetTradeOrPlaceholder(tradeId, otherPartyUuid)));
                emitted = true;
            }

            if (pendingTradeUpdateTokensByTradeId.TryGetValue(tradeId, out List<string> tokens))
            {
                pendingTradeUpdateTokensByTradeId.Remove(tradeId);
                ClientTradeSnapshot trade = GetTradeOrPlaceholder(tradeId);
                foreach (string token in tokens)
                {
                    OnTradeUpdateSuccess?.Invoke(this, new TradeUpdateEventArgs(trade, token));
                    emitted = true;
                }
            }

            if (emitUpdateSuccessWhenPendingCleared && emitted && !pendingTradeUpdateTokensByTradeId.ContainsKey(tradeId))
            {
                return true;
            }

            return emitted;
        }

        private ClientTradeSnapshot ToTradeSnapshot(FrameworkTradeStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            FrameworkTradeParticipantSnapshot localParticipant = (snapshot.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                .SingleOrDefault(participant => participant.Uuid == userDirectory?.Uuid);
            FrameworkTradeParticipantSnapshot otherParticipant = (snapshot.Participants ?? new List<FrameworkTradeParticipantSnapshot>())
                .SingleOrDefault(participant => participant.Uuid != userDirectory?.Uuid);

            if (otherParticipant == null)
            {
                return null;
            }

            ImmutableUser otherParty = new ImmutableUser(otherParticipant.Uuid);
            if (userDirectory != null && userDirectory.TryGetUser(otherParticipant.Uuid, out ImmutableUser existingUser))
            {
                otherParty = existingUser;
            }

            return new ClientTradeSnapshot(
                snapshot.TradeId,
                otherParty,
                DecodeTradeItems(localParticipant?.ItemsOnOffer),
                DecodeTradeItems(otherParticipant.ItemsOnOffer),
                localParticipant?.Accepted ?? false,
                otherParticipant.Accepted);
        }

        private static TradeItemSnapshot[] DecodeTradeItems(IEnumerable<FrameworkItemPayload> items)
        {
            return (items ?? Enumerable.Empty<FrameworkItemPayload>())
                .Select(item =>
                {
                    if (FrameworkTradeItemPayloadCodec.TryDecodeToTradeItemSnapshot(item, out TradeItemSnapshot tradeItem))
                    {
                        return tradeItem;
                    }

                    return FrameworkTradeItemPayloadCodec.CreateUnknownTradeItemSnapshot(item?.CodecId ?? "UnknownCodec");
                })
                .ToArray();
        }

        private ClientTradeSnapshot GetTradeOrPlaceholder(string tradeId, string otherPartyUuid = "")
        {
            if (TryGetTrade(tradeId, out ClientTradeSnapshot trade))
            {
                return trade;
            }

            return new ClientTradeSnapshot(tradeId, new ImmutableUser(otherPartyUuid));
        }

        private static TradeFailureReason ToClientFailureReason(FrameworkTradeFailureReason failureReason)
        {
            switch (failureReason)
            {
                case FrameworkTradeFailureReason.SessionInvalid: return TradeFailureReason.SessionInvalid;
                case FrameworkTradeFailureReason.LoginInvalid: return TradeFailureReason.LoginInvalid;
                case FrameworkTradeFailureReason.OtherPartyDoesNotExist: return TradeFailureReason.OtherPartyDoesNotExist;
                case FrameworkTradeFailureReason.OtherPartyOffline: return TradeFailureReason.OtherPartyOffline;
                case FrameworkTradeFailureReason.NotAcceptingTrades: return TradeFailureReason.NotAcceptingTrades;
                case FrameworkTradeFailureReason.AlreadyTrading: return TradeFailureReason.AlreadyTrading;
                case FrameworkTradeFailureReason.TradeDoesNotExist: return TradeFailureReason.TradeDoesNotExist;
                case FrameworkTradeFailureReason.NotTradeParticipant: return TradeFailureReason.NotTradeParticipant;
                case FrameworkTradeFailureReason.InternalServerError:
                default:
                    return TradeFailureReason.InternalServerError;
            }
        }
    }
}
