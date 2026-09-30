using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Phinix.InventoryExtension;
using Phinix.LegacyRedPacketExtension;
using Phinix.LegacyRedPacketExtension.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Verse;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static int Main()
    {
        try
        {
            RelayBatchIsAtomicAndRetainsRetryIdentity();
            CatchupRequiresCompleteDrainedHistory();
            CustodyPersistenceRejectsCorruption();
            StateChunksKeepEveryUnitAndByte();
            SelectionCountsItemsRatherThanOpaqueEntries();
            AcceptedCustodyCommitsAndPartialSelectionReturnsOnce();
            AcceptanceProofSurvivesPersistenceFailure();
            ReceiptRecoveryDoesNotSpamOrReplayRewards();
            Console.WriteLine("All 8 red packet inventory runtime scenarios passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RelayBatchIsAtomicAndRetainsRetryIdentity()
    {
        List<string> attempts = new List<string>();
        bool failFirst = true;
        bool accepted = false;
        RedPacketRelay relay = new RedPacketRelay(null, (message, sender, id) =>
        {
            attempts.Add(message + ":" + id);
            if (failFirst) { failFirst = false; throw new IOException("injected failure"); }
        });
        relay.Clear();
        True(relay.TryEnqueueBatch(new[] { "state", "create" }, "sender", "packet", () => accepted = true), "batch queued");
        True(!relay.TryEnqueueBatch(Enumerable.Repeat("x", 1024).ToArray(), "sender", "overflow", null), "whole overflow batch rejected");
        SendOnce(relay);
        SendOnce(relay);
        True(attempts[0] == attempts[1], "failed state part retries under the same event ID");
        True(!accepted, "state part acceptance does not confirm create");
        SendOnce(relay);
        True(attempts[2] == "create:packet:1", "create follows accepted state part");
        Queue<Action> callbacks = (Queue<Action>)Field(relay, "acceptedCallbacks");
        True(callbacks.Count == 1 && !accepted, "worker queues exactly one main-thread result");
        callbacks.Dequeue()();
        True(accepted, "main-thread delivery confirms the batch");
        relay.Clear();
    }

    private static void SendOnce(RedPacketRelay relay)
    {
        int generation = (int)typeof(RedPacketRelay).GetField("generation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Invoke(relay, "SendOnceWorker", generation, 0);
    }

    private static void CatchupRequiresCompleteDrainedHistory()
    {
        RedPacketRelay relay = new RedPacketRelay(null, (a, b, c) => { });
        relay.Clear();
        int generation = (int)typeof(RedPacketRelay).GetField("generation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        DateTime expiry = DateTime.UtcNow.AddSeconds(-1);
        string wire = "1\n1\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes("claim")) + "\n";
        using (StreamReader reader = Reader(wire)) Invoke(relay, "ParseRawResponseStreaming", reader, generation, DateTime.UtcNow);
        True(!relay.IsCaughtUpThrough(expiry), "queued incoming claims block return");
        relay.AcknowledgeIncoming();
        True(relay.IsCaughtUpThrough(expiry), "complete and drained poll proves catchup");
        relay.Clear();
        wire = "96\n" + string.Join("\n", Enumerable.Range(1, 96).Select(i => i + "\t" + Convert.ToBase64String(new byte[] { 120 }))) + "\n";
        generation = (int)typeof(RedPacketRelay).GetField("generation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        using (StreamReader reader = Reader(wire)) Invoke(relay, "ParseRawResponseStreaming", reader, generation, DateTime.UtcNow);
        for (int i = 0; i < 96; i++) relay.AcknowledgeIncoming();
        True(!relay.IsCaughtUpThrough(expiry), "full response cannot prove there is no more history");
        relay.Clear();
        generation = (int)typeof(RedPacketRelay).GetField("generation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        using (StreamReader reader = Reader("<html>error</html>\n"))
            Invoke(relay, "ParseRawResponseStreaming", reader, generation, DateTime.UtcNow);
        True(!relay.IsCaughtUpThrough(expiry), "HTTP 200 error page is not empty history");
    }

    private static StreamReader Reader(string text)
    {
        return new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    }

    private static void CustodyPersistenceRejectsCorruption()
    {
        string root = Path.Combine(Path.GetTempPath(), "phinix-redpacket-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RedPacketInventorySendStore store = new RedPacketInventorySendStore(name => Path.Combine(root, name));
            RedPacketInventorySendRecord original = Record();
            store.Save(original);
            RedPacketInventorySendRecord loaded = store.Load(original.ReservationId);
            True(loaded.Template.StatePayload.SequenceEqual(new byte[] { 1, 2, 3 }), "opaque bytes roundtrip");
            loaded.State = RedPacketInventorySendState.Accepted;
            store.Save(loaded);
            True(store.Load(original.ReservationId).State == RedPacketInventorySendState.Accepted, "atomic replacement persisted");
            True(store.Load("another-save-reservation") == null, "records do not cross reservation identity");
            string file = Directory.GetFiles(root, "*.json").Single();
            File.WriteAllText(file, "corrupt\nAQ==");
            bool rejected = false;
            try { store.Load(original.ReservationId); } catch (InvalidDataException) { rejected = true; }
            True(rejected, "corruption does not create empty custody");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void StateChunksKeepEveryUnitAndByte()
    {
        RedPacketInventorySendRecord record = Record();
        TradeItemSnapshot template = record.Template.ToSnapshot(1000);
        IList<InventoryItem> chunks = RedPacketInventoryCodec.BuildItems(template, 1000, "Rice", 75);
        True(chunks.Count == 14 && chunks.All(item => item.Quantity == 1), "opaque stacks split into 14 whole records");
        int count = 0;
        foreach (InventoryItem item in chunks)
        {
            RedPacketInventoryPayload payload = Utils.Framework.FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(Encoding.UTF8.GetString(item.Payload));
            count += payload.DeliveryStackCount;
            True(payload.StackCount == 1000 && payload.StatePayload.SequenceEqual(template.StatePayload), "each chunk retains original state");
        }
        True(count == 1000, "1000 = 13 * 75 + 25, no units lost");
        IList<InventoryItem> ordinary = RedPacketInventoryCodec.BuildItems(new TradeItemSnapshot("Rice", 1000, 10, TradeItemQuality.None, null), 1000, "Rice", 75);
        True(ordinary.Count == 1 && ordinary[0].Quantity == 1000, "old preview-only stock keeps aggregation");
    }

    private static void SelectionCountsItemsRatherThanOpaqueEntries()
    {
        // ThingDef's normal constructor loads Unity shaders. This scenario only
        // needs its plain count/category fields, not game content initialization.
        ThingDef def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        def.defName = "Rice";
        def.category = ThingCategory.Item;
        def.thingClass = typeof(Thing);
        Thing preview = new Thing { def = def, stackCount = 1000 };
        RedPacketAvailableItem opaque = RedPacketAvailableItem.FromInventory(new InventoryEntry { EntryId = "opaque", Quantity = 1 }, preview);
        opaque.Selected = 300;
        True(opaque.Count == 1000 && opaque.ReservationQuantity == 1, "select 300 actual items from one opaque stack");
        RedPacketAvailableItem ordinary = RedPacketAvailableItem.FromInventory(new InventoryEntry { EntryId = "ordinary", Quantity = 1000, AggregationKey = "same" }, preview);
        ordinary.Selected = 300;
        True(ordinary.Count == 1000 && ordinary.ReservationQuantity == 300, "aggregatable entry reserves actual units");
    }

    private static void AcceptedCustodyCommitsAndPartialSelectionReturnsOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "phinix-redpacket-custody-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            FakeInventory api = new FakeInventory();
            RedPacketRelay relay = new RedPacketRelay(null, (a, b, c) => { });
            RedPacketStateMachine machine = new RedPacketStateMachine(new Session(), null, null, null, null, relay, null);
            machine.BindInventory(api);
            machine.BindInventorySending(api, api, name => Path.Combine(root, name));
            RedPacketInventorySendRecord record = Record();
            RedPacket packet = new RedPacket
            {
                Id = record.PacketId, SenderUuid = record.SenderUuid,
                InventoryReservationId = record.ReservationId, CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = record.ExpiresAtUtc, RemainingCount = 300, RemainingPackets = 1
            };
            IDictionary packets = (IDictionary)typeof(RedPacketStateMachine).GetField("Packets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            packets[packet.Id] = packet;
            ((Dictionary<string, RedPacketInventorySendRecord>)Field(machine, "inventorySends"))[record.ReservationId] = record;
            True(!(bool)Invoke(machine, "RecordInventoryClaim", packet, "claimer", 300), "enqueue does not commit ownership");
            True(api.CommitCalls == 0 && api.Deposits.Count == 0, "no premature deduction or refund");
            Invoke(machine, "AcceptInventorySend", record.ReservationId, packet);
            True(api.CommitCalls == 1 && api.Deposits.Count == 1, "acceptance commits and returns unselected part");
            True(api.ReturnedCount == 700, "unselected 700 items returned");
            Invoke(machine, "AcceptInventorySend", record.ReservationId, packet);
            True(api.CommitCalls == 1, "duplicate acceptance has no repeated commit");
            True((bool)Invoke(machine, "RecordInventoryClaim", packet, "claimer", 300), "accepted claim durably consumed");
            True(api.Deposits.Count == 1 && api.ReturnedCount == 700, "return receipt prevents duplication");
            RedPacketInventorySendStore store = new RedPacketInventorySendStore(name => Path.Combine(root, name));
            True(store.Load(record.ReservationId).RemainingCount == 0, "claim balance persisted before completion");
            machine.Clear();
            True(store.Load(record.ReservationId).State == RedPacketInventorySendState.Uncertain, "disconnect preserves unknown custody");
            True(api.Deposits.Count == 1, "disconnect never refunds claimed items");
            machine.BindInventorySending(null, null, null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static RedPacketInventorySendRecord Record()
    {
        return new RedPacketInventorySendRecord
        {
            PacketId = "packet", ReservationId = "reservation", SenderUuid = "sender",
            State = RedPacketInventorySendState.Queued, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            Template = new RedPacketInventoryPayload { DefName = "Rice", StackCount = 1000, DeliveryStackCount = 1000,
                StateCodecId = StatefulTradeItemProtocol.ScribeCodecId, StatePayload = new byte[] { 1, 2, 3 } },
            TotalCount = 300, RemainingCount = 300, UnsentCount = 700, ReturnStackLimit = 75
        };
    }

    private static void AcceptanceProofSurvivesPersistenceFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), "phinix-redpacket-acceptance-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            bool failStorage = true;
            FakeInventory api = new FakeInventory();
            RedPacketRelay relay = new RedPacketRelay(null, (a, b, c) => { });
            RedPacketStateMachine machine = new RedPacketStateMachine(new Session(), null, null, null, null, relay, null);
            machine.BindInventory(api);
            machine.BindInventorySending(api, api, name => failStorage ? null : Path.Combine(root, name));
            RedPacketInventorySendRecord record = Record();
            RedPacket packet = new RedPacket
            {
                Id = record.PacketId, SenderUuid = record.SenderUuid, InventoryReservationId = record.ReservationId,
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = record.ExpiresAtUtc,
                RemainingCount = 300, RemainingPackets = 1
            };
            IDictionary packets = (IDictionary)typeof(RedPacketStateMachine).GetField("Packets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            packets[packet.Id] = packet;
            ((Dictionary<string, RedPacketInventorySendRecord>)Field(machine, "inventorySends"))[record.ReservationId] = record;
            bool failed = false;
            try { Invoke(machine, "AcceptInventorySend", record.ReservationId, packet); }
            catch (TargetInvocationException exception) { failed = exception.InnerException is IOException; }
            True(failed && api.CommitCalls == 0, "cannot transfer custody before durable acceptance");
            True(((HashSet<string>)Field(machine, "relayAccepted")).Contains(record.ReservationId), "real acceptance proof retained for retry");
            failStorage = false;
            Invoke(machine, "ProcessInventorySends");
            True(api.CommitCalls > 0 && api.ReturnedCount == 700, "storage recovery retries accepted custody exactly once");
            machine.Clear();
            machine.BindInventorySending(null, null, null);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void ReceiptRecoveryDoesNotSpamOrReplayRewards()
    {
        // Reconnect history can contain our already completed claim before the
        // inventory save has attached. It must stay queued, not become spam or
        // a fresh reward. Exercise both old claim and assign wire forms.
        foreach (bool assignment in new[] { false, true })
        {
            FakeInventory api = new FakeInventory { ReceiptStatus = InventoryDepositStatus.Unavailable };
            List<string> logs = new List<string>();
            RedPacketRelay relay = new RedPacketRelay(null, (a, b, c) => { });
            RedPacketStateMachine machine = new RedPacketStateMachine(new Session(), null, null, null, null, relay, (logMessage, level) => logs.Add(logMessage));
            machine.Clear();
            machine.BindInventory(api);
            RedPacket packet = new RedPacket
            {
                Id = "history-test", SenderUuid = "another-player", Template = new TradeItemSnapshot("Rice", 100, 10),
                TotalCount = 100, RemainingCount = 100, TotalPackets = 2, RemainingPackets = 2,
                Type = RedPacketType.Normal, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            };
            IDictionary packets = (IDictionary)typeof(RedPacketStateMachine).GetField("Packets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            packets.Add(packet.Id, packet);
            string message = assignment ? RedPacketProtocol.BuildAssign(packet.Id, "sender", 50, 1, 50) :
                RedPacketProtocol.BuildClaim(packet.Id, "sender");
            int generation = (int)typeof(RedPacketRelay).GetField("generation", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            using (StreamReader reader = Reader("1\n1\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(message)) + "\n"))
                Invoke(relay, "ParseRawResponseStreaming", reader, generation, DateTime.UtcNow);
            for (int i = 0; i < 20; i++) Invoke(machine, "PollRelayBuffer");
            string queued;
            True(relay.TryPeekIncoming(out queued), "receipt recovery holds the claim cursor");
            True(logs.Count == 1 && !logs[0].Contains("unsolicited"), "only one throttled recovery message, no unsolicited spam");
            True(packet.RemainingCount == 100 && api.Deposits.Count == 0, "waiting for receipts grants no reward");
            api.ReceiptStatus = InventoryDepositStatus.AlreadyCommitted;
            Invoke(machine, "PollRelayBuffer");
            True(!relay.TryPeekIncoming(out queued) && packet.RemainingCount == 50, "restored receipt consumes historical claim once");
            True(api.Deposits.Count == 0 && packet.HasClaimed("sender"), "history marks claim without depositing twice");
            machine.Clear();
            machine.BindInventory(null);
        }
    }

    private static object Field(object target, string name) { return target.GetType().GetField(name, Private).GetValue(target); }
    private static object Invoke(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, Private).Invoke(target, args);
    }
    private static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Session : IClientSessionContext
    {
        public bool Authenticated => true;
        public bool LoggedIn => true;
        public string SessionId => "test";
        public string Uuid => "sender";
    }

    private sealed class FakeInventory : IInventoryReadApi, IInventoryReservationApi, IInventoryDepositApi
    {
        public int CommitCalls;
        public int ReturnedCount;
        public InventoryDepositStatus ReceiptStatus = InventoryDepositStatus.NotCommitted;
        public readonly HashSet<string> Deposits = new HashSet<string>();
        public event EventHandler InventoryChanged { add { } remove { } }
        public event EventHandler AvailabilityChanged { add { } remove { } }
        public IReadOnlyList<InventoryEntry> GetSnapshot() => new InventoryEntry[0];
        public IReadOnlyList<InventoryEntry> GetAvailableSnapshot() => GetSnapshot();
        public IReadOnlyList<InventoryReservation> GetReservations() => new InventoryReservation[0];
        public InventoryStatus GetStatus() => new InventoryStatus { Availability = InventoryAvailability.Ready };
        public InventoryCapabilities GetCapabilities() => new InventoryCapabilities();
        public InventoryReservationResult TryReserve(InventoryReservationRequest request) { throw new NotSupportedException(); }
        public InventoryMaterializationResult CreatePreview(string id) { throw new NotSupportedException(); }
        public InventoryMaterializationResult MaterializeReservation(string id) { throw new NotSupportedException(); }
        public InventoryReservationResolutionResult ResolveReservation(string id, InventoryReservationResolution resolution, string evidence)
        {
            True(resolution == InventoryReservationResolution.Commit, "only accepted sends commit");
            CommitCalls++;
            return new InventoryReservationResolutionResult { Succeeded = true };
        }
        public InventoryDepositResult CheckDeposit(InventoryDeposit deposit) { return new InventoryDepositResult { Status = ReceiptStatus }; }
        public InventoryDepositResult TryDeposit(InventoryDeposit deposit)
        {
            if (!Deposits.Add(deposit.DepositId)) return new InventoryDepositResult { Status = InventoryDepositStatus.AlreadyCommitted };
            foreach (InventoryItem item in deposit.Items)
                ReturnedCount += Utils.Framework.FrameworkSerialization.DeserializePayload<RedPacketInventoryPayload>(Encoding.UTF8.GetString(item.Payload)).DeliveryStackCount;
            return new InventoryDepositResult { Status = InventoryDepositStatus.Committed };
        }
    }
}
