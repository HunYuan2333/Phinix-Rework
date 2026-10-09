using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    [PhinixExtension("phinix.plugin-store")]
    public sealed class PluginStoreClientExtension : ClientExtensionModule, IActivatablePhinixExtensionModule
    {
        private IClientCompositionScope composition;
        private IClientCompositionScope activationComposition;
        private PluginStoreSettingsPanel panel;
        private PluginStoreMainTabProvider tab;
        private PluginStoreUpdateBanner updateBanner;
        private ManagedStoreController managedController;
        private IClientLocalizer localizer;
        private StoreBadgeIcons badgeIcons;
        private StoreReleaseNotice releaseNotice;

        public override string ExtensionId => "phinix.plugin-store";

        public override void Compose(IExtensionBuilder builder)
        {
            if(composition!=null) throw new InvalidOperationException("Plugin Store is already composed.");
            composition=builder.HostContext.GetRequiredService<IClientCompositionFactory>().CreateScope(local=>
            {
                local.Register<IClientSettingsPanelProvider,PluginStoreSettingsPanel>();
                local.Register<IMainTabProvider,PluginStoreMainTabProvider>();
                local.Register<INoticeBannerProvider,PluginStoreUpdateBanner>();
            });
            panel=composition.Resolve<PluginStoreSettingsPanel>();
            tab=composition.Resolve<PluginStoreMainTabProvider>();
            updateBanner=composition.Resolve<PluginStoreUpdateBanner>();
            builder.RegisterApi<IClientSettingsPanelProvider>(panel);
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.RegisterApi<INoticeBannerProvider>(updateBanner);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if(composition==null) throw new InvalidOperationException("Plugin Store must be composed before activation.");
            StopActivation();
            try
            {
                var environment=hostContext.GetRequiredService<IClientEnvironmentService>();
                var settings=hostContext.GetRequiredService<IClientSettingsContext>();
                var dispatcher=hostContext.GetRequiredService<IClientMainThreadDispatcher>();
                var windows=hostContext.GetRequiredService<IClientWindowService>();
                var theme=hostContext.GetRequiredService<IUiTheme>();
                activationComposition=hostContext.GetRequiredService<IClientCompositionFactory>().CreateScope(local=>
                {
                    local.Borrow<IPhinixExtensionModule>(this);
                    local.Borrow(panel);
                    local.Borrow(environment);
                    local.Borrow(settings);
                    local.Borrow(dispatcher);
                    local.Borrow(windows);
                    local.Borrow(theme);
                    local.Borrow(hostContext.GetRequiredService<IClientLocalizationService>());
                    local.Borrow(hostContext.GetRequiredService<IClientExtensionManagementWindowService>());
                    IClientExtensionControlService controls;
                    if(hostContext.TryGetService(out controls)) local.Borrow(controls);
                    local.Borrow(hostContext.GetRequiredService<IClientLinkService>());
                    local.Borrow(hostContext.GetRequiredService<IManagedExtensionManagementService>());
                    local.Borrow(hostContext.GetRequiredService<IManagedExtensionInstallationService>());
                    local.Borrow<Action<string,Utils.LogLevel>>((message,level)=>hostContext.Log?.Invoke(message,level));
                    local.Register<StoreActivationDiagnostics,StoreActivationDiagnostics>();
                    local.Register<IClientLocalizer,StoreLocalizerLease>();
                    local.Register<StoreControllerLease,StoreControllerLease>();
                    local.Register<StoreBadgeIcons,StoreBadgeIcons>();
                    local.Register<StoreViewFactory,StoreViewFactory>();
                    local.Register<StoreReleaseNoticeLease,StoreReleaseNoticeLease>();
                });
                localizer=activationComposition.Resolve<IClientLocalizer>();
                theme.RegisterColor("plugin-store.badge.official",new UnityEngine.Color(.94f,.76f,.36f));
                theme.RegisterColor("plugin-store.badge.managed",new UnityEngine.Color(.43f,.81f,.77f));
                theme.RegisterColor("plugin-store.badge.workshop",new UnityEngine.Color(.53f,.73f,.96f));
                theme.RegisterColor("plugin-store.badge.local",new UnityEngine.Color(.96f,.49f,.61f));
                managedController=activationComposition.Resolve<StoreControllerLease>().Controller;
                badgeIcons=activationComposition.Resolve<StoreBadgeIcons>();
                var views=activationComposition.Resolve<StoreViewFactory>();
                tab.Initialize(views.Create());
                panel.Initialize(windows,dispatcher,views.Create);
                updateBanner.Initialize(managedController,panel.Open);
                releaseNotice=activationComposition.Resolve<StoreReleaseNoticeLease>().Notice;
                releaseNotice.Start();
                // Capture game/translation-sensitive facts before background work.
                var endpoint=new RepositoryEndpoint(RepositoryProfile.Official,
                    settings.Get("plugin-store.officialAccessMethod","github")=="cloudflare"?RepositoryAccessMethod.Cloudflare:RepositoryAccessMethod.GitHub);
                managedController.CheckUpdates(endpoint,environment.Capture());
            }
            catch(Exception error)
            {
                try { Shutdown(hostContext); }
                catch(Exception cleanup) { throw new AggregateException(error,cleanup); }
                throw;
            }
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            var failures=new List<Exception>();
            Release(StopActivation,failures);
            IClientCompositionScope oldComposition=composition; composition=null;
            Release(()=>oldComposition?.Dispose(),failures);
            panel=null; tab=null; updateBanner=null;
            if(failures.Count!=0) throw new AggregateException("Plugin Store shutdown failed.",failures);
        }

        private void StopActivation()
        {
            var failures=new List<Exception>();
            Release(()=>releaseNotice?.Stop(),failures);
            releaseNotice=null;
            Release(()=>tab?.Stop(),failures);
            Release(()=>panel?.Stop(),failures);
            Release(()=>updateBanner?.Stop(),failures);
            var owned=activationComposition; activationComposition=null;
            managedController=null; localizer=null; badgeIcons=null;
            Release(()=>owned?.Dispose(),failures);
            if(failures.Count!=0) throw new AggregateException("Plugin Store activation cleanup failed.",failures);
        }

        private static void Release(Action release,List<Exception> failures)
        {
            try { release(); }
            catch(Exception error) { failures.Add(error); }
        }

    }
}
