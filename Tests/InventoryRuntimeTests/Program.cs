using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Phinix.InventoryExtension;
using Phinix.InventoryExtension.Client;
using Verse;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ReservationLifecycleIsAtomicAndIdempotent();
            OldSnapshotsDefaultToNoReservations();
            InvalidOverlappingLocksAreRejected();
            DisplayGroupingPreservesOwnershipAndCounts();
            DisplayGroupingSeparatesUnknownsAndOverflow();
            ComponentTypeResolverIsStrictlyScoped();
            ResolvedComponentPreservesSnapshot();
            InventoryJournalScenarios.Run();
            Console.WriteLine("All 13 inventory reservation, grouping, component snapshot and journal runtime scenarios passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ReservationLifecycleIsAtomicAndIdempotent()
    {
        InventoryLedger ledger = CreateLedger(10);
        InventoryReservationRequest first = Request("operation-a", 6);
        InventoryReservationResult firstResult = ledger.PrepareReservation(first, out InventoryState firstState);
        Equal(InventoryReservationResultStatus.Reserved, firstResult.Status, "first reserve status");
        ledger.Publish(firstState);
        Equal(4L, ledger.AvailableSnapshot()[0].Quantity, "available after first reserve");

        InventoryReservationResult duplicate = ledger.PrepareReservation(first, out InventoryState duplicateState);
        Equal(InventoryReservationResultStatus.AlreadyReserved, duplicate.Status, "duplicate status");
        True(duplicateState == null, "duplicate must not write a state");

        InventoryReservationResult conflict = ledger.PrepareReservation(Request("operation-a", 5), out _);
        Equal(InventoryReservationResultStatus.Conflict, conflict.Status, "operation content conflict");

        InventoryReservationResult second = ledger.PrepareReservation(Request("operation-b", 4), out InventoryState secondState);
        Equal(InventoryReservationResultStatus.Reserved, second.Status, "second reserve status");
        ledger.Publish(secondState);
        Equal(0, ledger.AvailableSnapshot().Count, "all units locked");

        long sequence = ledger.Sequence;
        InventoryReservationResult rejected = ledger.PrepareReservation(Request("operation-c", 1), out InventoryState rejectedState);
        Equal(InventoryReservationResultStatus.Rejected, rejected.Status, "over-reserve rejected");
        True(rejectedState == null && ledger.Sequence == sequence, "failed batch must be atomic");

        InventoryReservationResolutionResult restored = ledger.PrepareReservationResolution(
            firstResult.Reservation.ReservationId, InventoryReservationResolution.Restore, "server-rejected", out InventoryState restoredState);
        True(restored.Succeeded, "restore succeeds");
        ledger.Publish(restoredState);
        Equal(6L, ledger.AvailableSnapshot()[0].Quantity, "restored quantity available");

        InventoryReservationResolutionResult committed = ledger.PrepareReservationResolution(
            second.Reservation.ReservationId, InventoryReservationResolution.Commit, "server-success", out InventoryState committedState);
        True(committed.Succeeded, "commit succeeds");
        ledger.Publish(committedState);
        Equal(6L, ledger.Snapshot()[0].Quantity, "commit deducts stored balance");

        InventoryReservationResult finalizedRetry = ledger.PrepareReservation(first, out _);
        Equal(InventoryReservationResultStatus.Conflict, finalizedRetry.Status, "finalized operation cannot be sent again");
    }

    private static void OldSnapshotsDefaultToNoReservations()
    {
        InventoryLedger ledger = new InventoryLedger();
        InventoryState old = State(3);
        old.Reservations = null;
        ledger.Restore(old);
        Equal(0, ledger.ReservationSnapshot().Count, "old snapshot reservation migration");
    }

    private static void InvalidOverlappingLocksAreRejected()
    {
        InventoryState state = State(2);
        state.Reservations.Add(new InventoryReservationRecord
        {
            ReservationId = "reservation",
            OperationId = "operation",
            RequestHash = "hash",
            OwnerExtensionId = "test",
            Purpose = "test",
            State = InventoryReservationState.Reserved,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Lines = new List<InventoryReservationLine>
            {
                new InventoryReservationLine { EntryId = "entry", Quantity = 3 }
            }
        });
        bool threw = false;
        try { new InventoryLedger().Restore(state); }
        catch (InvalidDataException) { threw = true; }
        True(threw, "overlapping locks must fail validation");
    }

    private static void DisplayGroupingPreservesOwnershipAndCounts()
    {
        InventoryEntry a = DisplayEntry("a", "sender", "event-a");
        InventoryEntry b = DisplayEntry("b", "sender", "event-b");
        InventoryEntry other = DisplayEntry("c", "other-sender", "event-c");
        InventoryEntry quality = DisplayEntry("d", "sender", "event-d");
        InventoryEntry[] entries = { a, b, other, quality };
        InventoryItemPresentation[] presentations =
        {
            DisplayPresentation("meat", 75), DisplayPresentation("meat", 61),
            DisplayPresentation("meat", 75), DisplayPresentation("meat-quality", 75)
        };
        byte[] before = (byte[])a.Payload.Clone();
        List<InventoryDisplayGroup> groups = InventoryDisplayGrouping.Build(entries, presentations, new long[] { 1, 1, 0, 1 });
        Equal(3, groups.Count, "same item/source groups across events; other source and quality separate");
        Equal(136L, groups[0].AvailableItems, "sum actual counts, not record units");
        Equal(2, groups[0].EntryIndices.Count, "retain each entry in group");
        Equal(0L, groups[1].AvailableItems, "locked quantities not available");
        Equal("event-a", a.OriginId, "grouping preserves provenance");
        Equal(1L, a.Quantity, "grouping preserves balance");
        Equal(before[0], a.Payload[0], "grouping preserves state payload");
        Equal("a", entries[groups[0].EntryIndices[0]].EntryId, "actions map to original entry");
        Equal("b", entries[groups[0].EntryIndices[1]].EntryId, "second action has independent entry");
        groups = InventoryDisplayGrouping.Build(entries, presentations, new long[] { 0, 1, 0, 1 });
        Equal(61L, groups[0].AvailableItems, "reserve updates display count");
    }

    private static void DisplayGroupingSeparatesUnknownsAndOverflow()
    {
        InventoryEntry[] entries = { DisplayEntry("a", "sender", "x"), DisplayEntry("b", "sender", "y") };
        InventoryItemPresentation[] unknown = { DisplayPresentation(null, 1), DisplayPresentation(null, 1) };
        Equal(2, InventoryDisplayGrouping.Build(entries, unknown, new long[] { 1, 1 }).Count, "no codec metadata means no grouping");
        InventoryItemPresentation[] known = { DisplayPresentation("same", 1), DisplayPresentation("same", 1) };
        List<InventoryDisplayGroup> groups = InventoryDisplayGrouping.Build(entries, known, new long[] { long.MaxValue, 1 });
        Equal(2, groups.Count, "overflow splits presentation groups");
        Equal(long.MaxValue, groups[0].AvailableItems, "overflow does not wrap");
        entries[0].OriginUserId = entries[1].OriginUserId = null;
        Equal(2, InventoryDisplayGrouping.Build(entries, known, new long[] { 1, 1 }).Count, "anonymous events remain separate");
        entries[0].Source = "ab"; entries[0].OriginKind = "c";
        entries[1].Source = "a"; entries[1].OriginKind = "bc";
        entries[0].OriginId = entries[1].OriginId = "same";
        Equal(2, InventoryDisplayGrouping.Build(entries, known, new long[] { 1, 1 }).Count, "identity fields do not collide");
    }

    private static InventoryEntry DisplayEntry(string id, string sender, string eventId)
    {
        return new InventoryEntry
        {
            EntryId = id, CodecId = "codec", CodecVersion = 1, Quantity = 1,
            Payload = new[] { (byte)id[0] }, Source = "trade", OriginKind = "trade",
            OriginUserId = sender, OriginId = eventId
        };
    }

    private static InventoryItemPresentation DisplayPresentation(string key, long count)
    {
        return new InventoryItemPresentation { Label = "Localized meat", GroupKey = key, ItemsPerUnit = count };
    }

    private static void ComponentTypeResolverIsStrictlyScoped()
    {
        Type result = typeof(string);
        True(InventoryGameComponentTypeResolutionPatch.Prefix("AnotherMod.InventoryGameComponent", ref result), "other mods keep native resolution");
        Equal(typeof(string), result, "other resolution result untouched");
        True(InventoryGameComponentTypeResolutionPatch.Prefix(null, ref result), "null name falls through");
        True(!InventoryGameComponentTypeResolutionPatch.Prefix(typeof(InventoryGameComponent).FullName, ref result), "exact inventory name bypasses stale cache");
        Equal(typeof(InventoryGameComponent), result, "resolve concrete component, not abstract GameComponent");
    }

    private static void ResolvedComponentPreservesSnapshot()
    {
        string name = typeof(InventoryGameComponent).FullName;
        InventoryState state = State(2778);
        state.Entries[0].Label = "Silver × 2778";
        state.Entries[0].OriginId = "redpacket-event";
        state.Sequence = 1;
        XmlDocument document = new XmlDocument();
        XmlElement node = document.CreateElement("li");
        document.AppendChild(node);
        node.SetAttribute("Class", name);
        XmlElement id = document.CreateElement("phinixInventorySaveId");
        id.InnerText = "d70a8501621f48a1b3a100b4f35c47a5";
        node.AppendChild(id);
        XmlElement snapshot = document.CreateElement("phinixInventorySnapshot");
        snapshot.InnerText = Utils.Framework.FrameworkSerialization.SerializePayload(state);
        node.AppendChild(snapshot);
        Type resolved = null;
        True(!InventoryGameComponentTypeResolutionPatch.Prefix(node.GetAttribute("Class"), ref resolved), "persisted class resolves despite a null native result");
        InventoryGameComponent component = (InventoryGameComponent)Activator.CreateInstance(resolved, new object[] { null });
        // Unit fixture supplies the values that Scribe_Values reads. Full Scribe
        // initialization requires Steamworks/Unity runtime dependencies absent
        // from compile-only game references; this is not in-game validation.
        typeof(InventoryGameComponent).GetField("saveId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, id.InnerText);
        typeof(InventoryGameComponent).GetField("snapshotJson", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, snapshot.InnerText);
        Equal(id.InnerText, component.SaveId, "save lineage ID restored without replacement");
        InventoryLedger restored = new InventoryLedger();
        restored.Restore(component.ReadSnapshot());
        Equal(2778L, restored.Snapshot()[0].Quantity, "saved silver quantity restored");
        Equal("redpacket-event", restored.Snapshot()[0].OriginId, "saved provenance restored");
        Equal(1L, restored.Sequence, "ledger sequence preserved");
    }

    private static InventoryLedger CreateLedger(long quantity)
    {
        InventoryLedger ledger = new InventoryLedger();
        ledger.Restore(State(quantity));
        return ledger;
    }

    private static InventoryState State(long quantity)
    {
        return new InventoryState
        {
            Entries = new List<InventoryEntry>
            {
                new InventoryEntry
                {
                    EntryId = "entry",
                    CodecId = "test.codec",
                    CodecVersion = 1,
                    Payload = new byte[] { 1 },
                    Quantity = quantity,
                    Label = "Test",
                    Source = "test"
                }
            }
        };
    }

    private static InventoryReservationRequest Request(string operationId, long quantity)
    {
        return new InventoryReservationRequest
        {
            OperationId = operationId,
            OwnerExtensionId = "test.extension",
            Purpose = "test.transfer",
            Lines = new[] { new InventoryReservationLine { EntryId = "entry", Quantity = quantity } }
        };
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Assertion failed: " + message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException("Assertion failed: " + message + "; expected=" + expected + ", actual=" + actual);
    }
}
