namespace PhinixClient
{
    /// <summary>
    /// Optional capability for the active UI provider to handle RimWorld's Accept key.
    /// </summary>
    public interface IUiAcceptKeyHandler
    {
        bool WantsAcceptKey { get; }

        bool TryHandleAcceptKey();
    }
}
