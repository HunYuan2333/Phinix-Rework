namespace PhinixClient.Framework
{
    public sealed class BuiltInTradeClientHostServices
    {
        public BuiltInTradeClientHostServices(PhinixFrameworkTradeClientService tradeService)
        {
            TradeService = tradeService;
        }

        public PhinixFrameworkTradeClientService TradeService { get; }
    }
}
