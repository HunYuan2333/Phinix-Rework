using System;
using System.IO;
using System.Threading;
using PhinixClient.Storage;

internal static partial class StorageTests
{
    private static int checks;
    private static void Check(bool value,string message) { if(!value)throw new Exception(message);checks++; }
    private static void Throws<T>(Action action,string message) where T:Exception
    { try{action();}catch(T){checks++;return;}throw new Exception(message); }
    private static DatabaseSchema Schema(string owner) => new DatabaseSchema(owner,new DatabaseMigration(1,"items-v1","CREATE TABLE items(id INTEGER PRIMARY KEY, value BLOB NOT NULL);"));
    public static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"phinix-storage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string identity;
            using(var service=new ClientDatabaseService(owner=>Path.Combine(root,owner)))
            {
                using(var db=service.Open("test.a","shared",Schema("test.a")))
                {
                    identity=db.Identity;
                    Check(Guid.TryParseExact(identity,"N",out _),"Stable database identity");
                    var payload=new byte[3*1024*1024];for(int i=0;i<payload.Length;i++)payload[i]=255;
                    db.Write((c,t)=>DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(1,$value)","$value",payload));
                    Check(db.Read((c,t)=>((byte[])DatabaseSql.Scalar(c,t,"SELECT value FROM items WHERE id=1")).Length)==payload.Length,"BLOB round trip");
                    Throws<InvalidOperationException>(()=>db.Write<int>((c,t)=>{DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(2,$v)","$v",new byte[]{2});throw new InvalidOperationException("synthetic");}),"Rollback callback failure");
                    Check(db.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM items")))==1,"Failed unit rolled back");
                    using(var reopened=service.Open("test.a","shared",Schema("test.a")))Check(reopened.Identity==identity,"Same logical store");
                    using(var other=service.Open("test.b","shared",Schema("test.b")))
                        Check(other.Identity!=identity&&other.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM items")))==0,"Independent module schema and data");
                    string backup=Path.Combine(root,"backup.sqlite3");db.Backup(backup);Check(new FileInfo(backup).Length>payload.Length,"Consistent backup");
                    Throws<IOException>(()=>db.Backup(backup),"Cannot replace backup");
                }
                using(var reopened=service.Open("test.a","shared",Schema("test.a")))
                    Check(reopened.Identity==identity&&reopened.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM items")))==1,"Restart retains committed rows only");
                LockContention(service);
                Throws<ClientDatabaseException>(()=>service.Open("test.a","shared",new DatabaseSchema("test.a",new DatabaseMigration(1,"changed","CREATE TABLE replacement(id INTEGER);"))),"Changed schema rejected");
                Throws<ArgumentException>(()=>service.Open("test.a","../escape",Schema("test.a")),"Logical names stay within owner");
                var schema2=new DatabaseSchema("test.a",Schema("test.a").Migrations[0],new DatabaseMigration(2,"items-v2","ALTER TABLE items ADD COLUMN note TEXT;"));
                using(var upgraded=service.Open("test.a","shared",schema2))Check(upgraded.Identity==identity,"Upgrade keeps identity");
                Throws<ClientDatabaseException>(()=>service.Open("test.a","shared",Schema("test.a")),"Older schema refuses downgrade");
                var bad=new DatabaseSchema("test.a",schema2.Migrations[0],schema2.Migrations[1],new DatabaseMigration(3,"broken","CREATE TABLE temporary_table(id INTEGER); INVALID SQL;"));
                Throws<ClientDatabaseException>(()=>service.Open("test.a","shared",bad),"Failed schema upgrade rejected");
                using(var upgraded=service.Open("test.a","shared",schema2))
                    Check(upgraded.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM sqlite_master WHERE name='temporary_table'")))==0,"Failed schema upgrade atomic");
                var lease=service.Open("test.b","leak",Schema("test.b"));service.Dispose();
                Throws<ObjectDisposedException>(()=>lease.Read((c,t)=>0),"Host closes remaining module leases");
                Throws<ObjectDisposedException>(()=>service.Open("test.c","db",Schema("test.c")),"Disposed host is terminal");
            }
            DiagnosticsRegression(root);
            Console.WriteLine("STORAGE PASS "+checks+" checks; real SQLite, isolated synthetic databases");
        }
        finally{Directory.Delete(root,true);}
    }

    private static void LockContention(IClientDatabaseService service)
    {
        using(var first=service.Open("test.lock","shared",Schema("test.lock")))
        using(var second=service.Open("test.lock","shared",Schema("test.lock")))
        using(var acquired=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false))
        {
            Exception workerFailure=null;
            var worker=new Thread(()=>{
                try{first.Write((c,t)=>{DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(1,$v)","$v",new byte[]{1});acquired.Set();if(!release.WaitOne(15000))throw new TimeoutException("Lock fixture timed out");return 0;});}
                catch(Exception error){workerFailure=error;acquired.Set();}
            });
            worker.Start();bool ran=false;var elapsed=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Check(acquired.WaitOne(5000)&&workerFailure==null,"Separate writer acquired the database lock");
                try {second.Write((c,t)=>{ran=true;return DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(2,$v)","$v",new byte[]{2});});throw new Exception("Competing writer must fail");}
                catch(ClientDatabaseException error){Check(error.Failure==DatabaseFailure.Busy,"Lock conflict is Busy rather than an uncertain operation");}
                Check(!ran&&elapsed.Elapsed.TotalSeconds<10,"Busy fails within a bound without replaying the business callback");
            }
            finally{release.Set();Check(worker.Join(5000),"Writer exits after releasing the lock");}
            if(workerFailure!=null)throw workerFailure;
            second.Write((c,t)=>DatabaseSql.Execute(c,t,"INSERT INTO items VALUES(2,$v)","$v",new byte[]{2}));
            Check(second.Read((c,t)=>Convert.ToInt64(DatabaseSql.Scalar(c,t,"SELECT count(*) FROM items")))==2,"Explicit retry works after a temporary lock clears");
        }
    }
}
