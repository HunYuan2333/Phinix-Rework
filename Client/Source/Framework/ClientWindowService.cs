using Verse;

namespace PhinixClient.Framework
{
    internal sealed class ClientWindowService : IClientWindowService, IClientSettingsWindowService
    {
        public void Open(Window window)
        {
            if (window == null)
            {
                return;
            }

            Find.WindowStack.Add(window);
        }

        public void OpenSettingsWindow()
        {
            Find.WindowStack.Add(new SettingsWindow());
        }
    }
}
