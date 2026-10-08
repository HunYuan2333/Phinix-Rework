using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhinixClient;
using PhinixClient.Framework;
using Phinix.InventoryExtension;
using Utils.Framework;
using Verse;

internal static class Program
{
    private static int assertions;
    private static void Assert(bool value,string message) { assertions++; if(!value) throw new InvalidOperationException(message); }
    private static int Subscribers(object module,string name)
    { var value=(Delegate)module.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(module); return value?.GetInvocationList().Length??0; }
    private static int Main(string[] args)
    {
        try
        {
            bool legacy=args.Length>0 && args[0]=="--legacy";
            string dll=args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"InventoryExtension.Client.dll");
            var assembly=Assembly.LoadFrom(Path.GetFullPath(dll));
            Type type=assembly.GetType("Phinix.InventoryExtension.Client.BuiltInInventoryClientExtension",true);
            var facts=new List<string>();
            foreach(string scenario in legacy?new[]{"success"}:new[]{"success","resolve-failure","register-failure","missing-host"})
            {
                var host=new ExtensionHostContext(); var services=new Services();
                using(var inner=new ClientCompositionFactory(()=>true,error=>{throw error;}))
                {
                    var factory=new Factory(inner,scenario=="resolve-failure");
                    host.AddService<IClientCompositionFactory>(factory);
                    host.AddService<IClientSettingsContext>(services);
                    host.AddService<IClientMainThreadDispatcher>(services);
                    host.AddService<IClientShellEventStream>(services);
                    if(scenario!="missing-host") host.AddService<IClientWindowService>(services);
                    var module=(IPhinixExtensionModule)Activator.CreateInstance(type);
                    object ledger=type.GetField("ledger",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(module);
                    var builder=new Builder(host,scenario=="register-failure"); bool failed=false;
                    try { module.Register(builder); } catch(Exception) { failed=true; }
                    Assert(failed==(scenario!="success"),"Expected compose outcome: "+scenario);
                    Assert(services.Queued==0 && services.Opened==0,"Registration starts no game/UI work.");
                    Assert(services.Subscribers==0,"Shell subscription still belongs to activation.");
                    if(scenario=="success")
                    {
                        Type[] apiTypes={typeof(IInventoryApi),typeof(IInventoryRegistrationApi),typeof(IInventoryDepositApi),typeof(IInventoryReadApi),typeof(IInventoryExtractionApi),typeof(IInventoryReservationApi)};
                        foreach(Type api in apiTypes) Assert(ReferenceEquals(builder.Items[api],module),"Facade identity preserved: "+api.Name);
                        Assert(builder.Items.Count==9,"Exactly the original nine API registrations.");
                        Assert(ReferenceEquals(builder.Items[typeof(IClientSettingsPanelProvider)],builder.Items[typeof(IClientQuickSettingsPanelProvider)]),"Settings and quick settings use one provider.");
                        Assert(Subscribers(module,"InventoryChanged")==1 && Subscribers(module,"AvailabilityChanged")==1,"Tab invalidation subscriptions installed once.");
                        var read=(IInventoryReadApi)module;
                        var capabilities=read.GetCapabilities();
                        facts.Add("apis="+string.Join(",",builder.Items.OrderBy(p=>p.Key.FullName).Select(p=>p.Key.FullName+":"+p.Value.GetType().FullName)));
                        facts.Add("caps="+string.Join(",",capabilities.GetType().GetProperties().OrderBy(p=>p.Name).Select(p=>p.Name+":"+p.GetValue(capabilities))));
                        Assert(read.GetStatus().Availability==InventoryAvailability.Inactive,"Pre-activation availability remains inactive.");
                        facts.Add("status="+read.GetStatus().Availability);
                        var registration=(IInventoryRegistrationApi)module; int events=0;
                        EventHandler observer=(sender,eventArgs)=>events++; read.AvailabilityChanged+=observer;
                        var codec=new Codec(); var token=registration.RegisterCodecScoped(codec);
                        bool duplicate=false; try { registration.RegisterCodecScoped(new Codec()); } catch(InvalidOperationException) { duplicate=true; }
                        Assert(duplicate,"Duplicate codec rejection preserved.");
                        token.Dispose(); token.Dispose();
                        Assert(codec.Disposed==0,"Codec tokens remove registration without disposing borrowed codecs.");
                        using(registration.RegisterCodecScoped(new Codec())) { }
                        read.AvailabilityChanged-=observer;
                        facts.Add("codecEvents="+events); Assert(events==4,"Scoped registration event counts and idempotence preserved.");
                        if(!legacy)
                        {
                            foreach(var item in builder.Items.Where(p=>p.Key==typeof(IMainTabProvider) || p.Key==typeof(IClientSettingsPanelProvider)))
                            {
                                object resolved=typeof(Scope).GetMethod("Resolve").MakeGenericMethod(item.Value.GetType()).Invoke(factory.Scope,null);
                                Assert(ReferenceEquals(resolved,item.Value),"Owned UI services resolve to one published instance.");
                            }
                            Assert(ReferenceEquals(factory.Scope.Resolve<IClientSettingsContext>(),services),"Settings dependency remains the original borrowed host instance.");
                        }
                    }
                    ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                    ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                    Assert(services.Disposed==0,"Borrowed host services never disposed.");
                    Assert(ReferenceEquals(ledger,type.GetField("ledger",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(module)),"Ledger ownership and identity unchanged.");
                    if(!legacy)
                    {
                        Assert(Subscribers(module,"InventoryChanged")==0 && Subscribers(module,"AvailabilityChanged")==0,"Owned tab detaches on stop and partial failure.");
                        Assert(factory.Created==0 || factory.Scope.Disposes==1,"Scope is disposed once, including failed resolution/registration.");
                    }
                }
            }
            using(var inner=new ClientCompositionFactory(()=>true,error=>{}))
            {
                var host=new ExtensionHostContext(); var services=new Services(); var factory=new Factory(inner,false);
                host.AddService<IClientCompositionFactory>(factory);
                host.AddService<IClientSettingsContext>(services); host.AddService<IClientMainThreadDispatcher>(services);
                host.AddService<IClientShellEventStream>(services); host.AddService<IClientWindowService>(services);
                host.AddService<IExtensionDiscoveryPolicy>(new Discovery(type));
                var disabled=PhinixExtensionRegistry.DiscoverExtensions(host,new Disabled());
                Assert(disabled.Modules.Count==0 && disabled.ExtensionResults.Single().State==ExtensionModuleState.Disabled && factory.Created==0,
                    "The ordinary registry honors disabled modules before composition.");
                var registered=PhinixExtensionRegistry.DiscoverExtensions(host);
                Assert(registered.Modules.Count==1 && registered.ExtensionResults.Single().State==ExtensionModuleState.Registered,"Inventory uses ordinary module discovery and registration.");
                Assert(registered.ApiRegistry.ResolveAll<IInventoryApi>().Count==1,"The registry publishes one inventory facade.");
                Assert(services.Queued==0 && services.Subscribers==0,"Passive discovery starts no activation work.");
                PhinixExtensionRegistry.ShutdownExtensions(registered,host);
                Assert(registered.ApiRegistry.ResolveAll<IInventoryApi>().Count==0,"Ordinary registry stop removes published APIs.");
                if(!legacy)
                {
                    var failing=new Factory(inner,true); host.AddService<IClientCompositionFactory>(failing);
                    var rejected=PhinixExtensionRegistry.DiscoverExtensions(host);
                    Assert(rejected.Modules.Count==0 && rejected.ExtensionResults.Single().State==ExtensionModuleState.Failed,
                        "Ordinary registration failure rejects the module.");
                    Assert(rejected.ApiRegistry.ResolveAll<IInventoryApi>().Count==0 && failing.Scope.Disposes==1,
                        "Ordinary failure rollback removes APIs and disposes its scope.");
                }
                Assert(services.Disposed==0,"Registry stop and rollback preserve borrowed host services.");
            }
            if(!legacy) { CheckSaveLifetime(type,assembly); CheckMenuLifetime(type,assembly); }
            foreach(string fact in facts) Console.WriteLine("FACT "+fact);
            Console.WriteLine("Inventory composition passed: "+assertions+" assertions; "+(legacy?"before":"after")); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void CheckSaveLifetime(Type type,Assembly assembly)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var active=type.GetField("active",BindingFlags.NonPublic|BindingFlags.Static);
        var componentType=assembly.GetType("Phinix.InventoryExtension.Client.InventoryGameComponent",true);
        var component=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(componentType);
        var belongs=componentType.GetMethod("BelongsTo",BindingFlags.NonPublic|BindingFlags.Static);
        var first=(Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));
        var second=(Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));
        first.components=new List<GameComponent>{(GameComponent)component}; second.components=new List<GameComponent>();
        Assert((bool)belongs.Invoke(null,new[]{component,(object)first}),"Restored component belongs to its game list.");
        Assert(!(bool)belongs.Invoke(null,new[]{component,(object)second}),"Old component cannot bind to another game.");
        Assert(!(bool)belongs.Invoke(null,new[]{component,null}),"No game cannot own a component.");
        var host=new ExtensionHostContext(); var services=new Services();
        using(var inner=new ClientCompositionFactory(()=>true,error=>{}))
        {
            var factory=new Factory(inner,false);
            host.AddService<IClientCompositionFactory>(factory); host.AddService<IClientSettingsContext>(services);
            host.AddService<IClientMainThreadDispatcher>(services); host.AddService<IClientShellEventStream>(services);
            host.AddService<IClientWindowService>(services);
            var module=(IPhinixExtensionModule)Activator.CreateInstance(type); module.Register(new Builder(host,false));
            try
            {
                active.SetValue(null,module);
                type.GetMethod("QueueAttach",flags).Invoke(module,new[]{component});
                Assert(services.Actions.Count==1,"Attachment is queued until game thread binding.");
                services.ThrowOnRemove=true; bool failed=false;
                try { ((IActivatablePhinixExtensionModule)module).Shutdown(host); } catch(AggregateException) { failed=true; }
                Assert(failed,"Cleanup failure is reported.");
                Assert(active.GetValue(null)==null && type.GetField("dispatcher",flags).GetValue(module)==null,"Failed cleanup still invalidates activation and dispatcher.");
                Assert(factory.Scope.Disposes==1 && Subscribers(module,"InventoryChanged")==0,"Failed shell cleanup still releases owned scope and UI.");
                // Simulate reuse of the same module identity: old work must remain invalid.
                active.SetValue(null,module); type.GetField("dispatcher",flags).SetValue(module,services);
                services.Actions[0]();
                Assert((int)type.GetField("mainThreadId",flags).GetValue(module)==0,"Prior activation work cannot bind a reused module.");
                Assert(type.GetField("attachedSave",flags).GetValue(module)==null,"Stale callback cannot publish a save attachment.");
            }
            finally
            {
                services.ThrowOnRemove=false;
                ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                active.SetValue(null,null);
            }
            Assert(factory.Scope.Disposes==1 && services.Disposed==0,"Repeated stop preserves scope and borrowed ownership.");
        }
    }
    private static void CheckMenuLifetime(Type type,Assembly assembly)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var module=(IInventoryReadApi)Activator.CreateInstance(type);
        var componentType=assembly.GetType("Phinix.InventoryExtension.Client.InventoryGameComponent",true);
        var component=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(componentType);
        var game=(Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));
        game.components=new List<GameComponent>{(GameComponent)component};
        var other=(Game)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Game));
        other.components=new List<GameComponent>();
        var stateType=assembly.GetType("Phinix.InventoryExtension.Client.InventoryState",true);
        var journalType=assembly.GetType("Phinix.InventoryExtension.Client.InventoryJournal",true);
        var state=Activator.CreateInstance(stateType);
        stateType.GetProperty("Sequence").SetValue(state,1L);
        stateType.GetProperty("Entries").SetValue(state,new List<InventoryEntry>{new InventoryEntry
        {EntryId="entry",CodecId="test",CodecVersion=1,Payload=new byte[]{1},Quantity=7,Label="Test",Source="test"}});
        object ledger=type.GetField("ledger",flags).GetValue(module);
        ledger.GetType().GetMethod("Restore",flags|BindingFlags.Public).Invoke(ledger,new[]{state});
        string directory=Path.Combine(Path.GetTempPath(),"phinix-inventory-lifetime-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"save.journal");
        object empty=Activator.CreateInstance(stateType);
        var journal=(IDisposable)Activator.CreateInstance(journalType,new[]{(object)path,empty});
        try
        {
            Assert((bool)journalType.GetMethod("TryAppend").Invoke(journal,new[]{state}),"Test transaction durably appended.");
            byte[] durable=File.ReadAllBytes(path);
            journal.Dispose(); journal=(IDisposable)Activator.CreateInstance(journalType,new[]{(object)path,empty});
            Assert((bool)journalType.GetProperty("HasPendingRecovery").GetValue(journal),"Lifecycle fixture contains pending recovery.");
            // Mono cannot load Verse.Current with this compile-only reference set (missing firstpass).
            // It exercises the inactive guard; .NET additionally exercises the active/no-game guard.
            if(Type.GetType("Mono.Runtime")==null)
                type.GetField("active",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,module);
            type.GetField("mainThreadId",flags).SetValue(module,System.Threading.Thread.CurrentThread.ManagedThreadId);
            type.GetField("attachedSave",flags).SetValue(module,component);
            type.GetField("attachedIdentity",flags).SetValue(module,"old-save.rws");
            type.GetField("journal",flags).SetValue(module,journal);
            int changed=0,availability=0; module.InventoryChanged+=(sender,args)=>changed++;
            module.AvailabilityChanged+=(sender,args)=>availability++;
            var sync=type.GetMethod("SynchronizeGame",flags);
            sync.Invoke(module,new object[]{game});
            Assert(module.GetSnapshot().Count==1 && changed==0,"Same game retains its inventory without notifications.");
            bool locked=false; try { using(var file=new FileStream(path+".lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { } }
            catch(IOException) { locked=true; }
            Assert(locked,"Same game retains journal exclusivity.");
            Assert(!(bool)type.GetMethod("ApproveRecovery",flags).Invoke(module,null) &&
                !(bool)type.GetMethod("RejectRecovery",flags).Invoke(module,null),"Unattached or inactive save cannot approve/discard recovery.");
            sync.Invoke(module,new object[]{other});
            Assert(module.GetSnapshot().Count==0 && ((IInventoryReservationApi)module).GetAvailableSnapshot().Count==0 && ((IInventoryReservationApi)module).GetReservations().Count==0,
                "Changing game clears old display and reservation snapshots.");
            Assert(type.GetField("journal",flags).GetValue(module)==null && type.GetField("attachedSave",flags).GetValue(module)==null,
                "Changing game drops save and journal references.");
            Assert(changed==1 && availability==1,"Detach publishes one inventory and availability notification.");
            using(var file=new FileStream(path+".lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
            Assert(File.ReadAllBytes(path).SequenceEqual(durable),"Detach releases lock and preserves durable journal bytes.");
            sync.Invoke(module,new object[]{null}); sync.Invoke(module,new object[]{other});
            Assert(changed==1 && availability==1,"Menu and repeated observations do not repeat detachment.");
            using(var reopened=(IDisposable)Activator.CreateInstance(journalType,new[]{(object)path,empty}))
                Assert((bool)journalType.GetProperty("HasPendingRecovery").GetValue(reopened),"Reentry still requires explicit recovery approval.");
            journal=(IDisposable)Activator.CreateInstance(journalType,new[]{(object)path,state});
            type.GetField("journal",flags).SetValue(module,journal);
            type.GetField("attachedSave",flags).SetValue(module,component);
            ledger.GetType().GetMethod("Restore",flags|BindingFlags.Public).Invoke(ledger,new[]{state});
            sync.Invoke(module,new object[]{null});
            Assert(module.GetSnapshot().Count==0 && changed==2 && availability==2,"Returning to menu independently clears inventory once.");
            using(var file=new FileStream(path+".lock",FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { }
            Assert(File.ReadAllBytes(path).SequenceEqual(durable),"Menu exit releases journal lock without changing records.");
            var identity=assembly.GetType("Phinix.InventoryExtension.Client.InventorySaveIdentityPatch",true);
            identity.GetProperty("CurrentPath").GetSetMethod(true).Invoke(null,new object[]{"old-save.rws"});
            componentType.GetMethod("StartedNewGame").Invoke(component,null);
            Assert(identity.GetProperty("CurrentPath").GetValue(null)==null,"New game cannot inherit an earlier save path.");
            ledger.GetType().GetMethod("Restore",flags|BindingFlags.Public).Invoke(ledger,new[]{state});
            ((IActivatablePhinixExtensionModule)module).Shutdown(new ExtensionHostContext());
            Assert(module.GetSnapshot().Count==0,"Shutdown clears stale display while preserving ledger instance ownership.");
        }
        finally { type.GetField("active",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,null); journal.Dispose(); if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
    private sealed class Services : IClientSettingsContext,IClientMainThreadDispatcher,IClientShellEventStream,IClientWindowService,IDisposable
    {
        public int Queued,Opened,Disposed; private EventHandler shell;
        public bool ThrowOnRemove; public readonly List<Action> Actions=new List<Action>();
        public int Subscribers=>shell?.GetInvocationList().Length??0;
        public event EventHandler MainWindowOpened { add{shell+=value;} remove{if(ThrowOnRemove) throw new InvalidOperationException("Injected unsubscribe failure."); shell-=value;} }
        public event Action<string,object> OnSettingChanged { add{} remove{} }
        public T Get<T>(string key,T defaultValue=default(T))=>defaultValue;
        public void Set<T>(string key,T value) { }
        public IEnumerable<string> BlockedUsers=>new string[0]; public bool CollapseBlockedUsers {get;set;}
        public void BlockUser(string uuid) { } public void UnBlockUser(string uuid) { }
        public void Enqueue(Action action) { Queued++; Actions.Add(action); }
        public void Open(Window window) { Opened++; } public void OpenSettingsWindow() { Opened++; }
        public void Dispose() { Disposed++; }
    }
    private sealed class Factory : IClientCompositionFactory
    {
        private readonly IClientCompositionFactory inner; private readonly bool fail;
        public int Created; public Scope Scope;
        public Factory(IClientCompositionFactory inner,bool fail) {this.inner=inner;this.fail=fail;}
        public IClientCompositionScope CreateScope(Action<IClientCompositionBuilder> configure)
        { var scope=inner.CreateScope(configure); Created++; Scope=new Scope(scope,fail); return Scope; }
    }
    private sealed class Scope : IClientCompositionScope
    {
        private readonly IClientCompositionScope inner; private readonly bool fail; public int Disposes;
        public Scope(IClientCompositionScope inner,bool fail) {this.inner=inner;this.fail=fail;}
        public T Resolve<T>() where T:class
        {if(fail && typeof(T).Name=="InventorySettingsPanel") throw new InvalidOperationException("Injected resolution failure.");return inner.Resolve<T>();}
        public void Dispose() { Disposes++; inner.Dispose(); }
    }
    private sealed class Discovery : IExtensionDiscoveryPolicy
    {
        private readonly Type type; public Discovery(Type type){this.type=type;}
        public bool ShouldScanAssembly(Assembly assembly)=>assembly==type.Assembly;
        public bool ShouldDiscoverType(Type candidate)=>candidate==type;
    }
    private sealed class Disabled : IExtensionActivationPolicy
    {
        public IReadOnlyCollection<string> DisabledExtensions=>new[]{"builtin.inventory"};
        public bool ShouldActivate(string id,out string reason){reason="disabled by test";return false;}
    }
    private sealed class Codec : IInventoryCodec,IDisposable
    {
        public int Disposed; public void Dispose(){Disposed++;}
        public string CodecId=>"test.codec"; public int Version=>1;
        public bool CanStore(InventoryItem item)=>true; public string GetAggregationKey(InventoryItem item)=>null;
        public int MaximumStackCount(InventoryEntry entry)=>1;
        public Thing Materialize(InventoryEntry entry,int count) {throw new NotSupportedException();}
        public void Deliver(Thing thing,Map map,IntVec3 spot) {throw new NotSupportedException();}
    }
    private sealed class Builder : IExtensionBuilder
    {
        private readonly bool fail; public readonly Dictionary<Type,object> Items=new Dictionary<Type,object>();
        public Builder(ExtensionHostContext host,bool fail) {HostContext=host;this.fail=fail;}
        public string ExtensionId=>"builtin.inventory"; public ExtensionHostContext HostContext {get;}
        public IExtensionApiRegistry ApiRegistry {get;}=new ExtensionApiRegistry();
        public void RegisterApi<T>(T value) where T:class
        {if(fail && typeof(T)==typeof(IClientQuickSettingsPanelProvider)) throw new InvalidOperationException("Injected registration failure.");Items.Add(typeof(T),value);ApiRegistry.RegisterApi(ExtensionId,value);}
        public bool TryResolveApi<T>(out T value) where T:class=>ApiRegistry.TryResolve(out value);
        public IReadOnlyList<T> ResolveApis<T>() where T:class=>ApiRegistry.ResolveAll<T>();
        public void AddCapabilityProvider(ICapabilityProvider capabilityProvider) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddMessageInterceptor(IMessageInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddMessageRenderer(IMessageRenderer renderer) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddClientMessageHandler(IClientMessageHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerMessageHandler(IServerMessageHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerInboundMessageInterceptor(IServerInboundMessageInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerDefaultMessageHandler(IServerDefaultMessageHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerMessageObserver(IServerMessageObserver observer) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddItemCodec(IItemCodec codec) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddClientItemHandler(IClientIncomingItemHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddClientOutgoingItemHandler(IClientOutgoingItemHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerItemHandler(IServerItemHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerInboundItemInterceptor(IServerInboundItemInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerDefaultItemHandler(IServerDefaultItemHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerItemObserver(IServerItemObserver observer) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddClientCommandHandler(IClientCommandHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerCommandHandler(IServerCommandHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerInboundCommandInterceptor(IServerInboundCommandInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerDefaultCommandHandler(IServerDefaultCommandHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerCommandObserver(IServerCommandObserver observer) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerOutboundPacketInterceptor(IServerOutboundPacketInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddConsoleCommandProvider(IServerConsoleCommandProvider provider) { throw new NotSupportedException("Unexpected pipeline registration."); }
    }
}
