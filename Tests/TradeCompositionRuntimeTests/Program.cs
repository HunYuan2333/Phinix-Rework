using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using PhinixClient;
using PhinixClient.Framework;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using Utils.Framework;
using UserManagement;

internal static partial class Program
{
    private static int assertions;
    private static void Assert(bool value,string message) { assertions++; if(!value) throw new InvalidOperationException(message); }
    private static object Field(object value,string name) => value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
    private static int Subscribers(object value,string name) => ((Delegate)Field(value,name))?.GetInvocationList().Length??0;
    private static int Main(string[] args)
    {
        try
        {
            bool legacy=args.Length>0 && args[0]=="--legacy";
            string dll=args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"TradeExtension.Client.dll");
            Type type=Assembly.LoadFrom(Path.GetFullPath(dll)).GetType("Phinix.TradeExtension.Client.BuiltInTradeClientExtension",true);
            Assert(Path.GetFullPath(type.Assembly.Location)==Path.GetFullPath(dll),"Probe must load the requested actual DLL.");
            var facts=new List<string>();
            foreach(string scenario in legacy?new[]{"success"}:new[]{"success","resolve-failure","register-failure","missing-factory"})
            using(var inner=new ClientCompositionFactory(()=>true,error=>{throw error;}))
            {
                var factory=new Factory(inner,scenario=="resolve-failure"); var host=new ExtensionHostContext();
                if(scenario!="missing-factory") host.AddService<IClientCompositionFactory>(factory);
                var module=(IPhinixExtensionModule)Activator.CreateInstance(type);
                var builder=new Builder(host,scenario=="register-failure"); bool failed=false;
                try { module.Register(builder); } catch(Exception) { failed=true; }
                Assert(failed==(scenario!="success"),"Expected construction outcome: "+scenario);
                if(scenario=="success")
                {
                    Assert(builder.Items.Count==11,"Same eleven API registrations.");
                    object service=builder.Items[typeof(IFrameworkTradeClientApi)];
                    object facade=builder.Items[typeof(IClientTradeService)];
                    object bridge=builder.Items[typeof(IFrameworkLegacyTradeRepositoryApi)];
                    Assert(ReferenceEquals(service,builder.Items[typeof(IFrameworkTradeUpdateResultApi)]),"One framework facade.");
                    Assert(ReferenceEquals(bridge,builder.Items[typeof(IFrameworkLegacyTradeCompletionApi)]) && ReferenceEquals(bridge,builder.Items[typeof(IFrameworkLegacyTradeDeliveryApi)]),"One legacy facade.");
                    Assert(ReferenceEquals(facade,builder.Items[typeof(ITradeRequestApi)]),"One domain/request facade.");
                    Assert(ReferenceEquals(builder.Items[typeof(IClientSettingsPanelProvider)],builder.Items[typeof(IClientLegacySettingsMigrator)]),"One settings/migration provider.");
                    Assert(ReferenceEquals(Field(bridge,"tradeService"),service) && ReferenceEquals(Field(facade,"tradeService"),service),"Adapters share the framework service.");
                    Assert(ReferenceEquals(((ITradeUiHostContext)Field(module,"tradeUiHostContext")).TradeService,facade),"UI shares the domain facade.");
                    Assert(builder.Capabilities.Count==1 && ReferenceEquals(builder.Capabilities[0],module) && builder.Commands.Count==1 && ReferenceEquals(builder.Commands[0],module),"Same capability and command owner.");
                    Assert(Field(module,"lifecycle")==null && Field(module,legacy?"inventoryCodecRegistration":"inventoryRegistrations")==null && Field(module,"defaultTradeBehaviour")==null,"Composition starts no connection, inventory registration or behavior.");
                    service.GetType().GetMethod("InitializeUserDirectory",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(service,new object[]{new Users()});
                    var repo=(IFrameworkLegacyTradeRepositoryApi)bridge;
                    repo.UpsertTrade(new FrameworkTradeStateSnapshot{TradeId="test",Participants=new List<FrameworkTradeParticipantSnapshot>{new FrameworkTradeParticipantSnapshot{Uuid="local"},new FrameworkTradeParticipantSnapshot{Uuid="remote"}}});
                    Assert(((IFrameworkTradeClientApi)service).GetTradeIds().SequenceEqual(new[]{"test"}) && ((IClientTradeService)facade).GetTradeIds().SequenceEqual(new[]{"test"}),"Legacy update reaches framework and domain views.");
                    repo.RemoveTrade("test");
                    Assert(((IFrameworkTradeClientApi)service).GetTradeIds().Length==0,"Removal reaches shared repository.");
                    var outgoing=(IClientOutgoingCommandHandler)module;
                    var packet=new FrameworkPacket{MessageType=FrameworkTradeProtocol.CreateRequestType};
                    Assert(outgoing.CanHandleOutgoingCommand(packet) && !outgoing.CanHandleOutgoingCommand(null),"Outgoing command classification unchanged.");
                    var result=outgoing.HandleOutgoingCommand(packet,null);
                    Assert(result.Action==MessageHandlingResultAction.Handled && ReferenceEquals(result.Command,packet),"Outgoing packet is passed through unchanged.");
                    Assert(Field(module,"lifecycle")==null && Field(module,legacy?"inventoryCodecRegistration":"inventoryRegistrations")==null && Field(module,"defaultTradeBehaviour")==null,"Composition starts no connection, inventory registration or behavior.");
                    string[] serviceEvents={"OnTradesSynced","OnTradeCancelled","OnTradeCompleted","OnTradeCreationSuccess","OnTradeUpdateFailure","OnTradeUpdateSuccess"};
                    object ui=Field(module,"tradeUiHostContext");
                    foreach(string name in serviceEvents) Assert(Subscribers(service,name)==1,"One passive list subscription: "+name);
                    foreach(string name in new[]{"disconnected","userDisplayNameChanged"}) Assert(Subscribers(ui,name)==1,"One passive UI subscription: "+name);
                    facts.Add("apis="+string.Join(",",builder.Items.OrderBy(p=>p.Key.FullName).Select(p=>p.Key.FullName+":"+p.Value.GetType().FullName)));
                    facts.Add("capabilities="+string.Join(",",((ICapabilityProvider)module).GetCapabilities()));
                    facts.Add("priority="+((IClientCommandHandler)module).Priority+";passThrough="+ReferenceEquals(result.Command,packet));
                    if(!legacy)
                    {
                        Assert(ReferenceEquals(factory.Scope.Resolve<IFrameworkTradeClientApi>(),service),"Resolved published service is singleton.");
                        Assert(ReferenceEquals(factory.Scope.GetType().GetMethod("Resolve").MakeGenericMethod(service.GetType()).Invoke(factory.Scope,null),service),"Interface and concrete aliases are one instance.");
                    }
                    ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                    if(!legacy)
                    {
                        foreach(string name in serviceEvents) Assert(Subscribers(service,name)==0,"Owned tab detaches: "+name);
                        foreach(string name in new[]{"disconnected","userDisplayNameChanged"}) Assert(Subscribers(ui,name)==0,"Owned tab detaches UI: "+name);
                    }
                }
                ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                if(!legacy && builder.Items.TryGetValue(typeof(IFrameworkTradeClientApi),out object published))
                    Assert(Subscribers(published,"OnTradesSynced")==0,"Stop detaches published or partially published UI subscriptions.");
                if(!legacy) Assert((factory.Scope==null || factory.Scope.Disposes==1) && Field(module,"composition")==null,"Partial and repeated stop dispose one scope.");
            }
            CheckActivationLifetime(type,legacy,facts);
            foreach(string fact in facts) Console.WriteLine("FACT "+fact);
            Console.WriteLine("Trade composition passed: "+assertions+" assertions; "+(legacy?"before":"after"));return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error);return 1; }
    }
    private sealed class Users : IClientUserDirectory
    {
        public string Uuid=>"local";
        public ImmutableUser[] GetUsers(bool loggedIn=false)=>new ImmutableUser[0];
        public bool TryGetUser(string uuid,out ImmutableUser user){user=new ImmutableUser(uuid);return true;}
    }
    private sealed class Factory : IClientCompositionFactory
    {
        private readonly IClientCompositionFactory inner;private readonly bool fail;public Scope Scope;
        public Factory(IClientCompositionFactory inner,bool fail){this.inner=inner;this.fail=fail;}
        public IClientCompositionScope CreateScope(Action<IClientCompositionBuilder> configure)
        { Scope=new Scope(inner.CreateScope(configure),fail);return Scope; }
    }
    private sealed class Scope : IClientCompositionScope
    {
        private readonly IClientCompositionScope inner;private readonly bool fail;public int Disposes;private int resolves;
        public Scope(IClientCompositionScope inner,bool fail){this.inner=inner;this.fail=fail;}
        public T Resolve<T>() where T:class { if(fail && ++resolves==3) throw new InvalidOperationException("Injected resolve failure.");return inner.Resolve<T>(); }
        public void Dispose(){Disposes++;inner.Dispose();}
    }
    private sealed class Builder : IExtensionBuilder
    {
        private readonly bool fail; public readonly Dictionary<Type,object> Items=new Dictionary<Type,object>();
        public Builder(ExtensionHostContext host,bool fail) {HostContext=host;this.fail=fail;}
        public string ExtensionId=>FrameworkTradeProtocol.Capability; public ExtensionHostContext HostContext {get;}
        public IExtensionApiRegistry ApiRegistry {get;}=new ExtensionApiRegistry();
        public void RegisterApi<T>(T value) where T:class
        {if(fail && typeof(T)==typeof(IClientLegacySettingsMigrator)) throw new InvalidOperationException("Injected registration failure.");Items.Add(typeof(T),value);ApiRegistry.RegisterApi(ExtensionId,value);}
        public bool TryResolveApi<T>(out T value) where T:class=>ApiRegistry.TryResolve(out value);
        public IReadOnlyList<T> ResolveApis<T>() where T:class=>ApiRegistry.ResolveAll<T>();
        public readonly List<object> Capabilities=new List<object>();
        public readonly List<object> Commands=new List<object>();
        public void AddCapabilityProvider(ICapabilityProvider capabilityProvider) { Capabilities.Add(capabilityProvider); }
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
        public void AddClientCommandHandler(IClientCommandHandler handler) { Commands.Add(handler); }
        public void AddServerCommandHandler(IServerCommandHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerInboundCommandInterceptor(IServerInboundCommandInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerDefaultCommandHandler(IServerDefaultCommandHandler handler) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerCommandObserver(IServerCommandObserver observer) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddServerOutboundPacketInterceptor(IServerOutboundPacketInterceptor interceptor) { throw new NotSupportedException("Unexpected pipeline registration."); }
        public void AddConsoleCommandProvider(IServerConsoleCommandProvider provider) { throw new NotSupportedException("Unexpected pipeline registration."); }
    }
}
