using System;
using System.Collections.Generic;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;

namespace Phinix.LegacyAdapter.Client
{
    internal sealed class LegacyTradeDependencies
    {
        internal IFrameworkTradeClientApi Api;
        internal IFrameworkLegacyTradeRepositoryApi Repository;
        internal IFrameworkLegacyTradeDeliveryApi Delivery;
        internal Action<string, LogLevel> Log;
    }

    // Owns registrations, never the borrowed transport or Trade services.
    internal sealed class LegacyAdapterSession : IDisposable
    {
        private readonly IFrameworkClientLifecycle lifecycle;
        private readonly SessionTransport transport;
        private readonly Action<string, LogLevel> log;
        private readonly object lifecycleGate = new object();
        private bool active;
        private bool tradeRegistered;
        internal LegacyChatProtocolAdapter Chat { get; }
        internal LegacyTradeProtocolAdapter Trade { get; }

        public LegacyAdapterSession(ILegacyModuleTransport transport, IDisplayMessageSink sink,
            IClientSessionContext session, IFrameworkClientLifecycle lifecycle, LegacyTradeDependencies dependencies)
        {
            this.lifecycle = lifecycle;
            log = dependencies.Log;
            this.transport = new SessionTransport(transport);
            Chat = new LegacyChatProtocolAdapter(this.transport, sink, session);
            Trade = new LegacyTradeProtocolAdapter(this.transport, sink, session, dependencies.Api,
                dependencies.Repository, dependencies.Delivery, lifecycle, dependencies.Log);
        }

        internal void Start()
        {
            lock (lifecycleGate)
            {
                if (active) return;
                active = true;
                try
                {
                    lifecycle.CompatibilityModeChanged += Changed;
                    Select(lifecycle.CompatibilityMode);
                }
                catch (Exception error)
                {
                    try { Dispose(); }
                    catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                    throw;
                }
            }
        }

        private void Changed(object sender, FrameworkCompatibilityModeChangedEventArgs args)
        {
            lock (lifecycleGate)
            {
                if (active) Select(args.CompatibilityMode);
            }
        }

        private void Select(FrameworkCompatibilityMode mode)
        {
            if (mode == FrameworkCompatibilityMode.Legacy)
            {
                Chat.RegisterHandlers();
                if (!tradeRegistered)
                {
                    tradeRegistered = true; // Cleanup also covers a partially successful registration.
                    Trade.RegisterHandlers();
                    log?.Invoke("[LegacyAdapter] Registered legacy protocol handlers (Chat + Trading).", LogLevel.INFO);
                }
            }
            else
            {
                if (tradeRegistered)
                {
                    tradeRegistered = false;
                    UnregisterTrade();
                }
                // History can arrive before negotiation finishes.
                if (mode == FrameworkCompatibilityMode.Unknown) Chat.RegisterHandlers();
                else Chat.UnregisterHandlers();
            }
        }

        public void Dispose()
        {
            lock (lifecycleGate)
            {
                if (!active) return;
                active = false;
                transport.Stop(); // Captured callbacks become inert before endpoint removal.
                var failures = new List<Exception>();
                Attempt(() => lifecycle.CompatibilityModeChanged -= Changed, failures);
                if (tradeRegistered)
                {
                    tradeRegistered = false;
                    Attempt(UnregisterTrade, failures);
                }
                Attempt(Chat.UnregisterHandlers, failures);
                if (failures.Count != 0) throw new AggregateException("Legacy adapter shutdown failed.", failures);
            }
        }

        private void UnregisterTrade()
        {
            Trade.UnregisterHandlers();
            log?.Invoke("[LegacyAdapter] Unregistered legacy trade protocol handlers.", LogLevel.INFO);
        }

        private static void Attempt(Action action, List<Exception> failures)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); }
        }

        private sealed class SessionTransport : ILegacyModuleTransport
        {
            private sealed class Registration { internal volatile bool Active = true; }
            private readonly ILegacyModuleTransport inner;
            private readonly Dictionary<string, Registration> registrations = new Dictionary<string, Registration>();
            private volatile bool stopped;
            internal SessionTransport(ILegacyModuleTransport inner) { this.inner = inner; }
            public void RegisterHandler(string module, RawPacketHandlerDelegate handler)
            {
                if (stopped) throw new ObjectDisposedException(nameof(LegacyAdapterSession));
                var registration = new Registration();
                registrations.Add(module, registration);
                inner.RegisterHandler(module, (name, connection, bytes) =>
                {
                    if (!stopped && registration.Active) handler(name, connection, bytes);
                });
            }
            public void UnregisterHandler(string module)
            {
                if (registrations.TryGetValue(module, out var registration))
                {
                    registration.Active = false;
                    registrations.Remove(module);
                    inner.UnregisterHandler(module);
                }
            }
            public void Send(string module, byte[] bytes)
            {
                if (stopped) throw new ObjectDisposedException(nameof(LegacyAdapterSession));
                inner.Send(module, bytes);
            }
            internal void Stop() { stopped = true; }
        }
    }
}
