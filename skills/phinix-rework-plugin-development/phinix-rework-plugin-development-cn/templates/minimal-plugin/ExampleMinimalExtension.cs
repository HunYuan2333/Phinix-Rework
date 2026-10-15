using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;

namespace Author.Minimal.Plugin
{
    [PhinixExtension("author.minimal.plugin")]
    public sealed class ExampleMinimalExtension : ClientExtensionModule, IActivatablePhinixExtensionModule
    {
        private IClientCompositionScope scope;
        private MinimalState state;

        public override string ExtensionId => "author.minimal.plugin";

        public override void Compose(IExtensionBuilder builder)
        {
            if (scope != null) throw new InvalidOperationException("Already composed.");
            var host = builder.HostContext;
            scope = host.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
            {
                local.Borrow(host.GetRequiredService<IClientSettingsContext>());
                local.Borrow(host.GetRequiredService<IClientTheme>());
                local.Register<MinimalState, MinimalState>();
                local.Register<IMainTabProvider, MinimalTab>();
            });

            try
            {
                state = scope.Resolve<MinimalState>();
                builder.RegisterApi(scope.Resolve<IMainTabProvider>());
            }
            catch
            {
                Shutdown(host);
                throw;
            }
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            state?.Start();
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            state?.Stop();
            var owned = scope;
            scope = null;
            state = null;
            owned?.Dispose();
        }
    }

    internal sealed class MinimalState
    {
        private readonly IClientSettingsContext settings;
        internal bool Active { get; private set; }

        public MinimalState(IClientSettingsContext settings)
        {
            this.settings = settings;
        }

        internal void Start() => Active = true;
        internal void Stop() => Active = false;

        internal int Count
        {
            get => settings.Get("author.minimal.count", 0);
            set => settings.Set("author.minimal.count", value);
        }
    }

    internal sealed class MinimalTab : IMainTabProvider
    {
        private readonly MinimalState state;
        private readonly IClientTheme theme;

        public MinimalTab(MinimalState state, IClientTheme theme)
        {
            this.state = state;
            this.theme = theme;
        }

        public string TabLabel => "Minimal";
        public float TabOrder => 500f;

        public void Draw(Rect inRect)
        {
            using (new ClientGuiScope(true))
            {
                ClientWidgets.Surface(inRect, theme);
                ClientWidgets.Label(new Rect(inRect.x + 10f, inRect.y + 10f, 200f, 30f), "Count: " + state.Count, theme);
                if (ClientWidgets.Button(new Rect(inRect.x + 10f, inRect.y + 45f, 100f, 30f), "+1", theme, enabled: state.Active))
                {
                    state.Count++;
                }
            }
        }
    }
}
