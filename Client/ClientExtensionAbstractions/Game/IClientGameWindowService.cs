using Verse;

namespace PhinixClient.Framework
{
    /// <summary>Explicit game boundary for owner-created native windows.</summary>
    public interface IClientGameWindowService
    {
        // Main thread only. Opening a window does not transfer business ownership
        // or imply completion, cancellation or a return of reserved resources.
        void Open(Window window);
    }
}
