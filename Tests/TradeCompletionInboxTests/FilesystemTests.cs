using System;
using System.IO;
using Phinix.TradeExtension.Client;
partial class Program
{
 static void CheckFilesystemIsolation()
 {
  if(OperatingSystem.IsWindows()) {Console.WriteLine("Windows reparse/junction execution requires Windows validation.");return;}
  string root=Path.Combine(Path.GetTempPath(),"phinix-evidence-path-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string external=Path.Combine(root,"external"),link=Path.Combine(root,"link");Directory.CreateDirectory(external);
  Directory.CreateSymbolicLink(link,external);
  try
  {
   Throws<IOException>(()=>new TradeCompletionInbox(Path.Combine(link,"new-evidence")));
   Check(!Directory.Exists(Path.Combine(external,"new-evidence")),"Rejected ancestor link created an external directory");
   string live=Path.Combine(root,"live");var journal=new TradeCompletionInbox(live);
   Directory.Move(live,Path.Combine(root,"original"));Directory.CreateSymbolicLink(live,external);
   try { Throws<IOException>(()=>journal.ReadAll());
    Throws<IOException>(()=>journal.Record("127.0.0.1:16200","a","blocked-link",true,"b",Items(1))); }
   finally {Directory.Delete(live);}
   Check(Directory.GetFileSystemEntries(external).Length==0,"Read-only safety check modified external contents");
  }
  finally {Directory.Delete(link);Directory.Delete(root,true);}
 }
}
