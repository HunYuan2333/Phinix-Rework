using System;
using System.Collections.Generic;
using Phinix.InventoryExtension;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.TradeExtension.Client
{
    [PhinixExtension(FrameworkTradeProtocol.Capability, DependsOn = new[] { "builtin.inventory" })]
    public sealed class BuiltInTradeClientExtension : ClientExtensionModule, IActivatablePhinixExtensionModule, ICapabilityProvider, IClientCommandHandler, IClientOutgoingCommandHandler
    {
        private IClientCompositionScope composition;
        private IClientCompositionScope activationComposition;
        private volatile bool activated;
        private int activationGeneration;
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
        private Action<string, LogLevel> hostLog;
        private TradeInventoryRegistrations inventoryRegistrations;
        private TradeInventoryDelivery inventoryDelivery;
        private IInventoryReadApi inventoryReadApi;
        private IInventoryReservationApi inventoryReservationApi;

        public override string ExtensionId => FrameworkTradeProtocol.Capability;

        public int Priority => 1100;

        public override void Compose(IExtensionBuilder builder)
        {
            if (composition != null) throw new InvalidOperationException("Trade is already composed.");
            Action<LogEventArgs> log = args => hostLog?.Invoke(args.Message, args.LogLevel);
            composition = builder.HostContext.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
            {
                local.Borrow(log);
                local.Register<ITradeItemPayloadEncoder, TradeClientItemPipeline>();
                local.Register<IFrameworkTradeClientApi, PhinixFrameworkTradeClientService>();
                local.Register<FrameworkLegacyTradeClientAdapter, FrameworkLegacyTradeClientAdapter>();
                local.Register<IClientTradeService, FrameworkClientTradeServiceAdapter>();
                local.Register<ITradeUiHostContext, ClientTradeUiHostContext>();
                local.Register<IMainTabProvider, TradeMainTabProvider>();
                local.Register<TradeSettingsPanelProvider, TradeSettingsPanelProvider>();
            });
            itemPipeline = composition.Resolve<TradeClientItemPipeline>();
            tradeApi = composition.Resolve<IFrameworkTradeClientApi>();
            legacyTradeAdapter = composition.Resolve<FrameworkLegacyTradeClientAdapter>();
            tradeFacade = composition.Resolve<IClientTradeService>();
            tradeUiHostContext = composition.Resolve<ClientTradeUiHostContext>();
            IMainTabProvider tab = composition.Resolve<IMainTabProvider>();
            TradeSettingsPanelProvider settingsPanelProvider = composition.Resolve<TradeSettingsPanelProvider>();

            builder.RegisterApi(tradeApi);
            builder.RegisterApi<IFrameworkTradeUpdateResultApi>((IFrameworkTradeUpdateResultApi)tradeApi);
            builder.RegisterApi<IFrameworkLegacyTradeRepositoryApi>(legacyTradeAdapter);
            builder.RegisterApi<IFrameworkLegacyTradeCompletionApi>(legacyTradeAdapter);
            builder.RegisterApi<IFrameworkLegacyTradeDeliveryApi>(legacyTradeAdapter);
            builder.RegisterApi(tradeFacade);
            builder.RegisterApi<ITradeRequestApi>((ITradeRequestApi)tradeFacade);
            builder.RegisterApi(tradeUiHostContext);
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.AddCapabilityProvider(this);
            builder.AddClientCommandHandler(this);
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanelProvider);
            builder.RegisterApi<IClientLegacySettingsMigrator>(settingsPanelProvider);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (hostContext == null)
            {
                return;
            }

            if (activated) return;
            if (composition == null) throw new InvalidOperationException("Trade must be composed before activation.");
            int generation = System.Threading.Interlocked.Increment(ref activationGeneration);
            activated = true;
            try
            {
                hostLog = hostContext.Log;

                IUiTheme theme = hostContext.GetRequiredService<IUiTheme>();
                RegisterThemeDefaults(theme);
                theme.Reload();
                TradeTheme.Refresh(theme);

                frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
                commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
                lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
                sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
                settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
                userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
                updateAcceptingTrades = hostContext.GetRequiredService<Action<bool>>();

                if (!hostContext.TryResolveApi<IInventoryRegistrationApi>(out IInventoryRegistrationApi inventoryRegistration) ||
                    !hostContext.TryResolveApi<IInventoryDepositApi>(out IInventoryDepositApi inventoryDeposit) ||
                    !hostContext.TryResolveApi<IInventoryReadApi>(out inventoryReadApi) ||
                    !hostContext.TryResolveApi<IInventoryReservationApi>(out inventoryReservationApi))
                {
                    throw new InvalidOperationException("Inventory registration, deposit, read, and reservation APIs are required by trade.");
                }
                Action<LogEventArgs> log = args => hostLog?.Invoke(args.Message, args.LogLevel);
                compatibilityChangedHandler = (_, args) =>
                {
                    if (!activated || generation != activationGeneration) return;
                    itemPipeline?.SetCompatibilityMode(args.CompatibilityMode);
                    if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2 &&
                        sessionContext.Authenticated && sessionContext.LoggedIn &&
                        frameworkClient.HasRemoteCapability(FrameworkTradeProtocol.Capability))
                        commandTransport.TryHandleOutgoingCommand(tradeApi.CreateSnapshotRequestPacket(sessionContext.SessionId, sessionContext.Uuid));
                };
                usersChangedHandler = (_, __) => { if (activated && generation == activationGeneration) syncAcceptingTrades(); };
                disconnectedHandler = (_, __) => { if (activated && generation == activationGeneration) lastSyncedAcceptingTrades = null; };
                settingChangedHandler = (key, _) =>
                {
                    if (activated && generation == activationGeneration && key == "trade.acceptingTrades") syncAcceptingTrades();
                };
                var callbacks = new TradeClientCallbacks(compatibilityChangedHandler, usersChangedHandler, disconnectedHandler, settingChangedHandler);
                activationComposition = hostContext.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
                {
                    local.Borrow(tradeFacade);
                    local.Borrow(itemPipeline);
                    local.Borrow(tradeUiHostContext);
                    local.Borrow(hostContext.GetRequiredService<IClientUserDirectory>());
                    local.Borrow(hostContext.GetRequiredService<IClientMainThreadDispatcher>());
                    local.Borrow(hostContext.GetRequiredService<IClientWindowService>());
                    local.Borrow(settingsContext);
                    local.Borrow(userEvents);
                    local.Borrow(lifecycle);
                    local.Borrow(inventoryRegistration);
                    local.Borrow(inventoryDeposit);
                    local.Borrow(inventoryReadApi);
                    local.Borrow(log);
                    local.Borrow<Func<string>>(() => sessionContext?.Uuid);
                    local.Borrow<Action<string, bool>>(acknowledgeCompletion);
                    local.Borrow(callbacks);
                    local.Register<TradeInventoryCodec, TradeInventoryCodec>();
                    local.Register<TradeInventorySourcePresenter, TradeInventorySourcePresenter>();
                    local.Register<TradeInventoryRegistrations, TradeInventoryRegistrations>();
                    local.Register<TradeInventoryDelivery, TradeInventoryDelivery>();
                    local.Register<PhinixDefaultTradeBehaviour, PhinixDefaultTradeBehaviour>();
                    local.Register<TradeConnectionSubscriptions, TradeConnectionSubscriptions>();
                });
                inventoryRegistrations = activationComposition.Resolve<TradeInventoryRegistrations>();
                inventoryDelivery = activationComposition.Resolve<TradeInventoryDelivery>();

                EnsureActivationServices(hostContext);
                tradeUiHostContext.Start();

                // 注入框架 registry 收集的所有 Item codec，让 Trade 能消费 Submod 注册的 codec。
                // 在 Activate 阶段执行，确保所有扩展的 Register() 已完成、codec 列表完整。
                if (hostContext.TryGetService<IItemCodecProvider>(out IItemCodecProvider codecProvider))
                {
                    itemPipeline?.SetExtensionCodecs(codecProvider.ItemCodecs);
                }

                activationComposition.Resolve<TradeConnectionSubscriptions>();

                if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                {
                    compatibilityChangedHandler(this, new FrameworkCompatibilityModeChangedEventArgs(lifecycle.CompatibilityMode));
                }

                syncAcceptingTrades();
                defaultTradeBehaviour?.Start();
            }
            catch (Exception error)
            {
                try { Shutdown(hostContext); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            activated = false;
            System.Threading.Interlocked.Increment(ref activationGeneration);
            var failures = new List<Exception>();
            TradeLifetimeCleanup.Try(() => (tradeFacade as IDisposable)?.Dispose(), failures);
            TradeLifetimeCleanup.Try(() => defaultTradeBehaviour?.Stop(), failures);
            TradeLifetimeCleanup.Try(() => tradeUiHostContext?.Stop(), failures);
            IClientCompositionScope oldActivation = activationComposition;
            IClientCompositionScope oldComposition = composition;
            activationComposition = null;
            composition = null;
            TradeLifetimeCleanup.Try(() => oldActivation?.Dispose(), failures);
            TradeLifetimeCleanup.Try(() => oldComposition?.Dispose(), failures);
            inventoryRegistrations = null;
            inventoryDelivery = null;
            inventoryReadApi = null;
            inventoryReservationApi = null;
            defaultTradeBehaviour = null;
            itemPipeline = null;
            tradeApi = null;
            legacyTradeAdapter = null;
            tradeFacade = null;
            tradeUiHostContext = null;
            frameworkClient = null;
            commandTransport = null;
            lifecycle = null;
            sessionContext = null;
            settingsContext = null;
            userEvents = null;
            updateAcceptingTrades = null;
            lastSyncedAcceptingTrades = null;
            compatibilityChangedHandler = null;
            usersChangedHandler = null;
            disconnectedHandler = null;
            settingChangedHandler = null;
            hostLog = null;
            TradeLifetimeCleanup.ThrowIfFailed(failures);
        }

        private void EnsureActivationServices(ExtensionHostContext hostContext)
        {
            Action<LogEventArgs> log = args => hostLog?.Invoke(args.Message, args.LogLevel);
            IClientUserDirectory userDirectory = hostContext.GetRequiredService<IClientUserDirectory>();
            IClientMainThreadDispatcher dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            IClientWindowService windowService = hostContext.GetRequiredService<IClientWindowService>();

            itemPipeline.SetCompatibilityMode(lifecycle.CompatibilityMode);
            (tradeApi as PhinixFrameworkTradeClientService)?.InitializeUserDirectory(userDirectory);
            (tradeFacade as FrameworkClientTradeServiceAdapter)?.Initialize(
                frameworkClient,
                commandTransport,
                lifecycle,
                sessionContext,
                hostContext.Log);
            tradeUiHostContext.Initialize(
                settingsContext,
                userEvents,
                dispatcher,
                windowService,
                log,
                inventoryReadApi,
                inventoryReservationApi);
            defaultTradeBehaviour = activationComposition.Resolve<PhinixDefaultTradeBehaviour>();

        }

        private static void RegisterThemeDefaults(IUiTheme theme)
        {
            theme.RegisterColor("trade.ourOfferAccent", new Color(0.30f, 0.65f, 0.35f, 0.70f));
            theme.RegisterColor("trade.theirOfferAccent", new Color(0.45f, 0.75f, 1.00f, 0.70f));
            theme.RegisterColor("trade.ourOfferBg", new Color(0.30f, 0.65f, 0.35f, 0.04f));
            theme.RegisterColor("trade.theirOfferBg", new Color(0.45f, 0.75f, 1.00f, 0.04f));
            theme.RegisterColor("trade.cancelButton", new Color(0.85f, 0.30f, 0.25f, 1.00f));
            theme.RegisterColor("trade.acceptedBadge", new Color(0.30f, 0.65f, 0.35f, 1.00f));
            theme.RegisterColor("trade.pendingBadge", new Color(0.55f, 0.52f, 0.48f, 0.80f));
            theme.RegisterColor("trade.rowHoverBg", new Color(1.00f, 1.00f, 1.00f, 0.04f));
            theme.RegisterColor("trade.panelBg", new Color(1.00f, 1.00f, 1.00f, 0.03f));
            theme.RegisterColor("trade.searchPlaceholder", new Color(0.55f, 0.52f, 0.48f, 0.50f));
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
            yield return FrameworkTradeProtocol.CompletionAckRequestType;
            yield return FrameworkTradeProtocol.CompletionAckResponseType;
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null &&
                   (command.MessageType == FrameworkTradeProtocol.SnapshotType ||
                    command.MessageType == FrameworkTradeProtocol.CreateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.OfferUpdateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.StatusUpdateResponseType ||
                    command.MessageType == FrameworkTradeProtocol.CompletedEventType ||
                    command.MessageType == FrameworkTradeProtocol.CancelledEventType ||
                    command.MessageType == FrameworkTradeProtocol.CompletionAckResponseType);
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
                case FrameworkTradeProtocol.CompletionAckResponseType:
                    handleCompletionAckResponse(command);
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

        private void acknowledgeCompletion(string tradeId, bool cancelled)
        {
            if (string.IsNullOrWhiteSpace(tradeId) ||
                lifecycle?.CompatibilityMode != FrameworkCompatibilityMode.FrameworkV2 ||
                frameworkClient == null ||
                !frameworkClient.HasRemoteCapability(FrameworkTradeProtocol.CompletionAckRequestType))
            {
                return;
            }

            FrameworkPacket packet = new FrameworkPacket
            {
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.CompletionAckRequestType,
                MessageId = Guid.NewGuid().ToString(),
                SessionId = sessionContext?.SessionId,
                SenderUuid = sessionContext?.Uuid,
                PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkTradeCompletionAckRequest
                {
                    TradeId = tradeId,
                    Cancelled = cancelled
                })
            };
            packet.SetCorrelationId(tradeId);
            commandTransport?.TryHandleOutgoingCommand(packet);
        }

        private void handleCompletionAckResponse(FrameworkPacket packet)
        {
            FrameworkTradeCompletionAckResponse response =
                FrameworkSerialization.DeserializePayload<FrameworkTradeCompletionAckResponse>(packet.PayloadJson);
            if (response != null && !response.Accepted)
            {
                hostLog?.Invoke(
                    $"[Trade] Completion acknowledgement rejected for '{response.TradeId}': {response.FailureMessage}",
                    LogLevel.WARNING);
            }
        }
    }
}
