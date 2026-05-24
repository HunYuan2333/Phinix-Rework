using Trading;

namespace PhinixClient.Framework
{
    public interface IClientTradeFacade
    {
        bool TryGetTrade(string tradeId, out ImmutableTrade trade);

        bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid);
    }
}
