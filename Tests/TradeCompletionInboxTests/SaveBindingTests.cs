using System;
using System.IO;
using System.Linq;
using Phinix.TradeExtension.Client;

partial class Program
{
 static void CheckSaveBindings()
 {
  string root=Path.Combine(Path.GetTempPath(),"phinix-binding-"+Guid.NewGuid().ToString("N"));
  try
  {
   string save=new string('a',64),other=new string('b',64);var journal=new TradeCompletionInbox(root);
   journal.Record("server","owner","new",true,"other",Items(23));
   Check(journal.SaveBinding("server","owner","new",true,save)=="Unbound","Receipt alone invented binding");
   Throws<InvalidDataException>(()=>journal.BindSave("server","owner","new",true,null));
   Throws<InvalidDataException>(()=>journal.BindSave("server","owner","new",true,"/private/save.rws"));
   long revision=journal.Revision;journal.BindSave("server","owner","new",true,save);
   Check(journal.Revision>revision,"Binding not observable by cached recovery view");
   var path=Directory.GetFiles(root,"*.binding").Single();var original=File.ReadAllBytes(path);
   journal.BindSave("server","owner","new",true,save);
   Check(File.ReadAllBytes(path).SequenceEqual(original),"Same binding rewrote committed evidence");
   var reopened=new TradeCompletionInbox(root);
   Check(reopened.SaveBinding("server","owner","new",true,save)=="Current","Restart lost save binding");
   Check(reopened.SaveBinding("server","owner","new",true,other)=="Different","Another saved game accepted origin binding");
   Throws<InvalidDataException>(()=>reopened.BindSave("server","owner","new",true,other));
   Check(File.ReadAllBytes(path).SequenceEqual(original),"Conflict replaced origin save");
   journal.Record("server","owner","old",true,"other",Items(17));journal.TryBeginDelivery("server","owner","old",true,true);
   Throws<InvalidDataException>(()=>journal.BindSave("server","owner","old",true,save));
   Check(journal.SaveBinding("server","owner","old",true,save)=="Unbound","Historical attempt silently adopted current save");
   File.WriteAllText(path,File.ReadAllText(path).Replace(save,other));Throws<InvalidDataException>(()=>new TradeCompletionInbox(root));File.WriteAllBytes(path,original);
   string orphan=Path.Combine(root,"orphan.binding");File.WriteAllText(orphan,"{}");Throws<IOException>(()=>new TradeCompletionInbox(root));File.Delete(orphan);
   string export=Environment.GetEnvironmentVariable("PHINIX_BINDING_ORACLE");
   if(!string.IsNullOrEmpty(export))
   {Directory.CreateDirectory(export);File.Copy(path,Path.Combine(export,Path.GetFileName(path)));string receipt=Path.ChangeExtension(path,".receipt");File.Copy(receipt,Path.Combine(export,Path.GetFileName(receipt)));}
  }
  finally{if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
