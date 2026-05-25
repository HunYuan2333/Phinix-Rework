using PhinixClient.Trade;

namespace PhinixClient.Framework
{
    public sealed class DefaultTradeCompletionHandler : IClientTradeCompletionHandler
    {
        private readonly PhinixDefaultTradeBehaviour defaultTradeBehaviour;

        public string HandlerId => "core.trade-completion.default";

        public int Priority => int.MaxValue;

        public DefaultTradeCompletionHandler(PhinixDefaultTradeBehaviour defaultTradeBehaviour)
        {
            this.defaultTradeBehaviour = defaultTradeBehaviour;
        }

        public bool CanHandle(ClientTradeCompletionContext context)
        {
            return context != null;
        }

        public void Handle(ClientTradeCompletionContext context)
        {
            defaultTradeBehaviour.HandleTradeCompleted(context);
        }
    }
}
