using System;
using Verse;

namespace PhinixClient.Framework
{
    internal sealed class ClientWindowService : IClientGameWindowService, IClientSettingsWindowService, IClientExtensionManagementWindowService
    {
        private ExtensionManagerWindow extensionManager;
        private readonly ClientExtensionManagementWindowService management;

        public ClientWindowService()
        {
            management = new ClientExtensionManagementWindowService(
                () => extensionManager != null && extensionManager.IsOpen,
                () =>
                {
                    extensionManager = new ExtensionManagerWindow();
                    Open(extensionManager);
                }, () => UnityData.IsInMainThread);
        }

        public void Open(Window window)
        {
            if (window == null)
            {
                return;
            }

            RequireMainThread();
            Find.WindowStack.Add(window);
        }

        public void OpenExtensionManagerWindow()
        {
            management.OpenExtensionManagerWindow();
        }

        private static void RequireMainThread()
        {
            if (!UnityData.IsInMainThread)
                throw new InvalidOperationException("Game windows must be opened on the main thread.");
        }

        public void OpenSettingsWindow()
        {
            RequireMainThread();
            Open(new SettingsWindow());
        }
    }
}
