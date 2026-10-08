using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

internal static partial class Program
{
    private sealed class StoreControlProbe : IClientExtensionControlService
    {
        public ClientExtensionControlSnapshot Capture()=>new ClientExtensionControlSnapshot(0,false,false,null,null,null,null);
        public ClientExtensionControlResult SetModuleEnabled(string id,bool enabled,long revision)=>new ClientExtensionControlResult(false,"ProbeOnly");
    }
    private static void CheckActivation(Type type,bool legacy,List<string> facts)
    {
        foreach(string scenario in legacy?new[]{"success"}:new[]{"success","without-controls","missing-service","localizer-failure","view-failure","environment-failure","release-failure"})
        {
            var services=new StoreHost {Scenario=scenario}; var errors=new List<Exception>();
            using(var factory=new ClientCompositionFactory(()=>true,errors.Add))
            {
                var host=new ExtensionHostContext();
                var controls=new StoreControlProbe();
                if(!legacy && scenario!="without-controls") host.AddService<IClientExtensionControlService>(controls);
                host.AddService<IClientCompositionFactory>(factory);
                host.AddService<IClientLocalizationService>(services); host.AddService<IClientEnvironmentService>(services);
                host.AddService<IClientSettingsContext>(services); host.AddService<IClientMainThreadDispatcher>(services);
                host.AddService<IClientWindowService>(services); host.AddService<IUiTheme>(services);
                host.AddService<IClientExtensionManagementWindowService>(services);
                if(scenario!="missing-service")host.AddService<IClientLinkService>(services);
                host.AddService<IManagedExtensionManagementService>(services); host.AddService<IManagedExtensionInstallationService>(services);
                var module=(IPhinixExtensionModule)Activator.CreateInstance(type); services.Module=module;
                module.Register(new Builder(host,false)); bool failed=false;
                try { ((IActivatablePhinixExtensionModule)module).Activate(host); } catch(Exception) { failed=true; }
                bool success=scenario=="success" || scenario=="without-controls" || scenario=="release-failure";
                Assert(failed!=success,"Expected activation outcome: "+scenario);
                object controller=services.LastController,icons=services.LastIcons;
                object oldView=null,secondView=null; Delegate create=null;
                if(success)
                {
                    controller=Field(module,"managedController"); icons=Field(module,"badgeIcons");
                    WaitIdle(controller);
                    Assert(services.Refreshes==1 && services.Captures==1,"Startup captures once and skips network with empty inventory.");
                    var panel=Field(module,"panel");var tab=Field(module,"tab");
                    oldView=Field(tab,"view"); create=(Delegate)Field(panel,"createView"); secondView=create.DynamicInvoke();
                    Assert(!ReferenceEquals(oldView,secondView) && ReferenceEquals(Field(oldView,"controller"),controller) && ReferenceEquals(Field(secondView,"controller"),controller),"Independent views share one controller.");
                    if(!legacy) Assert(ReferenceEquals(Field(oldView,"controls"),scenario=="without-controls"?null:controls) && ReferenceEquals(Field(secondView,"controls"),scenario=="without-controls"?null:controls),"F6-M both Store views borrow the same host control boundary.");
                    oldView.GetType().GetField("search",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(oldView,"tab-search");
                    Assert((string)Field(secondView,"search")=="","Tab/window searches do not leak across views.");
                    Assert(ReferenceEquals(Field(oldView,"localizer"),Field(secondView,"localizer")) && ReferenceEquals(Field(oldView,"badgeIcons"),icons),"Views share activation-owned resources.");
                    services.LastController=controller;services.LastIcons=icons;
                    if(scenario=="success") facts.Add("activation=captures:"+services.Captures+";refreshes:"+services.Refreshes+";independentViews:"+!ReferenceEquals(oldView,secondView));
                    if(scenario=="success" && !legacy)
                    {
                        object panelIdentity=panel;
                        ((IActivatablePhinixExtensionModule)module).Activate(host);
                        Assert((bool)Field(controller,"disposed") && (bool)Field(icons,"disposed") && (bool)Field(oldView,"stopped"),"Reactivation releases old session and invalidates views.");
                        Assert(ReferenceEquals(panelIdentity,Field(module,"panel")) && !ReferenceEquals(controller,Field(module,"managedController")),"Provider APIs stable while activation resources rebuild.");
                        controller=Field(module,"managedController");icons=Field(module,"badgeIcons");WaitIdle(controller);
                    }
                }
                // The old empty-icons Dispose unconditionally enters native Verse.UnityData.
                // Compare startup facts, but exclude that native-only baseline teardown in this fixture.
                if(legacy)type.GetField("badgeIcons",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(module,null);
                ((IActivatablePhinixExtensionModule)module).Shutdown(host); ((IActivatablePhinixExtensionModule)module).Shutdown(host);
                foreach(Action action in services.Queue.ToArray()) action();services.Queue.Clear();
                Assert(services.Opened==0,"Stopped release notices cannot open queued windows.");
                Assert(services.Localizers.TrueForAll(item=>item.Disposes==1),"Acquired localizers released exactly once, including failures.");
                Assert(services.Disposed==0,"Borrowed host/management/installation/localization services are not disposed.");
                Assert(Field(module,"composition")==null && Field(module,"managedController")==null,"Provider scope and module resource references clear.");
                if(!legacy)Assert(Field(module,"activationComposition")==null,"Activation scope clears.");
                if(controller!=null)Assert((bool)Field(controller,"disposed") && (legacy || (bool)Field(icons,"disposed")),"Controller and candidate icons released despite partial activation or localizer fault.");
                if(secondView!=null && !legacy)
                {
                    Assert((bool)Field(secondView,"stopped"),"Previously open window view is inert after stop.");
                    bool rejected=false;try { create.DynamicInvoke(); } catch(TargetInvocationException error) { rejected=error.InnerException is ObjectDisposedException; }
                    Assert(rejected,"Captured old view factory rejects use after stop.");
                }
                if(scenario=="release-failure")Assert(errors.Count>0,"Scoped localizer failure is reported while cleanup continues.");
            }
        }
    }
    private static void WaitIdle(object controller)
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        for(int attempt=0;attempt<200;attempt++)
        {
            object snapshot=controller.GetType().GetProperty("Snapshot",flags).GetValue(controller);
            if(!(bool)snapshot.GetType().GetProperty("Busy",flags).GetValue(snapshot))return;
            Thread.Sleep(5);
        }
        throw new InvalidOperationException("Startup operation did not finish.");
    }
    private sealed class StoreHost:IClientLocalizationService,IClientEnvironmentService,IClientSettingsContext,
        IClientMainThreadDispatcher,IClientWindowService,IUiTheme,IClientExtensionManagementWindowService,IClientLinkService,
        IManagedExtensionManagementService,IManagedExtensionInstallationService,IDisposable
    {
        public string Scenario; public object Module,LastController,LastIcons;
        public readonly List<StoreText> Localizers=new List<StoreText>();public readonly List<Action> Queue=new List<Action>();
        public int Captures,Refreshes,Opened,Disposed;
        public IClientLocalizer ForModule(IPhinixExtensionModule module)
        { if(Scenario=="localizer-failure")throw new InvalidOperationException("Injected localizer acquisition failure.");var text=new StoreText {Fail=Scenario=="release-failure"};Localizers.Add(text);return text; }
        public ClientEnvironmentSnapshot Capture()
        {
            Captures++;LastController=Field(Module,"managedController");LastIcons=Field(Module,"badgeIcons");
            if(Scenario=="environment-failure")throw new InvalidOperationException("Injected capture failure.");
            string root=Path.Combine(Path.GetTempPath(),"phinix-store-fixture");
            return new ClientEnvironmentSnapshot(new ClientEnvironmentPaths(Path.Combine(root,"Mods"),Path.Combine(root,"Data")),Path.Combine(root,"Host"),"1.6","0.9.7","1.9",null,null,null,null);
        }
        public T Get<T>(string key,T defaultValue=default(T))
        {
            if(Scenario=="view-failure" && key=="plugin-store.officialAccessMethod")
            {LastController=Field(Module,"managedController");LastIcons=Field(Module,"badgeIcons");throw new InvalidOperationException("Injected view creation failure.");}
            return defaultValue;
        }
        public void Set<T>(string key,T value) { }
        public event Action<string,object> OnSettingChanged {add{}remove{}}
        public IEnumerable<string> BlockedUsers=>new string[0]; public bool CollapseBlockedUsers{get;set;}
        public void BlockUser(string uuid){} public void UnBlockUser(string uuid){}
        public void Enqueue(Action action){Queue.Add(action);}public void Open(Window window){Opened++;throw new InvalidOperationException("Unexpected native UI.");}public void OpenSettingsWindow(){}
        public void OpenExtensionManagerWindow(){}public ClientLinkOpenResult Open(string url,ClientLinkOpenPreference preference=ClientLinkOpenPreference.PreferGameBrowser)=>ClientLinkOpenResult.Unavailable;
        public ManagedExtensionRuntimeSnapshot Snapshot=>null;
        public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> disabled,CancellationToken token)
        {Interlocked.Increment(ref Refreshes);return (ManagedExtensionManagementSnapshot)typeof(ManagedExtensionManagementSnapshot).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance)[0].Invoke(new object[]{null,null});}
        public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState state,IEnumerable<string> disabled,CancellationToken token){throw new NotSupportedException();}
        public ManagedExtensionInstallResult Install(ManagedExtensionInstallRequest request,IEnumerable<string> disabled,CancellationToken token){throw new NotSupportedException();}
        public Color PrimaryText=>default(Color);public Color SecondaryText=>default(Color);public Color Background=>default(Color);public Color Surface=>default(Color);public Color Separator=>default(Color);
        public Color HoverHighlight=>default(Color);public Color Pending=>default(Color);public Color Error=>default(Color);public Color Success=>default(Color);public Color Warning=>default(Color);
        public void RegisterColor(string key,Color value){}public Color GetColor(string key)=>default(Color);public bool TryGetColor(string key,out Color value){value=default(Color);return true;}
        public void RegisterFloat(string key,float value){}public float GetFloat(string key,float defaultValue=0f)=>defaultValue;public void Reload(){}
        public void Dispose(){Disposed++;}
    }
    private sealed class StoreText:IClientLocalizer
    {
        public bool Fail;public int Disposes;public string Locale=>"en-US";public event Action LanguageChanged{add{}remove{}}
        public string Text(string key,string fallback=null)=>fallback??key;public string Format(string key,params object[] values)=>key;
        public void Dispose(){Disposes++;if(Fail)throw new InvalidOperationException("Injected scoped text release failure.");}
    }
}
