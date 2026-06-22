using UnityEngine;

namespace PhinixClient
{
    public interface IServerSidebarProvider
    {
        float Order { get; }

        float PreferredWidth { get; }

        string TabLabel { get; }

        void Draw(Rect inRect);
    }
}
