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
            DailyScheduleServicePreservesOldRecords();
            SearchProjectionRetainsEntryDraft();
            BatchExtractionRunsOneVerifiedStackAtATime();
            BatchProjectionKeepsAvailableRecordsAndOwnership();
            ReservationLifecycleIsAtomicAndIdempotent();
            OldSnapshotsDefaultToNoReservations();
            InvalidOverlappingLocksAreRejected();
            DisplayGroupingPreservesOwnershipAndCounts();
            DisplayGroupingSeparatesUnknownsAndOverflow();
            ComponentTypeResolverIsStrictlyScoped();
            ResolvedComponentPreservesSnapshot();
            InventoryJournalScenarios.Run();
            Console.WriteLine("All 21 inventory schedule, search, batch, reservation, grouping, component snapshot and journal runtime scenarios passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void DailyScheduleServicePreservesOldRecords()
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var service = new BuiltInInventoryClientExtension();
        var type = typeof(BuiltInInventoryClientExtension);
        Action<string, object> set = (name, value) => type.GetField(name, fields).SetValue(service, value);
        var ledger = (InventoryLedger)type.GetField("ledger", fields).GetValue(service);
        var snapshot = State(6334); ledger.Restore(snapshot);
        string root = Path.Combine(Path.GetTempPath(), "phinix-plan-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string savePath = Path.Combine(root, "old-save.rws"); File.WriteAllText(savePath, "isolated test identity");
        Type patch = type.Assembly.GetType("Phinix.InventoryExtension.Client.InventorySaveIdentityPatch", true);
        var pathProperty = patch.GetProperty("CurrentPath", BindingFlags.Static | BindingFlags.Public);
        object previousPath = pathProperty.GetValue(null);
        Game previousGame = Current.Game;
        LoadedLanguage previousLanguage = LanguageDatabase.activeLanguage;
        // Supply only translation fixtures; no native Unity logging or language-file loading in this probe.
        var language = (LoadedLanguage)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(LoadedLanguage));
        var languageFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(LoadedLanguage).GetField("dataIsLoaded", languageFields).SetValue(language, true);
        typeof(LoadedLanguage).GetField("folderName", languageFields).SetValue(language, "English");
        var workerField = typeof(LoadedLanguage).GetField("workerInt", languageFields);
        workerField.SetValue(language, new LanguageWorker_English());
        var replacementsField = typeof(LoadedLanguage).GetField("keyedReplacements", languageFields);
        var replacements = (System.Collections.IDictionary)Activator.CreateInstance(replacementsField.FieldType);
        var replacementType = replacementsField.FieldType.GetGenericArguments()[1];
        foreach (string key in new[] { "planMissingItem", "inlineQuantityInvalid", "planInvalid", "planCodecMissing", "planCodecVersion", "planItemUnreadable", "planSaveFailed" })
        {
            string fullKey = "Phinix_inventory_" + key;
            object replacement = Activator.CreateInstance(replacementType);
            replacementType.GetField("key").SetValue(replacement, fullKey);
            replacementType.GetField("value").SetValue(replacement, fullKey);
            replacements.Add(fullKey, replacement);
        }
        replacementsField.SetValue(language, replacements);
        var game = (Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));
        game.components = new List<GameComponent>();
        var component = new InventoryGameComponent(game); game.components.Add(component);
        Type journalType = type.Assembly.GetType("Phinix.InventoryExtension.Client.InventoryJournal", true);
        string journalPath = Path.Combine(root, "inventory.journal");
        var journal = (IDisposable)Activator.CreateInstance(journalType, new object[] { journalPath, snapshot });
        var codec = new ScheduleCodec();
        try
        {
            Current.Game = game; LanguageDatabase.activeLanguage = language; pathProperty.SetValue(null, savePath);
            set("mainThreadId", System.Threading.Thread.CurrentThread.ManagedThreadId);
            set("attachedSave", component); set("attachedIdentity", savePath); set("journal", journal);
            True(!service.SetDailySchedule("entry", 20, 50, out string missing), "missing codec rejects plan");
            True(!string.IsNullOrEmpty(missing), "missing codec has a specific explanation");
            service.RegisterCodec(codec);
            codec.Throw = true;
            True(!service.SetDailySchedule("entry", 20, 50, out string unreadable), "unreadable old payload rejects plan");
            True(unreadable != missing, "payload failure is distinct from missing plugin");
            codec.Throw = false; codec.Limit = 0;
            True(!service.SetDailySchedule("entry", 20, 50, out _), "invalid stack limit rejects plan");
            codec.Limit = 75; codec.VersionValue = 2;
            True(!service.SetDailySchedule("entry", 20, 50, out string version), "wrong codec version rejects plan");
            True(version != missing && version != unreadable, "version rejection is specific");
            codec.VersionValue = 1;
            True(!service.SetDailySchedule("entry", 6335, 50, out string quantity), "over-balance rejected");
            True(quantity != missing && quantity != unreadable, "quantity rejection is distinct");
            True(!service.SetDailySchedule("entry", 20, 60000, out _), "invalid tick rejected");
            Equal(0L, ledger.Sequence, "all rejection paths leave ledger and journal untouched");
            True(service.SetDailySchedule("entry", 20, 50, out string reason), "old record with 6334 units accepts 20 at tick 50: " + reason);
            Equal(6334L, ledger.Snapshot()[0].Quantity, "saving a plan cannot extract or consume items");
            Equal(20L, service.GetSchedules()[0].QuantityPerRun, "daily quantity retained");
            Equal(50, service.GetSchedules()[0].DayTick, "tick retained");
            Equal(1L, ledger.Sequence, "one durable schedule write");
            journal.Dispose();
            using (var reloaded = (IDisposable)Activator.CreateInstance(journalType, new object[] { journalPath, snapshot }))
            {
                True((bool)journalType.GetProperty("HasPendingRecovery").GetValue(reloaded), "saved plan exists beyond the old snapshot");
                var recovered = (InventoryState)journalType.GetProperty("PendingState").GetValue(reloaded);
                Equal(20L, recovered.Schedules[0].QuantityPerRun, "journal retains real committed plan");
            }
        }
        finally
        {
            journal.Dispose(); Current.Game = previousGame; LanguageDatabase.activeLanguage = previousLanguage; pathProperty.SetValue(null, previousPath);
            Directory.Delete(root, true);
        }
    }

    private sealed class ScheduleCodec : IInventoryCodec
    {
        public string CodecId => "test.codec";
        public int VersionValue = 1, Limit = 75;
        public bool Throw;
        public int Version => VersionValue;
        public bool CanStore(InventoryItem item) => true;
        public string GetAggregationKey(InventoryItem item) => null;
        public int MaximumStackCount(InventoryEntry entry) { if (Throw) throw new InvalidDataException("old payload unreadable"); return Limit; }
        public Thing Materialize(InventoryEntry entry, int count) => throw new Exception("plan saving must not materialize items");
        public InventoryDeliveryResult DeliverVerified(Thing thing, Map map, IntVec3 dropSpot) => throw new Exception("plan saving must not deliver items");
    }

    private static void SearchProjectionRetainsEntryDraft()
    {
        // Exercise actual projection methods without opening a native window or initializing game services.
        Type type = typeof(InventoryLedger).Assembly.GetType("Phinix.InventoryExtension.Client.InventoryTab", true);
        object tab = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        Action<string,object> set = (name,value) => type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(tab,value);
        Func<string,object> get = name => type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tab);
        Action<string> search = query => type.GetMethod("ChangeSearch",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tab,new object[]{query});
        var entries = new[] { DisplayEntry("a","alice","event"), DisplayEntry("b","alice","event") };
        var presentations = new[] { DisplayPresentation("same",1), DisplayPresentation("same",1) };
        set("entries",entries);set("groups",InventoryDisplayGrouping.Build(entries,presentations,new long[]{3,7}));
        set("expandedGroups",new HashSet<string>(StringComparer.Ordinal));set("searchLabels",new[]{"first item","second item"});set("searchText","");
        set("selectedEntryId","b");set("extractQuantityText","7");
        search("second");
        var rows = (System.Collections.IList)get("visibleRows");
        Equal(1,rows.Count,"search reaches a record hidden inside a collapsed group");
        Equal(1,(int)rows[0].GetType().GetField("EntryIndex").GetValue(rows[0]),"result identity is the matched entry, not group head");
        Equal("b",(string)get("selectedEntryId"),"search preserves stable selected entry identity");
        Equal("7",(string)get("extractQuantityText"),"search does not clear the single extraction quantity draft");
        search("first");
        True(!(bool)type.GetMethod("EntryVisible",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(tab,new object[]{"b"}),"a hidden draft is detectable for the UI warning");
        Equal("b",(string)get("selectedEntryId"),"hiding a result cannot redirect its editor to a different entry");
        search("");rows=(System.Collections.IList)get("visibleRows");
        True((bool)rows[0].GetType().GetField("IsHeader").GetValue(rows[0]),"clearing query restores the original collapsed group");
        Equal(0,((HashSet<string>)get("expandedGroups")).Count,"search never alters expansion preferences");
        Equal(1L,entries[0].Quantity,"display search does not mutate quantity ownership");
    }

    private static void BatchProjectionKeepsAvailableRecordsAndOwnership()
    {
        var entries = new[] { DisplayEntry("a", "alice", "event"), DisplayEntry("b", "alice", "event"), DisplayEntry("c", "bob", "other") };
        var available = new long[] { 0, 5, 7 };
        var presentations = new[] { DisplayPresentation("stone", 1), DisplayPresentation("stone", 1), DisplayPresentation("stone", 1) };
        var groups = InventoryDisplayGrouping.Build(entries, presentations, available);
        using (var tab = new InventoryTab(new BuiltInInventoryClientExtension(), null))
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(InventoryTab).GetField("entries", flags).SetValue(tab, entries);
            typeof(InventoryTab).GetField("rowAvailable", flags).SetValue(tab, available);
            typeof(InventoryTab).GetField("groups", flags).SetValue(tab, groups);
            var plan = (List<InventoryBatchItem>)typeof(InventoryTab).GetMethod("GroupPlan", flags).Invoke(tab, new object[] { groups[0].Key, 1 });
            Equal(1, plan.Count, "batch record limit excludes locked zero records"); Equal("b", plan[0].EntryId, "original record identity"); Equal(5L, plan[0].Quantity, "only available quantity");
            plan = (List<InventoryBatchItem>)typeof(InventoryTab).GetMethod("GroupPlan", flags).Invoke(tab, new object[] { groups[0].Key, 10000 });
            Equal(1, plan.Count, "whole group cannot cross owner boundary");
        }
        var overflow = InventoryDisplayGrouping.Build(entries, presentations, new long[] { long.MaxValue, 5, 7 });
        True(overflow[0].Key != overflow[1].Key, "overflow groups need distinct selection identities");
    }

    private static void BatchExtractionRunsOneVerifiedStackAtATime()
    {
        var plan = new[] { new InventoryBatchItem("a", 5), new InventoryBatchItem("opaque", 2) };
        var batch = new InventoryExtractionBatch(plan, new[] { 3, 1 });
        var delivered = new List<string>();
        InventoryExtractionBatch.Deliver success = (string id, long units, out string reason) => { delivered.Add(id + ":" + units); reason = null; return true; };
        batch.Advance(success); Equal(1, delivered.Count, "one stack per frame"); Equal(0, batch.Progress.CompletedRecords, "partial record stays pending");
        batch.Advance(success); Equal(1, batch.Progress.CompletedRecords, "record completed after legal chunks");
        batch.Advance(success); batch.Advance(success); batch.Advance(success);
        Equal("a:3,a:2,opaque:1,opaque:1", string.Join(",", delivered), "opaque records never combine units");
        Equal(7L, batch.Progress.CompletedUnits, "conservation"); True(!batch.Progress.Running, "completed queue terminal");
        batch = new InventoryExtractionBatch(plan, new[] { 3, 1 }); batch.Advance(success);
        batch.Advance((string id, long units, out string reason) => { reason = "uncertain"; return false; });
        int before = delivered.Count; batch.Advance(success); Equal(before, delivered.Count, "failure cannot retry uncertain delivery");
        Equal(3L, batch.Progress.CompletedUnits, "already committed units retained"); Equal("uncertain", batch.Progress.Reason, "failure retained");
        batch = new InventoryExtractionBatch(plan, new[] { 3, 1 }); batch.Advance(success); batch.Stop("changed");
        before = delivered.Count; batch.Advance(success); Equal(before, delivered.Count, "cancel or save change stops only remaining work");
        batch = new InventoryExtractionBatch(new[] { new InventoryBatchItem("huge", long.MaxValue) }, new[] { 1 });
        batch.Advance(success); Equal(1L, batch.Progress.CompletedUnits, "huge quantity still bounded per frame"); batch.Stop("stop");
        var many = new InventoryBatchItem[1000]; var limits = new int[1000];
        for (int i = 0; i < many.Length; i++) { many[i] = new InventoryBatchItem("record-" + i, 1); limits[i] = 1; }
        batch = new InventoryExtractionBatch(many, limits);
        for (int i = 0; i < many.Length; i++) { batch.Advance(success); Equal(i + 1, batch.Progress.CompletedRecords, "large batch advances one independent record"); }
        True(!batch.Progress.Running && batch.Progress.CompletedUnits == 1000, "large batch complete");
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
