using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Utils.Framework;

namespace Phinix.InventoryExtension.Client
{
    /// <summary>Legacy-format write-ahead journal; recovery always requires explicit approval.</summary>
    internal sealed class InventoryJournal : IDisposable
    {
        private const long MaxJournalBytes = 128L * 1024 * 1024;
        // Migration scan bound, not a larger write budget. Existing over-limit files can be checkpointed.
        private const long MaxRecoveryBytes = 1024L * 1024 * 1024;
        private const int MaxRecordBytes = 8 * 1024 * 1024;
        private readonly string path;
        private readonly FileStream lockFile;
        private readonly object gate = new object();
        private readonly Action<string> diagnostic;
        private readonly Action<string> barrier;
        private InventoryState latest;
        private long committedSequence;
        private bool disposed, capacityFault;
        private readonly long snapshotSequence;
        private readonly string snapshotOwnership;
        private readonly string snapshotFingerprint;
        private bool recoveryOwnershipUnchanged = true;
        public string Identity { get; private set; }
        public bool CanRejectRecovery => HasPendingRecovery && recoveryOwnershipUnchanged;
        private static string OwnershipHash(InventoryState state)
        { var copy = FrameworkSerialization.DeserializePayload<InventoryState>(FrameworkSerialization.SerializePayload(state)); copy.Sequence = 0; return Fingerprint(copy); }
        public bool Faulted { get; private set; }
        public string FaultReason { get; private set; }
        public string LastError { get; private set; }
        public string CheckpointError { get; private set; }
        internal long LastCheckpointBytesReclaimed { get; private set; }
        public bool HasPendingRecovery { get; private set; }
        public InventoryState PendingState => HasPendingRecovery ? latest : null;

        public InventoryJournal(string path, InventoryState snapshot) : this(path, snapshot, null, null, null) { }
        public InventoryJournal(string path, InventoryState snapshot, string expectedIdentity) : this(path, snapshot, expectedIdentity, null, null) { }
        public InventoryJournal(string path, InventoryState snapshot, string expectedIdentity, Action<string> diagnostic) : this(path, snapshot, expectedIdentity, diagnostic, null) { }
        internal InventoryJournal(string path, InventoryState snapshot, Action<string> diagnostic, Action<string> barrier) : this(path, snapshot, null, diagnostic, barrier) { }
        private InventoryJournal(string path, InventoryState snapshot, string expectedIdentity, Action<string> diagnostic, Action<string> barrier, bool savedBranch = false)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.diagnostic = diagnostic; this.barrier = barrier;
            InventoryLedger.Validate(snapshot); committedSequence = snapshot.Sequence; snapshotSequence = snapshot.Sequence; snapshotOwnership = OwnershipHash(snapshot); snapshotFingerprint = Fingerprint(snapshot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            lockFile = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try
            {
                string marker = path + ".authority";
                if (File.Exists(marker))
                {
                    if (new FileInfo(marker).Length != 32) throw new InvalidDataException("Invalid inventory journal authority.");
                    Identity = File.ReadAllText(marker, Encoding.ASCII);
                    if (!Guid.TryParseExact(Identity, "N", out _) || (expectedIdentity != null && Identity != expectedIdentity) || !File.Exists(path))
                        throw new InvalidDataException("Expected inventory journal authority is missing or changed.");
                }
                else
                {
                    if (expectedIdentity != null && (!savedBranch || File.Exists(path)))
                        throw new InvalidDataException("Expected inventory journal marker is missing.");
                    Identity = expectedIdentity ?? Guid.NewGuid().ToString("N");
                    if (!File.Exists(path)) using (var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) created.Flush(true);
                    using (var authority = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { byte[] bytes = Encoding.ASCII.GetBytes(Identity); authority.Write(bytes, 0, bytes.Length); authority.Flush(true); }
                }
                if (new FileInfo(path).Length > MaxRecoveryBytes) throw new InvalidDataException("Recovery scan exceeds 1 GiB limit; retain file for offline recovery.");
                bool endsWithNewline = EndsWithNewline(path), ignoredTornTail = false;
                long lastSequence = -1; bool sawFuture = false;
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(input, Encoding.UTF8, false))
                {
                    string line;
                    while ((line = ReadLine(reader)) != null)
                    {
                        try
                        {
                            var state = Parse(line);
                            ValidateSequence(state.Sequence, lastSequence, snapshot.Sequence);
                            if (state.Sequence == snapshot.Sequence && Fingerprint(state) != Fingerprint(snapshot))
                                throw new InvalidDataException("Inventory save and journal have diverged.");
                            if (!sawFuture && state.Sequence > snapshot.Sequence)
                            { if (state.Sequence != snapshot.Sequence + 1) throw new InvalidDataException("Inventory journal has a gap after this save snapshot."); sawFuture = true; }
                            if (state.Sequence > snapshotSequence && OwnershipHash(state) != snapshotOwnership) recoveryOwnershipUnchanged = false;
                            latest = state; lastSequence = state.Sequence;
                        }
                        catch (Exception) when (!endsWithNewline && reader.Peek() < 0)
                        {
                            // A complete, conflicting record is evidence, never a torn-write exception.
                            InventoryState candidate = null;
                            try { candidate = Parse(line); } catch { }
                            if (candidate != null) throw;
                            ignoredTornTail = true; break;
                        }
                    }
                }
                if (ignoredTornTail) IsolateAndTruncateTornTail(path);
                else if (!endsWithNewline) CompleteRecordBoundary(path);
                HasPendingRecovery = sawFuture;
            }
            catch (Exception error) { Fault("Load", error.GetType().Name + ": " + SafeReason(error)); }
        }

        // Only a live, healthy attachment may create a new path-scoped journal after Save As.
        // A load with missing local evidence continues to fail closed in the ordinary constructor.
        internal InventoryJournal OpenSavedBranch(string target, InventoryState snapshot, string saveFile,
            string saveId, string expectedIdentity, Action<string> report)
        {
            lock (gate)
            {
                if (disposed || Faulted || HasPendingRecovery || expectedIdentity != Identity ||
                    snapshot.Sequence != committedSequence || Fingerprint(snapshot) !=
                    (latest != null && latest.Sequence == committedSequence ? Fingerprint(latest) : snapshotFingerprint))
                    throw new InvalidDataException("Live inventory journal cannot authorize a saved branch.");
                if (!File.Exists(path) || !File.Exists(path + ".authority") ||
                    new FileInfo(path + ".authority").Length != 32 ||
                    File.ReadAllText(path + ".authority", Encoding.ASCII) != Identity)
                    throw new InvalidDataException("Live inventory journal evidence is missing or changed.");
                if (Path.GetFullPath(target) == Path.GetFullPath(path))
                    throw new InvalidDataException("Saved branch requires a different journal path.");
                using (InventorySaveCheckpoint.Open(saveFile, saveId, snapshot, Identity))
                    return new InventoryJournal(target, snapshot, Identity, report, null, true);
            }
        }

        private static string ReadLine(StreamReader reader)
        {
            var text = new StringBuilder(); int value;
            while ((value = reader.Read()) >= 0 && value != '\n')
            { if (text.Length >= MaxRecordBytes * 2) throw new InvalidDataException("Encoded journal record exceeds limit."); text.Append((char)value); }
            if (value < 0 && text.Length == 0) return null;
            if (text.Length > 0 && text[text.Length - 1] == '\r') text.Length--;
            return text.ToString();
        }
        private static InventoryState Parse(string line)
        {
            if (line.Length == 0 || line.Length > MaxRecordBytes * 2) throw new InvalidDataException("Invalid inventory journal record length.");
            string[] fields = line.Split('|');
            if (fields.Length != 3 || !long.TryParse(fields[0], out long sequence) || sequence < 1) throw new InvalidDataException("Invalid inventory journal header.");
            byte[] bytes = Convert.FromBase64String(fields[2]);
            if (bytes.Length > MaxRecordBytes || Hash(bytes) != fields[1]) throw new InvalidDataException("Inventory journal checksum mismatch.");
            var state = FrameworkSerialization.DeserializePayload<InventoryState>(Encoding.UTF8.GetString(bytes));
            InventoryLedger.Validate(state);
            if (state.Sequence != sequence) throw new InvalidDataException("Inventory journal sequence mismatch.");
            return state;
        }
        private static void ValidateSequence(long sequence, long previous, long snapshot)
        {
            if (sequence <= previous || (previous >= 0 && sequence > snapshot && sequence != previous + 1 &&
                !(previous < snapshot && sequence == snapshot + 1))) throw new InvalidDataException("Inventory journal sequence mismatch.");
        }
        private static byte[] Encode(InventoryState state)
        {
            byte[] payload = Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(state));
            if (payload.Length > MaxRecordBytes) throw new InvalidDataException("Payload exceeds 8 MiB limit.");
            byte[] encoded = Encoding.UTF8.GetBytes(state.Sequence + "|" + Hash(payload) + "|" + Convert.ToBase64String(payload) + "\n");
            if (encoded.Length > MaxRecordBytes * 2) throw new InvalidDataException("Encoded record exceeds 16 MiB limit.");
            return encoded;
        }
        public bool TryAppend(InventoryState state)
        {
            lock (gate)
            {
                if (Faulted) { LastError = FaultReason; return false; }
                if (disposed) return Reject("Append", "Journal is closed.", true);
                if (HasPendingRecovery) return Reject("Append", "Recovery requires approval.", false);
                if (state == null || committedSequence == long.MaxValue || state.Sequence != committedSequence + 1)
                    return Reject("Append", "Unexpected sequence; expected next after " + committedSequence + ".", false);
                try
                {
                    InventoryLedger.Validate(state); byte[] bytes = Encode(state);
                    long length = File.Exists(path) ? new FileInfo(path).Length : 0;
                    if (length + bytes.Length > MaxJournalBytes)
                    { capacityFault = true; return Reject("Append", "Capacity exceeded; bytes=" + length + ", recordBytes=" + bytes.Length + ", limit=" + MaxJournalBytes + ". Save to checkpoint before retrying.", true); }
                    using (var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                    latest = state; committedSequence = state.Sequence; LastError = null; return true;
                }
                catch (Exception error) { return Reject("Append", error.GetType().Name + ": " + SafeReason(error), true); }
            }
        }

        internal bool TryCheckpoint(InventorySaveCheckpoint checkpoint)
        {
            lock (gate)
            {
                if (disposed || checkpoint == null || Faulted && !capacityFault) return false;
                LastCheckpointBytesReclaimed = 0;
                try
                {
                    long originalLength = File.Exists(path) ? new FileInfo(path).Length : 0;
                    long replacementLength;
                    var saved = checkpoint.State;
                    if (saved.Sequence > committedSequence) throw new InvalidDataException("Checkpoint is ahead of the live ledger.");
                    string temp = path + ".compact";
                    // Do not select a stale .compact at startup. Only validated source + actual save can replace it.
                    using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        if (saved.Sequence > 0) { byte[] anchor = Encode(saved); output.Write(anchor, 0, anchor.Length); }
                        if (File.Exists(path))
                        {
                            long previous = -1; bool future = false;
                            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var reader = new StreamReader(input, Encoding.UTF8, false))
                            {
                                string line;
                                while ((line = ReadLine(reader)) != null)
                                {
                                    var state = Parse(line); ValidateSequence(state.Sequence, previous, saved.Sequence); previous = state.Sequence;
                                    if (state.Sequence == saved.Sequence && Fingerprint(state) != Fingerprint(saved)) throw new InvalidDataException("Checkpoint and journal have diverged.");
                                    if (state.Sequence <= saved.Sequence) continue;
                                    if (!future && state.Sequence != saved.Sequence + 1) throw new InvalidDataException("Checkpoint future records have a gap.");
                                    future = true; byte[] raw = Encoding.UTF8.GetBytes(line + "\n"); output.Write(raw, 0, raw.Length);
                                }
                            }
                        }
                        barrier?.Invoke("inventory.compact.before-flush"); output.Flush(true);
                        replacementLength = output.Length;
                    }
                    barrier?.Invoke("inventory.compact.before-replace");
                    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                    barrier?.Invoke("inventory.compact.after-replace");
                    CheckpointError = null;
                    LastCheckpointBytesReclaimed = Math.Max(0, originalLength - replacementLength);
                    if (capacityFault && new FileInfo(path).Length < MaxJournalBytes)
                    { capacityFault = false; Faulted = false; FaultReason = null; LastError = null; }
                    return true;
                }
                catch (Exception error)
                {
                    CheckpointError = "Checkpoint: " + error.GetType().Name + ": " + SafeReason(error);
                    Report(CheckpointError); return false;
                }
            }
        }
        public InventoryState ApproveRecovery()
        {
            lock (gate) { if (disposed || Faulted || !HasPendingRecovery) return null; HasPendingRecovery = false; committedSequence = latest.Sequence; return latest; }
        }
        public bool RejectRecovery()
        {
            lock (gate)
            {
                if (disposed || Faulted || !CanRejectRecovery) return false;
                try
                {
                    if (File.Exists(path)) File.Move(path, path + ".rejected-" + DateTime.UtcNow.Ticks);
                    using (var empty = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) empty.Flush(true);
                    latest = null; HasPendingRecovery = false; return true;
                }
                catch (Exception error) { Fault("RejectRecovery", error.GetType().Name + ": " + SafeReason(error)); return false; }
            }
        }
        internal static string Fingerprint(InventoryState state) => Hash(Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(state)));
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        private bool Reject(string phase, string reason, bool persistenceFault)
        { LastError = phase + ": " + reason; if (persistenceFault) Fault(phase, reason); else Report(LastError); return false; }
        private void Fault(string phase, string reason) { Faulted = true; FaultReason = LastError = phase + ": " + reason; Report(FaultReason); }
        private void Report(string message)
        {
            try { diagnostic?.Invoke("journal=" + Path.GetFileName(path) + ", sequence=" + committedSequence + ", " + message); }
            catch (Exception) { /* A diagnostic observer cannot change persistence or recovery outcomes. */ }
        }
        // Exception messages from serializers/IO can include raw paths or payloads; report only owned validation messages.
        private static string SafeReason(Exception error) => error is InvalidDataException ? error.Message : "Persistence operation failed; code=" + error.HResult.ToString("X8") + "; original evidence retained.";
        private static bool EndsWithNewline(string journalPath)
        {
            using (FileStream input = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length == 0) return true;
                input.Position = input.Length - 1;
                return input.ReadByte() == (byte)'\n';
            }
        }

        private static void IsolateAndTruncateTornTail(string journalPath)
        {
            string isolatedPath = journalPath + ".torn-" + DateTime.UtcNow.Ticks;
            File.Copy(journalPath, isolatedPath, false);
            using (FileStream file = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                byte[] buffer = new byte[8192];
                long position = file.Length;
                long validLength = 0;
                while (position > 0)
                {
                    int count = (int)Math.Min(buffer.Length, position);
                    position -= count;
                    file.Position = position;
                    int read = file.Read(buffer, 0, count);
                    for (int index = read - 1; index >= 0; index--)
                    {
                        if (buffer[index] != (byte)'\n') continue;
                        validLength = position + index + 1;
                        position = 0;
                        break;
                    }
                }
                file.SetLength(validLength);
                file.Flush(true);
            }
        }

        private static void CompleteRecordBoundary(string journalPath)
        {
            // A crash can leave the entire verified payload but omit its newline.
            // Preserve it and add the separator before any future append.
            File.Copy(journalPath, journalPath + ".torn-" + DateTime.UtcNow.Ticks, false);
            using (FileStream file = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                file.WriteByte((byte)'\n');
                file.Flush(true);
            }
        }

        public void Dispose() { lock (gate) { if (disposed) return; disposed = true; lockFile.Dispose(); latest = null; } }
    }
}
