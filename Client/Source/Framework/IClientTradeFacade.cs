using PhinixClient.Trade;

namespace PhinixClient.Framework
{
    public interface IClientTradeFacade
    {
        bool TryGetTrade(string tradeId, out ClientTradeSnapshot trade);

        bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid);
    }
}
