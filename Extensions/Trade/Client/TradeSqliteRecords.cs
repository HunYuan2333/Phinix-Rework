using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PhinixClient.Storage;
using Utils.Framework;
using PhinixClient.Framework;
using System.Runtime.ExceptionServices;

namespace Phinix.TradeExtension.Client
{
    // Trade-owned append-once records: a row is one operation/evidence item, never a full ledger snapshot.
    // The indexed scope and subject avoid scanning other users' payloads for ordinary admission.
    internal sealed class TradeSqliteRecords : IDisposable
    {
        private readonly IClientDatabase database;
        private readonly string bucket, directory;
        private readonly Action<string> warning;
        private static readonly DatabaseSchema Schema = new DatabaseSchema("builtin.trade", new DatabaseMigration(1, "trade-records-v1", @"
CREATE TABLE trade_records(bucket TEXT NOT NULL,name TEXT NOT NULL,kind TEXT NOT NULL,scope TEXT,subject TEXT,body BLOB NOT NULL,checksum TEXT NOT NULL,problem TEXT,PRIMARY KEY(bucket,name));
CREATE INDEX trade_scope ON trade_records(bucket,kind,scope);
CREATE INDEX trade_subject ON trade_records(bucket,kind,subject);
CREATE TABLE trade_legacy_imports(bucket TEXT NOT NULL,name TEXT NOT NULL,checksum TEXT,problem TEXT,PRIMARY KEY(bucket,name));
CREATE TABLE trade_terminal(bucket TEXT NOT NULL,name TEXT NOT NULL,checksum TEXT NOT NULL,PRIMARY KEY(bucket,name));
"));
        private readonly Action<string,Exception> report;
        internal TradeSqliteRecords(IClientDatabaseService service, string directory, string bucket, Action<string> warning)
            :this(service,directory,bucket,warning,null) { }
        internal TradeSqliteRecords(IClientDatabaseService service, string directory, string bucket, Action<string> warning,Action<string,Exception> report)
        {
            this.report=report;
            if (service == null) throw new ArgumentNullException(nameof(service));
            this.directory = directory; this.bucket = bucket; this.warning = warning;
            database = service.Open("builtin.trade", "trade", Schema);
            // Historical files remain as migration evidence; failures affect one row and do not stop activation.
            try {
            if (Directory.Exists(directory))
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    string kind = Path.GetExtension(file);
                    if (Array.IndexOf(new[] { ".intent", ".receipt", ".attempt", ".result", ".query", ".reservation", ".binding", ".plan", ".delivery", ".checkpoint" }, kind) < 0) continue;
                    string name = Path.GetFileName(file);
                    if (database.Read((c,t) => DatabaseSql.Scalar(c,t,"SELECT 1 FROM trade_legacy_imports WHERE bucket=$b AND name=$n","$b",bucket,"$n",name)) != null) continue;
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 64L*1024*1024) throw new InvalidDataException("Unsupported historical record.");
                        byte[] body = File.ReadAllBytes(file); Put(file, body);
                        database.Write((c,t) => DatabaseSql.Execute(c,t,"INSERT OR IGNORE INTO trade_legacy_imports VALUES($b,$n,$h,NULL)","$b",bucket,"$n",name,"$h",Hash(body)));
                    }
                    catch (Exception error)
                    {
                        // Storage failures must not masquerade as successful migration.
                        Report("TradeLegacyImportFailed",error);
                        if (error is ClientDatabaseException) throw;
                        try { database.Write((c,t) => DatabaseSql.Execute(c,t,"INSERT OR IGNORE INTO trade_legacy_imports VALUES($b,$n,NULL,$p)","$b",bucket,"$n",name,"$p",error.GetType().Name)); }
                        catch(Exception marking) {Report("TradeLegacyImportMarkFailed",marking);throw new AggregateException("Historical trade import and failure marking failed.",error,marking);}
                        WriteWarning("Historical trade record could not be imported; its file is retained. " + error.GetType().Name);
                    }
                }
            }catch(Exception error){
                Report("TradeRecordsInitializationFailed",error);
                try{global::Phinix.TradeExtension.Client.TradeDiagnostics.Cleanup("TradeSqliteRecords/TradeSqliteRecords/Cleanup", () => { database.Dispose(); });}catch(Exception cleanup){Report("TradeRecordsInitializationCleanupFailed",cleanup);}
                throw;
            }
        }
        private void Report(string operation,Exception error)=>ClientErrorReporting.DeliverSensitive(report,operation,error);
        private void WriteWarning(string message)
        {try{warning?.Invoke(message);}catch(Exception error){Report("TradeRecordWarningObserverFailed",error);}}
        internal bool Exists(string path) => database.Read((c,t) => DatabaseSql.Scalar(c,t,"SELECT 1 FROM trade_records WHERE bucket=$b AND name=$n UNION ALL SELECT 1 FROM trade_terminal WHERE bucket=$b AND name=$n LIMIT 1","$b",bucket,"$n",Path.GetFileName(path)) != null);
        internal bool WasTerminal(string path,byte[] body)
        {
            string hash=database.Read((c,t)=>DatabaseSql.Scalar(c,t,"SELECT checksum FROM trade_terminal WHERE bucket=$b AND name=$n","$b",bucket,"$n",Path.GetFileName(path)) as string);
            if(hash==null)return false;
            if(hash!=Hash(body))throw new InvalidDataException("Completed trade operation was reused with different content.");
            return true;
        }
        internal bool IsTerminal(string path)=>database.Read((c,t)=>DatabaseSql.Scalar(c,t,"SELECT 1 FROM trade_terminal WHERE bucket=$b AND name=$n","$b",bucket,"$n",Path.GetFileName(path))!=null);
        internal void Archive(string path)
        {
            string prefix=Path.GetFileNameWithoutExtension(path)+".%";
            database.Write((c,t)=>{
                DatabaseSql.Execute(c,t,"INSERT OR IGNORE INTO trade_terminal SELECT bucket,name,checksum FROM trade_records WHERE bucket=$b AND name LIKE $prefix","$b",bucket,"$prefix",prefix);
                return DatabaseSql.Execute(c,t,"DELETE FROM trade_records WHERE bucket=$b AND name LIKE $prefix","$b",bucket,"$prefix",prefix);
            });
        }
        internal byte[] Read(string path)
        {
            return database.Read((c,t) =>
            {
                using (var command = DatabaseSql.Command(c,t,"SELECT body,checksum,problem FROM trade_records WHERE bucket=$b AND name=$n","$b",bucket,"$n",Path.GetFileName(path)))
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new FileNotFoundException("Trade record is unavailable.");
                    var body = (byte[])reader[0];
                    if (!reader.IsDBNull(2) || Hash(body) != reader.GetString(1)) throw new InvalidDataException("Trade record is quarantined or damaged.");
                    return body;
                }
            });
        }
        internal string Text(string path) => Encoding.UTF8.GetString(Read(path));
        internal string[] Keys(string extension, string scope = null, string subject = null)
        {
            return database.Read((c,t) =>
            {
                var result = new List<string>();
                using (var command = DatabaseSql.Command(c,t,"SELECT name FROM trade_records WHERE bucket=$b AND kind=$k AND problem IS NULL AND ($s IS NULL OR scope=$s) AND ($u IS NULL OR subject=$u) ORDER BY name","$b",bucket,"$k",extension,"$s",scope,"$u",subject))
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(Path.Combine(directory,reader.GetString(0)));
                return result.ToArray();
            });
        }
        internal void Put(string path, byte[] body)
        {
            if (body == null || body.Length > 64*1024*1024) throw new InvalidDataException("Trade record exceeds the per-operation size limit.");
            if(WasTerminal(path,body))return;
            string kind = Path.GetExtension(path), scope = null, subject = null;
            if (kind == ".intent") { var r = FrameworkSerialization.DeserializePayload<TradeIntentRecord>(Encoding.UTF8.GetString(body)); scope = Scope(r.Endpoint,r.Principal); }
            if (kind == ".receipt") { var r = FrameworkSerialization.DeserializePayload<TradeCompletionReceipt>(Encoding.UTF8.GetString(body)); scope = Scope(r.Endpoint,r.Recipient); subject = Scope(r.Recipient,r.TradeId); }
            string hash = Hash(body);
            database.Write((c,t) =>
            {
                string terminal = DatabaseSql.Scalar(c,t,"SELECT checksum FROM trade_terminal WHERE bucket=$b AND name=$n","$b",bucket,"$n",Path.GetFileName(path)) as string;
                if(terminal!=null){if(terminal!=hash)throw new InvalidDataException("Completed operation changed.");return 0;}
                string prior = DatabaseSql.Scalar(c,t,"SELECT checksum FROM trade_records WHERE bucket=$b AND name=$n","$b",bucket,"$n",Path.GetFileName(path)) as string;
                if (prior != null) { if (prior != hash) throw new InvalidDataException("Trade record conflicts with its durable value."); return 0; }
                return DatabaseSql.Execute(c,t,"INSERT INTO trade_records VALUES($b,$n,$k,$s,$u,$body,$hash,NULL)","$b",bucket,"$n",Path.GetFileName(path),"$k",kind,"$s",scope,"$u",subject,"$body",body,"$hash",hash);
            });
        }
        internal void Quarantine(string path, Exception error)
        {
            Report("TradeRecordReadFailed",error);
            if (error is ClientDatabaseException) {ExceptionDispatchInfo.Capture(error).Throw();return;}
            database.Write((c,t) => DatabaseSql.Execute(c,t,"UPDATE trade_records SET problem=$p WHERE bucket=$b AND name=$n","$p",error.GetType().Name,"$b",bucket,"$n",Path.GetFileName(path)));
            WriteWarning("A trade record was isolated; other trading remains available. " + error.GetType().Name);
        }
        internal static string Scope(string a, string b) => (a ?? "").ToLowerInvariant() + "\n" + (b ?? "").ToLowerInvariant();
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        public void Dispose() => database.Dispose();
    }
}
