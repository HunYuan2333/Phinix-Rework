using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Phinix.TradeExtension.Client;

partial class Program
{
 static void CheckDeliveryJournal()
 {
  string root=Path.Combine(Path.GetTempPath(),"phinix-attempt-"+Guid.NewGuid().ToString("N"));
  try
  {
   var journal=new TradeCompletionInbox(root);
   journal.Record("server","owner","trade",true,"other",Items(17));
   Check(journal.DeliveryState("server","owner","trade",true)=="Received","Receipt alone must not imply attempt");
   int begins=0;
   Parallel.For(0,8,_=>{if(journal.TryBeginDelivery("server","owner","trade",true,true))Interlocked.Increment(ref begins);});
   Check(begins==1,"Reentrant attempt invoked more than once");
   journal=new TradeCompletionInbox(root);
   Check(journal.DeliveryState("server","owner","trade",true)=="Attempted","Restart lost unknown attempt");
   Check(!journal.TryBeginDelivery("server","owner","trade",true,false),"Mode change authorized a second delivery");
   Throws<InvalidDataException>(()=>journal.RecordDeliveryReturn("server","owner","trade",true,false,true));
   Throws<IOException>(()=>journal.TryBeginDelivery("other-server","owner","trade",true,true));
   journal.RecordDeliveryReturn("server","owner","trade",true,true,false);
   journal.RecordDeliveryReturn("server","owner","trade",true,true,false);
   Check(new TradeCompletionInbox(root).DeliveryState("server","owner","trade",true)=="ReturnedFailure","Local failure evidence lost");
   Throws<InvalidDataException>(()=>journal.RecordDeliveryReturn("server","owner","trade",true,true,true));
   Check(!journal.TryBeginDelivery("server","owner","trade",true,true),"Returned failure granted automatic retry");
   journal.Record("server","owner","other",false,"other",Items(23));
   Check(journal.TryBeginDelivery("server","owner","other",false,false),"Unrelated attempt refused");
   journal.RecordDeliveryReturn("server","owner","other",false,false,true);
   Check(journal.DeliveryState("server","owner","other",false)=="ReturnedSuccess","Local success evidence lost");
   journal.Record("server","owner","blocked",true,"other",Items(17));
   var receipt=Directory.GetFiles(root,"*.receipt").Single(p=>File.ReadAllText(p).Contains("blocked"));
   var block=Path.ChangeExtension(receipt,".attempt");Directory.CreateDirectory(block);
   Throws<IOException>(()=>journal.TryBeginDelivery("server","owner","blocked",true,true));
   Check(!Directory.GetFiles(root).Any(p=>p.Contains(".tmp-")),"Failed attempt leaked temporary file");Directory.Delete(block);
   Check(journal.DeliveryState("server","owner","blocked",true)=="Received","Failed attempt forged invocation");
   var attempt=Directory.GetFiles(root,"*.attempt").First();var saved=File.ReadAllBytes(attempt);
   File.WriteAllText(attempt,"corrupt");Throws<Exception>(()=>new TradeCompletionInbox(root));File.WriteAllBytes(attempt,saved);
   var orphan=Path.Combine(root,"orphan.result");File.WriteAllText(orphan,"{}");Throws<System.IO.InvalidDataException>(()=>new TradeCompletionInbox(root));File.Delete(orphan);
   object game=new object(),original=game;string owner="owner",server="server";
   var scope=new TradeCompletionScope(owner,server,()=>game,()=>owner,()=>server);
   Check(scope.IsValid(),"Original live scope invalid");game=new object();Check(!scope.IsValid(),"Scope followed another game");game=original;Check(!scope.IsValid(),"Invalidated game scope revived");
   scope=new TradeCompletionScope(owner,server,()=>game,()=>owner,()=>server);owner="other";Check(!scope.IsValid(),"Scope followed another recipient");owner="owner";Check(!scope.IsValid(),"Recipient switch revived scope");
   scope=new TradeCompletionScope(owner,server,()=>game,()=>owner,()=>server);server=null;Check(!scope.IsValid(),"Disconnected endpoint retained delivery authority");
   scope=new TradeCompletionScope(owner,"server",()=>null,()=>owner,()=>"server");Check(!scope.IsValid(),"Menu receipt invented a game binding");
   string crash=Path.Combine(root,"crash");Directory.CreateDirectory(crash);
   var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};
   foreach(string a in new[]{Assembly.GetExecutingAssembly().Location,"--attempt-child",crash})start.ArgumentList.Add(a);
   using(var child=Process.Start(start))
   {
    try{Check(SpinWait.SpinUntil(()=>File.Exists(Path.Combine(crash,"delivery-attempt-held"))||child.HasExited,10000)&&!child.HasExited,"Attempt barrier failed");child.Kill(true);Check(child.WaitForExit(5000),"Attempt kill deadline");}
    finally{if(!child.HasExited){child.Kill(true);child.WaitForExit(5000);}}
   }
   var recovered=new TradeCompletionInbox(crash);
   Check(recovered.DeliveryState("127.0.0.1:16200","a","attempt-kill",true)=="Attempted","Kill invented success/failure");
   Check(!recovered.TryBeginDelivery("127.0.0.1:16200","a","attempt-kill",true,false),"Kill authorized second target");
   string export=Environment.GetEnvironmentVariable("PHINIX_ATTEMPT_ORACLE");
   if(!string.IsNullOrEmpty(export)){Directory.CreateDirectory(export);foreach(var f in Directory.GetFiles(crash).Where(p=>p.EndsWith(".attempt")||p.EndsWith(".receipt")))File.Copy(f,Path.Combine(export,Path.GetFileName(f)));}
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
