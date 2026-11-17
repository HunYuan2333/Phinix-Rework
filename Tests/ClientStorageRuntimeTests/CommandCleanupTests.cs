using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Reflection;
using PhinixClient.Storage;

internal static partial class StorageTests
{
    private sealed class FailingCommand : DbCommand
    {
        public override string CommandText {get;set;}
        public override int CommandTimeout {get;set;}
        public override CommandType CommandType {get;set;}
        public override bool DesignTimeVisible {get;set;}
        public override UpdateRowSource UpdatedRowSource {get;set;}
        protected override DbConnection DbConnection {get;set;}
        protected override DbTransaction DbTransaction {get;set;}
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
        public override void Cancel() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar() => throw new NotSupportedException();
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) {if(disposing)throw new IOException("command cleanup");base.Dispose(false);}
    }
    private static void CommandCleanupRegression()
    {
        AggregateException failure=null;
        var execute=typeof(DatabaseSql).GetMethod("WithCommand",BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(typeof(int));
        try {execute.Invoke(null,new object[]{new FailingCommand(),new Func<DbCommand,int>(command=>{StorageFaultOrigin();return 0;})});}
        catch(TargetInvocationException error){failure=error.InnerException as AggregateException;}
        Check(failure!=null && failure.InnerExceptions.Count==2 && failure.InnerExceptions[0].StackTrace.Contains("StorageFaultOrigin") && failure.InnerExceptions[1].StackTrace.Contains("Dispose"),"Command disposal failure retains both original business and cleanup call stacks");
    }
}
