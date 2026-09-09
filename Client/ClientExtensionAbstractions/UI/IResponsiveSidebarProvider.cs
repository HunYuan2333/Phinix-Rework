namespace PhinixClient
{
    /// <summary>
    /// Optional responsive extension for <see cref="IServerSidebarProvider"/>.
    /// Existing providers do not need to implement this interface.
    /// </summary>
    public interface IResponsiveSidebarProvider
    {
        /// <summary>Gets the smallest useful sidebar width in RimWorld UI coordinates.</summary>
        float MinimumWidth { get; }

        /// <summary>Gets whether the host may collapse this sidebar when space is constrained.</summary>
        bool CanCollapse { get; }
    }
}
