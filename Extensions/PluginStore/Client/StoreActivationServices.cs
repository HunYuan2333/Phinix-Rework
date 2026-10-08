using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class StoreActivationDiagnostics
    {
        private readonly Action<string,LogLevel> log;
        public StoreActivationDiagnostics(Action<string,LogLevel> log) { this.log=log; }
        internal void Audit(string line) { log("Plugin store audit: "+line,LogLevel.INFO); }
        internal void Badge(string message) { Warning("BadgeIcons","StoreBadgeUnavailable",message); }
        internal void Notice(string message) { Warning("ReleaseNotice","StoreReleaseNoticeFailed",message); }
        private void Warning(string operation,string code,string message)
        { new RepositoryDiagnostics(line=>log("Plugin store audit: "+line,LogLevel.WARNING)).Warning(operation,code,message); }
        internal void Maintainer(Exception error)
        { new RepositoryDiagnostics(line=>log("Plugin store audit: "+line,LogLevel.WARNING)) {Operation="Presentation"}
            .Failure("store.presentation_warning","MaintainerBadges",error,"StoreMaintainerUnavailable"); }
    }

    internal sealed class StoreLocalizerLease : IClientLocalizer
    {
        private IClientLocalizer inner;
        public StoreLocalizerLease(IClientLocalizationService service,IPhinixExtensionModule module)
        { inner=service.ForModule(module)??throw new InvalidOperationException("Store localizer unavailable."); }
        public string Locale => inner?.Locale;
        public event Action LanguageChanged { add { if(inner!=null) inner.LanguageChanged+=value; } remove { if(inner!=null) inner.LanguageChanged-=value; } }
        public string Text(string key,string fallback=null) => inner?.Text(key,fallback)??fallback??key;
        public string Format(string key,params object[] arguments) => inner?.Format(key,arguments)??key;
        public void Dispose() { var owned=inner; inner=null; owned?.Dispose(); }
    }

    internal sealed class StoreControllerLease : IDisposable
    {
        internal ManagedStoreController Controller { get; }
        public StoreControllerLease(IManagedExtensionManagementService management,IManagedExtensionInstallationService installation,
            IClientEnvironmentService environment,IClientMainThreadDispatcher dispatcher,StoreActivationDiagnostics diagnostics)
        { Controller=new ManagedStoreController(management,installation,diagnostics.Audit,
            token=>StoreEnvironmentRefresh.Capture(environment.Capture,dispatcher.Enqueue,token)); }
        public void Dispose() { Controller.Dispose(); }
    }

    // The factory is scoped; every tab/window receives its own UI state.
    internal sealed class StoreViewFactory : IDisposable
    {
        private readonly StoreControllerLease controller;
        private readonly IClientEnvironmentService environment;
        private readonly IClientSettingsContext settings;
        private readonly IClientExtensionManagementWindowService management;
        private readonly IClientLocalizer localizer;
        private readonly IUiTheme theme;
        private readonly IClientLinkService links;
        private readonly StoreBadgeIcons icons;
        private readonly StoreMaintainerRegistry maintainers;
        private readonly List<System.WeakReference<ManagedPluginStoreView>> views=new List<System.WeakReference<ManagedPluginStoreView>>();
        private bool disposed;
        public StoreViewFactory(StoreControllerLease controller,IClientEnvironmentService environment,IClientSettingsContext settings,
            IClientExtensionManagementWindowService management,IClientLocalizer localizer,IUiTheme theme,IClientLinkService links,
            StoreBadgeIcons icons,StoreActivationDiagnostics diagnostics)
        {
            this.controller=controller; this.environment=environment; this.settings=settings; this.management=management;
            this.localizer=localizer; this.theme=theme; this.links=links; this.icons=icons;
            maintainers=StoreMaintainerRegistry.Empty;
            try { maintainers=StoreMaintainerRegistry.Load(); }
            catch(Exception error) { diagnostics.Maintainer(error); }
        }
        internal ManagedPluginStoreView Create()
        {
            if(disposed) throw new ObjectDisposedException(nameof(StoreViewFactory));
            var view=new ManagedPluginStoreView(controller.Controller,environment,settings,management,localizer,theme,links,maintainers,icons);
            if(views.Count>=32) views.RemoveAll(reference=> { ManagedPluginStoreView target; return !reference.TryGetTarget(out target); });
            views.Add(new System.WeakReference<ManagedPluginStoreView>(view)); return view;
        }
        public void Dispose()
        { if(disposed) return; disposed=true; foreach(var reference in views) { ManagedPluginStoreView view; if(reference.TryGetTarget(out view)) view.Stop(); } views.Clear(); }
    }

    internal sealed class StoreReleaseNoticeLease : IDisposable
    {
        private readonly IClientMainThreadDispatcher dispatcher;
        private Dialog_MessageBox window;
        internal StoreReleaseNotice Notice { get; }
        private bool disposed;
        public StoreReleaseNoticeLease(IClientSettingsContext settings,IClientMainThreadDispatcher dispatcher,
            IClientWindowService windows,PluginStoreSettingsPanel panel,StoreActivationDiagnostics diagnostics)
        {
            this.dispatcher=dispatcher;
            Notice=new StoreReleaseNotice(()=>settings.Get(StoreReleaseNotice.SettingsKey,false),
                ()=>settings.Set(StoreReleaseNotice.SettingsKey,true),dispatcher.Enqueue,
                ()=> {
                    if(disposed) return;
                    window=new Dialog_MessageBox("Phinix_store2_optionalPluginsNotice".Translate(),
                        "Phinix_store_open".Translate(),panel.Open,"Phinix_store2_noticeUnderstood".Translate(),null,
                        "Phinix_store2_optionalPluginsNoticeTitle".Translate());
                    windows.Open(window);
                },diagnostics.Notice);
        }
        public void Dispose()
        {
            if(disposed) return; disposed=true; Notice.Stop();
            var closing=window; window=null;
            if(closing!=null) dispatcher.Enqueue(()=>closing.Close());
        }
    }
}
