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
                hostContext.TryGetService<IExtensionDiscoveryPolicy>(out var originalPolicy);
                var policy = new CurrentClientModules(originalPolicy);
                hostContext.AddService<IExtensionDiscoveryPolicy>(policy);
                try { Extensions = PhinixExtensionRegistry.DiscoverExtensions(hostContext); }
                finally
                {
                    if (originalPolicy == null) hostContext.RemoveService<IExtensionDiscoveryPolicy>(policy);
                    else hostContext.AddService<IExtensionDiscoveryPolicy>(originalPolicy);
                }
                Extensions.Warnings.AddRange(policy.Rejections);
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

        private sealed class CurrentClientModules : IExtensionDiscoveryPolicy
        {
            private readonly IExtensionDiscoveryPolicy inner;
            internal readonly System.Collections.Generic.List<string> Rejections = new System.Collections.Generic.List<string>();
            internal CurrentClientModules(IExtensionDiscoveryPolicy inner) { this.inner = inner; }
            public bool ShouldScanAssembly(System.Reflection.Assembly assembly) => inner == null || inner.ShouldScanAssembly(assembly);
            public bool ShouldDiscoverType(Type type)
            {
                if (inner != null && !inner.ShouldDiscoverType(type)) return false;
                if (typeof(IClientExtensionModule).IsAssignableFrom(type)) return true;
                if (type.IsClass && !type.IsAbstract && typeof(IPhinixExtensionModule).IsAssignableFrom(type))
                    Rejections.Add("[ClientEntryUnsupported] " + type.FullName + ": use ClientExtensionModule.Compose.");
                return false;
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
