using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using PhinixClient.Trade;
using RimWorld;
using Utils;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal sealed partial class RedPacketStateMachine
    {
        private IInventoryReservationApi reservations;
        private IInventoryReadApi inventoryRead;
        private RedPacketInventorySendStore sendStore;
        private readonly Dictionary<string, RedPacketInventorySendRecord> inventorySends =
            new Dictionary<string, RedPacketInventorySendRecord>(StringComparer.Ordinal);
        private Game inventoryGame;
        private bool inventoryScopeDirty = true;
        private DateTime nextInventorySendCheckUtc;
        private readonly HashSet<string> relayAccepted = new HashSet<string>(StringComparer.Ordinal);
        private int pendingInventorySendCount;
        private int custodyRecoveryFaultCount;

        public int PendingInventorySendCount => pendingInventorySendCount;

        private void UpdatePendingInventorySendCount()
        {
            int count = custodyRecoveryFaultCount;
            foreach (RedPacketInventorySendRecord record in inventorySends.Values)
                if (record.State != RedPacketInventorySendState.Settled) count++;
            pendingInventorySendCount = count;
        }

        public void BindInventorySending(IInventoryReadApi read, IInventoryReservationApi api,
            Func<string, string> storagePath)
        {
            if (inventoryRead != null) inventoryRead.InventoryChanged -= OnInventoryScopeChanged;
            inventoryRead = read;
            reservations = api;
            sendStore = storagePath == null ? null : new RedPacketInventorySendStore(storagePath);
            if (inventoryRead != null) inventoryRead.InventoryChanged += OnInventoryScopeChanged;
            inventoryScopeDirty = true;
            inventoryGame = Current.Game;
        }

        private void OnInventoryScopeChanged(object sender, EventArgs args) { inventoryScopeDirty = true; }

        private void UpdateInventoryScope()
        {
            if (!ReferenceEquals(inventoryGame, Current.Game))
            {
                Clear();
                inventoryGame = Current.Game;
                inventoryScopeDirty = true;
            }
            if (!inventoryScopeDirty || reservations == null || sendStore == null ||
                inventoryRead?.GetStatus().CanWrite != true) return;
            inventoryScopeDirty = false;
            custodyRecoveryFaultCount = 0;
            InventoryReservation[] current = reservations.GetReservations()
                .Where(item => item.OwnerExtensionId == "builtin.legacy-redpacket" &&
                    item.State != InventoryReservationState.Restored).ToArray();
            HashSet<string> ids = new HashSet<string>(current.Select(item => item.ReservationId));
            foreach (string id in inventorySends.Keys.Where(id => !ids.Contains(id)).ToArray())
                inventorySends.Remove(id);
            foreach (InventoryReservation item in current)
            {
                if (inventorySends.ContainsKey(item.ReservationId)) continue;
                try
                {
                    RedPacketInventorySendRecord record = sendStore.Load(item.ReservationId);
                    if (record == null)
                    {
                        custodyRecoveryFaultCount++;
                        // A crash between reserve and durable custody is unknown,
                        // not proof that the create event was never published.
                        log?.Invoke("[RedPacket] Missing sender custody for reservation " + item.ReservationId + ".", LogLevel.ERROR);
                        continue;
                    }
                    if (record.State == RedPacketInventorySendState.Settled && item.State == InventoryReservationState.Committed) continue;
                    if (inventorySends.Count >= RedPacketLimits.MaxActivePackets)
                    {
                        custodyRecoveryFaultCount++;
                        continue;
                    }
                    record.State = RedPacketInventorySendState.Uncertain;
                    sendStore.Save(record);
                    inventorySends[item.ReservationId] = record;
                    log?.Invoke("[RedPacket] Interrupted sender custody needs reconciliation: packet=" + record.PacketId + ".", LogLevel.WARNING);
                }
                catch (Exception exception)
                {
                    custodyRecoveryFaultCount++;
                    log?.Invoke("[RedPacket] Cannot recover sender custody " + item.ReservationId + ": " + exception, LogLevel.ERROR);
                }
            }
            UpdatePendingInventorySendCount();
        }

        public bool TryAddInventoryPacket(RedPacket packet, string reservationId,
            TradeItemSnapshot sourceTemplate, int unsentCount)
        {
            if (!IsOnline || reservations == null || sendStore == null || packet == null ||
                inventorySends.Count >= RedPacketLimits.MaxActivePackets ||
                !packet.IsSender(LocalUuid)) return false;
            lock (PacketsLock)
                if (Packets.Count >= RedPacketLimits.MaxActivePackets || Packets.ContainsKey(packet.Id)) return false;
            List<string> messages = BuildInventoryBroadcast(packet);
            RedPacketInventorySendRecord record = new RedPacketInventorySendRecord
            {
                PacketId = packet.Id, ReservationId = reservationId, SenderUuid = packet.SenderUuid,
                State = RedPacketInventorySendState.Queued, ExpiresAtUtc = packet.ExpiresAtUtc,
                Template = RedPacketInventoryPayload.FromSnapshot(sourceTemplate, sourceTemplate.StackCount),
                TotalCount = packet.TotalCount, RemainingCount = packet.TotalCount, UnsentCount = unsentCount,
                ReturnStackLimit = Math.Max(1, DefDatabase<ThingDef>.GetNamed(sourceTemplate.DefName).stackLimit)
            };
            sendStore.Save(record); // durable sender custody precedes any publication
            inventorySends.Add(reservationId, record);
            packet.InventoryReservationId = reservationId;
            lock (PacketsLock) Packets.Add(packet.Id, packet);
            MarkBadgeDirty();
            if (!relay.TryEnqueueBatch(messages, packet.SenderUuid, "redpacket-" + packet.Id,
                () => AcceptInventorySend(reservationId, packet)))
            {
                lock (PacketsLock) Packets.Remove(packet.Id);
                inventorySends.Remove(reservationId);
                UpdatePendingInventorySendCount();
                return false; // atomic queue rejection proves nothing was sent
            }
            UpdatePendingInventorySendCount();
            return true;
        }

        private static List<string> BuildInventoryBroadcast(RedPacket packet)
        {
            List<string> messages = new List<string>();
            byte[] payload = packet.Template.StatePayload;
            if (payload != null && payload.Length > 0)
            {
                string hash = ComputePayloadHash(payload);
                int parts = (payload.Length + RedPacketProtocol.StatePartBytes - 1) / RedPacketProtocol.StatePartBytes;
                for (int i = 0; i < parts; i++)
                {
                    int offset = i * RedPacketProtocol.StatePartBytes;
                    messages.Add(RedPacketProtocol.BuildStatePart(packet.Id, i, parts, hash,
                        payload, offset, Math.Min(RedPacketProtocol.StatePartBytes, payload.Length - offset)));
                }
            }
            messages.Add(RedPacketProtocol.BuildCreate(ToCreateData(packet)));
            return messages;
        }

        private void PublishSendRecord(RedPacketInventorySendRecord record)
        {
            sendStore.Save(record);
            inventorySends[record.ReservationId] = record;
            UpdatePendingInventorySendCount();
        }

        private void AcceptInventorySend(string id, RedPacket expectedPacket)
        {
            lock (PacketsLock)
                if (!Packets.TryGetValue(expectedPacket.Id, out RedPacket active) || !ReferenceEquals(active, expectedPacket)) return;
            if (!inventorySends.TryGetValue(id, out RedPacketInventorySendRecord record) ||
                record.State == RedPacketInventorySendState.Settled) return;
            relayAccepted.Add(id); // retry persistence if the acceptance callback cannot flush
            if (record.State == RedPacketInventorySendState.Accepted || record.State == RedPacketInventorySendState.Settling) return;
            RedPacketInventorySendRecord accepted = record.Clone();
            accepted.State = RedPacketInventorySendState.Accepted;
            PublishSendRecord(accepted);
            CommitAcceptedSend(accepted);
            MarkBadgeDirty();
        }

        private bool TryConfirmInventoryCreate(string[] parts)
        {
            if (parts == null || parts.Length < 4) return false;
            RedPacket packet;
            lock (PacketsLock) Packets.TryGetValue(parts[3], out packet);
            if (packet == null || string.IsNullOrEmpty(packet.InventoryReservationId) || packet.Expired ||
                !string.Equals(string.Join("|", parts), RedPacketProtocol.BuildCreate(ToCreateData(packet)), StringComparison.Ordinal))
                return false;
            AcceptInventorySend(packet.InventoryReservationId, packet);
            MarkProcessed(RedPacketMessageType.Create, parts);
            return true;
        }

        private bool CommitAcceptedSend(RedPacketInventorySendRecord record)
        {
            InventoryReservationResolutionResult result = reservations.ResolveReservation(record.ReservationId,
                InventoryReservationResolution.Commit, "redpacket-relay-accepted:" + record.PacketId);
            if (!result.Succeeded) return false;
            // Selecting part of an indivisible stateful entry reserves the entire
            // entry. Return the unselected part with a separate stable receipt.
            return record.UnsentCount == 0 || inventory.TryDeposit(
                BuildSenderReturn(record, record.UnsentCount, 1, "unselected")).Succeeded;
        }

        private bool RecordInventoryClaim(RedPacket packet, string claimer, int amount)
        {
            if (string.IsNullOrEmpty(packet.InventoryReservationId)) return true;
            if (!inventorySends.TryGetValue(packet.InventoryReservationId, out RedPacketInventorySendRecord record) ||
                record.State == RedPacketInventorySendState.Queued || record.State == RedPacketInventorySendState.Uncertain)
            {
                HoldCurrentMessage("[RedPacket] Sender acceptance is not confirmed; claim cursor held.");
                return false;
            }
            if (record.Claims.TryGetValue(claimer, out int previous)) return previous == amount;
            if (amount < 1 || amount > record.RemainingCount || !CommitAcceptedSend(record))
            {
                HoldCurrentMessage("[RedPacket] Sender custody commit is pending; claim cursor held.");
                return false;
            }
            RedPacketInventorySendRecord claimed = record.Clone();
            claimed.Claims.Add(claimer, amount);
            claimed.RemainingCount -= amount;
            if (claimed.RemainingCount == 0)
            {
                claimed.State = RedPacketInventorySendState.Settling;
                claimed.ReturnMultiplier = 0;
            }
            PublishSendRecord(claimed); // persist before consuming the protocol event
            return true;
        }

        private void ProcessInventorySends()
        {
            if (!IsOnline || reservations == null || sendStore == null || inventoryRead?.GetStatus().CanWrite != true ||
                DateTime.UtcNow < nextInventorySendCheckUtc) return;
            nextInventorySendCheckUtc = DateTime.UtcNow.AddSeconds(1);
            foreach (RedPacketInventorySendRecord original in inventorySends.Values.ToArray())
            {
                RedPacket packet;
                lock (PacketsLock) Packets.TryGetValue(original.PacketId, out packet);
                if (packet == null || packet.InventoryReservationId != original.ReservationId) continue;
                try
                {
                    RedPacketInventorySendRecord record = original;
                    if ((record.State == RedPacketInventorySendState.Queued || record.State == RedPacketInventorySendState.Uncertain) &&
                        relayAccepted.Contains(record.ReservationId))
                    {
                        AcceptInventorySend(record.ReservationId, packet);
                        record = inventorySends[record.ReservationId];
                    }
                    if (record.State == RedPacketInventorySendState.Queued &&
                        DateTime.UtcNow > packet.CreatedAtUtc.AddSeconds(20))
                    {
                        record = record.Clone();
                        record.State = RedPacketInventorySendState.Uncertain;
                        PublishSendRecord(record);
                        reservations.ResolveReservation(record.ReservationId, InventoryReservationResolution.MarkUncertain,
                            "redpacket-acceptance-timeout:" + record.PacketId);
                    }
                    if (record.State != RedPacketInventorySendState.Accepted &&
                        record.State != RedPacketInventorySendState.Settling) continue;
                    if (!CommitAcceptedSend(record)) continue;
                    if (record.State == RedPacketInventorySendState.Accepted)
                    {
                        // A failed poll or backlog must never refund unseen claims.
                        if (DateTime.UtcNow < record.ExpiresAtUtc || !relay.IsCaughtUpThrough(record.ExpiresAtUtc)) continue;
                        record = record.Clone();
                        record.State = RedPacketInventorySendState.Settling;
                        record.ReturnMultiplier = 1;
                        if (IsSpecialPacketItem(packet.Template))
                        {
                            // Separate RNG from Verse's global game stream.
                            int outcome = new Random(packet.Id.Aggregate(17, (hash, c) => unchecked(hash * 31 + c))).Next(100);
                            record.ReturnMultiplier = outcome >= 50 && outcome < 75 ? 2 : 0;
                            record.TriggerSpecialRaid = outcome < 50;
                        }
                        PublishSendRecord(record);
                        if (IsSpecialPacketItem(packet.Template) && record.ReturnMultiplier == 0)
                            log?.Invoke("[RedPacket] Special inventory packet expired without a return: " + packet.Id, LogLevel.INFO);
                    }
                    if (record.RemainingCount > 0 && record.ReturnMultiplier > 0 &&
                        !inventory.TryDeposit(BuildSenderReturn(record, record.RemainingCount,
                            record.ReturnMultiplier, "expired")).Succeeded) continue;
                    RedPacketInventorySendRecord settled = record.Clone();
                    settled.State = RedPacketInventorySendState.Settled;
                    PublishSendRecord(settled);
                    packet.InventoryCustodySettled = true;
                    inventorySends.Remove(record.ReservationId);
                    relayAccepted.Remove(record.ReservationId);
                    UpdatePendingInventorySendCount();
                    if (record.TriggerSpecialRaid) TryTriggerSpecialRaid();
                    if (record.RemainingCount > 0)
                    {
                        packet.Expired = true;
                        packet.CompletedAtUtc = DateTime.UtcNow;
                        QueueSenderSummary(packet, new Dictionary<string, int>(packet.ClaimedAmounts),
                            packet.CompletedAtUtc.Value, true, record.RemainingCount * record.ReturnMultiplier);
                    }
                    MarkBadgeDirty();
                }
                catch (Exception exception)
                {
                    log?.Invoke("[RedPacket] Sender custody remains pending: " + original.PacketId + ": " + exception, LogLevel.ERROR);
                }
            }
        }

        private static InventoryDeposit BuildSenderReturn(RedPacketInventorySendRecord record, int amount,
            int multiplier, string kind)
        {
            List<InventoryItem> items = new List<InventoryItem>();
            for (int i = 0; i < multiplier; i++)
                items.AddRange(RedPacketInventoryCodec.BuildItems(record.Template.ToSnapshot(record.Template.StackCount),
                    amount, record.Template.DefName, record.ReturnStackLimit));
            return new InventoryDeposit
            {
                DepositId = "redpacket-send-return:" + kind + ":" + record.PacketId,
                Source = "legacy-redpacket", OriginKind = "redpacket-return", OriginId = record.PacketId,
                Items = items
            };
        }

        private void InterruptInventorySends()
        {
            foreach (RedPacketInventorySendRecord record in inventorySends.Values.ToArray())
            {
                if (record.State == RedPacketInventorySendState.Settled) continue;
                try
                {
                    RedPacketInventorySendRecord uncertain = record.Clone();
                    uncertain.State = RedPacketInventorySendState.Uncertain;
                    PublishSendRecord(uncertain);
                }
                catch (Exception exception)
                {
                    log?.Invoke("[RedPacket] Could not mark interrupted custody: " + exception, LogLevel.ERROR);
                }
            }
            inventorySends.Clear();
            relayAccepted.Clear();
            custodyRecoveryFaultCount = 0;
            UpdatePendingInventorySendCount();
            inventoryScopeDirty = true;
        }
    }
}
