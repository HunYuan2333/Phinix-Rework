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
        var placeholder = new ClientTradeSnapshot("placeholder", new UserManagement.ImmutableUser("remote"), null, null, false, false);
        refused = false;
        try { require.Invoke(null, new object[] { placeholder }); }
        catch (TargetInvocationException e) { refused = e.InnerException is InvalidOperationException; }
        Assert(refused, "An unbound placeholder cannot manufacture confirmation consent.");
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
