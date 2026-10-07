using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    [PhinixExtension("phinix.plugin-store")]
    public sealed class PluginStoreClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        private readonly PluginStoreSettingsPanel panel = new PluginStoreSettingsPanel();
        private readonly PluginStoreMainTabProvider tab = new PluginStoreMainTabProvider();
        private readonly PluginStoreUpdateBanner updateBanner = new PluginStoreUpdateBanner();
        private ManagedStoreController managedController;
        private IClientLocalizer localizer;
        private StoreBadgeIcons badgeIcons;
        private StoreReleaseNotice releaseNotice;
        private Dialog_MessageBox releaseNoticeWindow;
        private IClientMainThreadDispatcher noticeDispatcher;
        public string ExtensionId => "phinix.plugin-store";

        public void Register(IExtensionBuilder builder)
        {
            builder.RegisterApi<IClientSettingsPanelProvider>(panel);
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.RegisterApi<INoticeBannerProvider>(updateBanner);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            StopReleaseNotice();
            tab.Stop();
            panel.Stop();
            updateBanner.Stop();
            managedController?.Dispose();
            localizer?.Dispose();
            badgeIcons?.Dispose();
            localizer=hostContext.GetRequiredService<IClientLocalizationService>().ForModule(this);
            var environment = hostContext.GetRequiredService<IClientEnvironmentService>();
            var settings = hostContext.GetRequiredService<IClientSettingsContext>();
            var management = hostContext.GetRequiredService<IClientExtensionManagementWindowService>();
            var dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            var links = hostContext.GetRequiredService<IClientLinkService>();
            var theme = hostContext.GetRequiredService<IUiTheme>();
            theme.RegisterColor("plugin-store.badge.official",new UnityEngine.Color(.94f,.76f,.36f));
            theme.RegisterColor("plugin-store.badge.managed",new UnityEngine.Color(.43f,.81f,.77f));
            theme.RegisterColor("plugin-store.badge.workshop",new UnityEngine.Color(.53f,.73f,.96f));
            var maintainers=StoreMaintainerRegistry.Empty;
            try { maintainers=StoreMaintainerRegistry.Load(); }
            catch(System.Exception ex) { hostContext.Log("Store maintainer badges unavailable: "+ex.GetType().Name,Utils.LogLevel.WARNING); }
            badgeIcons=new StoreBadgeIcons(dispatcher,message=>hostContext.Log("Store badge resource unavailable: "+message,Utils.LogLevel.WARNING));
            managedController=new ManagedStoreController(hostContext.GetRequiredService<IManagedExtensionManagementService>(),hostContext.GetRequiredService<IManagedExtensionInstallationService>(),
                line=>hostContext.Log("Plugin store audit: "+line,Utils.LogLevel.INFO));
            System.Func<ManagedPluginStoreView> createView=()=>new ManagedPluginStoreView(managedController,environment,settings,management,localizer,theme,links,maintainers,badgeIcons);
            tab.Initialize(createView());
            var windows=hostContext.GetRequiredService<IClientWindowService>();
            panel.Initialize(windows,
                dispatcher,createView);
            updateBanner.Initialize(managedController,panel.Open);
            noticeDispatcher=dispatcher;
            releaseNotice=new StoreReleaseNotice(
                ()=>settings.Get(StoreReleaseNotice.SettingsKey,false),
                ()=>settings.Set(StoreReleaseNotice.SettingsKey,true),
                dispatcher.Enqueue,
                ()=> {
                    releaseNoticeWindow=new Dialog_MessageBox(
                        "Phinix_store2_optionalPluginsNotice".Translate(),
                        "Phinix_store_open".Translate(),panel.Open,
                        "Phinix_store2_noticeUnderstood".Translate(),null,
                        "Phinix_store2_optionalPluginsNoticeTitle".Translate());
                    windows.Open(releaseNoticeWindow);
                },message=>hostContext.Log(message,Utils.LogLevel.WARNING));
            releaseNotice.Start();
            // Capture game/translation-sensitive facts now, before any background work.
            var endpoint=new RepositoryEndpoint(RepositoryProfile.Official,
                settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub);
            managedController.CheckUpdates(endpoint,environment.Capture());
        }

        public void Shutdown(ExtensionHostContext hostContext)
        { StopReleaseNotice(); tab.Stop(); panel.Stop(); updateBanner.Stop(); managedController?.Dispose(); managedController=null; localizer?.Dispose(); localizer=null; badgeIcons?.Dispose(); badgeIcons=null; }
        private void StopReleaseNotice()
        {
            releaseNotice?.Stop(); releaseNotice=null;
            var closing=releaseNoticeWindow; releaseNoticeWindow=null;
            if(closing!=null) noticeDispatcher?.Enqueue(()=>closing.Close());
            noticeDispatcher=null;
        }
    }
}
