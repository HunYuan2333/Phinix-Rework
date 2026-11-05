using System;
using System.IO;
using Phinix.TradeExtension.Client;
static partial class Program
{
 static void CheckFilesystemIsolation()
 {
  if(OperatingSystem.IsWindows()) {Console.WriteLine("Windows reparse/junction execution requires Windows validation.");return;}
  string root=Path.Combine(Path.GetTempPath(),"phinix-evidence-path-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string external=Path.Combine(root,"external"),link=Path.Combine(root,"link");Directory.CreateDirectory(external);
  Directory.CreateSymbolicLink(link,external);
  try
  {
   Throws<IOException>(()=>new TradeIntentJournal(Path.Combine(link,"new-evidence")));
   Check(!Directory.Exists(Path.Combine(external,"new-evidence")),"Rejected ancestor link created an external directory");
   string live=Path.Combine(root,"live");var journal=new TradeIntentJournal(live);
   Directory.Move(live,Path.Combine(root,"original"));Directory.CreateSymbolicLink(live,external);
   try { Throws<IOException>(()=>journal.Read("127.0.0.1:16200","a"));
    Throws<IOException>(()=>journal.Prepare("127.0.0.1:16200","a",Packet("blocked-link"),Array.Empty<Utils.Framework.FrameworkPacket>())); }
   finally {Directory.Delete(live);}
   Check(Directory.GetFileSystemEntries(external).Length==0,"Read-only safety check modified external contents");
  }
  finally {Directory.Delete(link);Directory.Delete(root,true);}
 }
}
