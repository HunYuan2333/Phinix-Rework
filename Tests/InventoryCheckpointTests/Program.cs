using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using Phinix.InventoryExtension.Client;
using Utils.Framework;

internal static class Program
{
    static int checks;
    static string artifacts;
    static readonly string Id = "synthetic-save";
    static InventoryState State(long n) => CapacityRegression.State(n);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static string Record(InventoryState state)
    {
        byte[] payload = Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(state));
        using (var sha = SHA256.Create()) return state.Sequence + "|" + Convert.ToBase64String(sha.ComputeHash(payload)) + "|" + Convert.ToBase64String(payload) + "\n";
    }
    static void Save(string file, InventoryState state, string id = null, string journalIdentity = null)
    {
        using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write))
        {
            using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { CloseOutput = false }))
            {
                xml.WriteStartElement("savegame"); xml.WriteStartElement("game"); xml.WriteStartElement("components"); xml.WriteStartElement("li");
                xml.WriteAttributeString("Class", "Phinix.InventoryExtension.Client.InventoryGameComponent");
                xml.WriteElementString("phinixInventorySaveId", id ?? Id); xml.WriteElementString("phinixInventorySnapshot", FrameworkSerialization.SerializePayload(state));
                if (journalIdentity != null) xml.WriteElementString("phinixInventoryJournalIdentity", journalIdentity);
                xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
            }
            stream.Flush(true);
        }
    }
    static bool Compact(InventoryJournal journal, string file, InventoryState state)
    { using (var saved = InventorySaveCheckpoint.Open(file, Id, state)) return journal.TryCheckpoint(saved); }
    static void Throws(Action action, string reason) { try { action(); } catch { checks++; return; } throw new Exception(reason); }
    static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--crash") { CrashChild(args[1], args[2]); return; }
        string root = Path.Combine(Path.GetTempPath(), "phinix-checkpoint-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            artifacts = args[1]; Directory.CreateDirectory(artifacts);
            CapacityRegression.Run(root); InventoryJournalScenarios.Run(); LegacyFixture(root, args[0]);
            CompatibilityMaintenance(root); CheckpointAndFuture(root); SavedFileValidation(root); CompactionFailures(root); LargeLegacy(root); LongRun(root); PendingExtraction(root); RecoveryVeto(root); MoreFailures(root); ConcurrentMaintenance(root); Crashes(root);
            Console.WriteLine("CHECKPOINT RESULT passed; assertions=" + checks + "; fixed schedule; no real game assets or player data");
        }
        finally { Directory.Delete(root, true); }
    }
    static void LegacyFixture(string root, string fixture)
    {
        string path = Path.Combine(root, "legacy-v1.journal"), save = Path.Combine(root, "legacy-v1.rws");
        File.Copy(Path.Combine(fixture, "legacy-v1.journal"), path); File.Copy(Path.Combine(fixture, "legacy-v1.rws"), save);
        using (var journal = new InventoryJournal(path, State(1)))
        { Check(!journal.Faulted && !journal.HasPendingRecovery, "Independent older schema without Reservations loads"); Check(Compact(journal, save, State(1)), "Old actual saved snapshot permits safe checkpoint"); Check(journal.TryAppend(State(2)), "Old file upgrade remains writable"); }
        using (var journal = new InventoryJournal(path, State(1))) Check(journal.HasPendingRecovery && journal.PendingState.Sequence == 2, "Legacy upgrade retains explicit future recovery");
    }
    static void CompatibilityMaintenance(string root)
    {
        string path = Path.Combine(root, "compatibility.journal"), save = Path.Combine(root, "compatibility.rws");
        string history = Record(State(1)) + Record(State(2)); File.WriteAllText(path, history);
        string sibling = Path.Combine(root, "unloaded.journal"), torn = path + ".torn-old", rejected = path + ".rejected-old";
        foreach (string file in new[] { sibling, torn, rejected }) File.WriteAllText(file, "forensic evidence");
        var messages = new System.Collections.Generic.List<string>();
        using (var journal = new InventoryJournal(path, State(2)))
        {
            string identity = (string)typeof(InventoryJournal).GetProperty("Identity")?.GetValue(journal);
            Save(save, State(2), null, identity);
            Check(InventoryJournalCompatibility.TryMaintain(journal, save, Id, State(2), (message, warning) => messages.Add(message)), "Compatibility entrypoint verifies and cleans covered legacy history");
            Check(File.ReadAllText(path) == Record(State(2)) && journal.LastCheckpointBytesReclaimed == Encoding.UTF8.GetByteCount(Record(State(1))), "Exact reclaimed bytes equal discarded covered record");
            Check(messages.Count == 1 && messages[0].Contains("[Compatibility]") && !messages[0].Contains(root) && !messages[0].Contains(Id), "Tagged compatibility summary contains no player path/identity/payload");
            Check(InventoryJournalCompatibility.TryMaintain(journal, save, Id, State(2), (message, warning) => messages.Add(message)) && messages.Count == 1 && journal.LastCheckpointBytesReclaimed == 0, "Repeated maintenance is safe and does not log a fake reclaim");
            File.WriteAllText(path, history); Save(save, State(1), null, identity);
            Check(!InventoryJournalCompatibility.TryMaintain(journal, save, Id, State(2), (message, warning) => messages.Add(message)) && File.ReadAllText(path) == history && !journal.Faulted, "Unproven save is skipped without erasing history or inventing a journal fault");
            Check(messages.Last().Contains("History retained"), "Skipped compatibility maintenance is diagnosable");
            Save(save, State(2), null, identity);
            Check(InventoryJournalCompatibility.TryMaintain(journal, save, Id, State(2), (message, warning) => { throw new Exception("synthetic diagnostic observer"); }), "Observer failure cannot undo successful cleanup");
        }
        foreach (string file in new[] { sibling, torn, rejected }) Check(File.ReadAllText(file) == "forensic evidence", "Compatibility entrypoint must not sweep unrelated journal/evidence: " + Path.GetFileName(file));
        File.WriteAllText(path, Record(State(1)) + Record(State(2)) + Record(State(3)));
        using (var journal = new InventoryJournal(path, State(1)))
        {
            string identity = (string)typeof(InventoryJournal).GetProperty("Identity")?.GetValue(journal); Save(save, State(1), null, identity);
            Check(InventoryJournalCompatibility.TryMaintain(journal, save, Id, State(1), null) && journal.HasPendingRecovery && journal.PendingState.Sequence == 3, "Automatic compatibility maintenance never approves future state");
        }
    }
    static void CheckpointAndFuture(string root)
    {
        string path = Path.Combine(root, "future.journal"), save = Path.Combine(root, "future.rws");
        File.WriteAllText(path, Record(State(1)) + Record(State(2)) + Record(State(3)), new UTF8Encoding(false)); Save(save, State(1));
        string future = Record(State(2)) + Record(State(3));
        using (var journal = new InventoryJournal(path, State(1)))
        {
            Check(journal.HasPendingRecovery, "Legacy future records require approval");
            Check(Compact(journal, save, State(1)), "Verified checkpoint can compact covered history");
            Check(File.ReadAllText(path) == Record(State(1)) + future && journal.HasPendingRecovery, "Checkpoint preserves every future byte and approval requirement");
            Check(!journal.TryAppend(State(2)) && !journal.Faulted && journal.LastError.Contains("approval"), "Recovery block is a visible business refusal");
            Check(journal.ApproveRecovery().Sequence == 3, "Latest future full state is retained");
            Check(journal.TryAppend(State(4)), "Approval advances append baseline"); Save(save, State(3));
            Check(Compact(journal, save, State(3)) && File.ReadAllText(path) == Record(State(3)) + Record(State(4)), "Older checkpoint keeps later append");
        }
        using (var reopened = new InventoryJournal(path, State(3))) Check(!reopened.Faulted && reopened.PendingState.Sequence == 4, "Restart recovers future only");
        using (var oldSave = new InventoryJournal(path, State(0))) Check(oldSave.Faulted, "Rollback older than retained anchor fails closed");
        Save(save, State(8));
        using (var newer = new InventoryJournal(path, State(8)))
        { Check(Compact(newer, save, State(8)), "Newer actual save covers journal history"); Check(File.ReadAllText(path) == Record(State(8)), "Actual saved anchor retained"); Check(newer.TryAppend(State(9)), "Future record for legacy reader compatibility"); }
        File.Copy(path, Path.Combine(artifacts, "compacted-for-old-reader.journal"));
    }
    static void SavedFileValidation(string root)
    {
        string path = Path.Combine(root, "proof.journal"), save = Path.Combine(root, "proof.rws");
        File.WriteAllText(path, Record(State(1)) + Record(State(2)));
        using (var journal = new InventoryJournal(path, State(2)))
        {
            byte[] before = File.ReadAllBytes(path); Save(save, State(2), "other-save");
            Throws(() => Compact(journal, save, State(2)), "Wrong identity cannot checkpoint");
            var conflict = State(2); conflict.Commits.Add(new InventoryCommit { DepositId = "different", ContentHash = "synthetic", HashVersion = 3 }); Save(save, conflict);
            Throws(() => Compact(journal, save, State(2)), "Same sequence alone is not proof");
            Save(save, State(2)); File.AppendAllText(save, "<broken>");
            Throws(() => Compact(journal, save, State(2)), "Complete document required, not just component prefix");
            Check(File.ReadAllBytes(path).SequenceEqual(before), "Invalid saved evidence never changes journal");
            Save(save, State(2)); using (var writer = new FileStream(save, FileMode.Open, FileAccess.Write, FileShare.None))
                Throws(() => Compact(journal, save, State(2)), "Save still writing cannot checkpoint");
            // Save-as/autosave filenames keep separate journals; a valid actual saved copy can cover its own history.
            for (int i = 0; i < 4; i++) { string copy = Path.Combine(root, "AutoSave-" + i + ".rws"); Save(copy, State(2)); Check(Compact(journal, copy, State(2)), "Actual rotated save snapshot verified"); }
            Save(save, State(3)); Check(!Compact(journal, save, State(3)), "Memory/ledger behind file cannot checkpoint");
        }
    }
    static void CompactionFailures(string root)
    {
        string path = Path.Combine(root, "failure.journal"), save = Path.Combine(root, "failure.rws"); Save(save, State(2));
        foreach (string stage in new[] { "inventory.compact.before-flush", "inventory.compact.before-replace", "inventory.compact.after-replace" })
        {
            File.WriteAllText(path, Record(State(1)) + Record(State(2))); byte[] old = File.ReadAllBytes(path);
            using (var journal = new InventoryJournal(path, State(2), (Action<string>)null, id => { if (id == stage) throw new IOException("synthetic disk failure"); }))
            { Check(!Compact(journal, save, State(2)) && journal.CheckpointError != null, "Controlled failure is visible"); if (stage != "inventory.compact.after-replace") Check(File.ReadAllBytes(path).SequenceEqual(old), "Pre-replace failure keeps original bytes"); }
            using (var reopened = new InventoryJournal(path, State(2))) Check(!reopened.Faulted && reopened.TryAppend(State(3)), "Old or atomically replaced source remains usable");
        }
        File.WriteAllText(path, Record(State(1))); File.WriteAllText(path + ".compact", "untrusted orphan");
        using (var reopened = new InventoryJournal(path, State(1))) Check(!reopened.Faulted && File.ReadAllText(path).StartsWith("1|"), "Startup never promotes orphan compact file");
        File.Delete(path + ".compact"); Directory.CreateDirectory(path + ".compact"); Save(save, State(1));
        using (var journal = new InventoryJournal(path, State(1))) Check(!Compact(journal, save, State(1)) && !journal.Faulted && journal.TryAppend(State(2)), "Unwritable temp path preserves writable original");
        Directory.Delete(path + ".compact");
        using (var journal = new InventoryJournal(Path.Combine(root, "write-io.journal"), State(0)))
        { if (File.Exists(Path.Combine(root, "write-io.journal"))) File.Delete(Path.Combine(root, "write-io.journal")); Directory.CreateDirectory(Path.Combine(root, "write-io.journal")); Check(!journal.TryAppend(State(1)) && journal.Faulted && journal.FaultReason.Contains("Append"), "Real IO append failure is visible"); }
    }
    static void LargeLegacy(string root)
    {
        string path = Path.Combine(root, "large.journal"), save = Path.Combine(root, "large.rws");
        byte[] payload = new byte[256 * 1024]; for (int i = 0; i < payload.Length; i++) payload[i] = 255;
        long n = 0;
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        { while (stream.Length <= 128L * 1024 * 1024) { byte[] row = Encoding.UTF8.GetBytes(Record(CapacityRegression.State(++n, payload))); stream.Write(row, 0, row.Length); } stream.Flush(true); }
        var saved = CapacityRegression.State(n, payload); Save(save, saved);
        using (var journal = new InventoryJournal(path, saved))
        { Check(!journal.Faulted && !journal.HasPendingRecovery, "Legacy file over 128 MiB can be verified for upgrade"); Check(Compact(journal, save, saved), "Over-limit legacy history can be safely reclaimed"); Check(new FileInfo(path).Length < 2 * 1024 * 1024 && journal.TryAppend(CapacityRegression.State(n + 1, payload)), "Upgrade resumes bounded writes"); }
        using (var journal = new InventoryJournal(path, saved)) Check(journal.HasPendingRecovery && journal.PendingState.Sequence == n + 1, "Upgrade preserves later recovery and bytes");
        Console.WriteLine("LARGE legacy fixture: records=" + n + "; verified >128 MiB; no players");
    }
    static void LongRun(string root)
    {
        string path = Path.Combine(root, "long.journal"), save = Path.Combine(root, "long.rws");
        using (var journal = new InventoryJournal(path, State(0)))
        {
            for (int n = 1; n <= 10000; n++)
            { Check(journal.TryAppend(State(n)), "Monotonic append"); if (n % 100 == 0) { Save(save, State(n)); Check(Compact(journal, save, State(n)), "Repeated save checkpoint"); Check(new FileInfo(path).Length < 1024, "History bounded after checkpoint"); } }
        }
        using (var reopened = new InventoryJournal(path, State(10000))) Check(!reopened.Faulted && !reopened.HasPendingRecovery, "Long-run restart exact snapshot");
    }
    static void PendingExtraction(string root)
    {
        string path = Path.Combine(root, "pending.journal"), save = Path.Combine(root, "pending.rws");
        var pending = CapacityRegression.State(2, new byte[] { 17 }); pending.PendingExtraction = new InventoryPendingExtraction { EntryId = "synthetic", Quantity = 2 };
        File.WriteAllText(path, Record(State(1)) + Record(pending)); Save(save, pending);
        using (var journal = new InventoryJournal(path, pending)) Check(Compact(journal, save, pending), "Pending extraction can be retained in full saved anchor");
        using (var reopened = new InventoryJournal(path, State(1))) Check(reopened.HasPendingRecovery && reopened.PendingState.PendingExtraction.Quantity == 2, "Pending extraction not refunded or replayed by compaction");
    }
    static void RecoveryVeto(string root)
    {
        var veto = typeof(InventoryJournal).GetProperty("CanRejectRecovery"); if (veto == null) return;
        string path = Path.Combine(root, "p3-veto.journal"), save = Path.Combine(root, "p3-veto.rws");
        File.WriteAllText(path, Record(State(1)) + Record(CapacityRegression.State(2, new byte[] { 1 })) + Record(State(3))); Save(save, State(1));
        using (var journal = new InventoryJournal(path, State(1)))
        {
            Check(journal.HasPendingRecovery && !(bool)veto.GetValue(journal) && !journal.RejectRecovery(), "P3 ownership-changing intermediate state veto survives latest-only memory retention");
            Check(Compact(journal, save, State(1)) && !(bool)veto.GetValue(journal), "P3 compaction preserves recovery veto");
        }
    }
    static void MoreFailures(string root)
    {
        string path = Path.Combine(root, "capacity.journal"), save = Path.Combine(root, "capacity.rws");
        string row = File.ReadLines(path).Last();
        var state = FrameworkSerialization.DeserializePayload<InventoryState>(Encoding.UTF8.GetString(Convert.FromBase64String(row.Split('|')[2])));
        using (var journal = new InventoryJournal(path, state))
        {
            var next = CapacityRegression.State(state.Sequence + 1, state.Entries[0].Payload);
            Check(!journal.TryAppend(next) && journal.Faulted, "Capacity fault is sticky until verified saved checkpoint");
            Save(save, state); Check(Compact(journal, save, state) && !journal.Faulted && journal.TryAppend(next), "Verified checkpoint clears capacity-only fault and resumes exact next write");
        }
        byte[] payload = new byte[3 * 1024 * 1024]; for (int i = 0; i < payload.Length; i++) payload[i] = 255;
        using (var journal = new InventoryJournal(Path.Combine(root, "oversized-record.journal"), State(0)))
            Check(!journal.TryAppend(CapacityRegression.State(1, payload)) && journal.Faulted && journal.FaultReason.Contains("Payload"), "Oversized serialized payload is an observable persistence fault");
        var closed = new InventoryJournal(Path.Combine(root, "closed.journal"), State(0)); closed.Dispose();
        Check(!closed.TryAppend(State(1)) && closed.Faulted && closed.FaultReason.Contains("closed"), "Closed journal refuses write explicitly");
        Save(save, State(1));
        string duplicate = File.ReadAllText(save).Replace("</components>", File.ReadAllText(save).Split(new[]{"<components>"},StringSplitOptions.None)[1].Split(new[]{"</components>"},StringSplitOptions.None)[0]+"</components>");
        File.WriteAllText(save, duplicate); Throws(() => { using (InventorySaveCheckpoint.Open(save, Id, State(1))) { } }, "Duplicate saved component is not proof");
    }
    static void ConcurrentMaintenance(string root)
    {
        string path = Path.Combine(root, "concurrent.journal"), save = Path.Combine(root, "concurrent.rws"); Save(save, State(0));
        using (var journal = new InventoryJournal(path, State(0)))
        {
            Exception failure = null;
            var maintenance = new Thread(() => { try { for (int n = 0; n < 30; n++) if (!Compact(journal, save, State(0))) throw new Exception("Concurrent checkpoint failed"); } catch (Exception error) { failure = error; } });
            maintenance.Start(); for (int n = 1; n <= 100; n++) Check(journal.TryAppend(State(n)), "Serialized append during checkpoint");
            Check(maintenance.Join(10000) && failure == null, "Maintenance and writer serialize without loss");
        }
        using (var journal = new InventoryJournal(path, State(0))) Check(journal.HasPendingRecovery && journal.PendingState.Sequence == 100, "Concurrent result has every contiguous future record");
    }
    static void CrashChild(string root, string stage)
    {
        string path = Path.Combine(root, "kill.journal"), save = Path.Combine(root, "kill.rws");
        File.WriteAllText(path, Record(State(1)) + Record(State(2)) + Record(State(3))); Save(save, State(2));
        using (var journal = new InventoryJournal(path, State(2), (Action<string>)null, id =>
        { if (id == stage) { using (var marker = new FileStream(Path.Combine(root, "barrier"), FileMode.Create, FileAccess.Write)) { marker.WriteByte(1); marker.Flush(true); } Thread.Sleep(Timeout.Infinite); } })) Compact(journal, save, State(2));
    }
    static void Crashes(string root)
    {
        foreach (string stage in new[] { "inventory.compact.before-flush", "inventory.compact.before-replace", "inventory.compact.after-replace" })
        {
            string dir = Path.Combine(root, stage); Directory.CreateDirectory(dir);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(Program).Assembly.Location); start.ArgumentList.Add("--crash"); start.ArgumentList.Add(dir); start.ArgumentList.Add(stage);
            using (var child = Process.Start(start))
            {
                try { Check(SpinWait.SpinUntil(() => File.Exists(Path.Combine(dir, "barrier")) || child.HasExited, 15000) && !child.HasExited, "Child reaches fixed barrier: " + stage); child.Kill(true); Check(child.WaitForExit(5000), "Strong kill completes"); }
                finally { if (!child.HasExited) { child.Kill(true); child.WaitForExit(5000); } }
            }
            string path = Path.Combine(dir, "kill.journal"); var lines = File.ReadAllLines(path); Check(lines.Last() == Record(State(3)).TrimEnd('\n'), "Independent raw future oracle survives kill");
            Check(lines.First() == Record(State(stage == "inventory.compact.after-replace" ? 2 : 1)).TrimEnd('\n'), "Source is exactly old or replaced at barrier");
            using (var journal = new InventoryJournal(path, State(2))) Check(!journal.Faulted && journal.HasPendingRecovery && journal.PendingState.Sequence == 3, "Kill/restart preserves explicit recovery");
        }
    }
}
