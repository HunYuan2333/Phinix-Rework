using System;
using System.Collections.Generic;
using System.Data.Common;

namespace PhinixClient.Storage
{
    public interface IClientDatabaseService
    {
        IClientDatabase Open(string ownerId, string logicalName, DatabaseSchema schema);
    }

    // Callbacks are synchronous, short database-only work. They are never automatically replayed.
    // Connections, commands/readers and transactions must not escape a callback or change game objects.
    public interface IClientDatabase : IDisposable
    {
        string Identity { get; }
        T Read<T>(Func<DbConnection, DbTransaction, T> read);
        T Write<T>(Func<DbConnection, DbTransaction, T> write);
        void Backup(string destination);
    }

    public sealed class DatabaseSchema
    {
        public string Owner { get; }
        public IReadOnlyList<DatabaseMigration> Migrations { get; }
        public DatabaseSchema(string owner, params DatabaseMigration[] migrations)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Schema owner required.", nameof(owner));
            Owner = owner;
            Migrations = Array.AsReadOnly((DatabaseMigration[])(migrations ?? throw new ArgumentNullException(nameof(migrations))).Clone());
            for (int i = 0; i < Migrations.Count; i++)
                if (Migrations[i] == null || Migrations[i].Version != i + 1)
                    throw new ArgumentException("Schema migrations must be contiguous from version 1.", nameof(migrations));
        }
    }

    public sealed class DatabaseMigration
    {
        public int Version { get; }
        public string Id { get; }
        public string Sql { get; }
        public DatabaseMigration(int version, string id, string sql)
        {
            if (version < 1 || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("Migration version, ID and SQL required.");
            Version = version; Id = id; Sql = sql;
        }
    }

    public enum DatabaseFailure { Busy, Unavailable, Corrupt, UnsupportedSchema, MigrationFailed, OutcomeUnknown }
    public sealed class ClientDatabaseException : Exception
    {
        public DatabaseFailure Failure { get; }
        public ClientDatabaseException(DatabaseFailure failure, string message, Exception inner = null) : base(message, inner)
        { Failure = failure; }
    }

    public static class DatabaseSql
    {
        public static DbCommand Command(DbConnection connection, DbTransaction transaction, string sql, params object[] parameters)
        {
            var command = connection.CreateCommand();
            try
            {
                command.Transaction = transaction; command.CommandText = sql;
                if (parameters.Length % 2 != 0) throw new ArgumentException("Named parameter/value pairs required.");
                for (int i = 0; i < parameters.Length; i += 2)
                {
                    var parameter = command.CreateParameter(); parameter.ParameterName = (string)parameters[i];
                    parameter.Value = parameters[i + 1] ?? DBNull.Value; command.Parameters.Add(parameter);
                }
                return command;
            }
            catch(Exception primary) {
                try { command.Dispose(); }
                catch(Exception cleanup) { throw new AggregateException("Database command preparation and cleanup failed.",primary,cleanup); }
                throw;
            }
        }
        public static int Execute(DbConnection connection, DbTransaction transaction, string sql, params object[] parameters)
        { return WithCommand(Command(connection,transaction,sql,parameters),command=>command.ExecuteNonQuery()); }
        public static object Scalar(DbConnection connection, DbTransaction transaction, string sql, params object[] parameters)
        { return WithCommand(Command(connection,transaction,sql,parameters),command=>command.ExecuteScalar()); }
        private static T WithCommand<T>(DbCommand command,Func<DbCommand,T> execute)
        {
            Exception primary=null;
            try{return execute(command);}catch(Exception error){primary=error;throw;}
            finally{
                try{command.Dispose();}
                catch(Exception cleanup){if(primary!=null)throw new AggregateException("Database command execution and cleanup failed.",primary,cleanup);throw;}
            }
        }
    }
}
