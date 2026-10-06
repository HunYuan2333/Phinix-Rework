using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhinixClient.Framework;
using Phinix.PluginStore;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    [PhinixExtension("test.localization")]
    public sealed class LocalizationModule : IPhinixExtensionModule,IActivatablePhinixExtensionModule
    {
        // No parameterless constructor: do not pollute unrelated subprocess discovery.
        public LocalizationModule(bool fixture) { }
        public string ExtensionId => "test.localization";
        public override bool Equals(object other) { return other is IPhinixExtensionModule; }
        public override int GetHashCode() { return 1; }
        public IClientLocalizer Localizer;
        public bool FailActivation,FailShutdown;
        public void Register(IExtensionBuilder builder) { }
        public void Activate(ExtensionHostContext context)
        { Localizer=context.GetRequiredService<IClientLocalizationService>().ForModule(this); if(FailActivation) throw new InvalidOperationException("Intentional activation failure"); }
        public void Shutdown(ExtensionHostContext context) { if(FailShutdown) throw new InvalidOperationException("Intentional shutdown failure"); }
    }
    [PhinixExtension("test.localization.second")]
    public sealed class OtherLocalizationModule : IPhinixExtensionModule
    { public OtherLocalizationModule(bool fixture) { } public override bool Equals(object other) { return other is IPhinixExtensionModule; } public override int GetHashCode() { return 1; } public string ExtensionId => "test.localization.second"; public void Register(IExtensionBuilder builder) { } }
    private static DiscoveredPhinixExtensions LocalizationRegistration(LocalizationModule module)
    {
        var result=new DiscoveredPhinixExtensions(); result.Modules.Add(module);
        result.ExtensionResults.Add(new ExtensionDiscoveryResult {ExtensionId=module.ExtensionId,State=ExtensionModuleState.Registered});
        return result;
    }
    private static byte[] Language(string locale,string strings,string display=null)
    { return Utf8("{\"schemaVersion\":1,\"locale\":"+Q(locale)+",\"display\":"+(display??"{\"name\":\"Language sample\",\"summary\":\"Package summary\"}")+",\"strings\":"+strings+"}"); }
    private static ManagedExtensionManifest LanguageManifest(Dictionary<string,byte[]> code,Dictionary<string,byte[]> languages,string fallback=null)
    {
        string declaration="\"localization\":{\"files\":["+string.Join(",",languages.Keys.Select(Q))+"]"+(fallback==null?"":",\"defaultLocale\":"+Q(fallback))+"}";
        string manifest=Manifest(code).Replace("\"resources\":[]","\"resources\":["+string.Join(",",languages.Select(p=>FileEntry(p.Key,p.Value)))+"],"+declaration);
        return ManagedExtensionManifestReader.Read(Utf8(manifest));
    }
    private static ExtensionLocalizationCatalog LanguageCatalog(Dictionary<string,byte[]> code,Dictionary<string,byte[]> languages,string fallback=null)
    { var manifest=LanguageManifest(code,languages,fallback); return ExtensionLocalizationCatalog.Load(manifest.Localization,f=>languages[f.Path],CancellationToken.None); }
    private static void DisplayLocalizationRegression(Dictionary<string,byte[]> code)
    {
        var cases=CatalogReader.Array(CatalogReader.ReadJson(File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"LocalizationDisplay","cases.json"))).Element("cases"),"cases",64);
        foreach(var item in cases)
        {
            var fields=CatalogReader.Object(item,"case","id","localization","code","queries");
            string id=fields["id"].Value, expected=fields["code"].Value;
            if(expected.Length!=0) { Failure(expected,()=>ExtensionDisplayLocalization.Read(fields["localization"])); continue; }
            var display=ExtensionDisplayLocalization.Read(fields["localization"]);
            foreach(var query in fields["queries"].Elements())
            { var f=CatalogReader.Object(query,"query","key","locale","expected"); Assert(display.Resolve(f["key"].Value,f["locale"].Value)==f["expected"].Value,"Shared display fixture: "+id); }
        }
        var language=LanguageCatalog(code,new Dictionary<string,byte[]> {{"Resources/Localization/en-US.json",Language("en-US","{}")}});
        var correct=ExtensionDisplayLocalization.Read(CatalogReader.ReadJson(Utf8("{\"translations\":{\"en-US\":{\"name\":\"Language sample\",\"summary\":\"Package summary\"}}}")));
        correct.VerifyProjection(language); Assert(true,"Catalog display matches package-owned language files");
        var changed=ExtensionDisplayLocalization.Read(CatalogReader.ReadJson(Utf8("{\"translations\":{\"en-US\":{\"name\":\"Forged name\",\"summary\":\"Package summary\"}}}")));
        Failure("CatalogLocalizationMismatch",()=>changed.VerifyProjection(language));
        Failure("DuplicateField",()=>ExtensionDisplayLocalization.Read(CatalogReader.ReadJson(Utf8("{\"translations\":{\"en\":{\"name\":\"A\",\"name\":\"B\",\"summary\":\"C\"}}}"))));
    }
    private static void LocalizationRegression(Dictionary<string,byte[]> code)
    {
        DisplayLocalizationRegression(code);
        const string en="Resources/Localization/en-US.json",zh="Resources/Localization/zh-CN.json",ja="Resources/Localization/ja-JP.json";
        byte[] english=Language("en-us","{\"tab\":\"Test tab\",\"silver\":\"Silver\",\"counter\":\"Clicks {0}\",\"braces\":\"{{count}} {0}\"}");
        byte[] chinese=Language("zh-CN","{\"tab\":\"测试页\",\"counter\":\"次数 {0}\"}","{\"name\":\"中文名\",\"changelog\":\"第一行\\n第二行\"}");
        byte[] japanese=Language("ja-JP","{\"tab\":\"テスト\"}");
        Assert(ExtensionLocale.Normalize("ZH-hans-cn")=="zh-Hans-CN","Locale normalization is invariant");
        foreach(string bad in new[]{"", "en_US","../zh-CN","english","en-US-x-private","en-1234"}) Failure("InvalidLocale",()=>ExtensionLocale.Normalize(bad));
        var onlyEnglish=LanguageCatalog(code,new Dictionary<string,byte[]>{{en,english}});
        Assert(onlyEnglish.Resolve("tab","zh-CN")=="Test tab","Single English file serves other game languages");
        var onlyChinese=LanguageCatalog(code,new Dictionary<string,byte[]>{{zh,Language("zh-CN","{\"tab\":\"中文\"}")}});
        Assert(onlyChinese.Resolve("tab","en-US")=="中文","Single Chinese file needs no English");
        var onlyJapanese=LanguageCatalog(code,new Dictionary<string,byte[]>{{ja,japanese}});
        Assert(onlyJapanese.Resolve("tab","fr-FR")=="テスト","A third single language is valid");
        var files=new Dictionary<string,byte[]>{{en,english},{zh,chinese},{ja,japanese}};
        var catalog=LanguageCatalog(code,files,"ja-jp");
        Assert(catalog.Resolve("tab","zh-CN")=="测试页" && catalog.Resolve("silver","zh-CN")=="Silver","Per-key fallback preserves available Chinese text");
        Assert(catalog.Resolve("tab","en-GB")=="Test tab" && catalog.Resolve("tab","de-DE")=="Test tab","Same-language variant and English fallback");
        Assert(catalog.Resolve("name","zh-CN",true)=="中文名" && catalog.Resolve("summary","zh-CN",true)=="Package summary","Display fields fall back individually");
        var nonEnglish=LanguageCatalog(code,new Dictionary<string,byte[]>{{zh,chinese},{ja,japanese}},"ja-JP");
        Assert(nonEnglish.Resolve("tab","fr-FR")=="テスト","Missing English falls back to author default");
        string[] preference=ExtensionLocale.Preference(new[]{"zh-TW","zh-CN","en-US"},"zh-Hant","zh-CN").ToArray();
        Assert(preference[0]=="zh-TW" && Array.IndexOf(preference,"en-US")<Array.IndexOf(preference,"zh-CN"),"Chinese scripts do not cross during same-language matching");
        Assert(LanguageCatalog(code,new Dictionary<string,byte[]>{{zh,chinese},{ja,japanese}}).Resolve("tab","de-DE")=="テスト","Last fallback uses stable locale order");
        Failure("InvalidDefaultLocale",()=>LanguageManifest(code,files,"fr-FR"));
        Failure("NonCanonicalLocalePath",()=>LanguageManifest(code,new Dictionary<string,byte[]>{{"Resources/Localization/en-us.json",english}}));
        Failure("LocalizationLocaleMismatch",()=>LanguageCatalog(code,new Dictionary<string,byte[]>{{en,japanese}}));
        Failure("MissingDisplayText",()=>LanguageCatalog(code,new Dictionary<string,byte[]>{{zh,chinese}}));
        Failure("DuplicateField",()=>ExtensionLanguageFile.Read(Language("en-US","{\"tab\":\"A\",\"tab\":\"B\"}")));
        Failure("UnknownField",()=>ExtensionLanguageFile.Read(Utf8(System.Text.Encoding.UTF8.GetString(english).Replace("\"strings\":","\"imageUrl\":\"https://invalid.test\",\"strings\":"))));
        Failure("InvalidLocalizationKey",()=>ExtensionLanguageFile.Read(Language("en-US","{\"_outside\":\"A\"}")));
        Failure("InvalidLocalizationMarkup",()=>ExtensionLanguageFile.Read(Language("en-US","{\"tab\":\"<color=red>Title</color>\"}")));
        foreach(string format in new[]{"bad {", "bad }","{01}","{32}","{0,10}","{0:D}"})
            Failure("InvalidLocalizationFormat",()=>ExtensionLanguageFile.Read(Language("en-US","{\"tab\":"+Q(format)+"}")));
        var mismatch=new Dictionary<string,byte[]>(files); mismatch[zh]=Language("zh-CN","{\"counter\":\"次数 {1}\"}");
        Failure("LocalizationParametersMismatch",()=>LanguageCatalog(code,mismatch));
        Failure("DocumentLimit",()=>ExtensionLanguageFile.Read(new byte[128*1024+1]));
        var manifest=LanguageManifest(code,files);
        Failure("LocalizationDigestMismatch",()=>ExtensionLocalizationCatalog.Load(manifest.Localization,f=>f.Path==en?english.Reverse().ToArray():files[f.Path],CancellationToken.None));
        Failure("LocalizationLengthMismatch",()=>ExtensionLocalizationCatalog.Load(manifest.Localization,f=>new byte[0],CancellationToken.None));
        bool cancelled=false; try { ExtensionLocalizationCatalog.Load(manifest.Localization,f=>files[f.Path],new CancellationToken(true)); } catch(OperationCanceledException) { cancelled=true; }
        Assert(cancelled,"Language loading honors cancellation");

        int mainThread=Thread.CurrentThread.ManagedThreadId; var events=new List<string>();
        var module=new LocalizationModule(true); var other=new OtherLocalizationModule(true);
        using(var service=new ClientLocalizationService(new[]{typeof(LocalizationModule),typeof(OtherLocalizationModule)},t=>t==typeof(LocalizationModule)?catalog:onlyJapanese,()=>Thread.CurrentThread.ManagedThreadId==mainThread,(id,reason,file,key)=>events.Add(id+":"+reason)))
        {
            bool before=false; try { service.ForModule(module); } catch(InvalidOperationException) { before=true; } Assert(before,"A module must be activating before acquiring resources");
            service.UpdateLanguage("en-US"); service.OnActivating(module); service.OnActivating(other);
            var localizer=service.ForModule(module); var otherLocalizer=service.ForModule(other);
            Assert(localizer.Text("tab")=="Test tab" && otherLocalizer.Text("tab")=="テスト","Same key is isolated between module-owned catalogs");
            Assert(localizer.Format("counter",5)=="Clicks 5" && localizer.Format("braces",5)=="{count} 5","Numbered parameters and escaped braces render");
            Assert(localizer.Format("counter")=="Clicks {0}","Wrong format arguments show text without a GUI exception");
            Assert(localizer.Format("counter",new string('a',8193))=="Clicks {0}","Oversized parameters are refused before interpolation");
            int changed=0; localizer.LanguageChanged+=()=>{ throw new Exception("Intentional listener failure"); }; localizer.LanguageChanged+=()=>changed++;
            service.UpdateLanguage("zh-CN"); Assert(changed==1 && localizer.Text("tab")=="测试页","Language change updates existing handles and isolates event failures");
            Assert(events.Contains("test.localization:LocalizationListenerFailed"),"Listener failure has a diagnostic code");
            Assert(Task.Run(()=>localizer.Text("tab")).GetAwaiter().GetResult()=="测试页","Background reads use snapshots without game translation APIs");
            Assert(Task.Run(()=>{ try { service.UpdateLanguage("ja-JP"); return false; } catch(InvalidOperationException) { return true; } }).GetAwaiter().GetResult(),"Background game-language mutation is rejected");
            Assert(localizer.Text("missing","Visible fallback")=="Visible fallback" && localizer.Text("missing")=="missing","Missing keys use caller fallback or a visible key");
            Assert(events.Count(s=>s=="test.localization:LocalizationKeyMissing")==1,"Missing-key diagnostics are throttled");
            int disposedCallback=0; otherLocalizer.LanguageChanged+=()=>otherLocalizer.Dispose(); otherLocalizer.LanguageChanged+=()=>disposedCallback++;
            service.OnStopped(module);
            Assert(localizer.Text("tab")=="tab" && otherLocalizer.Text("tab")=="テスト","Shutdown removes owned resources without touching another plugin");
            service.UpdateLanguage("ja-JP");
            Assert(disposedCallback==0 && changed==1,"Disposing in a language callback cancels remaining callbacks; stopped module receives none");
        }

        // Exercise real registry failure cleanup, not just direct service callbacks.
        var context=new ExtensionHostContext();
        using(var service=new ClientLocalizationService(new[]{typeof(LocalizationModule)},t=>catalog,()=>true,null))
        {
            context.AddService<IClientLocalizationService>(service); context.AddService<IExtensionModuleLifecycleObserver>(service);
            var instance=new LocalizationModule(true) {FailActivation=true};
            var discovered=LocalizationRegistration(instance);
            PhinixExtensionRegistry.ActivateExtensions(discovered,context);
            Assert(instance.Localizer.Text("tab")=="tab" && discovered.ExtensionResults.Single().State==ExtensionModuleState.Failed,"Failed activation automatically closes host resources");
            instance=new LocalizationModule(true) {FailShutdown=true};
            discovered=LocalizationRegistration(instance);
            PhinixExtensionRegistry.ActivateExtensions(discovered,context); PhinixExtensionRegistry.ShutdownExtensions(discovered,context);
            Assert(instance.Localizer.Text("tab")=="tab","Failed plugin shutdown still closes host resources");
        }

        Assert(ClientLocalizationService.AuditJson("module\"\n", "Code").Contains("module\\\"\\u000a"),"Localization diagnostics escape caller identifiers");
        string bundleRoot=Path.Combine(Path.GetTempPath(),"phinix-bundles-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(bundleRoot); string plugin=Path.Combine(bundleRoot,"one-plugin"); Directory.CreateDirectory(plugin);
            File.WriteAllBytes(Path.Combine(plugin,"Fixture.Managed.Plugin.dll"),code["Assemblies/Fixture.Managed.Plugin.dll"]);
            foreach(string folder in new[]{"Resources","one-plugin/Resources/Localization","not-a-plugin/deep"})
            { string directory=Path.Combine(bundleRoot,folder); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,"not-an-entry.dll"),new byte[0]); }
            Assert(ExtensionBundleDirectories.GetProbeDirectories(bundleRoot).SequenceEqual(new[]{bundleRoot,plugin}),"Bundle discovery includes one plugin folder without loading resource/deep DLLs");
            Assert(ExtensionBundleDirectories.GetProbeDirectories(Path.Combine(bundleRoot,"missing")).Count()==1,"Missing bundle root preserves normal loader diagnostics");
            var bundleLanguages=new Dictionary<string,byte[]> {{en,english},{zh,chinese}};
            foreach(var pair in bundleLanguages) { string path=Path.Combine(plugin,pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,pair.Value); }
            string companion="{\"schemaVersion\":1,\"assemblyName\":\"Fixture.Managed.Plugin\",\"resources\":["+string.Join(",",bundleLanguages.Select(p=>FileEntry(p.Key,p.Value)))+"],\"localization\":{\"files\":["+string.Join(",",bundleLanguages.Keys.Select(Q))+"]}}";
            string dll=Path.Combine(plugin,"Fixture.Managed.Plugin.dll"); File.WriteAllBytes(dll+".localization.json",Utf8(companion));
            Assert(ExtensionLocalizationCatalog.LoadCompanion(dll,"Fixture.Managed.Plugin",CancellationToken.None).Resolve("tab","zh-CN")=="测试页","Nested bundle resolves companion and resources relative to its own DLL");
        }
        finally { if(Directory.Exists(bundleRoot)) Directory.Delete(bundleRoot,true); }
        string root=Path.Combine(Path.GetTempPath(),"phinix-language-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            foreach(var pair in files) { string path=Path.Combine(root,pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,pair.Value); }
            var invalidLanguages=new Dictionary<string,byte[]> {{en,Language("en-US","{\"counter\":\"Bad {\"}")}};
            string invalidManifest=Manifest(code).Replace("\"resources\":[]","\"resources\":["+FileEntry(en,invalidLanguages[en])+"],\"localization\":{\"files\":["+Q(en)+"]}");
            var invalidContent=new Dictionary<string,byte[]>(code); invalidContent.Add(en,invalidLanguages[en]);
            var paths=new ManagedExtensionPaths(Path.Combine(root,"invalid-install"));
            var runtimeLogs=new List<ManagedExtensionRuntimeAudit>();
            using(var runtime=EmptyRuntime(paths,Facts(code),runtimeLogs))
            {
                var result=runtime.Install(InstallationRequest(invalidContent,invalidManifest),new string[0],CancellationToken.None);
                Assert(ManagedExtensionAuditJson.Format(runtimeLogs.Single(e=>e.Code=="InvalidLocalizationFormat")).Contains("\"resourcePath\":\"Resources/Localization/en-US.json\""),"Invalid language audit identifies the owned relative file");
                Assert(!result.Succeeded && result.Code=="InvalidLocalizationFormat" && !Directory.Exists(paths.PackagesDirectory),"Host installer refuses hashed but invalid language JSON before commit");
            }
            var startupPaths=new ManagedExtensionPaths(Path.Combine(root,"invalid-startup"));
            Install(startupPaths,"test.source",invalidManifest,invalidContent,"enabled");
            using(var runtime=new ManagedExtensionRuntime(startupPaths))
            {
                runtime.Start(Facts(code),new string[0],new string[0],CancellationToken.None);
                Assert(runtime.Snapshot.Packages.Single().DiagnosticCode=="InvalidLocalizationFormat" && !runtime.Snapshot.Packages.Single().AssembliesLoaded,"Startup rejects hashed but invalid language data before DLL loading");
            }
            string companion="{\"schemaVersion\":1,\"assemblyName\":\"Example\",\"resources\":["+string.Join(",",files.Select(p=>FileEntry(p.Key,p.Value)))+"],\"localization\":{\"files\":["+string.Join(",",files.Keys.Select(Q))+"]}}";
            File.WriteAllBytes(Path.Combine(root,"Example.dll.localization.json"),Utf8(companion));
            Assert(ExtensionLocalizationCatalog.LoadCompanion(Path.Combine(root,"Example.dll"),"Example",CancellationToken.None).Resolve("tab","zh-CN")=="测试页","Normally discovered bundled DLL uses the same hashed language core");
            Failure("LocalizationOwnerMismatch",()=>ExtensionLocalizationCatalog.LoadCompanion(Path.Combine(root,"Example.dll"),"Different",CancellationToken.None));
            File.WriteAllBytes(Path.Combine(root,en),english.Reverse().ToArray());
            Failure("LocalizationDigestMismatch",()=>ExtensionLocalizationCatalog.LoadCompanion(Path.Combine(root,"Example.dll"),"Example",CancellationToken.None));
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
