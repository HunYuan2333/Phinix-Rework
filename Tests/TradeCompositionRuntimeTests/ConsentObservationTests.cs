using System;
using System.Reflection;
using System.Linq;
using Phinix.TradeExtension.Client;
using Phinix.TradeExtension;
using PhinixClient.Trade;
using Utils.Framework;

internal static partial class Program
{
    private static void CheckConsentObservation()
    {
        CheckCompletionInboxIntegration();
        CheckLegacyIntentIsolation();
        var service = new PhinixFrameworkTradeClientService(null, new Users(), null);
        Action<long> receive = version =>
        {
            var packet = TradeSnapshot("observed", null);
            var payload = FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(packet.PayloadJson);
            payload.Trades[0].SnapshotVersion = version;
            packet.PayloadJson = FrameworkSerialization.SerializePayload(payload);
            service.HandleSnapshot(packet);
        };
        var type = typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.TradeSnapshotObservation", true);
        var require = type.GetMethod("Require", BindingFlags.Static | BindingFlags.Public);
        receive(2); ClientTradeSnapshot displayed;
        Assert(service.TryGetTrade("observed", out displayed), "The displayed quote exists.");
        receive(3); ClientTradeSnapshot newest;
        Assert(service.TryGetTrade("observed", out newest), "The newer quote exists.");
        Assert((long)require.Invoke(null, new object[] { displayed }) == 2, "Background update cannot change the version of the displayed quote.");
        Assert((long)require.Invoke(null, new object[] { newest }) == 3, "A newly displayed quote carries its own revision.");
        using (var host = new TradeHost())
        {
            var adapterType = typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.FrameworkClientTradeServiceAdapter", true);
            var adapter = Activator.CreateInstance(adapterType, new object[] { service });
            adapterType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(adapter, new object[] { host, host, host, host, null });
            adapterType.GetMethod("ConfigureSaveReadiness",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(adapter,new object[]{(Func<Phinix.InventoryExtension.InventoryStatus>)(()=>new Phinix.InventoryExtension.InventoryStatus{Availability=Phinix.InventoryExtension.InventoryAvailability.Ready,SaveIdentity=new string('a',64)})});
            adapterType.GetMethod("AcceptObservedTrade").Invoke(adapter, new object[] { displayed });
            long sentVersion;
            Assert(host.Commands.Single().TryGetSnapshotVersion(out sentVersion) && sentVersion == 2, "Actual consent packet carries the displayed revision even when repository has advanced.");
            bool unboundRefused = false;
            try { ((IClientTradeService)adapter).UpdateTradeStatus("observed", accepted: true); }
            catch (InvalidOperationException) { unboundRefused = true; }
            Assert(unboundRefused && host.Commands.Count == 1, "The old unbound positive-confirmation API cannot bypass observation.");
            ((IClientTradeService)adapter).UpdateTradeStatus("observed", accepted: false);
            Assert(host.Commands.Count == 2, "Withdrawal remains possible without granting consent.");
            ((IDisposable)adapter).Dispose();
        }
        receive(0); ClientTradeSnapshot unversioned; service.TryGetTrade("observed", out unversioned);
        bool refused = false;
        try { require.Invoke(null, new object[] { unversioned }); }
        catch (TargetInvocationException e) { refused = e.InnerException is InvalidOperationException; }
        Assert(refused, "An unversioned quote cannot manufacture confirmation consent.");
        using (var host = new TradeHost())
        {
            var adapterType = typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.FrameworkClientTradeServiceAdapter", true);
            var adapter = Activator.CreateInstance(adapterType, new object[] { service });
            adapterType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(adapter, new object[] { host, host, host, host, null });
            adapterType.GetMethod("ConfigureSaveReadiness", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(adapter,
                new object[] { (Func<Phinix.InventoryExtension.InventoryStatus>)(() => new Phinix.InventoryExtension.InventoryStatus
                { Availability = Phinix.InventoryExtension.InventoryAvailability.Ready, SaveIdentity = new string('a', 64) }) });
            var accept = adapterType.GetMethod("AcceptObservedTrade");
            refused = false;
            try { accept.Invoke(adapter, new object[] { unversioned }); }
            catch (TargetInvocationException error) { refused = error.InnerException is InvalidOperationException; }
            Assert(refused && host.Commands.Count == 0, "Modern connection still refuses an unversioned quote before sending.");
            host.Mode = FrameworkCompatibilityMode.Legacy;
            accept.Invoke(adapter, new object[] { unversioned });
            Assert(host.Commands.Count == 1 && !host.Commands[0].TryGetSnapshotVersion(out _),
                "Known Legacy connection sends current quote consent without inventing a version.");
            var changedQuote = service.GetRepositoryTrades().Single(trade => trade.TradeId == "observed");
            changedQuote.Participants[0].Accepted = true;
            service.UpsertTrade(changedQuote);
            refused = false;
            try { accept.Invoke(adapter, new object[] { unversioned }); }
            catch (TargetInvocationException error) { refused = error.InnerException is InvalidOperationException; }
            Assert(refused && host.Commands.Count == 1, "A superseded Legacy quote must be redisplayed before consent.");
            ((IDisposable)adapter).Dispose();
        }
        var placeholder = new ClientTradeSnapshot("placeholder", new UserManagement.ImmutableUser("remote"), null, null, false, false);
        refused = false;
        try { require.Invoke(null, new object[] { placeholder }); }
        catch (TargetInvocationException e) { refused = e.InnerException is InvalidOperationException; }
        Assert(refused, "An unbound placeholder cannot manufacture confirmation consent.");
    }
    private static void CheckLegacyIntentIsolation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phinix-legacy-intents-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pipelineType = typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.TradeClientItemPipeline", true);
            var pipeline = (ITradeItemPayloadEncoder)Activator.CreateInstance(pipelineType, null, FrameworkCompatibilityMode.Legacy, null);
            var service = new PhinixFrameworkTradeClientService(pipeline, new Users(), null);
            service.HandleSnapshot(TradeSnapshot("legacy-capacity", null));
            using (var host = new TradeHost())
            {
                var type = typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.FrameworkClientTradeServiceAdapter", true);
                var adapter = Activator.CreateInstance(type, new object[] { service });
                type.GetMethod("Initialize", flags).Invoke(adapter, new object[] { host, host, host, host, null });
                type.GetMethod("ConfigureSaveReadiness", flags).Invoke(adapter, new object[] {
                    (Func<Phinix.InventoryExtension.InventoryStatus>)(() => new Phinix.InventoryExtension.InventoryStatus {
                        Availability = Phinix.InventoryExtension.InventoryAvailability.Ready, SaveIdentity = new string('a',64) }) });
                var journalType = type.Assembly.GetType("Phinix.TradeExtension.Client.TradeIntentJournal", true);
                var journal = Activator.CreateInstance(journalType, flags, null, new object[] { root }, null);
                type.GetMethod("ConfigureIntentJournal", flags).Invoke(adapter, new object[] { journal, (Func<string>)(() => host.RemoteEndpoint) });
                var facade = (IClientTradeService)adapter;
                bool full = false;
                for (int n = 0; n < 100 && !full; n++)
                {
                    int before = host.Commands.Count;
                    try { facade.CreateTrade("remote"); }
                    catch (System.IO.IOException error)
                    {
                        Assert(error.Message.Contains("headroom"), "Regression reaches the actual result reserve admission limit.");
                        Assert(host.Commands.Count == before, "Modern full intent journal refuses before transport.");
                        full = true;
                    }
                }
                Assert(full, "Real journal reaches reserved result capacity within bounded requests.");
                var evidence = System.IO.Directory.GetFiles(root).ToDictionary(f => f, System.IO.File.ReadAllBytes);
                int sent = host.Commands.Count;
                host.Mode = FrameworkCompatibilityMode.Legacy;
                facade.CreateTrade("remote");
                facade.UpdateTradeItems("legacy-capacity", Array.Empty<TradeItemSnapshot>(), "legacy-offer");
                facade.CancelTrade("legacy-capacity");
                service.TryGetTrade("legacy-capacity", out ClientTradeSnapshot displayed);
                type.GetMethod("AcceptObservedTrade", flags).Invoke(adapter, new object[] { displayed });
                Assert(host.Commands.Count == sent + 4, "Known original endpoint can create, quote, cancel and confirm without modern result reservations.");
                Assert(System.IO.Directory.GetFiles(root).Length == evidence.Count && evidence.All(pair => System.IO.File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)),
                    "Legacy routing neither adds modern intents nor deletes or mutates historical recovery evidence.");
                host.Mode = FrameworkCompatibilityMode.FrameworkV2;
                bool refused = false;
                try { facade.CreateTrade("remote"); } catch (System.IO.IOException) { refused = true; }
                Assert(refused && host.Commands.Count == sent + 4, "Switching back retains strict modern durability admission.");
                host.Mode = FrameworkCompatibilityMode.Legacy;
                Assert(!(bool)type.GetProperty("CanQueryOperation", flags).GetValue(adapter), "Original endpoint cannot advertise modern operation lookup through stale capabilities.");
                type.GetMethod("AcknowledgeCompletion", flags).Invoke(adapter, new object[] { "legacy-capacity", false });
                Assert(host.Commands.Count == sent + 4, "Legacy delivery cannot synthesize an unsupported completion ACK.");
                host.Mode = FrameworkCompatibilityMode.FrameworkV2;
                type.GetMethod("AcknowledgeCompletion", flags).Invoke(adapter, new object[] { "legacy-capacity", false });
                Assert(host.Commands.Count == sent + 5 && host.Commands.Last().MessageType == FrameworkTradeProtocol.CompletionAckRequestType,
                    "Modern delivery retains its real completion ACK route.");
                ((IDisposable)adapter).Dispose();
            }
        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    private static void CheckCompletionInboxIntegration()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phinix-inbox-integration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inboxType = typeof(TradeWindow).Assembly.GetType("Phinix.TradeExtension.Client.TradeCompletionInbox", true);
            var inbox = Activator.CreateInstance(inboxType, new object[] { root });
            var service = new PhinixFrameworkTradeClientService(null, new Users(), null);
            var configure = service.GetType().GetMethod("ConfigureCompletionInbox", BindingFlags.Instance | BindingFlags.NonPublic);
            configure.Invoke(service, new object[] { inbox, (Func<string>)(() => "127.0.0.1:16200") });
            int deliveries = 0;
            service.OnTradeCancelled += (_, args) =>
            {
                Assert(System.IO.Directory.GetFiles(root, "*.receipt").Length == 1, "Actual service records the batch before delivery callbacks.");
                deliveries++;
            };
            service.CompleteTrade("receipt-trade", false, "remote", Array.Empty<FrameworkItemPayload>());
            service.CompleteTrade("receipt-trade", false, "remote", Array.Empty<FrameworkItemPayload>());
            Assert(deliveries == 1, "Duplicate completion cannot repeat delivery callbacks.");
            var restarted = new PhinixFrameworkTradeClientService(null, new Users(), null);
            configure.Invoke(restarted, new object[] { Activator.CreateInstance(inboxType, new object[] { root }), (Func<string>)(() => "127.0.0.1:16200") });
            restarted.OnTradeCancelled += (_, args) => deliveries++;
            restarted.CompleteTrade("receipt-trade", false, "remote", Array.Empty<FrameworkItemPayload>());
            Assert(deliveries == 1, "A new service instance preserves recorded receipt without automatic redelivery.");
            var target = System.IO.Directory.GetFiles(root, "*.receipt").Single(); System.IO.File.WriteAllText(target, "corrupt");
            bool refused = false;
            try { restarted.CompleteTrade("receipt-trade", false, "remote", Array.Empty<FrameworkItemPayload>()); }
            catch { refused = true; }
            Assert(refused && deliveries == 1, "Corrupt evidence must not fall through to business delivery.");
            var previousGame=Verse.Current.Game;
            try
            {
                var scopeType=typeof(PhinixClient.Client).Assembly.GetType("PhinixClient.Framework.ClientGameScopeService",true);
                var scopes=Activator.CreateInstance(scopeType,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{(Func<object>)(()=>Verse.Current.Game),(Func<bool>)(()=>true)},null);
                Verse.Current.Game=(Verse.Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Game));
                scopeType.GetMethod("Publish",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(scopes,null);
                var receiving=new PhinixFrameworkTradeClientService(null,new Users(),null);
                var saveScopes=new ReceiptSaveScopeProbe {Identity=new string('a',64)};
                receiving.GetType().GetMethod("ConfigureReceiveScopes",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(receiving,new object[]{saveScopes});
                receiving.GetType().GetMethod("ConfigureGameScopes",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(receiving,new[]{scopes});
                configure.Invoke(receiving,new object[]{Activator.CreateInstance(inboxType,new object[]{System.IO.Path.Combine(root,"scope")}), (Func<string>)(()=>"127.0.0.1:16200")});
                int scopedEvents=0;
                receiving.OnTradeCancelled+=(_,args)=>
                {
                    var captured=(PhinixClient.Framework.IClientGameScope)args.GetType().GetField("GameScope",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(args);
                    Assert(captured!=null&&!captured.IsCurrent(),"Receipt scope must be captured before payload enumeration and durable IO.");
                    var capturedSave=(Phinix.InventoryExtension.IInventoryReceiveScope)args.GetType().GetField("SaveScope",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(args);
                    Assert(capturedSave!=null&&capturedSave.SaveIdentity==new string('a',64)&&!capturedSave.IsCurrent(),"The original inventory save scope must travel with the event across enumeration and IO.");
                    scopedEvents++;
                };
                receiving.CompleteTrade("scope-before-io",false,"remote",ChangeDuringReceipt(()=>{saveScopes.Identity=new string('b',64);Verse.Current.Game=(Verse.Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Verse.Game));}));
                Assert(scopedEvents==1,"Real service emits the originally captured scope exactly once.");
            }
            finally {Verse.Current.Game=previousGame;}

        }
        finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
    }

    private sealed class ReceiptSaveScopeProbe : Phinix.InventoryExtension.IInventoryReceiveScopeApi
    {
        public string Identity;
        public Phinix.InventoryExtension.IInventoryReceiveScope CaptureReceiveScope() => new Snapshot(this,Identity);
        private sealed class Snapshot : Phinix.InventoryExtension.IInventoryReceiveScope
        {
            private readonly ReceiptSaveScopeProbe owner;
            public string SaveIdentity {get;}
            public Snapshot(ReceiptSaveScopeProbe owner,string id){this.owner=owner;SaveIdentity=id;}
            public bool IsCurrent() => owner.Identity==SaveIdentity;
        }
    }
    private static System.Collections.Generic.IEnumerable<FrameworkItemPayload> ChangeDuringReceipt(Action change)
    {change();yield break;}
}
