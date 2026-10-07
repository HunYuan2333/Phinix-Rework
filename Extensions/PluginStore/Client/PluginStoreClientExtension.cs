using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

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
        public string ExtensionId => "phinix.plugin-store";

        public void Register(IExtensionBuilder builder)
        {
            builder.RegisterApi<IClientSettingsPanelProvider>(panel);
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.RegisterApi<INoticeBannerProvider>(updateBanner);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
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
            panel.Initialize(hostContext.GetRequiredService<IClientWindowService>(),
                dispatcher,createView);
            updateBanner.Initialize(managedController,panel.Open);
            // Capture game/translation-sensitive facts now, before any background work.
            var endpoint=new RepositoryEndpoint(RepositoryProfile.Official,
                settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub);
            managedController.CheckUpdates(endpoint,environment.Capture());
        }

        public void Shutdown(ExtensionHostContext hostContext)
        { tab.Stop(); panel.Stop(); updateBanner.Stop(); managedController?.Dispose(); managedController=null; localizer?.Dispose(); localizer=null; badgeIcons?.Dispose(); badgeIcons=null; }
    }
}
