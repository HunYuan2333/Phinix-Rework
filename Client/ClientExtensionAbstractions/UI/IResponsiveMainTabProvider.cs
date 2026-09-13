namespace PhinixClient
{
    /// <summary>
    /// Optional responsive extension for <see cref="IMainTabProvider"/>.
    /// Existing providers do not need to implement this interface.
    /// </summary>
    public interface IResponsiveMainTabProvider
    {
        /// <summary>
        /// Gets cached, allocation-free layout preferences for this provider.
        /// </summary>
        UiLayoutHints LayoutHints { get; }
    }
}
