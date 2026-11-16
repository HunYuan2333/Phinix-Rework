using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Data.Common;
using PhinixClient.Storage;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils.Framework;

// Synthetic encoder and transport isolate external game/network dependencies only.
// The actual packaged adapter, business request builder and wire serializer run here.
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }
    private static FrameworkPacket Wire(FrameworkPacket p) => FrameworkSerialization.DeserializePacket(FrameworkSerialization.SerializePacket(p));
    public static int Main(string[] args)
    {
        string journalRoot=Path.Combine(Path.GetTempPath(),"phinix-wire-intents-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var storage=new ClientDatabaseService(owner=>Path.Combine(journalRoot,"sqlite",owner));
            var faults=new FaultStorage(storage);
            var encoder = new Encoder();
            var service = new PhinixFrameworkTradeClientService(encoder, null);
            var host = new Host();
            var type = typeof(PhinixFrameworkTradeClientService).Assembly.GetType("Phinix.TradeExtension.Client.FrameworkClientTradeServiceAdapter", true);
            var adapter = (IClientTradeService)Activator.CreateInstance(type, new object[] { service });
            type.GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(adapter, new object[] { host, host, host, host, null });
            type.GetMethod("ConfigureSaveReadiness",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(adapter,new object[]{(Func<Phinix.InventoryExtension.InventoryStatus>)(()=>new Phinix.InventoryExtension.InventoryStatus{Availability=Phinix.InventoryExtension.InventoryAvailability.Ready,SaveIdentity=new string('a',64)})});
            var journalType=type.Assembly.GetType("Phinix.TradeExtension.Client.TradeIntentJournal",true);
            var journal=Activator.CreateInstance(journalType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{journalRoot,faults,null},null);
            using var journalLifetime=(IDisposable)journal;
            type.GetMethod("ConfigureIntentJournal",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(adapter,new object[]{journal,(Func<string>)(()=>"127.0.0.1:16200")});
            host.BeforeSend=()=>
            {
                var records=((Array)journalType.GetMethod("Read",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(journal,new object[]{"127.0.0.1:16200","synthetic-owner"})).Cast<object>().ToArray();
                Check(records.Length>0&&records.All(record=>(string)journalType.GetMethod("State",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(journal,new[]{record})=="OutcomeUnknown"),"Every transport callback must follow durable SQLite intent and attempt.");
                Check(records.All(record=>!Serialized(record).Contains("synthetic-session")),"Transport session is excluded from durable intent.");
            };
            adapter.UpdateTradeItems("synthetic-trade", Array.Empty<TradeItemSnapshot>(), "synthetic-operation");
            Check(host.Items.Count == 2 && host.Commands.Count == 1, "Whole encoded batch precedes one offer command.");
            Check(host.Order.SequenceEqual(new[] { "item", "item", "command" }), "Offer references must follow item transmission.");
            foreach (var item in host.Items)
            {
                Check(item.MessageType == "builtin.trade", "Item must carry the independently specified negotiated builtin.trade capability.");
                Check(item.SenderUuid == "synthetic-owner" && item.SessionId == "synthetic-session", "Item is scoped to its authenticated sender/session.");
                var payload = FrameworkSerialization.DeserializePayload<FrameworkItemPayload>(item.PayloadJson);
                Check(payload.PayloadBytes.SequenceEqual(encoder.Payloads[host.Items.IndexOf(item)].PayloadBytes), "Opaque item bytes survive wire encoding.");
            }
            var request = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(host.Commands.Single().PayloadJson);
            Check(request.TradeId == "synthetic-trade", "Offer targets requested trade.");
            Check(request.ItemPacketRefs.SequenceEqual(host.Items.Select(i => i.MessageId)), "Offer references the exact transmitted packet identities.");
            Check(request.ItemPacketRefs.Distinct().Count() == 2, "Distinct assets have distinct references.");
            Check(host.Commands.Single().GetCorrelationId() == "synthetic-operation", "Stable operation correlation survives wire encoding.");
            using(var json=JsonDocument.Parse(Serialized(((Array)journalType.GetMethod("Read",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(journal,new object[]{"127.0.0.1:16200","synthetic-owner"})).Cast<object>().Single())))
            {
                Check(json.RootElement.GetProperty("OperationId").GetString()==host.Commands.Single().MessageId,"The exact wire operation ID is durable.");
                Check(json.RootElement.GetProperty("Items").EnumerateArray().Select(i=>i.GetProperty("MessageId").GetString()).SequenceEqual(request.ItemPacketRefs),"Durable staged IDs match the complete wire batch.");
            }
            encoder.Payloads = Array.Empty<FrameworkItemPayload>(); host.Items.Clear(); host.Commands.Clear();
            adapter.UpdateTradeItems("synthetic-trade", Array.Empty<TradeItemSnapshot>(), "empty-operation");
            request = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(host.Commands.Single().PayloadJson);
            Check(host.Items.Count == 0 && request.ItemPacketRefs.Count == 0, "Empty offer sends no invented asset references.");
            if (args.Length == 1)
            {
                encoder.Payloads = new[] { new FrameworkItemPayload { CodecId = "synthetic.opaque", PayloadBytes = new byte[] {17,0,255} } };
                host.Commands.Clear();
                adapter.UpdateTradeItems("synthetic-trade", Array.Empty<TradeItemSnapshot>(), "wire-offer");
                System.IO.Directory.CreateDirectory(args[0]);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(args[0], "item.bin"), FrameworkSerialization.SerializePacket(host.Items.Single()));
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(args[0], "offer.bin"), FrameworkSerialization.SerializePacket(host.Commands.Single()));
            }
            encoder.Payloads=new[]{new FrameworkItemPayload{CodecId="synthetic.opaque",PayloadBytes=new byte[]{7}}};
            faults.FailWrite=true;
            int sent=host.Order.Count;bool rejected=false;
            try {adapter.UpdateTradeItems("synthetic-trade",Array.Empty<TradeItemSnapshot>(),"oversize-operation");}catch(Exception error) when(error.GetType().Name=="TradeRequestNotSentException" && error.InnerException is IOException){rejected=true;}
            Check(rejected&&host.Order.Count==sent,"Journal failure must stop the entire batch before any transport callback.");
            ((IDisposable)adapter).Dispose();
            Console.WriteLine("PASS: " + checks + " packaged adapter wire checks; synthetic FrameworkV2 profile; no game delivery claim.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        finally {if(Directory.Exists(journalRoot))Directory.Delete(journalRoot,true);}
    }
    private static string Serialized(object record)=>(string)typeof(FrameworkSerialization).GetMethod("SerializePayload").MakeGenericMethod(record.GetType()).Invoke(null,new[]{record});
    private sealed class FaultStorage : IClientDatabaseService
    {
        private readonly IClientDatabaseService inner;
        internal bool FailWrite;
        internal FaultStorage(IClientDatabaseService inner){this.inner=inner;}
        public IClientDatabase Open(string owner,string name,DatabaseSchema schema)=>new FaultDatabase(this,inner.Open(owner,name,schema));
        private sealed class FaultDatabase : IClientDatabase
        {
            private readonly FaultStorage owner;private readonly IClientDatabase inner;
            internal FaultDatabase(FaultStorage owner,IClientDatabase inner){this.owner=owner;this.inner=inner;}
            public string Identity=>inner.Identity;
            public T Read<T>(Func<DbConnection,DbTransaction,T> work)=>inner.Read(work);
            public T Write<T>(Func<DbConnection,DbTransaction,T> work)
            {if(owner.FailWrite){owner.FailWrite=false;throw new IOException("Synthetic local journal write failure");}return inner.Write(work);}
            public void Backup(string path)=>inner.Backup(path);
            public void Dispose()=>inner.Dispose();
        }
    }
    private sealed class Encoder : ITradeItemPayloadEncoder
    {
        public FrameworkItemPayload[] Payloads = new[] {
            new FrameworkItemPayload { CodecId = "core.item.scribe-v1", PayloadBytes = new byte[] {17,23} },
            new FrameworkItemPayload { CodecId = "core.item.scribe-v1", PayloadBytes = new byte[] {42,71} }
        };
        public FrameworkItemPayload[] EncodeTradeItems(IEnumerable<TradeItemSnapshot> items) => Payloads;
    }
    private sealed class Host : IFrameworkClientTransport, IFrameworkClientCommandTransport, IFrameworkClientLifecycle, IClientSessionContext
    {
        public readonly List<FrameworkPacket> Items = new List<FrameworkPacket>(), Commands = new List<FrameworkPacket>();
        public readonly List<string> Order = new List<string>();
        public FrameworkCompatibilityMode CompatibilityMode => FrameworkCompatibilityMode.FrameworkV2;
        public event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged { add {} remove {} }
        public bool Authenticated => true; public bool LoggedIn => true;
        public string SessionId => "synthetic-session"; public string Uuid => "synthetic-owner";
        public bool HasRemoteCapability(string value) => value == "builtin.trade";
        public Action BeforeSend;
        public void SendFrameworkPacket(FrameworkPacket p) { BeforeSend?.Invoke(); Items.Add(Wire(p)); Order.Add("item"); }
        public bool TryHandleOutgoingCommand(FrameworkPacket p) { BeforeSend?.Invoke(); Commands.Add(Wire(p)); Order.Add("command"); return true; }
        public bool TryHandleOutgoingMessage(string value) => false;
        public bool TryHandleOutgoingItem(FrameworkItemPayload value) {BeforeSend?.Invoke();return false;}
    }
}
