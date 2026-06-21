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
    public sealed class BuiltInTradeClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientCommandHandler, IClientOutgoingCommandHandler
    {
        private TradeClientItemPipeline itemPipeline;
        private IFrameworkTradeClientApi tradeApi;
        private FrameworkLegacyTradeClientAdapter legacyTradeAdapter;
        private IClientTradeService tradeFacade;
        private ClientTradeUiHostContext tradeUiHostContext;
        private PhinixDefaultTradeBehaviour defaultTradeBehaviour;
        private IFrameworkClientTransport frameworkClient;
        private IFrameworkClientCommandTransport commandTransport;
        private IFrameworkClientLifecycle lifecycle;
        private IClientSessionContext sessionContext;
        private IClientSettingsContext settingsContext;
        private IClientUserEventStream userEvents;
        private Action<bool> updateAcceptingTrades;
        private bool? lastSyncedAcceptingTrades;
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChangedHandler;
        private EventHandler usersChangedHandler;
        private EventHandler disconnectedHandler;
        private Action<string, object> settingChangedHandler;

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
            legacyTradeAdapter = legacyTradeAdapter ?? new FrameworkLegacyTradeClientAdapter((PhinixFrameworkTradeClientService)tradeApi);
            tradeFacade = tradeFacade ?? new FrameworkClientTradeServiceAdapter(
                tradeApi,
                builder.HostContext.GetRequiredService<IFrameworkClientTransport>(),
                builder.HostContext.GetRequiredService<IFrameworkClientCommandTransport>(),
                builder.HostContext.GetRequiredService<IFrameworkClientLifecycle>(),
                builder.HostContext.GetRequiredService<IClientSessionContext>(),
                builder.HostContext.Log);
            builder.RegisterApi(tradeApi);
            builder.RegisterApi<IFrameworkLegacyTradeRepositoryApi>(legacyTradeAdapter);
            builder.RegisterApi<IFrameworkLegacyTradeCompletionApi>(legacyTradeAdapter);
            builder.RegisterApi(tradeFacade);
            builder.RegisterApi<ITradeRequestApi>((ITradeRequestApi)tradeFacade);
            builder.AddCapabilityProvider(this);
            builder.AddClientCommandHandler(this);

            tradeUiHostContext = tradeUiHostContext ?? new ClientTradeUiHostContext(
                tradeFacade,
                builder.HostContext.GetRequiredService<IClientSettingsContext>(),
                builder.HostContext.GetRequiredService<IClientUserEventStream>(),
                builder.HostContext.GetRequiredService<IClientMainThreadDispatcher>(),
                builder.HostContext.GetRequiredService<IClientWindowService>(),
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
            var settingsPanelProvider = new TradeSettingsPanelProvider();
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanelProvider);
            builder.RegisterApi<IClientLegacySettingsMigrator>(settingsPanelProvider);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (tradeApi == null || hostContext == null)
            {
                return;
            }

            frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
            commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
            settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
            userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
            updateAcceptingTrades = hostContext.GetRequiredService<Action<bool>>();

            // 注入框架 registry 收集的所有 Item codec，让 Trade 能消费 Submod 注册的 codec。
            // 在 Activate 阶段执行，确保所有扩展的 Register() 已完成、codec 列表完整。
            if (hostContext.TryGetService<IItemCodecProvider>(out IItemCodecProvider codecProvider))
            {
                itemPipeline?.SetExtensionCodecs(codecProvider.ItemCodecs);
            }

            if (compatibilityChangedHandler == null)
            {
                compatibilityChangedHandler = (_, args) =>
                {
                    itemPipeline?.SetCompatibilityMode(args.CompatibilityMode);
                    if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                    {
                        if (sessionContext.Authenticated &&
                            sessionContext.LoggedIn &&
                            frameworkClient.HasRemoteCapability(FrameworkTradeProtocol.Capability))
                        {
                            FrameworkPacket snapshotRequest = tradeApi.CreateSnapshotRequestPacket(
                                sessionContext.SessionId,
                                sessionContext.Uuid);
                            commandTransport.TryHandleOutgoingCommand(snapshotRequest);
                        }
                    }
                };
            }

            if (usersChangedHandler == null)
            {
                usersChangedHandler = (_, __) => syncAcceptingTrades();
            }

            if (disconnectedHandler == null)
            {
                disconnectedHandler = (_, __) => lastSyncedAcceptingTrades = null;
            }

            if (settingChangedHandler == null)
            {
                settingChangedHandler = (key, _) =>
                {
                    if (key == "trade.acceptingTrades")
                    {
                        syncAcceptingTrades();
                    }
                };
            }

            lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            lifecycle.CompatibilityModeChanged += compatibilityChangedHandler;
            userEvents.UsersChanged -= usersChangedHandler;
            userEvents.UsersChanged += usersChangedHandler;
            userEvents.Disconnected -= disconnectedHandler;
            userEvents.Disconnected += disconnectedHandler;
            settingsContext.OnSettingChanged -= settingChangedHandler;
            settingsContext.OnSettingChanged += settingChangedHandler;

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
            {
                compatibilityChangedHandler(this, new FrameworkCompatibilityModeChangedEventArgs(lifecycle.CompatibilityMode));
            }

            syncAcceptingTrades();
            defaultTradeBehaviour?.Start();
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (lifecycle != null && compatibilityChangedHandler != null)
            {
                lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            }

            if (userEvents != null && usersChangedHandler != null)
            {
                userEvents.UsersChanged -= usersChangedHandler;
            }

            if (userEvents != null && disconnectedHandler != null)
            {
                userEvents.Disconnected -= disconnectedHandler;
            }

            if (settingsContext != null && settingChangedHandler != null)
            {
                settingsContext.OnSettingChanged -= settingChangedHandler;
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

        // ========== IClientOutgoingCommandHandler ==========

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            return command?.MessageType?.StartsWith("trade.") == true;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            // V2 模式下原样返回 FrameworkPacket 供框架发送。
            // Legacy 模式时 LegacyAdapter（Priority=500 < 1100）已抢先拦截并翻译，
            // 此方法在 Legacy 模式下不会被调用。
            return new ClientOutgoingCommandResult
            {
                Action = MessageHandlingResultAction.Handled,
                Command = command
            };
        }

        private void syncAcceptingTrades()
        {
            if (updateAcceptingTrades == null || settingsContext == null || sessionContext == null || !sessionContext.LoggedIn)
            {
                return;
            }

            bool acceptingTrades = settingsContext.Get("trade.acceptingTrades", true);
            if (lastSyncedAcceptingTrades.HasValue && lastSyncedAcceptingTrades.Value == acceptingTrades)
            {
                return;
            }

            updateAcceptingTrades(acceptingTrades);
            lastSyncedAcceptingTrades = acceptingTrades;
        }
    }
}
