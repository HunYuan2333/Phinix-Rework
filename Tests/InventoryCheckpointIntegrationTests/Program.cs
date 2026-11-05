using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using PhinixClient.Framework;
using Utils.Framework;
using Verse;

internal sealed class Dispatcher : IClientMainThreadDispatcher
{
    internal readonly Queue<Action> Queue = new Queue<Action>();
    public void Enqueue(Action action) { Queue.Enqueue(action); }
    internal void Drain() { while (Queue.Count > 0) Queue.Dequeue()(); }
}
internal static class Program
{
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static void Check(bool value,string reason) { checks++; if (!value) throw new Exception(reason); }
    static void Set(object target,string field,object value) => target.GetType().GetField(field,Fields).SetValue(target,value);
    static void Main()
    {
        var assembly = typeof(Phinix.InventoryExtension.Client.BuiltInInventoryClientExtension).Assembly;
        var moduleType = assembly.GetType("Phinix.InventoryExtension.Client.BuiltInInventoryClientExtension");
        var componentType = assembly.GetType("Phinix.InventoryExtension.Client.InventoryGameComponent");
        var stateType = assembly.GetType("Phinix.InventoryExtension.Client.InventoryState");
        var journalType = assembly.GetType("Phinix.InventoryExtension.Client.InventoryJournal");
        var pathProperty = assembly.GetType("Phinix.InventoryExtension.Client.InventorySaveIdentityPatch").GetProperty("CurrentPath",Fields);
        var active = moduleType.GetField("active",Fields); object previousActive = active.GetValue(null),previousPath=pathProperty.GetValue(null);
        Game previousGame = Current.Game;
        string root=Path.Combine(Path.GetTempPath(),"phinix-save-callback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        IDisposable journal=null;
        try
        {
            var module=Activator.CreateInstance(moduleType);var dispatcher=new Dispatcher();
            var game=(Game)FormatterServices.GetUninitializedObject(typeof(Game)); game.components=new List<GameComponent>();
            var component=(GameComponent)Activator.CreateInstance(componentType,new object[]{game});game.components.Add(component);Current.Game=game;
            object one=Activator.CreateInstance(stateType),two=Activator.CreateInstance(stateType);stateType.GetProperty("Sequence").SetValue(one,1L);stateType.GetProperty("Sequence").SetValue(two,2L);
            foreach(object state in new[]{one,two})
                ((System.Collections.IList)stateType.GetProperty("Entries").GetValue(state)).Add(
                    new Phinix.InventoryExtension.InventoryEntry { EntryId="synthetic-entry",CodecId="test.codec",CodecVersion=1,
                        Payload=new byte[]{1,0,255},Quantity=42,Label="Synthetic",Source="test" });
            var serialize=typeof(FrameworkSerialization).GetMethod("SerializePayload").MakeGenericMethod(stateType);
            string json=(string)serialize.Invoke(null,new[]{two});Set(component,"saveId","synthetic");Set(component,"snapshotJson",json);
            string file=Path.Combine(root,"save.rws"),log=Path.Combine(root,"inventory.journal");
            string journalIdentity = null;
            Action<string> writeSave=payload=>{
                using(var stream=new FileStream(file,FileMode.Create,FileAccess.Write))
                {using(var xml=XmlWriter.Create(stream,new XmlWriterSettings{CloseOutput=false}))
                {xml.WriteStartElement("savegame");xml.WriteStartElement("game");xml.WriteStartElement("components");xml.WriteStartElement("li");xml.WriteAttributeString("Class",componentType.FullName);xml.WriteElementString("phinixInventorySaveId","synthetic");xml.WriteElementString("phinixInventorySnapshot",payload);if(journalIdentity!=null)xml.WriteElementString("phinixInventoryJournalIdentity",journalIdentity);xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();xml.WriteEndElement();}stream.Flush(true);}};
            Func<object,string> record=state=>{byte[] data=Encoding.UTF8.GetBytes((string)serialize.Invoke(null,new[]{state}));using(var sha=SHA256.Create())return stateType.GetProperty("Sequence").GetValue(state)+"|"+Convert.ToBase64String(sha.ComputeHash(data))+"|"+Convert.ToBase64String(data)+"\n";};
            string original=record(one)+record(two);File.WriteAllText(log,original);writeSave(json);
            journal=(IDisposable)Activator.CreateInstance(journalType,new object[]{log,two});
            journalIdentity = (string)journalType.GetProperty("Identity",Fields)?.GetValue(journal);
            componentType.GetProperty("JournalIdentity",Fields)?.SetValue(component,journalIdentity);writeSave(json);
            Set(module,"journal",journal);Set(module,"attachedSave",component);Set(module,"attachedIdentity",file);Set(module,"mainThreadId",Thread.CurrentThread.ManagedThreadId);Set(module,"dispatcher",dispatcher);
            var ledger=moduleType.GetField("ledger",Fields).GetValue(module);ledger.GetType().GetMethod("Restore").Invoke(ledger,new[]{two});active.SetValue(null,module);pathProperty.SetValue(null,file);
            var refresh=moduleType.GetMethod("RefreshAfterSave",Fields);
            refresh.Invoke(null,null);dispatcher.Drain();
            Check(File.ReadAllText(log)==record(two),"Actual same-attachment save callback must checkpoint; Attach early return must not suppress maintenance");
            File.WriteAllText(log,original);writeSave((string)serialize.Invoke(null,new[]{one}));refresh.Invoke(null,null);dispatcher.Drain();
            Check(File.ReadAllText(log)==original,"Failed/stale actual save must not reclaim a newer in-memory snapshot");
            writeSave(json);refresh.Invoke(null,null);pathProperty.SetValue(null,Path.Combine(root,"another.rws"));dispatcher.Drain();
            Check(File.ReadAllText(log)==original,"Queued save path switch cannot checkpoint another attachment");
            if (journalType.GetProperty("Identity", Fields) != null)
            {
                var context = new ExtensionHostContext { StorageProvider = new FileSystemExtensionStorageProvider(Path.Combine(root,"storage")) };
                Set(module,"host",context);
                foreach (long sequence in new long[] {2, 0})
                {
                    if (sequence == 0)
                    {
                        journal.Dispose();
                        two=Activator.CreateInstance(stateType); json=(string)serialize.Invoke(null,new[]{two});
                        Set(component,"snapshotJson",json); ledger.GetType().GetMethod("Restore").Invoke(ledger,new[]{two});
                        log=Path.Combine(root,"empty.journal"); journal=(IDisposable)Activator.CreateInstance(journalType,new object[]{log,two});
                        journalIdentity=(string)journalType.GetProperty("Identity",Fields).GetValue(journal);
                        componentType.GetProperty("JournalIdentity",Fields).SetValue(component,journalIdentity);
                        Set(module,"journal",journal);Set(module,"attachedSave",component);Set(module,"attachedIdentity",file);
                    }
                    byte[] sourceBytes=File.ReadAllBytes(log);
                    // Manual Save As, followed by a rotating autosave; both retain the live authority.
                    foreach(string name in new[]{"manual-"+sequence,"autosave-"+sequence})
                    {
                        file=Path.Combine(root,name+".rws"); pathProperty.SetValue(null,file);writeSave(json);
                        refresh.Invoke(null,null);dispatcher.Drain();
                        journal=(IDisposable)moduleType.GetField("journal",Fields).GetValue(module);
                        Check(journal!=null && !(bool)journalType.GetProperty("Faulted").GetValue(journal) &&
                            moduleType.GetField("fault",Fields).GetValue(module)==null,
                            "Verified live Save As must initialize the new path, including sequence zero");
                        Check((string)journalType.GetProperty("Identity").GetValue(journal)==journalIdentity,
                            "Saved branch preserves receipt authority instead of inventing a new identity");
                        string key=(string)moduleType.GetMethod("Hash",Fields).Invoke(null,new object[]{"synthetic|"+file});
                        string target=context.GetStoragePath("builtin.inventory",key+".journal");
                        Check(File.ReadAllText(target+".authority")==journalIdentity,"New marker is durably created");
                        Check((string)serialize.Invoke(null,new[]{ledger.GetType().GetMethod("Export").Invoke(ledger,null)})==json,
                            "Full inventory snapshot, quantities and opaque payload remain unchanged after each save path switch");
                    }
                    Check(Convert.ToBase64String(File.ReadAllBytes(log))==Convert.ToBase64String(sourceBytes),"Branch creation retains source history");
                    string missing=Path.Combine(root,"missing-"+sequence+".journal");
                    using(var cold=(IDisposable)Activator.CreateInstance(journalType,new object[]{missing,two,journalIdentity}))
                        Check((bool)journalType.GetProperty("Faulted").GetValue(cold) && !File.Exists(missing+".authority"),
                            "Cold load with missing authority must remain read only, even for an empty inventory");
                    string unsafeTarget=Path.Combine(root,"orphan-"+sequence+".journal");File.WriteAllText(unsafeTarget,"retain evidence");
                    var openBranch=journalType.GetMethod("OpenSavedBranch",Fields);
                    if(openBranch!=null)
                    {
                        using(var orphan=(IDisposable)openBranch.Invoke(journal,new object[]{unsafeTarget,two,file,"synthetic",journalIdentity,null}))
                            Check((bool)journalType.GetProperty("Faulted").GetValue(orphan),"An existing orphan journal cannot be adopted as a fresh branch");
                        Check(File.ReadAllText(unsafeTarget)=="retain evidence" && !File.Exists(unsafeTarget+".authority"),"Orphan evidence stays unchanged");
                        writeSave((string)serialize.Invoke(null,new[]{one}));
                        string rejected=Path.Combine(root,"unproved-"+sequence+".journal");bool refused=false;
                        try {openBranch.Invoke(journal,new object[]{rejected,two,file,"synthetic",journalIdentity,null});}
                        catch(TargetInvocationException error) {refused=error.InnerException is InvalidDataException;}
                        Check(refused && !File.Exists(rejected) && !File.Exists(rejected+".authority"),"Stale save proof cannot create branch evidence");
                        writeSave(json);
                        string source=(string)journalType.GetField("path",Fields).GetValue(journal);
                        File.Move(source+".authority",source+".authority.kept");
                        try
                        {
                            string noSource=Path.Combine(root,"no-source-"+sequence+".journal");refused=false;
                            try {openBranch.Invoke(journal,new object[]{noSource,two,file,"synthetic",journalIdentity,null});}
                            catch(TargetInvocationException error) {refused=error.InnerException is InvalidDataException;}
                            Check(refused && !File.Exists(noSource),"Missing live source authority cannot authorize a fresh journal");
                        }
                        finally {File.Move(source+".authority.kept",source+".authority");}
                        string pendingPath=Path.Combine(root,"pending-"+sequence+".journal");File.WriteAllText(pendingPath,record(one));
                        using(var pending=(IDisposable)Activator.CreateInstance(journalType,new object[]{pendingPath,Activator.CreateInstance(stateType)}))
                        {
                            string pendingIdentity=(string)journalType.GetProperty("Identity").GetValue(pending);
                            string pendingTarget=Path.Combine(root,"pending-target-"+sequence+".journal");refused=false;
                            try {openBranch.Invoke(pending,new object[]{pendingTarget,Activator.CreateInstance(stateType),file,"synthetic",pendingIdentity,null});}
                            catch(TargetInvocationException error) {refused=error.InnerException is InvalidDataException;}
                            Check((bool)journalType.GetProperty("HasPendingRecovery").GetValue(pending) && refused && !File.Exists(pendingTarget),
                                "Unapproved future journal cannot authorize a saved branch");
                        }
                    }
                }
            }
            if (journalType.GetProperty("Identity", Fields) == null)
            {
                // Real first-attachment path for Workshop legacy saves; no artificial Attach cache hit.
                journal.Dispose(); Set(module,"journal",null); Set(module,"attachedSave",null); Set(module,"attachedIdentity",null);
                var context = new ExtensionHostContext { StorageProvider = new FileSystemExtensionStorageProvider(Path.Combine(root,"storage")) };
                Set(module,"host",context); pathProperty.SetValue(null,file); writeSave(json);
                string key = (string)moduleType.GetMethod("Hash",Fields).Invoke(null,new object[]{"synthetic|"+file});
                string target = context.GetStoragePath("builtin.inventory",key+".journal"); File.WriteAllText(target,original);
                moduleType.GetMethod("Attach",Fields).Invoke(module,new object[]{component});
                journal = (IDisposable)moduleType.GetField("journal",Fields).GetValue(module);
                Check(File.ReadAllText(target)==record(two),"Actual first attachment automatically reclaims verified legacy history without a user action");
            }
            Console.WriteLine("SAVE CALLBACK RESULT passed; checks="+checks+"; actual packaged module/journal; synthetic save, external dispatcher/game shell only; no native GUI claim");
        }
        finally {journal?.Dispose();active.SetValue(null,previousActive);pathProperty.SetValue(null,previousPath);Current.Game=previousGame;Directory.Delete(root,true);}
    }
}
