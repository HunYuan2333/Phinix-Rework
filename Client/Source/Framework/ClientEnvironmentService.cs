using System;

namespace PhinixClient.Framework
{
    internal sealed class ClientEnvironmentService : IClientEnvironmentService
    {
        private readonly Func<bool> isMainThread;
        private readonly Func<ClientEnvironmentSnapshot> capture;

        public ClientEnvironmentService(Func<ClientEnvironmentSnapshot> capture, Func<bool> isMainThread)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.isMainThread = isMainThread ?? throw new ArgumentNullException(nameof(isMainThread));
        }

        public ClientEnvironmentSnapshot Capture()
        {
            if (!isMainThread())
                throw new InvalidOperationException("Capture client environment on the main thread before starting background work.");
            return capture();
        }
    }
}
