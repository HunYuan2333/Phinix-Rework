using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Phinix.TradeExtension.Client;
using Utils.Framework;
partial class Program
{
 static int checks;
 static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static void Throws<T>(Action act)where T:Exception{try{act();}catch(T){checks++;return;}throw new Exception("Expected "+typeof(T).Name);}
 static FrameworkItemPayload[] Items(byte value)=>new[]{new FrameworkItemPayload{CodecId="synthetic.unavailable-codec",PayloadBytes=new[]{value}}};
 static void Main(string[] args)
 {
  if(args.Length==1&&args[0]=="--filesystem-only") {CheckFilesystemIsolation();Console.WriteLine("Filesystem isolation passed.");return;}
  if(args.Length==2&&args[0]=="--child")
  {new TradeCompletionInbox(args[1]).Record("127.0.0.1:16200","a","kill",true,"b",Items(23));File.WriteAllText(Path.Combine(args[1],"committed-receipt-held"),"ready");Thread.Sleep(Timeout.Infinite);return;}
  if(args.Length==2&&args[0]=="--attempt-child")
  {var journal=new TradeCompletionInbox(args[1]);journal.Record("127.0.0.1:16200","a","attempt-kill",true,"b",Items(23));journal.TryBeginDelivery("127.0.0.1:16200","a","attempt-kill",true,true);File.WriteAllText(Path.Combine(args[1],"delivery-attempt-held"),"ready");Thread.Sleep(Timeout.Infinite);return;}
  string root=Path.Combine(Path.GetTempPath(),"phinix-inbox-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   CheckFilesystemIsolation();
   CheckDeliveryProtocol();
   CheckDeliveryJournal();
   CheckSaveBindings();
   CheckGameScopes();
   var inbox=new TradeCompletionInbox(root);
   Check(inbox.Record("127.0.0.1:16200","a","trade",true,"b",Items(17)),"First receipt not recorded");
   var file=Directory.GetFiles(root,"*.receipt").Single();var before=File.ReadAllBytes(file);
   var reopened=new TradeCompletionInbox(root);
   Check(reopened.ReadAll().Single().Items.Single().PayloadBytes.Single()==17,"Restart lost opaque payload");
   Check(!reopened.Record("127.0.0.1:16200","a","trade",true,"b",Items(17)),"Restart authorized duplicate delivery");
   Throws<InvalidDataException>(()=>reopened.Record("127.0.0.1:16200","a","trade",true,"b",Items(99)));
   Check(File.ReadAllBytes(file).SequenceEqual(before),"Conflict overwrote evidence");
   Throws<InvalidDataException>(()=>reopened.Record("127.0.0.1:16200","A","TRADE",true,"b",Items(17)));
   Throws<InvalidDataException>(()=>reopened.Record("127.0.0.2:16200","a","trade",true,"b",Items(17)));
   Check(reopened.Record("127.0.0.2:16200","a","other-trade",true,"b",Items(17)),"Unrelated server receipt rejected");
   Check(reopened.Record("127.0.0.1:16200","c","trade",true,"b",Items(17)),"Recipient scopes collided");
   Throws<InvalidDataException>(()=>reopened.Record("127.0.0.1:16200","a","trade",false,"b",Items(17)));
   Throws<InvalidDataException>(()=>reopened.Record("127.0.0.1:16200","a","bad-batch",true,"b",new FrameworkItemPayload[]{Items(1)[0],null}));
   Throws<InvalidDataException>(()=>reopened.Record("127.0.0.1:16200","a","large",true,"b",new[]{new FrameworkItemPayload{CodecId="opaque",PayloadBytes=new byte[4*1024*1024]}}));
   Check(reopened.ReadAll().Length==3,"Rejected record leaked partial batch");
   string blocked=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("127.0.0.1:16200\na\nblocked\nTrue"))).ToLowerInvariant();
   Directory.CreateDirectory(Path.Combine(root,blocked+".receipt"));
   Throws<IOException>(()=>reopened.Record("127.0.0.1:16200","a","blocked",true,"b",Items(17)));
   Check(!Directory.GetFiles(root).Any(p=>p.Contains(".tmp-"))&&reopened.ReadAll().Length==3,"Failed publication changed committed records or leaked temp files");
   if(!OperatingSystem.IsWindows()) Check((File.GetUnixFileMode(file)&(UnixFileMode.GroupRead|UnixFileMode.OtherRead|UnixFileMode.GroupWrite|UnixFileMode.OtherWrite))==0,"Receipt is public");
   File.WriteAllText(file,File.ReadAllText(file).Replace("synthetic.unavailable-codec","synthetic.changed-codec"));
   Throws<InvalidDataException>(()=>new TradeCompletionInbox(root));File.WriteAllBytes(file,before);
   string childRoot=Path.Combine(root,"crash");Directory.CreateDirectory(childRoot);
   var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(string a in new[]{Assembly.GetExecutingAssembly().Location,"--child",childRoot})start.ArgumentList.Add(a);
   using(var child=Process.Start(start))
   {
    try {Check(SpinWait.SpinUntil(()=>File.Exists(Path.Combine(childRoot,"committed-receipt-held"))||child.HasExited,10000)&&!child.HasExited,"Receipt barrier failed");child.Kill(true);Check(child.WaitForExit(5000),"Kill deadline");}
    finally {if(!child.HasExited){child.Kill(true);child.WaitForExit(5000);}}
   }
   var afterKill=new TradeCompletionInbox(childRoot);Check(afterKill.ReadAll().Single().Items.Single().PayloadBytes.Single()==23,"Kill lost receipt");
   Check(!afterKill.Record("127.0.0.1:16200","a","kill",true,"b",Items(23)),"Kill recovery authorized redelivery");
   if(args.Length==2&&args[0]=="--oracle") File.Copy(Directory.GetFiles(childRoot,"*.receipt").Single(),args[1]);
   Console.WriteLine("INBOX RESULT passed; checks="+checks+"; real files/restart/conflict/scopes/whole-batch/budget/private/hash/committed-receipt-held SIGKILL; no game assets");
  }
  finally{Directory.Delete(root,true);}
 }
}
