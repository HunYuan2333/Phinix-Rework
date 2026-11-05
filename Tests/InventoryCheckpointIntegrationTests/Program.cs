using System;
using System.IO;
using System.Linq;
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
    static void CheckFaultChoices(Assembly assembly,object module)
    {
        var previous=LanguageDatabase.activeLanguage;
        try
        {
            var language=(LoadedLanguage)FormatterServices.GetUninitializedObject(typeof(LoadedLanguage));
            typeof(LoadedLanguage).GetField("dataIsLoaded",Fields).SetValue(language,true);
            typeof(LoadedLanguage).GetField("folderName",Fields).SetValue(language,"English");
            typeof(LoadedLanguage).GetField("workerInt",Fields).SetValue(language,new LanguageWorker_English());
            var field=typeof(LoadedLanguage).GetField("keyedReplacements",Fields);
            var values=(System.Collections.IDictionary)Activator.CreateInstance(field.FieldType);
            var replacementType=field.FieldType.GetGenericArguments()[1];
            foreach(string label in new[]{"keepReadOnly","readOnlyKept","discardHistory","discardHistoryWarning","discardHistoryConfirm","saveProofRequired"})
            {
                string key="Phinix_inventory_"+label;object replacement=Activator.CreateInstance(replacementType);
                replacementType.GetField("key").SetValue(replacement,key);replacementType.GetField("value").SetValue(replacement,key);values.Add(key,replacement);
            }
            field.SetValue(language,values);LanguageDatabase.activeLanguage=language;
            Type tabType=assembly.GetType("Phinix.InventoryExtension.Client.InventoryTab",true);
            var tab=FormatterServices.GetUninitializedObject(tabType);
            Set(tab,"inventory",module);
            var confirmationsField=tabType.GetField("batchConfirmations",Fields);
            confirmationsField.SetValue(tab,Activator.CreateInstance(confirmationsField.FieldType));
            var context=new Phinix.InventoryExtension.InventoryPresentationActionContext(0,"fault",null,0,1,"","",null,0);
            var actions=((IEnumerable<ClientActionDefinition>)tabType.GetMethod("BuildActions",Fields).Invoke(tab,new object[]{context})).ToArray();
            Check(actions.Length==2 && actions.Any(a=>a.Id=="keep-readonly") && actions.Single(a=>a.Id=="discard-history").DisabledReason==null,
                "Fault presentation offers only read-only or an eligible explicit discard choice");
            var confirmations=(System.Collections.IDictionary)confirmationsField.GetValue(tab);
            var confirmation=(ClientPresentationConfirmation)confirmations["discard-history"];
            Check(confirmation!=null && confirmation.CancelLabel=="Phinix_inventory_keepReadOnly" && !string.IsNullOrEmpty(confirmation.Text),
                "Destructive choice uses the existing confirmation contract with a read-only cancel action");
            actions.Single(a=>a.Id=="keep-readonly").Execute();
            Check(((Phinix.InventoryExtension.IInventoryReadApi)module).GetStatus().Availability==Phinix.InventoryExtension.InventoryAvailability.ReadOnlyFault,
                "Choosing read-only does not reset or make the inventory writable");
        }
        finally {LanguageDatabase.activeLanguage=previous;}
    }

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
                        if (sequence > 0)
                        {
                            string matching = Path.Combine(root,"matching-unmarked.journal");
                            string matchingBytes = record(one) + record(two);
                            File.WriteAllText(matching, matchingBytes);
                            using(var adopted=(IDisposable)openBranch.Invoke(journal,new object[]{matching,two,file,"synthetic",journalIdentity,null}))
                                Check(!(bool)journalType.GetProperty("Faulted").GetValue(adopted) &&
                                    File.ReadAllText(matching+".authority")==journalIdentity && File.ReadAllText(matching)==matchingBytes,
                                    "A live source and saved-file proof may bind an exactly matching unmarked legacy target without rewriting history");
                            string coldMatching=Path.Combine(root,"cold-matching.journal");File.WriteAllText(coldMatching,matchingBytes);
                            using(var cold=(IDisposable)Activator.CreateInstance(journalType,new object[]{coldMatching,two,journalIdentity}))
                                Check((bool)journalType.GetProperty("Faulted").GetValue(cold) && !File.Exists(coldMatching+".authority"),
                                    "Exact bytes alone cannot authorize an unmarked cold load");
                            foreach(string kind in new[]{"stale", "future", "conflict", "truncated"})
                            {
                                object other=Activator.CreateInstance(stateType);
                                stateType.GetProperty("Sequence").SetValue(other,kind=="future"?3L:2L);
                                ((System.Collections.IList)stateType.GetProperty("Entries").GetValue(other)).Add(
                                    new Phinix.InventoryExtension.InventoryEntry { EntryId="synthetic-entry",CodecId="test.codec",CodecVersion=1,
                                        Payload=new byte[]{1,0,255},Quantity=43,Label="Synthetic",Source="test" });
                                string bytes=kind=="stale"?record(one):kind=="truncated"?matchingBytes+"3|partial":record(other);
                                string target=Path.Combine(root,"unmarked-"+kind+".journal");File.WriteAllText(target,bytes);
                                using(var invalid=(IDisposable)openBranch.Invoke(journal,new object[]{target,two,file,"synthetic",journalIdentity,null}))
                                    Check((bool)journalType.GetProperty("Faulted").GetValue(invalid) && !File.Exists(target+".authority") && File.ReadAllText(target)==bytes,
                                        "Unsafe unmarked target remains untouched and cannot receive authority: "+kind);
                            }
                        }
                        // Reproduce the two-stage user failure through actual saved callbacks.
                        var sourceJournal=journal;
                        file=Path.Combine(root,"blocked-save-"+sequence+".rws");pathProperty.SetValue(null,file);writeSave(json);
                        string blockedKey=(string)moduleType.GetMethod("Hash",Fields).Invoke(null,new object[]{"synthetic|"+file});
                        string blocked=context.GetStoragePath("builtin.inventory",blockedKey+".journal");File.WriteAllText(blocked,"retain orphan evidence");
                        refresh.Invoke(null,null);dispatcher.Drain();
                        journal=(IDisposable)moduleType.GetField("journal",Fields).GetValue(module);
                        Check(ReferenceEquals(journal,sourceJournal) && !(bool)journalType.GetProperty("Faulted").GetValue(journal),
                            "Failed Save As must retain the original healthy journal and its lease");
                        Check(File.ReadAllText(blocked)=="retain orphan evidence" && !File.Exists(blocked+".authority"),
                            "Failed path transition never overwrites orphan evidence");
                        Check(((Phinix.InventoryExtension.IInventoryReadApi)module).GetStatus().Availability==Phinix.InventoryExtension.InventoryAvailability.ReadOnlyFault,
                            "Keeping the recovery anchor does not permit writes under the rejected save path");
                        file=Path.Combine(root,"retry-save-"+sequence+".rws");pathProperty.SetValue(null,file);writeSave(json);
                        refresh.Invoke(null,null);dispatcher.Drain();
                        journal=(IDisposable)moduleType.GetField("journal",Fields).GetValue(module);
                        Check(!ReferenceEquals(journal,sourceJournal) && !(bool)journalType.GetProperty("Faulted").GetValue(journal) &&
                            ((Phinix.InventoryExtension.IInventoryReadApi)module).GetStatus().Availability==Phinix.InventoryExtension.InventoryAvailability.Ready,
                            "A later proved fresh save can recover without inheriting the rejected target's fault");
                        Check((string)serialize.Invoke(null,new[]{ledger.GetType().GetMethod("Export").Invoke(ledger,null)})==json,
                            "Failure and successful retry preserve the full inventory snapshot");
                        // User explicitly chooses the persisted snapshot after a new target fails.
                        file=Path.Combine(root,"discard-save-"+sequence+".rws");pathProperty.SetValue(null,file);writeSave(json);
                        string discardKey=(string)moduleType.GetMethod("Hash",Fields).Invoke(null,new object[]{"synthetic|"+file});
                        string discardPath=context.GetStoragePath("builtin.inventory",discardKey+".journal");File.WriteAllText(discardPath,"unrecoverable later changes");
                        refresh.Invoke(null,null);dispatcher.Drain();
                        Check(File.ReadAllText(discardPath)=="unrecoverable later changes" &&
                            Directory.GetDirectories(Path.GetDirectoryName(discardPath),Path.GetFileName(discardPath)+".discarded-*").Length==0,
                            "Read-only default does not implicitly discard files");
                        CheckFaultChoices(assembly,module);
                        var discardMethod=moduleType.GetMethod("DiscardUnrecoverableHistory",Fields);
                        object[] discardArgs={null};
                        Check((bool)discardMethod.Invoke(module,discardArgs),"Explicit saved-snapshot decision can resolve a faulted target");
                        journal=(IDisposable)moduleType.GetField("journal",Fields).GetValue(module);
                        string backup=Directory.GetDirectories(Path.GetDirectoryName(discardPath),Path.GetFileName(discardPath)+".discarded-*")[0];
                        Check(File.ReadAllText(Path.Combine(backup,"inventory.journal"))=="unrecoverable later changes" &&
                            (string)serialize.Invoke(null,new[]{ledger.GetType().GetMethod("Export").Invoke(ledger,null)})==json &&
                            ((Phinix.InventoryExtension.IInventoryReadApi)module).GetStatus().Availability==Phinix.InventoryExtension.InventoryAvailability.Ready,
                            "Explicit reset retains all saved quantities/payloads, preserves evidence and returns the actual module to ready");
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
