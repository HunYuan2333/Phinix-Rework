using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PhinixClient.Storage
{
    // Host-owned infrastructure; no game, inventory or trading dependencies.
    public sealed class ClientDatabaseService : IClientDatabaseService, IDisposable
    {
        private readonly Func<string, string> ownerDirectory;
        private readonly object gate = new object();
        private readonly HashSet<Database> leases = new HashSet<Database>();
        private bool disposed;
        private readonly Action<string,string,Exception> report;
        public ClientDatabaseService(Func<string, string> ownerDirectory) : this(ownerDirectory,null) { }
        public ClientDatabaseService(Func<string, string> ownerDirectory, Action<string,string,Exception> report)
        { this.ownerDirectory = ownerDirectory ?? throw new ArgumentNullException(nameof(ownerDirectory));this.report=report; }
        public IClientDatabase Open(string ownerId, string logicalName, DatabaseSchema schema)
        {
            ValidateName(ownerId); ValidateName(logicalName);
            if (schema == null || schema.Owner != ownerId) throw new ArgumentException("Database schema owner differs.");
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ClientDatabaseService));
                var diagnostics=new DatabaseDiagnostics(report==null?(Action<string,Exception>)null:(operation,error)=>report(ownerId,operation,error));
                try {
                    var directory = Path.Combine(ownerDirectory(ownerId), "databases");
                    Directory.CreateDirectory(directory);
                    var database = new Database(Path.Combine(directory, logicalName + ".sqlite3"), schema, d => { lock (gate) leases.Remove(d); },diagnostics);
                    leases.Add(database); return database;
                } catch(Exception error) {
                    diagnostics.Report(error is ClientDatabaseException classified?"DatabaseOpen"+classified.Failure:"DatabaseOpenUnavailable",error is ClientDatabaseException && error.InnerException!=null?error.InnerException:error);
                    if(error is IOException || error is UnauthorizedAccessException)
                        throw new ClientDatabaseException(DatabaseFailure.Unavailable,"Database storage is unavailable; existing data retained.",error);
                    throw;
                }
            }
        }
        private static void ValidateName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 120 || value == "." || value == ".." ||
                value.Any(c => !(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')))
                throw new ArgumentException("Invalid logical database name.");
        }
        public void Dispose()
        {
            Database[] owned;
            lock (gate) { if (disposed) return; disposed = true; owned = leases.ToArray(); leases.Clear(); }
            foreach (var lease in owned) lease.Dispose();
        }

        private sealed class Database : IClientDatabase
        {
            private readonly string path;
            private readonly Action<Database> release;
            private readonly object gate = new object();
            private bool disposed;
            public string Identity { get; private set; }
            private readonly DatabaseDiagnostics diagnostics;
            internal Database(string path, DatabaseSchema schema, Action<Database> release,DatabaseDiagnostics diagnostics)
            {
                this.path = path; this.release = release;this.diagnostics=diagnostics;
                try
                {
                    Transaction(true,true,(connection,transaction)=>
                    {
                        DatabaseSql.Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS __database_meta(id INTEGER PRIMARY KEY CHECK(id=1), identity TEXT NOT NULL, owner TEXT NOT NULL); CREATE TABLE IF NOT EXISTS __schema_migrations(version INTEGER PRIMARY KEY, name TEXT NOT NULL, checksum TEXT NOT NULL);");
                        DatabaseSql.Execute(connection, transaction, "INSERT OR IGNORE INTO __database_meta VALUES(1,$id,$owner)", "$id", Guid.NewGuid().ToString("N"), "$owner", schema.Owner);
                        using (var command = DatabaseSql.Command(connection, transaction, "SELECT identity,owner FROM __database_meta WHERE id=1"))
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read() || reader.GetString(1) != schema.Owner || !Guid.TryParseExact(reader.GetString(0), "N", out _))
                                throw new ClientDatabaseException(DatabaseFailure.UnsupportedSchema, "Database identity or schema owner differs.");
                            Identity = reader.GetString(0);
                        }
                        var previous = new List<string>();
                        using (var command = DatabaseSql.Command(connection, transaction, "SELECT version,name,checksum FROM __schema_migrations ORDER BY version"))
                        using (var reader = command.ExecuteReader())
                            while (reader.Read())
                            {
                                int version = reader.GetInt32(0);
                                if (version != previous.Count + 1 || version > schema.Migrations.Count ||
                                    schema.Migrations[version - 1].Id != reader.GetString(1) || Hash(schema.Migrations[version - 1].Sql) != reader.GetString(2))
                                    throw new ClientDatabaseException(DatabaseFailure.UnsupportedSchema, "Database schema is newer or its migration history differs.");
                                previous.Add(reader.GetString(1));
                            }
                        foreach (var step in schema.Migrations.Skip(previous.Count))
                        {
                            DatabaseSql.Execute(connection, transaction, step.Sql);
                            DatabaseSql.Execute(connection, transaction, "INSERT INTO __schema_migrations VALUES($v,$id,$hash)", "$v", step.Version, "$id", step.Id, "$hash", Hash(step.Sql));
                        }
                        return true;
                    },()=>{});
                }
                catch (ClientDatabaseException) { throw; }
                catch (SqliteException error) {
                    var failure=Classify(error,true);
                    throw new ClientDatabaseException(failure,"Database could not open or upgrade; existing data retained.",error);
                }
                catch (Exception error) { throw new ClientDatabaseException(DatabaseFailure.Unavailable, "Database runtime is unavailable; existing data retained.", error); }
            }
            private SqliteConnection Connect(bool create = false)
            {
                SqliteRuntime.Ensure();
                var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,
                    Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 2 }.ToString());
                try
                {
                    connection.Open();
                    DatabaseSql.Execute(connection, null, "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;");
                    return connection;
                }
                catch(Exception error) {
                    diagnostics.Report(error is SqliteException sql?"DatabaseConnect"+Classify(sql,false):"DatabaseConnectUnavailable",error);
                    Cleanup(connection,"DatabaseConnectionCleanupFailed");throw;
                }
            }
            public T Read<T>(Func<DbConnection, DbTransaction, T> read) => Run(read, false);
            public T Write<T>(Func<DbConnection, DbTransaction, T> write) => Run(write, true);
            private T Run<T>(Func<DbConnection, DbTransaction, T> work, bool write)
            {
                if (work == null) throw new ArgumentNullException(nameof(work));
                lock (gate)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(Database));
                    bool committing = false;
                    try
                    {
                        return Transaction(false,write,(connection,transaction)=>work(connection,transaction),()=>committing=write);
                    }
                    catch (SqliteException error)
                    {
                        var failure = committing ? DatabaseFailure.OutcomeUnknown : Classify(error,false);
                        throw new ClientDatabaseException(failure, committing ? "Database commit could not be confirmed; check this operation before retrying." : "Database operation failed; changes were not committed.", error);
                    }
                    catch (Exception error) when (committing)
                    { throw new ClientDatabaseException(DatabaseFailure.OutcomeUnknown,"Database commit could not be confirmed; check this operation before retrying.",error); }
                }
            }
            private static DatabaseFailure Classify(SqliteException error,bool migration)
                => error.SqliteErrorCode==5 || error.SqliteErrorCode==6?DatabaseFailure.Busy:
                   error.SqliteErrorCode==11 || error.SqliteErrorCode==26?DatabaseFailure.Corrupt:
                   migration && (error.SqliteErrorCode==1 || error.SqliteErrorCode==17)?DatabaseFailure.MigrationFailed:DatabaseFailure.Unavailable;
            private T Transaction<T>(bool create,bool write,Func<SqliteConnection,SqliteTransaction,T> work,Action enteringCommit)
            {
                SqliteConnection connection=null;SqliteTransaction transaction=null;bool committed=false,committing=false;
                try {
                    connection=Connect(create);transaction=connection.BeginTransaction(deferred:!write);
                    var value=work(connection,transaction);enteringCommit();committing=write;
                    transaction.Commit();committed=true;return value;
                } catch(Exception error) {
                    diagnostics.Report(committing?"DatabaseCommitOutcomeUnknown":error is ClientDatabaseException known?"DatabaseWork"+known.Failure:error is SqliteException sql?"DatabaseWork"+Classify(sql,create):create?"DatabaseOpenUnavailable":"DatabaseWorkFailed",error);throw;
                } finally {
                    string operation=committed?"DatabaseCommittedCleanupFailed":"DatabaseCleanupFailed";
                    Cleanup(transaction,operation);Cleanup(connection,operation);
                }
            }
            private void Cleanup(IDisposable resource,string operation)
            {try{resource?.Dispose();}catch(Exception error){diagnostics.Report(operation,error);}}
            public void Backup(string destination)
            {
                lock (gate)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(Database));
                    if (File.Exists(destination)) throw new IOException("Backup destination already exists.");
                    SqliteConnection source=null,target=null;bool completed=false;
                    try {
                        source=Connect();target=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=destination,Pooling=false }.ToString());
                        target.Open();source.BackupDatabase(target);completed=true;
                    }catch(Exception error){diagnostics.Report("DatabaseBackupFailed",error);throw;}
                    finally {string operation=completed?"DatabaseBackupSavedCleanupFailed":"DatabaseBackupCleanupFailed";Cleanup(target,operation);Cleanup(source,operation);}
                }
            }
            public void Dispose() { lock (gate) { if (disposed) return; disposed = true; } release(this); }
            private static string Hash(string value)
            { using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        }
    }
}
