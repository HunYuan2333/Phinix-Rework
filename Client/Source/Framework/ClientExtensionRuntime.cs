using System;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Owns module lifecycle only. Host services and game objects are borrowed.
    // Calls are serialized by the host on its game main thread.
    internal sealed class ClientExtensionRuntime : IDisposable
    {
        private readonly ExtensionHostContext hostContext;
        private readonly Func<bool> isMainThread;
        private bool starting;
        private bool started;
        private bool stopped;

        internal ClientExtensionRuntime(ExtensionHostContext hostContext, Func<bool> isMainThread)
        {
            this.hostContext = hostContext ?? throw new ArgumentNullException(nameof(hostContext));
            this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        }

        internal DiscoveredPhinixExtensions Extensions { get; private set; } = new DiscoveredPhinixExtensions();

        internal void Start()
        {
            RequireMainThread();
            if (stopped) throw new ObjectDisposedException(nameof(ClientExtensionRuntime));
            if (starting) throw new InvalidOperationException("Extension startup is already in progress.");
            if (started) return;

            starting = true;
            try
            {
                Extensions = PhinixExtensionRegistry.DiscoverExtensions(hostContext);
                ReportLegacyClientEntries();
                PhinixExtensionRegistry.ActivateExtensions(Extensions, hostContext);
                started = true;
            }
            catch
            {
                starting = false;
                Stop();
                throw;
            }
            finally
            {
                starting = false;
            }
        }

        private void ReportLegacyClientEntries()
        {
            // Only registered instances are inspected. Disabled/failed candidates are never
            // constructed for diagnostics; the shared server registry has no such policy.
            foreach (IPhinixExtensionModule module in Extensions.Modules)
            {
                if (module is IClientExtensionModule) continue;
                string warning = "[ClientRegistrationDeprecated] Extension '" + module.ExtensionId +
                    "' uses legacy client Register. Migrate to ClientExtensionModule.Compose " +
                    "(client abstractions 1.9). Removal is planned for host 1.0 / abstractions 2.0 " +
                    "after migration and acceptance gates.";
                Extensions.Warnings.Add(warning);
            }
        }

        // Stop/Dispose are terminal and idempotent; a new host creates a new runtime.
        internal void Stop()
        {
            if (stopped) return;
            if (starting) throw new InvalidOperationException("Extension startup is still in progress.");
            if (starting || started) RequireMainThread();
            stopped = true;
            PhinixExtensionRegistry.ShutdownExtensions(Extensions, hostContext);
        }

        public void Dispose()
        {
            Stop();
        }

        internal void RequireMainThread()
        {
            if (!isMainThread()) throw new InvalidOperationException("Extension lifecycle requires the game main thread.");
        }
    }
}
