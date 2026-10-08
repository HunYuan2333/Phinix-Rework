using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using Verse;

internal static partial class Program
{
    private static int assertions;
    private static void Assert(bool value,string message){assertions++;if(!value)throw new InvalidOperationException(message);}
    private static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);
    private static int Main(string[] args)
    {
        try
        {
            bool legacy=args.Length>0 && args[0]=="--legacy";
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PluginStore.Client.dll");
            Type type=Assembly.LoadFrom(path).GetType("Phinix.PluginStore.PluginStoreClientExtension",true);
            Assert(Path.GetFullPath(type.Assembly.Location)==Path.GetFullPath(path),"Load requested actual DLL.");
            var facts=new List<string>();
            foreach(string scenario in legacy?new[]{"success"}:new[]{"success","resolve-failure","register-failure","missing-factory","stop-failure"})
            using(var inner=new ClientCompositionFactory(()=>true,error=>{}))
            {
                var factory=new Factory(inner,scenario=="resolve-failure");var host=new ExtensionHostContext();
                if(scenario!="missing-factory")host.AddService<IClientCompositionFactory>(factory);
                var module=(IPhinixExtensionModule)Activator.CreateInstance(type);
                if(!legacy)Assert(Field(module,"panel")==null && Field(module,"tab")==null && Field(module,"updateBanner")==null,"Module constructor creates no providers.");
                var builder=new Builder(host,scenario=="register-failure");bool failed=false;
                try{module.Register(builder);}catch(Exception){failed=true;}
                bool success=scenario=="success" || scenario=="stop-failure";
                Assert(failed!=success,"Expected compose outcome: "+scenario);
                if(success)
                {
                    Assert(builder.Items.Count==3 && builder.Capabilities.Count==0 && builder.Commands.Count==0,"Original three API registrations, no networking handlers.");
                    var panel=(IClientSettingsPanelProvider)builder.Items[typeof(IClientSettingsPanelProvider)];
                    var tab=(IMainTabProvider)builder.Items[typeof(IMainTabProvider)];
                    var banner=(INoticeBannerProvider)builder.Items[typeof(INoticeBannerProvider)];
                    Assert(!panel.IsVisible(null) && banner.CurrentHeight==0,"Providers remain passive before activation.");
                    Assert(Field(module,"managedController")==null && Field(module,"localizer")==null && Field(module,"badgeIcons")==null && Field(module,"releaseNotice")==null,"No controller, localization, icon or notice starts during composition.");
                    Assert(ReferenceEquals(panel,Field(module,"panel")) && ReferenceEquals(tab,Field(module,"tab")) && ReferenceEquals(banner,Field(module,"updateBanner")),"Module uses published provider instances.");
                    if(scenario=="success")
                    {
                        facts.Add("apis="+string.Join(",",builder.Items.OrderBy(p=>p.Key.FullName).Select(p=>p.Key.FullName+":"+p.Value.GetType().FullName)));
                        facts.Add("presentation="+panel.SectionId+":"+panel.Order+";tabOrder="+tab.TabOrder+";visible="+panel.IsVisible(null)+";height="+banner.CurrentHeight);
                    }
                    if(!legacy)
                    {
                        foreach(var item in builder.Items)
                        {
                            object resolved=factory.Scope.GetType().GetMethod("Resolve").MakeGenericMethod(item.Key).Invoke(factory.Scope,null);
                            Assert(ReferenceEquals(resolved,item.Value),"Published provider alias is a singleton.");
                        }
                        if(scenario=="stop-failure")CheckFailingStop(type,module,host,factory,panel);
                    }
                }
                ((IActivatablePhinixExtensionModule)module).Shutdown(host);((IActivatablePhinixExtensionModule)module).Shutdown(host);
                if(!legacy)Assert((factory.Scope==null || factory.Scope.Disposes==1) && Field(module,"composition")==null,"Partial/repeated stop disposes one scope.");
            }
            if(!legacy)
            using(var inner=new ClientCompositionFactory(()=>true,error=>{}))
            {
                var host=new ExtensionHostContext();var factory=new Factory(inner,false);
                host.AddService<IClientCompositionFactory>(factory);host.AddService<IExtensionDiscoveryPolicy>(new Discovery(type));
                var disabled=PhinixExtensionRegistry.DiscoverExtensions(host,new Disabled());
                Assert(disabled.Modules.Count==0 && disabled.ExtensionResults.Single().State==ExtensionModuleState.Disabled && factory.Created==0,"Disabled Store is not instantiated/composed.");
                var registered=PhinixExtensionRegistry.DiscoverExtensions(host);
                Assert(registered.Modules.Count==1 && registered.ApiRegistry.ResolveAll<IMainTabProvider>().Count==1,"Ordinary registry discovers/publishes Store.");
                PhinixExtensionRegistry.ShutdownExtensions(registered,host);
                Assert(registered.ApiRegistry.ResolveAll<IMainTabProvider>().Count==0 && factory.Scope.Disposes==1,"Ordinary registry stop removes APIs and disposes scope.");
                var failing=new Factory(inner,true);host.AddService<IClientCompositionFactory>(failing);
                var rejected=PhinixExtensionRegistry.DiscoverExtensions(host);
                Assert(rejected.Modules.Count==0 && rejected.ExtensionResults.Single().State==ExtensionModuleState.Failed && rejected.ApiRegistry.ResolveAll<IMainTabProvider>().Count==0 && failing.Scope.Disposes==1,"Ordinary failure rollback releases scope and publishes no tab.");
            }
            CheckActivation(type,legacy,facts);
            foreach(string fact in facts)Console.WriteLine("FACT "+fact);
            Console.WriteLine("Store composition passed: "+assertions+" assertions; "+(legacy?"before":"after"));return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static T EmptyView<T>() where T:class=>null;
    private static void CheckFailingStop(Type type,object module,ExtensionHostContext host,Factory factory,IClientSettingsPanelProvider panel)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        var assembly=type.Assembly;var dispatcher=new FailingDispatcher();
        Type viewType=assembly.GetType("Phinix.PluginStore.ManagedPluginStoreView",true);
        Type delegateType=typeof(Func<>).MakeGenericType(viewType);
        var create=Delegate.CreateDelegate(delegateType,typeof(Program).GetMethod("EmptyView",BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(viewType));
        panel.GetType().GetMethod("Initialize").Invoke(panel,new object[]{null,dispatcher,create});
        Type windowType=assembly.GetType("Phinix.PluginStore.PluginStoreWindow",true);
        panel.GetType().GetField("window",flags).SetValue(panel,System.Runtime.Serialization.FormatterServices.GetUninitializedObject(windowType));
        var localizer=new Localizer();type.GetField("localizer",flags).SetValue(module,localizer);
        Type controllerType=assembly.GetType("Phinix.PluginStore.ManagedStoreController",true);
        object controller=controllerType.GetConstructors(flags).Single().Invoke(new object[]{null,null,null,null});
        type.GetField("managedController",flags).SetValue(module,controller);
        type.GetField("activationComposition",flags).SetValue(module,new OwnedFixture(controller,localizer));
        bool failed=false;try{((IActivatablePhinixExtensionModule)module).Shutdown(host);}catch(AggregateException){failed=true;}
        Assert(failed && dispatcher.Attempts==1,"Close scheduling failure is reported once.");
        Assert(!panel.IsVisible(null) && Field(panel,"windows")==null && Field(panel,"window")==null,"Panel clears its references before failing enqueue.");
        Assert((bool)Field(controller,"disposed") && localizer.Disposes==1,"Controller/localizer still disposed after earlier stop failure.");
        Assert(factory.Scope.Disposes==1 && Field(module,"panel")==null,"Scope still releases providers after multiple cleanup failures.");
    }
    private sealed class OwnedFixture:IClientCompositionScope
    {
        private readonly object controller;private readonly IClientLocalizer localizer;
        public OwnedFixture(object controller,IClientLocalizer localizer){this.controller=controller;this.localizer=localizer;}
        public T Resolve<T>()where T:class{throw new NotSupportedException();}
        public void Dispose(){((IDisposable)controller).Dispose();localizer.Dispose();}
    }
    private sealed class FailingDispatcher:IClientMainThreadDispatcher
    {public int Attempts;public void Enqueue(Action action){Attempts++;throw new InvalidOperationException("Injected enqueue failure.");}}
    private sealed class Localizer:IClientLocalizer
    {
        public int Disposes;public string Locale=>"en-US";public event Action LanguageChanged{add{}remove{}}
        public string Text(string key,string fallback=null)=>fallback??key;public string Format(string key,params object[] values)=>key;
        public void Dispose(){Disposes++;throw new InvalidOperationException("Injected localizer release failure.");}
    }
    private sealed class Factory:IClientCompositionFactory
    {
        private readonly IClientCompositionFactory inner;private readonly bool fail;public Scope Scope;public int Created;
        public Factory(IClientCompositionFactory inner,bool fail){this.inner=inner;this.fail=fail;}
        public IClientCompositionScope CreateScope(Action<IClientCompositionBuilder> configure){Created++;Scope=new Scope(inner.CreateScope(configure),fail);return Scope;}
    }
    private sealed class Scope:IClientCompositionScope
    {
        private readonly IClientCompositionScope inner;private readonly bool fail;private int resolves;public int Disposes;
        public Scope(IClientCompositionScope inner,bool fail){this.inner=inner;this.fail=fail;}
        public T Resolve<T>()where T:class{T result=inner.Resolve<T>();if(fail && ++resolves==2)throw new InvalidOperationException("Injected resolve failure.");return result;}
        public void Dispose(){Disposes++;inner.Dispose();}
    }
    private sealed class Discovery:IExtensionDiscoveryPolicy
    {
        private readonly Type type;public Discovery(Type type){this.type=type;}
        public bool ShouldScanAssembly(Assembly assembly)=>assembly==type.Assembly;public bool ShouldDiscoverType(Type candidate)=>candidate==type;
    }
    private sealed class Disabled:IExtensionActivationPolicy
    {
        public IReadOnlyCollection<string> DisabledExtensions=>new[]{"phinix.plugin-store"};
        public bool ShouldActivate(string id,out string reason){reason="test disabled";return false;}
    }
    private sealed class Builder : IExtensionBuilder
    {
        private readonly bool fail; public readonly Dictionary<Type,object> Items=new Dictionary<Type,object>();
        public Builder(ExtensionHostContext host,bool fail) {HostContext=host;this.fail=fail;}
        public string ExtensionId=>"phinix.plugin-store"; public ExtensionHostContext HostContext {get;}
        public IExtensionApiRegistry ApiRegistry {get;}=new ExtensionApiRegistry();
        public void RegisterApi<T>(T value) where T:class
        {if(fail && typeof(T)==typeof(INoticeBannerProvider)) throw new InvalidOperationException("Injected registration failure.");Items.Add(typeof(T),value);ApiRegistry.RegisterApi(ExtensionId,value);}
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
