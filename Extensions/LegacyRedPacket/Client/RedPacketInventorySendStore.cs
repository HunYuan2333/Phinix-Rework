using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Phinix.InventoryExtension;
using Utils.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal enum RedPacketInventorySendState { Queued, Accepted, Settling, Settled, Uncertain }

    [DataContract]
    internal sealed class RedPacketInventorySendRecord
    {
        [DataMember] public string PacketId { get; set; }
        [DataMember] public string ReservationId { get; set; }
        [DataMember] public string SenderUuid { get; set; }
        [DataMember] public RedPacketInventorySendState State { get; set; }
        [DataMember] public DateTime ExpiresAtUtc { get; set; }
        [DataMember] public RedPacketInventoryPayload Template { get; set; }
        [DataMember] public int TotalCount { get; set; }
        [DataMember] public int RemainingCount { get; set; }
        [DataMember] public int UnsentCount { get; set; }
        [DataMember] public int ReturnMultiplier { get; set; }
        [DataMember] public int ReturnStackLimit { get; set; }
        [DataMember] public bool TriggerSpecialRaid { get; set; }
        [DataMember] public Dictionary<string, int> Claims { get; set; } = new Dictionary<string, int>();

        public RedPacketInventorySendRecord Clone()
        {
            return FrameworkSerialization.DeserializePayload<RedPacketInventorySendRecord>(
                FrameworkSerialization.SerializePayload(this));
        }
    }

    /// <summary>
    /// Sender custody, keyed by a reservation in the current save. It contains
    /// opaque item bytes, never live world objects. Unknown sends are not refunded.
    /// </summary>
    internal sealed class RedPacketInventorySendStore
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private const long MaxStorageBytes = 128L * 1024 * 1024;
        private const int MaxStoredRecords = 2048;
        private readonly Func<string, string> pathProvider;

        public RedPacketInventorySendStore(Func<string, string> pathProvider)
        {
            this.pathProvider = pathProvider;
        }

        public void Save(RedPacketInventorySendRecord record)
        {
            Validate(record);
            string path = PathFor(record.ReservationId);
            byte[] payload = Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(record));
            if (payload.Length > MaxBytes) throw new InvalidDataException("Red packet custody record is too large.");
            byte[] envelope = Encoding.UTF8.GetBytes(Hash(payload) + "\n" + Convert.ToBase64String(payload));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            long storedBytes = envelope.Length;
            int records = 1;
            foreach (string existing in Directory.EnumerateFiles(Path.GetDirectoryName(path), "inventory-send-*.json"))
            {
                if (string.Equals(existing, path, StringComparison.Ordinal)) continue;
                storedBytes += new FileInfo(existing).Length;
                records++;
                if (storedBytes > MaxStorageBytes || records > MaxStoredRecords)
                    throw new IOException("Red packet custody storage has reached its capacity.");
            }
            using (FileStream output = new FileStream(path + ".new", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                output.Write(envelope, 0, envelope.Length);
                output.Flush(true);
            }
            if (File.Exists(path)) File.Replace(path + ".new", path, null);
            else File.Move(path + ".new", path);
        }

        public RedPacketInventorySendRecord Load(string reservationId)
        {
            string path = PathFor(reservationId);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxBytes * 2L)
                throw new InvalidDataException("Red packet custody record exceeds its limit.");
            string[] fields = File.ReadAllText(path, Encoding.UTF8).Split('\n');
            if (fields.Length != 2) throw new InvalidDataException("Invalid red packet custody envelope.");
            byte[] payload = Convert.FromBase64String(fields[1]);
            if (payload.Length > MaxBytes || fields[0] != Hash(payload))
                throw new InvalidDataException("Red packet custody checksum mismatch.");
            RedPacketInventorySendRecord record = FrameworkSerialization.DeserializePayload<RedPacketInventorySendRecord>(
                Encoding.UTF8.GetString(payload));
            Validate(record);
            if (record.ReservationId != reservationId)
                throw new InvalidDataException("Red packet custody belongs to another reservation.");
            return record;
        }

        private string PathFor(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 100)
                throw new InvalidDataException("Invalid reservation ID.");
            string safeName;
            using (SHA256 sha = SHA256.Create())
                safeName = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "");
            string path = pathProvider?.Invoke("inventory-send-" + safeName + ".json");
            if (string.IsNullOrEmpty(path)) throw new IOException("Red packet extension storage is unavailable.");
            return path;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private static void Validate(RedPacketInventorySendRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.PacketId) || record.PacketId.Length > 100 ||
                string.IsNullOrEmpty(record.ReservationId) || record.ReservationId.Length > 100 ||
                string.IsNullOrEmpty(record.SenderUuid) || record.SenderUuid.Length > 100 ||
                record.Template == null || record.TotalCount < 1 || record.RemainingCount < 0 ||
                record.RemainingCount > record.TotalCount || record.UnsentCount < 0 ||
                record.ReturnStackLimit < 1 || record.Template.StackCount != (long)record.TotalCount + record.UnsentCount ||
                record.ReturnMultiplier < 0 || record.ReturnMultiplier > 2 ||
                !Enum.IsDefined(typeof(RedPacketInventorySendState), record.State) ||
                record.Claims == null || record.Claims.Count > RedPacketLimits.MaxClaimDetailsPerPacket)
                throw new InvalidDataException("Invalid red packet custody record.");
            long claimed = 0;
            foreach (KeyValuePair<string, int> claim in record.Claims)
            {
                if (string.IsNullOrEmpty(claim.Key) || claim.Key.Length > 100 || claim.Value < 1)
                    throw new InvalidDataException("Invalid red packet custody claim.");
                claimed += claim.Value;
            }
            if (claimed != (long)record.TotalCount - record.RemainingCount)
                throw new InvalidDataException("Red packet custody balance mismatch.");
        }
    }
}
