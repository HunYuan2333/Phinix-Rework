using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading;
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
            ExtensionAssemblyLoader.LoadAssemblies(ExtensionBundleDirectories.GetProbeDirectories(Path.GetFullPath(args.Single())),(line,level)=>Console.WriteLine(line));
            var sample=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Phinix.Store.Playtest");
            var context=new ExtensionHostContext {Log=(line,level)=>Console.WriteLine(line)};
            context.AddService<IExtensionDiscoveryPolicy>(new SampleDiscovery(sample));
            var types=PhinixExtensionRegistry.ScanCandidateModuleTypes(context.GetRequiredService<IExtensionDiscoveryPolicy>());
            using(var localization=new ClientLocalizationService(types,t=>ExtensionLocalizationCatalog.LoadCompanion(t.Assembly.Location,t.Assembly.GetName().Name,CancellationToken.None),()=>true,(module,code,file,key)=>Console.WriteLine(ClientLocalizationService.AuditJson(module,code,file,key))))
            {
                context.AddService<IClientLocalizationService>(localization);
                context.AddService<IExtensionModuleLifecycleObserver>(localization);
                localization.UpdateLanguage("en-US");
                var found=PhinixExtensionRegistry.DiscoverExtensions(context);
                PhinixExtensionRegistry.ActivateExtensions(found,context);
                var item=found.ExtensionResults.Single(x=>x.ExtensionId=="phinix.poc.playtest");
                if(item.State!=ExtensionModuleState.Active) throw new Exception("Not activated: "+item.State+" "+item.StateDetail);
                var provider=context.ResolveApis<IMainTabProvider>().Single();
                if(provider.TabLabel!="Store test") throw new Exception("English resource lookup failed");
                localization.UpdateLanguage("zh-CN");
                if(provider.TabLabel!="商店测试") throw new Exception("Existing tab did not change language");
                localization.UpdateLanguage("ja-JP");
                if(provider.TabLabel!="Store test") throw new Exception("Unavailable language did not fall back to English");
                var handle=localization.ForModule(found.Modules.Single());
                PhinixExtensionRegistry.ShutdownExtensions(found,context);
                if(item.State!=ExtensionModuleState.Shutdown || handle.Text("tab")!="tab") throw new Exception("Shutdown resource cleanup failed");
            }
            Console.WriteLine("Actual sample discovered, registered, localized, switched language, fell back, and shut down under Mono. No game UI/silver action invoked.");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
