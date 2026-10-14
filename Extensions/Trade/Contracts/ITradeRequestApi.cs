namespace Phinix.TradeExtension
{
    /// <summary>
    /// Trade-owned capability for requesting a trade with another user.
    /// Consumers resolve it through the ordinary API directory; dispatch is not
    /// an acknowledgement that a trade was created or any items were delivered.
    /// </summary>
    public interface ITradeRequestApi
    {
        void CreateTrade(string uuid);
    }
}
