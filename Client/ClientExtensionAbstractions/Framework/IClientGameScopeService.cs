namespace PhinixClient.Framework
{
    /// <summary>Captures a main-thread-published game lifetime without reading game APIs on the receiving thread.</summary>
    public interface IClientGameScopeService
    {
        // Null until an active game has been published. Not a persistent save identity or delivery receipt.
        IClientGameScope Capture();
    }

    public interface IClientGameScope
    {
        // Main thread only. A false result is permanent for this scope.
        bool IsCurrent();
    }
}
