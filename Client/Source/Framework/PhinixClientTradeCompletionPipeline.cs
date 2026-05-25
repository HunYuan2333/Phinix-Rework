using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhinixClient.Trade;
using Utils;

namespace PhinixClient.Framework
{
    public sealed class PhinixClientTradeCompletionPipeline
    {
        private readonly List<IClientTradeCompletionHandler> handlers;
        private readonly PhinixClientItemPipeline itemPipeline;
        private readonly Action<LogEventArgs> log;

        public PhinixClientTradeCompletionPipeline(PhinixClientItemPipeline itemPipeline, Action<LogEventArgs> log, IClientTradeCompletionHandler defaultHandler)
        {
            this.itemPipeline = itemPipeline;
            this.log = log;

            handlers = discoverHandlers();
            if (defaultHandler != null)
            {
                handlers.Add(defaultHandler);
            }

            handlers = handlers
                .Where(handler => handler != null)
                .OrderBy(handler => handler.Priority)
                .ToList();
        }

        public void HandleTradeCompleted(TradeCompletionEventArgs args)
        {
            ClientTradeCompletionContext context = new ClientTradeCompletionContext
            {
                TradeId = args?.TradeId,
                OtherPartyUuid = args?.OtherPartyUuid,
                Items = itemPipeline.EncodeTradeItems(args?.Items),
                Log = (message, level) => log?.Invoke(new LogEventArgs(message, level))
            };

            foreach (IClientTradeCompletionHandler handler in handlers)
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

        private List<IClientTradeCompletionHandler> discoverHandlers()
        {
            List<IClientTradeCompletionHandler> discoveredHandlers = new List<IClientTradeCompletionHandler>();

            IEnumerable<Type> candidateTypes = AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(getLoadableTypes)
                .Where(type => type.IsClass && !type.IsAbstract && typeof(IClientTradeCompletionHandler).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null);

            foreach (Type candidateType in candidateTypes)
            {
                try
                {
                    if (Activator.CreateInstance(candidateType) is IClientTradeCompletionHandler handler)
                    {
                        discoveredHandlers.Add(handler);
                    }
                }
                catch (Exception exception)
                {
                    log?.Invoke(new LogEventArgs($"Failed to initialize trade completion handler '{candidateType.FullName}': {exception.Message}", LogLevel.WARNING));
                }
            }

            return discoveredHandlers;
        }

        private static IEnumerable<Type> getLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }
    }
}
