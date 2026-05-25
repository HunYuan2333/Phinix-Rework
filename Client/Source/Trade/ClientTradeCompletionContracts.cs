using System;
using System.Collections.Generic;
using Utils;
using Utils.Framework;

namespace PhinixClient.Trade
{
    public interface IClientTradeCompletionHandler
    {
        string HandlerId { get; }

        int Priority { get; }

        bool CanHandle(ClientTradeCompletionContext context);

        void Handle(ClientTradeCompletionContext context);
    }

    public sealed class ClientTradeCompletionContext
    {
        public string TradeId { get; set; }

        public string OtherPartyUuid { get; set; }

        public IReadOnlyCollection<FrameworkItemPayload> Items { get; set; } = Array.Empty<FrameworkItemPayload>();

        public Action<string, LogLevel> Log { get; set; }
    }
}
