using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhinixClient.Storage;
using Utils.Framework;
using PhinixClient.Framework;

namespace Phinix.InventoryExtension.Client
{
    // Database persistence belongs to Inventory; the shared service knows none of these tables.
    internal sealed class InventoryDatabaseStore : IDisposable
    {
        private readonly IClientDatabase database;
        private InventoryState committed;
        private string gameId;
        internal string Identity => database.Identity;
        internal bool Faulted { get; private set; }
        internal string FaultReason { get; private set; }
        internal string LastError { get; private set; }
        private Exception lastFailure;
        internal static readonly DatabaseSchema Schema = new DatabaseSchema("builtin.inventory", new DatabaseMigration(1, "inventory-v1", @"
CREATE TABLE inventory_meta(id INTEGER PRIMARY KEY CHECK(id=1),revision INTEGER NOT NULL CHECK(revision>=0),pending TEXT);
INSERT INTO inventory_meta VALUES(1,0,NULL);
CREATE TABLE inventory_entries(id TEXT PRIMARY KEY,metadata TEXT NOT NULL,payload BLOB NOT NULL);
CREATE TABLE inventory_receipts(id TEXT PRIMARY KEY,body TEXT NOT NULL);
CREATE TABLE inventory_reservations(id TEXT PRIMARY KEY,body TEXT NOT NULL);
CREATE TABLE inventory_schedules(game_id TEXT NOT NULL,entry_id TEXT NOT NULL REFERENCES inventory_entries(id) ON DELETE CASCADE,enabled INTEGER NOT NULL,body TEXT NOT NULL,PRIMARY KEY(game_id,entry_id));
CREATE TABLE inventory_imports(source_id TEXT PRIMARY KEY,fingerprint TEXT NOT NULL,source_sequence INTEGER NOT NULL,revision INTEGER NOT NULL);
"));
        private readonly Action<string,Exception> report;
        internal InventoryDatabaseStore(IClientDatabaseService service) : this(service,null) { }
        internal InventoryDatabaseStore(IClientDatabaseService service,Action<string,Exception> report)
        { this.report=report;database = service.Open("builtin.inventory", "inventory", Schema); }

        internal InventoryState Load(string sourceGame)
        {
            gameId = sourceGame;
            committed = database.Read((connection, transaction) => Read(connection, transaction));
            Faulted=false;FaultReason=null;LastError=null;lastFailure=null;
            return Clone(committed);
        }
        private InventoryState Read(DbConnection connection, DbTransaction transaction)
        {
            var state = new InventoryState();
            using (var command = DatabaseSql.Command(connection, transaction, "SELECT revision,pending FROM inventory_meta WHERE id=1"))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) throw new InvalidDataException("Inventory database metadata is missing.");
                state.Sequence = reader.GetInt64(0);
                state.PendingExtraction = reader.IsDBNull(1) ? null : Decode<InventoryPendingExtraction>(reader.GetString(1));
            }
            using (var command = DatabaseSql.Command(connection, transaction, "SELECT metadata,payload FROM inventory_entries ORDER BY id"))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) { var entry = Decode<InventoryEntry>(reader.GetString(0)); entry.Payload = (byte[])reader[1]; state.Entries.Add(entry); }
            state.Commits = Rows<InventoryCommit>(connection, transaction, "SELECT body FROM inventory_receipts ORDER BY id");
            state.Reservations = Rows<InventoryReservationRecord>(connection, transaction, "SELECT body FROM inventory_reservations ORDER BY id");
            state.Schedules = Rows<InventorySchedule>(connection, transaction, "SELECT body FROM inventory_schedules WHERE game_id=$game AND enabled=1", "$game", gameId);
            InventoryLedger.Validate(state); return state;
        }
        internal bool TryAppend(InventoryState next)
        {
            if (Faulted) { LastError = FaultReason; return false; }
            try
            {
                InventoryLedger.Validate(next);
                if (committed == null || next.Sequence != committed.Sequence + 1) throw new InvalidOperationException("Inventory revision is not the next version.");
                database.Write((connection, transaction) => { Save(connection, transaction, committed, next); return true; });
                committed = Clone(next); LastError = null; lastFailure=null;return true;
            }
            catch (ClientDatabaseException error)
            {
                lastFailure=error;
                ClientErrorReporting.DeliverSensitive(report,"InventoryAppend"+error.Failure,error);
                LastError = error.Failure==DatabaseFailure.OutcomeUnknown?"Inventory commit outcome is unknown.":error.Failure==DatabaseFailure.Corrupt?"Inventory data is damaged; existing data was retained.":"Inventory storage is temporarily unavailable.";
                if (error.Failure == DatabaseFailure.OutcomeUnknown || error.Failure == DatabaseFailure.Corrupt)
                { Faulted = true; FaultReason = LastError; }
                return false;
            }
            catch (Exception error) { lastFailure=error;ClientErrorReporting.DeliverSensitive(report,"InventoryAppendFailed",error);LastError="Inventory changes could not be saved.";return false; }
        }
        private void Save(DbConnection connection, DbTransaction transaction, InventoryState before, InventoryState next)
        {
            int changed = DatabaseSql.Execute(connection, transaction, "UPDATE inventory_meta SET revision=$next,pending=$pending WHERE id=1 AND revision=$old",
                "$next", next.Sequence, "$old", before.Sequence, "$pending", next.PendingExtraction == null ? null : Json(next.PendingExtraction));
            if (changed != 1) throw new InvalidOperationException("Inventory changed in another process; reopen before retrying.");
            var prior = before.Entries.ToDictionary(entry => entry.EntryId, StringComparer.Ordinal);
            foreach (var entry in next.Entries)
            {
                if (prior.TryGetValue(entry.EntryId, out var old) && EntryMetadata(old) == EntryMetadata(entry) && old.Payload.SequenceEqual(entry.Payload)) continue;
                DatabaseSql.Execute(connection, transaction, "INSERT INTO inventory_entries VALUES($id,$meta,$payload) ON CONFLICT(id) DO UPDATE SET metadata=excluded.metadata,payload=excluded.payload",
                    "$id", entry.EntryId, "$meta", EntryMetadata(entry), "$payload", entry.Payload);
            }
            var ids = new HashSet<string>(next.Entries.Select(entry => entry.EntryId), StringComparer.Ordinal);
            foreach (string id in prior.Keys.Where(id => !ids.Contains(id))) DatabaseSql.Execute(connection, transaction, "DELETE FROM inventory_entries WHERE id=$id", "$id", id);
            SaveRows(connection, transaction, "inventory_receipts", before.Commits, next.Commits, item => item.DepositId);
            SaveRows(connection, transaction, "inventory_reservations", before.Reservations, next.Reservations, item => item.ReservationId);
            var schedules = before.Schedules.ToDictionary(item => item.EntryId, StringComparer.Ordinal);
            foreach (var item in next.Schedules)
            {
                if (schedules.TryGetValue(item.EntryId, out var old) && Json(old) == Json(item)) continue;
                DatabaseSql.Execute(connection, transaction, "INSERT INTO inventory_schedules VALUES($game,$id,1,$body) ON CONFLICT(game_id,entry_id) DO UPDATE SET enabled=1,body=excluded.body",
                    "$game", gameId, "$id", item.EntryId, "$body", Json(item));
            }
            var scheduled = new HashSet<string>(next.Schedules.Select(item => item.EntryId), StringComparer.Ordinal);
            foreach (string id in schedules.Keys.Where(id => !scheduled.Contains(id))) DatabaseSql.Execute(connection, transaction, "DELETE FROM inventory_schedules WHERE game_id=$game AND entry_id=$id", "$game", gameId, "$id", id);
        }

        // One claim per stable save lineage. Paths and snapshot versions cannot create extra balances.
        // A conflicting independent source is retained in the save, rather than guessing a merge.
        internal bool Import(string sourceId, InventoryState legacy, out string warning)
            => Import(sourceId, legacy, out warning, out _);

        internal bool Import(string sourceId, InventoryState legacy, out string warning, out bool firstImport)
        {
            InventoryLedger.Validate(legacy); string fingerprint = Fingerprint(legacy), note = null; bool added = false;
            bool imported = database.Write((connection, transaction) =>
            {
                var existing = DatabaseSql.Scalar(connection, transaction, "SELECT fingerprint FROM inventory_imports WHERE source_id=$source", "$source", sourceId) as string;
                if (existing != null)
                {
                    if (existing != fingerprint) note = "Phinix_inventory_legacyBranchSkipped";
                    return true;
                }
                var current = Read(connection, transaction);
                bool hasHistory = legacy.Entries.Count != 0 || legacy.Commits.Count != 0 || legacy.Reservations.Count != 0 || legacy.PendingExtraction != null;
                if (hasHistory && (legacy.Entries.Any(entry => current.Entries.Any(item => item.EntryId == entry.EntryId)) ||
                    legacy.Commits.Any(receipt => current.Commits.Any(item => item.DepositId == receipt.DepositId)) ||
                    legacy.Reservations.Any(reservation => current.Reservations.Any(item => item.ReservationId == reservation.ReservationId || item.OperationId == reservation.OperationId))))
                { note = "Phinix_inventory_legacyImportConflict"; return false; }
                var next = Clone(current);
                next.Entries.AddRange(legacy.Entries.Select(item => item.Clone()));
                next.Commits.AddRange(legacy.Commits);
                next.Reservations.AddRange(legacy.Reservations);
                foreach(var reservation in next.Reservations.Where(item=>legacy.Reservations.Contains(item) && (item.State==InventoryReservationState.Reserved||item.State==InventoryReservationState.Uncertain)))
                { reservation.State=InventoryReservationState.Uncertain; reservation.Evidence="legacy-import; local outcome not recovered"; }
                // Unfinished physical extraction is abandoned, never replayed into a different game.
                if (legacy.PendingExtraction != null)
                {
                    var pending = legacy.PendingExtraction;
                    var entry = next.Entries.Single(item => item.EntryId == pending.EntryId);
                    entry.Quantity -= pending.Quantity; if (entry.Quantity == 0) next.Entries.Remove(entry);
                    note = "Phinix_inventory_interruptedAbandoned";
                }
                next.Sequence = checked(current.Sequence + 1);
                InventoryLedger.Validate(next); Save(connection, transaction, current, next);
                DatabaseSql.Execute(connection, transaction, "INSERT INTO inventory_imports VALUES($source,$hash,$sequence,$revision)",
                    "$source", sourceId, "$hash", fingerprint, "$sequence", legacy.Sequence, "$revision", next.Sequence);
                // Save historical plans paused, so a new world's clock cannot silently replay them.
                foreach (var schedule in legacy.Schedules.Where(item => next.Entries.Any(entry => entry.EntryId == item.EntryId)))
                    DatabaseSql.Execute(connection, transaction, "INSERT INTO inventory_schedules VALUES($game,$id,0,$body)", "$game", sourceId, "$id", schedule.EntryId, "$body", Json(schedule));
                added = true;
                return true;
            });
            warning = note; firstImport = added; committed = null; return imported;
        }
        internal bool AbandonInterruptedExtraction()
        {
            if (committed?.PendingExtraction == null) return false;
            var ledger = new InventoryLedger(); ledger.Restore(committed);
            if (!TryAppend(ledger.PrepareExtractionResolution(true))) throw new IOException(LastError,lastFailure);
            return true;
        }
        internal string DepositFingerprint(InventoryDeposit deposit)
        {
            return database.Read((connection, transaction) =>
            {
                var body = DatabaseSql.Scalar(connection, transaction, "SELECT body FROM inventory_receipts WHERE id=$id", "$id", deposit.DepositId) as string;
                return body == null ? null : Hash(Identity + "\n" + body);
            });
        }
        private static List<T> Rows<T>(DbConnection connection, DbTransaction transaction, string sql, params object[] args)
        {
            var rows = new List<T>(); using (var command = DatabaseSql.Command(connection, transaction, sql, args))
            using (var reader = command.ExecuteReader()) while (reader.Read()) rows.Add(Decode<T>(reader.GetString(0))); return rows;
        }
        private static void SaveRows<T>(DbConnection connection, DbTransaction transaction, string table, IEnumerable<T> before, IEnumerable<T> after, Func<T, string> id)
        {
            var old = before.ToDictionary(id, Json, StringComparer.Ordinal);
            foreach (var item in after)
            {
                var json = Json(item); if (old.TryGetValue(id(item), out var previous) && previous == json) continue;
                DatabaseSql.Execute(connection, transaction, "INSERT INTO " + table + " VALUES($id,$body) ON CONFLICT(id) DO UPDATE SET body=excluded.body", "$id", id(item), "$body", json);
            }
        }
        private static string EntryMetadata(InventoryEntry entry) => Json(new InventoryEntry {
            EntryId=entry.EntryId,CodecId=entry.CodecId,CodecVersion=entry.CodecVersion,Quantity=entry.Quantity,
            Label=entry.Label,Source=entry.Source,OriginKind=entry.OriginKind,OriginId=entry.OriginId,
            OriginUserId=entry.OriginUserId,OriginDisplayName=entry.OriginDisplayName,AggregationKey=entry.AggregationKey,Payload=Array.Empty<byte>() });
        private static InventoryState Clone(InventoryState state) => InventoryLedger.Clone(state);
        private static string Json<T>(T value) => FrameworkSerialization.SerializePayload(value);
        private static T Decode<T>(string json) => FrameworkSerialization.DeserializePayload<T>(json);
        internal static string Fingerprint(InventoryState state) => Hash(Json(state));
        internal static string LegacySourceIdentity(string json) => "legacy-" + Hash(json);
        private static string Hash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public void Dispose() => database.Dispose();
    }
}
