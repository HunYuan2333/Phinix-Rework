using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Utils.Framework;

namespace Phinix.InventoryExtension.Client
{
    /// <summary>Bounded, per-save write-ahead journal. No entry is replayed without player approval.</summary>
    internal sealed class InventoryJournal : IDisposable
    {
        private const long MaxJournalBytes = 128L * 1024 * 1024;
        private const int MaxRecordBytes = 8 * 1024 * 1024;
        private readonly string path;
        private readonly FileStream lockFile;
        private readonly List<InventoryState> recovered = new List<InventoryState>();

        public bool Faulted { get; private set; }
        public string FaultReason { get; private set; }
        public bool HasPendingRecovery { get; private set; }
        public InventoryState PendingState => HasPendingRecovery ? recovered.LastOrDefault() : null;

        public InventoryJournal(string path, InventoryState snapshot)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            lockFile = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > MaxJournalBytes)
            {
                Fault("Inventory journal exceeds its size limit.");
                return;
            }

            try
            {
                bool endsWithNewline = EndsWithNewline(path);
                bool ignoredTornTail = false;
                using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (StreamReader reader = new StreamReader(input, Encoding.UTF8, false))
                {
                    string line;
                    long lastSequence = -1;
                    while ((line = reader.ReadLine()) != null)
                    {
                        try
                        {
                            if (line.Length == 0 || line.Length > MaxRecordBytes * 2)
                                throw new InvalidDataException("Invalid inventory journal record length.");
                            string[] fields = line.Split('|');
                            if (fields.Length != 3 || !long.TryParse(fields[0], out long sequence) || sequence < 1)
                                throw new InvalidDataException("Invalid inventory journal header.");
                            byte[] bytes = Convert.FromBase64String(fields[2]);
                            if (bytes.Length > MaxRecordBytes || !string.Equals(Hash(bytes), fields[1], StringComparison.Ordinal))
                                throw new InvalidDataException("Inventory journal checksum mismatch.");
                            InventoryState state = FrameworkSerialization.DeserializePayload<InventoryState>(Encoding.UTF8.GetString(bytes));
                            InventoryLedger.Validate(state);
                            if (state.Sequence != sequence || sequence <= lastSequence ||
                                (lastSequence >= 0 && sequence != lastSequence + 1 &&
                                 !(lastSequence < snapshot.Sequence && sequence == snapshot.Sequence + 1)))
                                throw new InvalidDataException("Inventory journal sequence mismatch.");
                            recovered.Add(state);
                            lastSequence = sequence;
                        }
                        catch (Exception) when (!endsWithNewline && reader.Peek() < 0)
                        {
                            ignoredTornTail = true;
                            break;
                        }
                    }
                }
                if (ignoredTornTail)
                    IsolateAndTruncateTornTail(path);
                InventoryState sameSequence = recovered.FirstOrDefault(state => state.Sequence == snapshot.Sequence);
                if (sameSequence != null && !string.Equals(StateHash(sameSequence), StateHash(snapshot), StringComparison.Ordinal))
                    throw new InvalidDataException("Inventory save and journal have diverged.");
                InventoryState firstFuture = recovered.FirstOrDefault(state => state.Sequence > snapshot.Sequence);
                if (firstFuture != null && firstFuture.Sequence != snapshot.Sequence + 1)
                    throw new InvalidDataException("Inventory journal has a gap after this save snapshot.");
                HasPendingRecovery = recovered.Any(state => state.Sequence > snapshot.Sequence);
            }
            catch (Exception ex)
            {
                Fault(ex.Message);
            }
        }

        public bool TryAppend(InventoryState state)
        {
            if (Faulted || HasPendingRecovery || state == null) return false;
            try
            {
                InventoryLedger.Validate(state);
                byte[] payload = Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(state));
                if (payload.Length > MaxRecordBytes) return false;
                string line = state.Sequence + "|" + Hash(payload) + "|" + Convert.ToBase64String(payload) + "\n";
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                if (bytes.Length > MaxRecordBytes * 2) return false;
                long currentLength = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (currentLength + bytes.Length > MaxJournalBytes) return false;
                using (FileStream output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                recovered.Add(state);
                return true;
            }
            catch (Exception)
            {
                Fault("Inventory journal could not be appended.");
                return false;
            }
        }

        public InventoryState ApproveRecovery()
        {
            if (Faulted || !HasPendingRecovery) return null;
            HasPendingRecovery = false;
            return recovered.LastOrDefault();
        }

        public bool RejectRecovery()
        {
            if (Faulted || !HasPendingRecovery) return false;
            try
            {
                if (File.Exists(path))
                {
                    string isolatedPath = path + ".rejected-" + DateTime.UtcNow.Ticks;
                    File.Move(path, isolatedPath);
                }
                recovered.Clear();
                HasPendingRecovery = false;
                return true;
            }
            catch (Exception ex)
            {
                Fault("Inventory recovery branch could not be isolated: " + ex.Message);
                return false;
            }
        }

        private static string StateHash(InventoryState state)
        {
            return Hash(Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(state)));
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private void Fault(string reason)
        {
            Faulted = true;
            HasPendingRecovery = false;
            FaultReason = string.IsNullOrWhiteSpace(reason) ? "Inventory journal is damaged or locked." : reason;
        }

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

        public void Dispose()
        {
            lockFile.Dispose();
        }
    }
}
