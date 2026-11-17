using System;
using Verse;

namespace PhinixClient.Framework
{
    internal sealed class ClientWindowService : IClientGameWindowService, IClientSettingsWindowService, IClientExtensionManagementWindowService
    {
        private ExtensionManagerWindow extensionManager;
        private readonly ClientExtensionManagementWindowService management;
        private readonly Func<string,string,Exception,string> fault;

        internal static bool IsReady => UnityData.IsInMainThread && Find.UIRoot != null && Find.WindowStack != null;

        public ClientWindowService(Func<string,string,Exception,string> fault = null)
        {
            this.fault = fault;
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
            if (!IsReady) throw new InvalidOperationException("Game window system is not ready.");
            try { Find.WindowStack.Add(window); }
            catch (Exception error)
            {
                try { fault?.Invoke("builtin.host", "OpenWindow", error); } catch { }
                throw;
            }
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
