using System;
using System.Collections.Generic;
using System.Linq;
using Trading;
using Utils;
using Utils.Framework;

namespace PhinixClient.Framework
{
    public sealed class PhinixClientTradeCompletionPipeline
    {
        private readonly List<ITradeCompletionHandler> handlers;
        private readonly PhinixClientItemPipeline itemPipeline;
        private readonly Action<LogEventArgs> log;

        public PhinixClientTradeCompletionPipeline(PhinixClientItemPipeline itemPipeline, Action<LogEventArgs> log, ITradeCompletionHandler defaultHandler)
        {
            this.itemPipeline = itemPipeline;
            this.log = log;

            handlers = new List<ITradeCompletionHandler>();
            DiscoveredPhinixExtensions discovered = PhinixExtensionRegistry.DiscoverExtensions();
            handlers.AddRange(discovered.TradeCompletionHandlers);
            if (defaultHandler != null)
            {
                handlers.Add(defaultHandler);
            }

            handlers = handlers
                .Where(handler => handler != null)
                .OrderBy(handler => handler.Priority)
                .ToList();
        }

        public void HandleTradeCompleted(CompleteTradeEventArgs args)
        {
            TradeCompletionContext context = new TradeCompletionContext
            {
                TradeId = args?.TradeId,
                OtherPartyUuid = args?.OtherPartyUuid,
                Items = itemPipeline.EncodeTradeItems(args?.Items),
                Log = (message, level) => log?.Invoke(new LogEventArgs(message, level))
            };

            foreach (ITradeCompletionHandler handler in handlers)
            {
                try
                {
                    if (!handler.CanHandle(context))
                    {
                        continue;
                    }

                    handler.Handle(context);
                    return;
                }
                catch (Exception exception)
                {
                    log?.Invoke(new LogEventArgs($"Trade completion handler '{handler.HandlerId}' failed: {exception.Message}", LogLevel.WARNING));
                }
            }

            log?.Invoke(new LogEventArgs("No trade completion handler accepted the current completion context.", LogLevel.WARNING));
        }
    }
}
