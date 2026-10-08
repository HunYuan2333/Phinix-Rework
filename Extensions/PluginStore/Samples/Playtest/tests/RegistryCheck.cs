using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Utils;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using PhinixClient;
using PhinixClient.Framework;

internal static class PlaytestRegistryCheck
{
    private sealed class SampleDiscovery : IExtensionDiscoveryPolicy
    {
        private readonly Assembly sample;
        internal SampleDiscovery(Assembly sample) { this.sample=sample; }
        public bool ShouldScanAssembly(Assembly assembly) { return assembly==sample; }
        public bool ShouldDiscoverType(Type type) { return type.Assembly==sample; }
    }
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length>0 && args[0]=="--managed") return ManagedSampleCheck.Run(args[1],args[2]);
            ExtensionAssemblyLoader.LoadAssemblies(ExtensionBundleDirectories.GetProbeDirectories(Path.GetFullPath(args[0])),(line,level)=>Console.WriteLine(line));
            string assemblyName=args.Length>1?args[1]:"Phinix.Store.Playtest";
            bool example=assemblyName=="Phinix.Example.Basic";
            string moduleId=example?"phinix.example.basic":"phinix.poc.playtest";
            string english=example?"Example":"Store test", chinese=example?"示例":"商店测试";
            var sample=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==assemblyName);
            var context=new ExtensionHostContext {Log=(line,level)=>Console.WriteLine(line)};
            var settings=new TestSettings();
            settings.Set(example?"phinix.example.basic.clicks":"playtest.clicks",7);
            context.AddService<IClientSettingsContext>(settings);
            using(var composition=new ClientCompositionFactory(()=>true,error=>{throw error;}))
            {
            context.AddService<IClientCompositionFactory>(composition);
            context.AddService<IExtensionDiscoveryPolicy>(new SampleDiscovery(sample));
            var types=PhinixExtensionRegistry.ScanCandidateModuleTypes(context.GetRequiredService<IExtensionDiscoveryPolicy>());
            using(var localization=new ClientLocalizationService(types,t=>ExtensionLocalizationCatalog.LoadCompanion(t.Assembly.Location,t.Assembly.GetName().Name,CancellationToken.None),()=>true,(module,code,file,key)=>Console.WriteLine(ClientLocalizationService.AuditJson(module,code,file,key))))
            {
                context.AddService<IClientLocalizationService>(localization);
                context.AddService<IExtensionModuleLifecycleObserver>(localization);
                localization.UpdateLanguage("en-US");
                var found=PhinixExtensionRegistry.DiscoverExtensions(context);
                if(found.Modules.Count!=1 || !(found.Modules.Single() is IClientExtensionModule)) throw new Exception("Sample must use new client Compose entry");
                var preactive=context.ResolveApis<IMainTabProvider>().Single();
                if(preactive.TabLabel!="tab") throw new Exception("Composition must be passive before activation");
                PhinixExtensionRegistry.ActivateExtensions(found,context);
                var item=found.ExtensionResults.Single(x=>x.ExtensionId==moduleId);
                if(item.State!=ExtensionModuleState.Active) throw new Exception("Not activated: "+item.State+" "+item.StateDetail);
                var provider=context.ResolveApis<IMainTabProvider>().Single();
                if(provider.TabLabel!=english) throw new Exception("English resource lookup failed");
                localization.UpdateLanguage("zh-CN");
                if(provider.TabLabel!=chinese) throw new Exception("Existing tab did not change language");
                localization.UpdateLanguage("ja-JP");
                if(provider.TabLabel!=english) throw new Exception("Unavailable language did not fall back to English");
                if(settings.Get(example?"phinix.example.basic.clicks":"playtest.clicks",0)!=7) throw new Exception("Sample changed retained settings while starting");
                var handle=localization.ForModule(found.Modules.Single());
                PhinixExtensionRegistry.ShutdownExtensions(found,context);
                if(item.State!=ExtensionModuleState.Shutdown || handle.Text("tab")!="tab") throw new Exception("Shutdown resource cleanup failed");
                PhinixExtensionRegistry.ShutdownExtensions(found,context);
                var disabled=PhinixExtensionRegistry.DiscoverExtensions(context,new DisabledSample(moduleId));
                if(disabled.Modules.Count!=0 || disabled.ExtensionResults.Single().State!=ExtensionModuleState.Disabled || context.ResolveApis<IMainTabProvider>().Count!=0)
                    throw new Exception("Disabled sample must not compose or publish a tab");
                PhinixExtensionRegistry.ShutdownExtensions(disabled,context);
                var missing=new ExtensionHostContext {Log=context.Log};
                missing.AddService<IClientCompositionFactory>(composition);
                missing.AddService<IExtensionDiscoveryPolicy>(new SampleDiscovery(sample));
                var failed=PhinixExtensionRegistry.DiscoverExtensions(missing);
                if(failed.ExtensionResults.Single().State!=ExtensionModuleState.Failed || missing.ResolveApis<IMainTabProvider>().Count!=0)
                    throw new Exception("Missing required settings must fail without publishing partial APIs");
                PhinixExtensionRegistry.ShutdownExtensions(failed,missing);
            }
            }
            Console.WriteLine("Actual sample discovered, registered, localized, switched language, fell back, and shut down under Mono. Disabled/missing-settings cases passed. No game UI/silver action invoked.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private sealed class DisabledSample : IExtensionActivationPolicy
    {
        private readonly string id;
        internal DisabledSample(string id) {this.id=id;}
        public IReadOnlyCollection<string> DisabledExtensions=>new[]{id};
        public bool ShouldActivate(string extensionId,out string reason) {reason="disabled test";return false;}
    }
    private sealed class TestSettings : IClientSettingsContext
    {
        private readonly Dictionary<string,object> values=new Dictionary<string,object>();
        public T Get<T>(string key,T defaultValue=default(T)) {object value; return values.TryGetValue(key,out value)?(T)value:defaultValue;}
        public void Set<T>(string key,T value) {values[key]=value; OnSettingChanged?.Invoke(key,value);}
        public IEnumerable<string> BlockedUsers=>new string[0];
        public bool CollapseBlockedUsers {get;set;}
        public void BlockUser(string uuid) {}
        public void UnBlockUser(string uuid) {}
        public event Action<string,object> OnSettingChanged;
    }
}
