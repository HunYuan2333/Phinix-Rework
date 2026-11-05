using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Phinix.InventoryExtension;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Trade;
using PhinixClient.Framework;
using Utils.Framework;

internal static partial class Program
{
    private static void CheckSaveReadiness()
    {using(new SaveHintLanguage())CheckSaveReadinessCore();}
    private static void CheckSaveReadinessCore()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        var read=new InventoryHost{Status=new InventoryStatus{Availability=InventoryAvailability.UnsavedGame}};
        var uiType=typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.ClientTradeUiHostContext",true);
        var ui=Activator.CreateInstance(uiType,flags,null,new object[]{null},null);
        ui.GetType().GetField("started",flags).SetValue(ui,true);
        ui.GetType().GetField("inventoryReadApi",flags).SetValue(ui,read);
        var type=typeof(TradeWindow);
        var window=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        void Set(string name,object value)=>type.GetField(name,flags).SetValue(window,value);
        Set("clientHostContext",ui);Set("pendingAcceptedLock",new object());
        Set("rowErrors",Activator.CreateInstance(type.GetField("rowErrors",flags).FieldType));
        Set("availableItems",Activator.CreateInstance(type.GetField("availableItems",flags).FieldType));
        Set("trade",new ClientTradeSnapshot("save-preflight",new UserManagement.ImmutableUser("remote"),null,null,false,false));
        ClientActionDefinition[] Actions()=>((IEnumerable<ClientActionDefinition>)type.GetMethod("PresentationActions",flags).Invoke(window,null)).ToArray();
        var actions=Actions();
        Assert(!string.IsNullOrWhiteSpace(actions.Single(a=>a.Id=="accept").DisabledReason),"Unsaved game must explain why confirmation is blocked before the user can submit it.");
        Assert(actions.Single(a=>a.Id=="submit").DisabledReason==actions.Single(a=>a.Id=="accept").DisabledReason,"Quote and confirmation show the same save-first explanation.");
        Assert(actions.Single(a=>a.Id=="cancel").DisabledReason==null,"An unsaved game does not trap cancellation behind the save guard.");
        type.GetMethod("UpdateTradeItems",flags).Invoke(window,null);
        Assert(!string.IsNullOrWhiteSpace((string)Field(window,"editorNotice")),"Programmatic UI submit is rejected before missing escrow/asset services can run.");
        read.Status=new InventoryStatus{Availability=InventoryAvailability.Ready,SaveIdentity=new string('a',64)};
        Assert(Actions().Single(a=>a.Id=="accept").DisabledReason==null,"Saving and becoming ready enables confirmation without recreating the view.");
        read.Status.Availability=InventoryAvailability.ReadOnlyFault;
        Assert(!string.IsNullOrWhiteSpace(Actions().Single(a=>a.Id=="accept").DisabledReason),"A saved but faulty inventory cannot be mistaken for ready.");
        Set("trade",new ClientTradeSnapshot("save-preflight",new UserManagement.ImmutableUser("remote"),null,null,true,false));
        Assert(Actions().Single(a=>a.Id=="accept").DisabledReason==null,"Readiness guard does not prevent withdrawing existing consent.");

        string root=Path.Combine(Path.GetTempPath(),"phinix-save-preflight-"+Guid.NewGuid().ToString("N"));
        try
        {
            var service=new PhinixFrameworkTradeClientService(null,new Users(),null);
            var packet=TradeSnapshot("save-preflight",null);
            var snapshot=FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(packet.PayloadJson);
            snapshot.Trades[0].SnapshotVersion=2;packet.PayloadJson=FrameworkSerialization.SerializePayload(snapshot);service.HandleSnapshot(packet);
            ClientTradeSnapshot displayed;Assert(service.TryGetTrade("save-preflight",out displayed),"Real observed quote exists for guard test.");
            using(var host=new TradeHost())
            {
                var adapterType=type.Assembly.GetType("Phinix.TradeExtension.Client.FrameworkClientTradeServiceAdapter",true);
                var adapter=Activator.CreateInstance(adapterType,new object[]{service});
                adapterType.GetMethod("Initialize",flags).Invoke(adapter,new object[]{host,host,host,host,null});
                bool missingGuardRefused=false;
                try{((IClientTradeService)adapter).UpdateTradeItems("save-preflight",Array.Empty<TradeItemSnapshot>());}
                catch(InvalidOperationException error){missingGuardRefused=error.Message=="Phinix_trade_saveBeforeTrading";}
                Assert(missingGuardRefused&&host.Commands.Count==0,"Missing readiness provider fails closed before any send.");
                adapterType.GetMethod("ConfigureSaveReadiness",flags).Invoke(adapter,new object[]{(Func<InventoryStatus>)(()=>read.Status)});
                var journalType=type.Assembly.GetType("Phinix.TradeExtension.Client.TradeIntentJournal",true);
                var journal=Activator.CreateInstance(journalType,flags,null,new object[]{root},null);
                adapterType.GetMethod("ConfigureIntentJournal",flags).Invoke(adapter,new object[]{journal,(Func<string>)(()=>host.RemoteEndpoint)});
                int enumerated=0;
                IEnumerable<TradeItemSnapshot> Items(){enumerated++;yield break;}
                void Refused(Action action)
                {
                    Exception failure=null;try{action();}catch(TargetInvocationException error){failure=error.InnerException;}catch(InvalidOperationException error){failure=error;}
                    Assert(failure is InvalidOperationException&&failure.Message.StartsWith("Phinix_trade_",StringComparison.Ordinal),"Public business port gives an explicit readiness reason before any send.");
                }
                read.Status=new InventoryStatus{Availability=InventoryAvailability.UnsavedGame};
                Refused(()=>((IClientTradeService)adapter).UpdateTradeItems("save-preflight",Items(),"unsaved"));
                Refused(()=>adapterType.GetMethod("AcceptObservedTrade").Invoke(adapter,new object[]{displayed}));
                Assert(enumerated==0&&host.Commands.Count==0&&Directory.GetFiles(root).Length==0,"Unsaved rejection neither enumerates items nor writes intent/attempt or sends packets.");
                read.Status=new InventoryStatus{Availability=InventoryAvailability.ReadOnlyFault,SaveIdentity=new string('a',64)};
                Refused(()=>((IClientTradeService)adapter).UpdateTradeItems("save-preflight",Items(),"faulted"));
                Assert(enumerated==0&&host.Commands.Count==0,"Faulted inventory is rejected before asset preparation too.");
                read.Status.Availability=InventoryAvailability.Ready;
                adapterType.GetMethod("AcceptObservedTrade").Invoke(adapter,new object[]{displayed});
                Assert(host.Commands.Count==1&&Directory.GetFiles(root,"*.intent").Length==1,"Ready guard preserves the original durable intent/send path.");
                read.Status.Availability=InventoryAvailability.UnsavedGame;read.Status.SaveIdentity=null;
                ((IClientTradeService)adapter).UpdateTradeStatus("save-preflight",accepted:false);
                ((IClientTradeService)adapter).CancelTrade("save-preflight");
                Assert(host.Commands.Count==3,"Withdrawal and cancel remain available without granting new consent.");
                ((IDisposable)adapter).Dispose();
            }
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    // Isolate language loading/native logging, not admission or asset behavior.
    private sealed class SaveHintLanguage:IDisposable
    {
        private readonly Verse.LoadedLanguage previous=Verse.LanguageDatabase.activeLanguage;
        internal SaveHintLanguage()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var language=(Verse.LoadedLanguage)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.LoadedLanguage));
            typeof(Verse.LoadedLanguage).GetField("dataIsLoaded",flags).SetValue(language,true);
            typeof(Verse.LoadedLanguage).GetField("folderName",flags).SetValue(language,"English");
            typeof(Verse.LoadedLanguage).GetField("workerInt",flags).SetValue(language,new Verse.LanguageWorker_English());
            var field=typeof(Verse.LoadedLanguage).GetField("keyedReplacements",flags);
            var replacements=(System.Collections.IDictionary)Activator.CreateInstance(field.FieldType);
            var replacementType=field.FieldType.GetGenericArguments()[1];
            foreach(string name in new[]{"editorSubmitQuote","editorWithdraw","confirmTrade","resetButton","cancelButton","editorRefresh","quoteEmpty","editorSelectQuantity","editorWaiting","saveBeforeTrading","inventoryUnavailableBeforeTrading"})
            {
                string key="Phinix_trade_"+name;object replacement=Activator.CreateInstance(replacementType);
                replacementType.GetField("key").SetValue(replacement,key);replacementType.GetField("value").SetValue(replacement,key);replacements.Add(key,replacement);
            }
            field.SetValue(language,replacements);Verse.LanguageDatabase.activeLanguage=language;
        }
        public void Dispose(){Verse.LanguageDatabase.activeLanguage=previous;}
    }
}
