using Utils.Framework;

namespace PhinixClient.Framework
{
    public sealed class DefaultTradeCompletionHandler : ITradeCompletionHandler
    {
        private readonly PhinixDefaultTradeBehaviour defaultTradeBehaviour;

        public string HandlerId => "core.trade-completion.default";

        public int Priority => int.MaxValue;

        public DefaultTradeCompletionHandler(PhinixDefaultTradeBehaviour defaultTradeBehaviour)
        {
            this.defaultTradeBehaviour = defaultTradeBehaviour;
        }

        public bool CanHandle(TradeCompletionContext context)
        {
            return context != null;
        }

        public void Handle(TradeCompletionContext context)
        {
            defaultTradeBehaviour.HandleTradeCompleted(context);
        }
    }
}
