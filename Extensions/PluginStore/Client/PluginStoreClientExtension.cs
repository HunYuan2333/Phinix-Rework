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
            localizer=hostContext.GetRequiredService<IClientLocalizationService>().ForModule(this);
            var environment = hostContext.GetRequiredService<IClientEnvironmentService>();
            var settings = hostContext.GetRequiredService<IClientSettingsContext>();
            var management = hostContext.GetRequiredService<IClientExtensionManagementWindowService>();
            managedController=new ManagedStoreController(hostContext.GetRequiredService<IManagedExtensionManagementService>(),hostContext.GetRequiredService<IManagedExtensionInstallationService>(),
                line=>hostContext.Log("Plugin store audit: "+line,Utils.LogLevel.INFO));
            System.Func<ManagedPluginStoreView> createView=()=>new ManagedPluginStoreView(managedController,environment,settings,management,localizer);
            tab.Initialize(createView());
            panel.Initialize(hostContext.GetRequiredService<IClientWindowService>(),
                hostContext.GetRequiredService<IClientMainThreadDispatcher>(),createView);
            updateBanner.Initialize(managedController,panel.Open);
            // Capture game/translation-sensitive facts now, before any background work.
            var endpoint=new RepositoryEndpoint(RepositoryProfile.Official,
                settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub);
            managedController.CheckUpdates(endpoint,environment.Capture());
        }

        public void Shutdown(ExtensionHostContext hostContext)
        { tab.Stop(); panel.Stop(); updateBanner.Stop(); managedController?.Dispose(); managedController=null; localizer?.Dispose(); localizer=null; }
    }
}
