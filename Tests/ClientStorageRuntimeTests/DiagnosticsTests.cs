using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PhinixClient.Storage;

internal static partial class StorageTests
{
    private sealed class BrokenCleanup : IDisposable
    { internal int Calls; public void Dispose(){Calls++;throw new IOException("private cleanup");} }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StorageFaultOrigin(){throw new InvalidOperationException("private SQL parameter");}
    private static void DiagnosticsRegression(string root)
    {
        CommandCleanupRegression();
        var reports=new List<Tuple<string,string,Exception>>();
        using(var service=new ClientDatabaseService(owner=>Path.Combine(root,"diagnostics",owner),(owner,operation,error)=>reports.Add(Tuple.Create(owner,operation,error))))
        using(var db=service.Open("test.diag","main",Schema("test.diag"))) {
            Exception original=null;
            try{db.Write<int>((c,t)=>{DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(1,$v)","$v",new byte[]{1});StorageFaultOrigin();return 0;});}catch(Exception error){original=error;}
            Check(original is InvalidOperationException && original.StackTrace.Contains("StorageFaultOrigin"),"Original callback failure propagates without replacing its stack");
            Check(reports.Any(r=>r.Item1=="test.diag" && ReferenceEquals(r.Item3,original)),"Original database exception enters independent owner-scoped diagnostics");
            Check(db.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM items")))==0,"Failed callback rolls back without replay or committing partial changes");
            db.Write((c,t)=>DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(2,$v)","$v",new byte[]{2}));
            var cleanup=new BrokenCleanup();db.GetType().GetMethod("Cleanup",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(db,new object[]{cleanup,"DatabaseCommittedCleanupFailed"});
            Check(cleanup.Calls==1 && reports.Any(r=>r.Item2=="DatabaseCommittedCleanupFailed"),"Post-commit disposal failure is reported independently without turning saved work into a failed submission");
            Check(db.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM items")))==1,"Cleanup diagnostics do not undo committed data");
            db.Write((c,t)=>DatabaseSql.Execute(c,t,"CREATE TABLE parent(id INTEGER PRIMARY KEY);CREATE TABLE child(id INTEGER REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED);"));
            ClientDatabaseException unknown=null;int attempts=0;
            try{db.Write((c,t)=>{attempts++;return DatabaseSql.Execute(c,t,"INSERT INTO child VALUES(99)");});}catch(ClientDatabaseException error){unknown=error;}
            Check(unknown!=null && unknown.Failure==DatabaseFailure.OutcomeUnknown && unknown.InnerException!=null && !string.IsNullOrEmpty(unknown.InnerException.StackTrace),"Commit-time failure remains OutcomeUnknown and retains native cause/stack");
            Check(attempts==1 && reports.Any(r=>r.Item2=="DatabaseCommitOutcomeUnknown" && ReferenceEquals(r.Item3,unknown.InnerException)),"Uncertain commit is observed without automatic callback replay");
        }
        string corruptRoot=Path.Combine(root,"corrupt-open","databases");Directory.CreateDirectory(corruptRoot);
        byte[] corrupt=System.Text.Encoding.UTF8.GetBytes("This is deliberately not a SQLite database file.");File.WriteAllBytes(Path.Combine(corruptRoot,"main.sqlite3"),corrupt);
        using(var service=new ClientDatabaseService(owner=>Path.Combine(root,"corrupt-open"),(owner,operation,error)=>reports.Add(Tuple.Create(owner,operation,error)))) {
            ClientDatabaseException failure=null;try{service.Open("test.corrupt","main",Schema("test.corrupt"));}catch(ClientDatabaseException error){failure=error;}
            Check(failure!=null && failure.Failure==DatabaseFailure.Corrupt && failure.InnerException!=null && File.ReadAllBytes(Path.Combine(corruptRoot,"main.sqlite3")).SequenceEqual(corrupt),"Corrupt open retains native cause and exact original file instead of pretending a migration failed or recreating data");
        }
        Check(reports.Any(r=>r.Item1=="test.corrupt" && r.Item2.EndsWith("Corrupt",StringComparison.Ordinal)),"Frozen sensitive diagnostics retain the known Corrupt classification without exposing SQL or exception messages");
        using(var service=new ClientDatabaseService(owner=>{throw new IOException("private directory");})) {
            ClientDatabaseException failure=null;try{service.Open("test.unavailable","main",Schema("test.unavailable"));}catch(ClientDatabaseException error){failure=error;}
            Check(failure!=null && failure.Failure==DatabaseFailure.Unavailable && failure.InnerException is IOException,"Unavailable directory remains distinct from migration failure and preserves cause");
        }
        using(var service=new ClientDatabaseService(owner=>Path.Combine(root,"broken-sink",owner),(owner,operation,error)=>{throw new Exception("diagnostic sink");}))
        using(var db=service.Open("test.sink","main",Schema("test.sink"))){
            Exception original=null;try{db.Read<int>((c,t)=>{StorageFaultOrigin();return 0;});}catch(Exception error){original=error;}
            Check(original is InvalidOperationException && original.StackTrace.Contains("StorageFaultOrigin"),"Diagnostic sink failure never replaces business exception");
        }
    }
}
