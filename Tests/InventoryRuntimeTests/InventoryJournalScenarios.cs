using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Phinix.InventoryExtension.Client;
using Utils.Framework;

internal static class InventoryJournalScenarios
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "phinix-journal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            HistoricalGapsAreCoveredBySavedSnapshots(root);
            FutureGapsAndDuplicateRecordsAreRejected(root);
            RecoveryRemainsExplicitAndTracksTheApprovedSequence(root);
            SnapshotConflictsAndCorruptionPreserveEvidence(root);
            AppendsCannotWriteDuplicateOrSkippedVersions(root);
            CrashTailsPreserveCompleteRecords(root);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void HistoricalGapsAreCoveredBySavedSnapshots(string root)
    {
        long[][] patterns = {
            new long[] { 1, 23 }, new long[] { 14, 17, 18, 19, 20, 21, 22 },
            new long[] { 15, 27, 28 }, Enumerable.Range(2, 12).Select(n => (long)n).Concat(new long[] { 16 }).ToArray()
        };
        for (int i = 0; i < patterns.Length; i++)
        {
            string path = Path.Combine(root, "history-" + i + ".journal");
            long latest = patterns[i].Last();
            Write(path, patterns[i]);
            byte[] original = File.ReadAllBytes(path);
            using (InventoryJournal journal = new InventoryJournal(path, State(latest)))
            {
                Check(!journal.Faulted && !journal.HasPendingRecovery, "saved snapshot covers historical path gaps");
                Check(File.ReadAllBytes(path).SequenceEqual(original), "verified history must not be rewritten");
                Check(journal.TryAppend(State(latest + 1)), "next operation remains writable");
            }
            using (InventoryJournal journal = new InventoryJournal(path, State(latest + 1)))
                Check(!journal.Faulted, "saving a later snapshot cannot invalidate the same history");
        }
    }

    private static void FutureGapsAndDuplicateRecordsAreRejected(string root)
    {
        long[][] patterns = { new long[] { 1, 23 }, new long[] { 2, 4 }, new long[] { 1, 1 }, new long[] { 2, 1 }, new long[] { 5 } };
        foreach (long[] pattern in patterns)
        {
            string path = Path.Combine(root, "invalid.journal");
            Write(path, pattern);
            byte[] original = File.ReadAllBytes(path);
            using (InventoryJournal journal = new InventoryJournal(path, State(1)))
            {
                Check(journal.Faulted && !journal.HasPendingRecovery, "future gaps, duplicates and backwards versions must fault");
                Check(journal.ApproveRecovery() == null && !journal.TryAppend(State(2)), "faults cannot authorize recovery or writes");
            }
            Check(File.ReadAllBytes(path).SequenceEqual(original), "faulted journal evidence retained");
        }
    }

    private static void RecoveryRemainsExplicitAndTracksTheApprovedSequence(string root)
    {
        string path = Path.Combine(root, "recovery.journal");
        Write(path, 1, 23, 24);
        using (InventoryJournal journal = new InventoryJournal(path, State(22)))
        {
            Check(!journal.Faulted && journal.HasPendingRecovery, "contiguous future tail needs approval");
            Check(!journal.TryAppend(State(23)), "no optimistic replay or writes before approval");
            Check(journal.ApproveRecovery().Sequence == 24, "approval selects verified latest state");
            Check(journal.TryAppend(State(25)), "append baseline follows approved state");
        }
        Write(path, 2, 3);
        using (InventoryJournal journal = new InventoryJournal(path, State(1)))
        {
            Check(journal.RejectRecovery(), "player can isolate abandoned future branch");
            Check(Directory.GetFiles(root, "recovery.journal.rejected-*").Length == 1, "rejected branch retained");
            Check(journal.TryAppend(State(2)), "rejected branch resumes at original snapshot baseline");
        }
    }

    private static void SnapshotConflictsAndCorruptionPreserveEvidence(string root)
    {
        string path = Path.Combine(root, "conflict.journal");
        Write(path, 1);
        InventoryState conflict = State(1);
        conflict.Commits.Add(new InventoryCommit { DepositId = "different", ContentHash = "hash", HashVersion = 3 });
        using (InventoryJournal journal = new InventoryJournal(path, conflict))
            Check(journal.Faulted && journal.FaultReason.Contains("diverged"), "same sequence must retain full content equality");
        string unterminated = Record(State(1)).TrimEnd('\n');
        File.WriteAllText(path, unterminated, new UTF8Encoding(false));
        using (InventoryJournal journal = new InventoryJournal(path, conflict))
            Check(journal.Faulted, "unterminated conflicting record still faults");
        Check(File.ReadAllText(path) == unterminated && Directory.GetFiles(root, "conflict.journal.torn-*").Length == 0,
            "snapshot conflict must be detected before any tail repair");
        string record = Record(State(1));
        File.WriteAllText(path, record.Replace(record.Split('|')[1], "bad-checksum") + Record(State(2)), new UTF8Encoding(false));
        string original = File.ReadAllText(path);
        using (InventoryJournal journal = new InventoryJournal(path, State(2)))
            Check(journal.Faulted, "historical gaps must not bypass checksums");
        Check(File.ReadAllText(path) == original, "corrupt source unchanged");
        File.WriteAllText(path, "2|" + record.Substring(record.IndexOf('|') + 1), new UTF8Encoding(false));
        using (InventoryJournal journal = new InventoryJournal(path, State(2)))
            Check(journal.Faulted, "header sequence and payload sequence must agree");
    }

    private static void AppendsCannotWriteDuplicateOrSkippedVersions(string root)
    {
        string path = Path.Combine(root, "append.journal");
        using (InventoryJournal journal = new InventoryJournal(path, State(0)))
        {
            Check(!journal.TryAppend(State(0)) && !journal.TryAppend(State(2)), "only next version can be appended");
            Check(journal.TryAppend(State(1)), "first version accepted");
            long length = new FileInfo(path).Length;
            Check(!journal.TryAppend(State(1)) && new FileInfo(path).Length == length, "duplicate rejected before writing");
            Check(journal.TryAppend(State(2)), "invalid append cannot prevent valid next version");
        }
        using (InventoryJournal journal = new InventoryJournal(Path.Combine(root, "overflow.journal"), State(long.MaxValue)))
            Check(!journal.TryAppend(State(0)), "sequence exhaustion cannot wrap into a new branch");
    }

    private static void CrashTailsPreserveCompleteRecords(string root)
    {
        string path = Path.Combine(root, "tail.journal");
        foreach (bool complete in new[] { true, false })
        {
            string original = complete ? Record(State(1)).TrimEnd('\n') : Record(State(1)) + "2|partial";
            File.WriteAllText(path, original, new UTF8Encoding(false));
            using (InventoryJournal journal = new InventoryJournal(path, State(1)))
            {
                Check(!journal.Faulted, "crash tail can be isolated without losing verified history");
                Check(File.ReadAllText(path) == Record(State(1)), "complete payload receives boundary; partial tail removed");
                Check(journal.TryAppend(State(2)), "append after tail repair");
            }
            using (InventoryJournal journal = new InventoryJournal(path, State(2)))
                Check(!journal.Faulted, "repaired boundary survives next load");
            Check(Directory.GetFiles(root, "tail.journal.torn-*").Any(p => File.ReadAllText(p) == original), "original tail retained");
        }
    }

    private static InventoryState State(long sequence) => new InventoryState { Sequence = sequence };
    private static void Write(string path, params long[] versions) => File.WriteAllText(path,
        string.Concat(versions.Select(n => Record(State(n)))), new UTF8Encoding(false));
    private static string Record(InventoryState state)
    {
        byte[] payload = Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(state));
        using (SHA256 sha = SHA256.Create())
            return state.Sequence + "|" + Convert.ToBase64String(sha.ComputeHash(payload)) + "|" + Convert.ToBase64String(payload) + "\n";
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Journal assertion failed: " + message);
    }
}
