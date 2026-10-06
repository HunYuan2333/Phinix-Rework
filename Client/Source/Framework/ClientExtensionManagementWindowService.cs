using System;

namespace PhinixClient.Framework
{
    /// <summary>Keeps game callbacks on the host thread and avoids duplicate management windows.</summary>
    internal sealed class ClientExtensionManagementWindowService : IClientExtensionManagementWindowService
    {
        private readonly Func<bool> isMainThread;
        private readonly Func<bool> isOpen;
        private readonly Action open;

        public ClientExtensionManagementWindowService(Func<bool> isOpen, Action open, Func<bool> isMainThread)
        {
            this.isOpen = isOpen ?? throw new ArgumentNullException(nameof(isOpen));
            this.open = open ?? throw new ArgumentNullException(nameof(open));
            this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        }

        public void OpenExtensionManagerWindow()
        {
            if (!isMainThread())
                throw new InvalidOperationException("Extension management windows must be opened on the main thread.");
            if (!isOpen()) open();
        }
    }
}
