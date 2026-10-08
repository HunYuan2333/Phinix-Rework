using System;
using System.Collections.Generic;
using Phinix.InventoryExtension;
using PhinixClient.Framework;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    internal static class TradeLifetimeCleanup
    {
        internal static void Try(Action release, List<Exception> failures)
        {
            try { release(); }
            catch (Exception error) { failures.Add(error); }
        }

        internal static void ThrowIfFailed(List<Exception> failures)
        {
            if (failures.Count != 0) throw new AggregateException("Trade lifetime cleanup failed.", failures);
        }
    }

    internal sealed class TradeInventoryRegistrations : IDisposable
    {
        private IDisposable codec;
        private IDisposable source;

        public TradeInventoryRegistrations(IInventoryRegistrationApi inventory,
            TradeInventoryCodec codec, TradeInventorySourcePresenter source)
        {
            try
            {
                this.codec = inventory.RegisterCodecScoped(codec);
                this.source = inventory.RegisterSourcePresenter(source);
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }

        public void Dispose()
        {
            IDisposable oldSource = source;
            IDisposable oldCodec = codec;
            source = null;
            codec = null;
            var failures = new List<Exception>();
            TradeLifetimeCleanup.Try(() => oldSource?.Dispose(), failures);
            TradeLifetimeCleanup.Try(() => oldCodec?.Dispose(), failures);
            TradeLifetimeCleanup.ThrowIfFailed(failures);
        }
    }

    internal sealed class TradeClientCallbacks
    {
        internal readonly EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityChanged;
        internal readonly EventHandler UsersChanged;
        internal readonly EventHandler Disconnected;
        internal readonly Action<string, object> SettingChanged;

        public TradeClientCallbacks(EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChanged,
            EventHandler usersChanged, EventHandler disconnected, Action<string, object> settingChanged)
        {
            CompatibilityChanged = compatibilityChanged;
            UsersChanged = usersChanged;
            Disconnected = disconnected;
            SettingChanged = settingChanged;
        }
    }

    internal sealed class TradeConnectionSubscriptions : IDisposable
    {
        private readonly IFrameworkClientLifecycle lifecycle;
        private readonly IClientUserEventStream users;
        private readonly IClientSettingsContext settings;
        private readonly TradeClientCallbacks callbacks;
        private bool disposed;

        public TradeConnectionSubscriptions(IFrameworkClientLifecycle lifecycle, IClientUserEventStream users,
            IClientSettingsContext settings, TradeClientCallbacks callbacks)
        {
            this.lifecycle = lifecycle;
            this.users = users;
            this.settings = settings;
            this.callbacks = callbacks;
            try
            {
                lifecycle.CompatibilityModeChanged += callbacks.CompatibilityChanged;
                users.UsersChanged += callbacks.UsersChanged;
                users.Disconnected += callbacks.Disconnected;
                settings.OnSettingChanged += callbacks.SettingChanged;
            }
            catch (Exception error)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var failures = new List<Exception>();
            TradeLifetimeCleanup.Try(() => lifecycle.CompatibilityModeChanged -= callbacks.CompatibilityChanged, failures);
            TradeLifetimeCleanup.Try(() => users.UsersChanged -= callbacks.UsersChanged, failures);
            TradeLifetimeCleanup.Try(() => users.Disconnected -= callbacks.Disconnected, failures);
            TradeLifetimeCleanup.Try(() => settings.OnSettingChanged -= callbacks.SettingChanged, failures);
            TradeLifetimeCleanup.ThrowIfFailed(failures);
        }
    }
}
