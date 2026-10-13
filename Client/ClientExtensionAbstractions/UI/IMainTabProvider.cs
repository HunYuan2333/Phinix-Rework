using UnityEngine;

namespace PhinixClient
{
    public interface IMainTabProvider
    {
        string TabLabel { get; }
        float TabOrder { get; }
        void Draw(Rect inRect);
    }
}

namespace PhinixClient.Framework
{
    // Optional for main/sidebar/banner providers. Host combines this local ID with the actual API owner.
    // Labels remain provider-owned and localized. Older Draw providers need not implement this.
    public interface IClientNavigationMetadata
    {
        string NavigationId { get; }
    }
}

namespace PhinixClient.Framework
{
    // Optional shell view adapter; projection/input update runs outside IMGUI measure/draw.
    // These hooks do not mean business start, cancellation or rollback.
    public interface IClientViewLifecycle
    {
        void ViewOpened();
        void ViewUpdate();
        void ViewClosed();
    }
}
