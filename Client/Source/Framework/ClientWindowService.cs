using Verse;

namespace PhinixClient.Framework
{
    internal sealed class ClientWindowService : IClientWindowService, IClientSettingsWindowService, IClientExtensionManagementWindowService
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

            Find.WindowStack.Add(window);
        }

        public void OpenExtensionManagerWindow()
        {
            management.OpenExtensionManagerWindow();
        }

        public void OpenSettingsWindow()
        {
            Find.WindowStack.Add(new SettingsWindow());
        }
    }
}
