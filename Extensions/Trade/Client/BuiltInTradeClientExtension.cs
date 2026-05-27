using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    [PhinixExtension(FrameworkTradeProtocol.Capability)]
    public sealed class BuiltInTradeClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientCommandHandler
    {
        private TradeClientItemPipeline itemPipeline;
        private IFrameworkTradeClientApi tradeApi;
        private IClientTradeService tradeFacade;
        private ClientTradeUiHostContext tradeUiHostContext;
        private PhinixDefaultTradeBehaviour defaultTradeBehaviour;
        private IFrameworkClientTransport frameworkClient;
        private IFrameworkClientLifecycle lifecycle;
        private IClientSessionContext sessionContext;
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChangedHandler;

        public string ExtensionId => FrameworkTradeProtocol.Capability;

        public int Priority => 1100;

        public void Register(IExtensionBuilder builder)
        {
            var log = new Action<Utils.LogEventArgs>(args => builder.HostContext.Log?.Invoke(args.Message, args.LogLevel));
            itemPipeline = itemPipeline ?? new TradeClientItemPipeline(log, builder.HostContext.GetRequiredService<IFrameworkClientLifecycle>().CompatibilityMode);
            tradeApi = tradeApi ?? new PhinixFrameworkTradeClientService(
                itemPipeline,
                builder.HostContext.GetRequiredService<IClientUserDirectory>(),
                logEvent => builder.HostContext.Log?.Invoke(logEvent.Message, logEvent.LogLevel));
            tradeFacade = tradeFacade ?? new FrameworkClientTradeServiceAdapter(
                tradeApi,
                builder.HostContext.GetRequiredService<IFrameworkClientTransport>(),
                builder.HostContext.GetRequiredService<IFrameworkClientLifecycle>(),
                builder.HostContext.GetRequiredService<IClientSessionContext>(),
                builder.HostContext.Log);
            builder.RegisterApi(tradeApi);
            builder.RegisterApi(tradeFacade);
            builder.RegisterApi<ITradeRequestApi>((ITradeRequestApi)tradeFacade);
            builder.AddCapabilityProvider(this);
            builder.AddClientCommandHandler(this);

            var dropPods = builder.HostContext.GetRequiredService<Func<IEnumerable<Verse.Thing>, Verse.LookTargets>>();
            tradeUiHostContext = tradeUiHostContext ?? new ClientTradeUiHostContext(
                tradeFacade,
                builder.HostContext.GetRequiredService<IClientSettingsContext>(),
                builder.HostContext.GetRequiredService<IClientUserEventStream>(),
                dropPods,
                log);
            defaultTradeBehaviour = defaultTradeBehaviour ?? new PhinixDefaultTradeBehaviour(
                tradeFacade,
                builder.HostContext.GetRequiredService<IClientUserDirectory>(),
                builder.HostContext.GetRequiredService<IClientSettingsContext>(),
                builder.HostContext.GetRequiredService<IClientMainThreadDispatcher>(),
                builder.HostContext.GetRequiredService<IClientWindowService>(),
                tradeUiHostContext,
                log);
            builder.RegisterApi(tradeUiHostContext);
            builder.RegisterApi<IMainTabProvider>(new TradeMainTabProvider(tradeUiHostContext));
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (tradeApi == null || hostContext == null)
            {
                return;
            }

            frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();

            if (compatibilityChangedHandler == null)
            {
                compatibilityChangedHandler = (_, args) =>
                {
                    itemPipeline?.SetCompatibilityMode(args.CompatibilityMode);
                    if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                    {
                        tradeApi.RequestSnapshot(
                            frameworkClient,
                            sessionContext.Authenticated,
                            sessionContext.LoggedIn,
                            sessionContext.SessionId,
                            sessionContext.Uuid);
                    }
                };
            }

            lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            lifecycle.CompatibilityModeChanged += compatibilityChangedHandler;

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
            {
                compatibilityChangedHandler(this, new FrameworkCompatibilityModeChangedEventArgs(lifecycle.CompatibilityMode));
            }

            defaultTradeBehaviour?.Start();
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (lifecycle != null && compatibilityChangedHandler != null)
            {
                lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            }

            defaultTradeBehaviour?.Stop();
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return FrameworkTradeProtocol.Capability;
            yield return FrameworkTradeProtocol.CreateRequestType;
            yield return FrameworkTradeProtocol.CreateResponseType;
            yield return FrameworkTradeProtocol.SnapshotType;
            yield return FrameworkTradeProtocol.OfferUpdateRequestType;
            yield return FrameworkTradeProtocol.OfferUpdateResponseType;
            yield return FrameworkTradeProtocol.StatusUpdateRequestType;
            yield return FrameworkTradeProtocol.StatusUpdateResponseType;
            yield return FrameworkTradeProtocol.CompletedEventType;
            yield return FrameworkTradeProtocol.CancelledEventType;
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null &&
                   (command.MessageType == FrameworkTradeProtocol.SnapshotType ||
                    command.MessageType == FrameworkTradeProtocol.CreateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.OfferUpdateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.StatusUpdateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.CompletedEventType ||
                    command.MessageType == FrameworkTradeProtocol.CancelledEventType);
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            switch (command.MessageType)
            {
                case FrameworkTradeProtocol.SnapshotType:
                    tradeApi.HandleSnapshot(command);
                    break;
                case FrameworkTradeProtocol.CreateResponseType:
                    tradeApi.HandleCreateResponse(command);
                    break;
                case FrameworkTradeProtocol.OfferUpdateResponseType:
                    tradeApi.HandleOfferUpdateResponse(command);
                    break;
                case FrameworkTradeProtocol.StatusUpdateResponseType:
                    tradeApi.HandleStatusUpdateResponse(command);
                    break;
                case FrameworkTradeProtocol.CompletedEventType:
                    tradeApi.HandleCompletedEvent(command);
                    break;
                case FrameworkTradeProtocol.CancelledEventType:
                    tradeApi.HandleCancelledEvent(command);
                    break;
            }

            return new ClientIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }
    }
}
