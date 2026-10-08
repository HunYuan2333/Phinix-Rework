using System;

namespace PhinixClient.Framework
{
    // Module Register is the composition boundary. Constructors must not start work.
    public interface IClientCompositionFactory
    {
        IClientCompositionScope CreateScope(Action<IClientCompositionBuilder> configure);
    }

    public interface IClientCompositionBuilder
    {
        void Borrow<T>(T instance) where T : class;
        // Owned resources must provide synchronous IDisposable cleanup; async-only ownership is rejected.
        void Register<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    }

    // A plugin owns its scope. Borrowed dependencies are never disposed here.
    // Resolve is for composition, not service location in business/UI code.
    public interface IClientCompositionScope : IDisposable
    {
        T Resolve<T>() where T : class;
    }
}

namespace PhinixClient.Framework
{
    // Temporary source adapter for legacy client authors; direct shared Register clients
    // receive the same migration diagnostic from the client host. Servers keep Register.
    [Obsolete("Legacy client Register is deprecated. Derive from ClientExtensionModule and override Compose. Removal is planned for host 1.0 / client abstractions 2.0 after migration gates.")]
    public abstract class LegacyClientExtensionModule : Utils.Framework.IPhinixExtensionModule
    {
        public abstract string ExtensionId { get; }
        [Obsolete("Use ClientExtensionModule.Compose; legacy client Register is a temporary migration entry.")]
        public abstract void Register(Utils.Framework.IExtensionBuilder builder);
    }

    // New client author entry. The shared registry still owns discovery and lifecycle.
    public interface IClientExtensionModule : Utils.Framework.IPhinixExtensionModule
    {
        void Compose(Utils.Framework.IExtensionBuilder builder);
    }

    public abstract class ClientExtensionModule : IClientExtensionModule
    {
        public abstract string ExtensionId { get; }
        public abstract void Compose(Utils.Framework.IExtensionBuilder builder);

        // Internal compatibility bridge, not a second author override point.
        void Utils.Framework.IPhinixExtensionModule.Register(Utils.Framework.IExtensionBuilder builder)
        {
            if (builder == null) throw new System.ArgumentNullException(nameof(builder));
            builder.HostContext.GetRequiredService<IClientCompositionFactory>();
            Compose(builder);
        }
    }
}
