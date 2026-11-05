using System;
using System.Linq;
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
        private IClientTheme currentTheme;
        private IClientThemeRegistration themeRegistration;
        private Action themeChanged;
        private IClientTradeService tradeFacade;
        private ClientTradeUiHostContext tradeUiHostContext;
        private PhinixDefaultTradeBehaviour defaultTradeBehaviour;
        private TradeDeliveryReceiptClient deliveryReceipts;
        private TradeDeliveryRecovery deliveryRecovery;
        private TradeIntentSettlement intentSettlement;
        private IFrameworkClientTransport frameworkClient;
        private IFrameworkClientCommandTransport commandTransport;
        private IFrameworkClientLifecycle lifecycle;
        private IClientSessionContext sessionContext;
        private IClientSettingsContext settingsContext;
        private IClientUserEventStream userEvents;
        private Action<bool> updateAcceptingTrades;
        private bool? lastSyncedAcceptingTrades;
        private int basicTermsNoticeShown;
        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChangedHandler;
        private EventHandler usersChangedHandler;
        private EventHandler disconnectedHandler;
        private Action<string, object> settingChangedHandler;
        private Action<string, LogLevel> hostLog;
        private TradeInventoryRegistrations inventoryRegistrations;
        private TradeInventoryDelivery inventoryDelivery;
        private TradeOfferOperations offerOperations;
        private TradeInventoryRecovery inventoryRecovery;
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
                local.Register<IClientTradeService, FrameworkClientTradeServiceAdapter>();
                local.Register<ITradeUiHostContext, ClientTradeUiHostContext>();
                local.Register<IMainTabProvider, TradeMainTabProvider>();
                local.Register<TradeSettingsPanelProvider, TradeSettingsPanelProvider>();
            });
            itemPipeline = composition.Resolve<TradeClientItemPipeline>();
            tradeApi = composition.Resolve<IFrameworkTradeClientApi>();
            tradeFacade = composition.Resolve<IClientTradeService>();
            tradeUiHostContext = composition.Resolve<ClientTradeUiHostContext>();
            offerOperations=new TradeOfferOperations(tradeFacade,tradeUiHostContext.RunOnMainThread,
                message=>hostLog?.Invoke(message,LogLevel.ERROR),()=>UnityData.IsInMainThread);
            inventoryRecovery=new TradeInventoryRecovery(()=>UnityData.IsInMainThread,()=>Verse.Current.Game);
            tradeUiHostContext.ConfigureOfferOperations(offerOperations,itemPipeline);
            ((FrameworkClientTradeServiceAdapter)tradeFacade).SetOfferSubmissionGuard(offerOperations.CanSubmit);
            IMainTabProvider tab = composition.Resolve<IMainTabProvider>();
            TradeSettingsPanelProvider settingsPanelProvider = composition.Resolve<TradeSettingsPanelProvider>();

            builder.RegisterApi<ITradeOfferOperationApi>(offerOperations);
            builder.RegisterApi<ITradeInventoryRecoveryApi>(inventoryRecovery);
            builder.RegisterApi<ITradeOperationQueryApi>(inventoryRecovery);
            builder.RegisterApi(tradeApi);
            builder.RegisterApi<IFrameworkTradeUpdateResultApi>((IFrameworkTradeUpdateResultApi)tradeApi);
            builder.RegisterApi<ITradeStateInput>((ITradeStateInput)tradeApi);
            builder.RegisterApi(tradeFacade);
            builder.RegisterApi<ITradeRequestApi>((ITradeRequestApi)tradeFacade);
            builder.RegisterApi(tradeUiHostContext);
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.AddCapabilityProvider(this);
            builder.AddClientCommandHandler(this);
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanelProvider);
            builder.RegisterApi<IClientLegacySettingsMigrator>(new TradeSettingsInputAdapter());
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
                var recoveryTasks=hostContext.GetRequiredService<IClientTaskService>();

                IClientTheme theme = hostContext.GetRequiredService<IClientTheme>();
                themeRegistration = hostContext.GetRequiredService<IClientThemeRegistry>().RegisterModule(this, "trade", "Phinix_theme_owner_trade");
                RegisterThemeDefaults(themeRegistration);
                themeRegistration.Commit();
                currentTheme = theme;
                themeChanged = () => TradeTheme.Refresh(theme);
                theme.Changed += themeChanged;
                TradeTheme.Refresh(theme);

                frameworkClient = hostContext.GetRequiredService<IFrameworkClientTransport>();
                commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
                lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
                sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
                settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
                var completionPaths = hostContext.GetRequiredService<ClientEnvironmentPaths>();
                var connection = hostContext.GetRequiredService<IClientConnectionIdentity>();
                var completionJournal=new TradeCompletionInbox(System.IO.Path.Combine(completionPaths.GetExtensionDataDirectory(ExtensionId), "completion-inbox"));
                Func<string> completionEndpoint=() => sessionContext.Authenticated && sessionContext.LoggedIn ? connection.RemoteEndpoint : null;
                ((PhinixFrameworkTradeClientService)tradeApi).ConfigureCompletionInbox(
                    completionJournal,completionEndpoint);
                var intentJournal = new TradeIntentJournal(System.IO.Path.Combine(completionPaths.GetExtensionDataDirectory(ExtensionId), "operation-intents"));
                var intentAdapter = (FrameworkClientTradeServiceAdapter)tradeFacade;
                intentAdapter.ConfigureIntentJournal(intentJournal, completionEndpoint);
                deliveryReceipts=new TradeDeliveryReceiptClient(completionJournal,completionEndpoint,()=>sessionContext.Uuid,()=>sessionContext.SessionId,
                    ()=>sessionContext.Authenticated&&sessionContext.LoggedIn&&frameworkClient.HasRemoteCapability(TradeDeliveryProtocol.Request)&&frameworkClient.HasRemoteCapability(TradeDeliveryProtocol.Response),
                    packet=>commandTransport.TryHandleOutgoingCommand(packet),message=>hostLog?.Invoke(message,LogLevel.WARNING));
                ((PhinixFrameworkTradeClientService)tradeApi).ConfigureDeliveryReceipts(deliveryReceipts);
                userEvents = hostContext.GetRequiredService<IClientUserEventStream>();
                updateAcceptingTrades = hostContext.GetRequiredService<Action<bool>>();

                if (!hostContext.TryResolveApi<IInventoryRegistrationApi>(out IInventoryRegistrationApi inventoryRegistration) ||
                    !hostContext.TryResolveApi<IInventoryDepositApi>(out IInventoryDepositApi inventoryDeposit) ||
                    !hostContext.TryResolveApi<IInventoryReadApi>(out inventoryReadApi) ||
                    !hostContext.TryResolveApi<IInventoryReservationApi>(out inventoryReservationApi))
                {
                    throw new InvalidOperationException("Inventory registration, deposit, read, and reservation APIs are required by trade.");
                }
                intentAdapter.ConfigureSaveReadiness(()=>inventoryReadApi.GetStatus());
                intentAdapter.IntentPrepared=intent=>
                {
                    if(intent.RequestType!=FrameworkTradeProtocol.OfferUpdateRequestType)return;
                    string expected="trade-offer:"+TradeIntentJournal.TradeId(intent)+":"+intent.CorrelationId;
                    var reservation=inventoryReservationApi.GetReservations().SingleOrDefault(r=>r.OwnerExtensionId=="builtin.trade"&&r.Purpose=="trade.offer"&&r.OperationId==expected);
                    if(reservation!=null)intentJournal.BindReservation(intent,inventoryReadApi.GetStatus(),reservation);
                };
                Action<LogEventArgs> log = args => hostLog?.Invoke(args.Message, args.LogLevel);
                compatibilityChangedHandler = (_, args) =>
                {
                    if (!activated || generation != activationGeneration) return;
                    itemPipeline?.SetCompatibilityMode(args.CompatibilityMode);
                    deliveryRecovery?.RequestRecovery();
                    intentAdapter.RequestSnapshot();
                };
                usersChangedHandler = (_, __) => { if (activated && generation == activationGeneration) {syncAcceptingTrades();deliveryRecovery?.RequestRecovery();} };
                disconnectedHandler = (_, __) => { if (activated && generation == activationGeneration) { lastSyncedAcceptingTrades = null; System.Threading.Interlocked.Exchange(ref basicTermsNoticeShown, 0); } };
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
                    local.Borrow(hostContext.GetRequiredService<IClientGameWindowService>());
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
                tradeUiHostContext.ConfigureOfferDelivery(inventoryDelivery);
                tradeUiHostContext.ConfigureOfferPresentation(new TradeOfferPresentationFactory(this,
                    hostContext.GetRequiredService<IClientPresentationService>(),hostContext.GetRequiredService<IClientExtensionPointService>(),currentTheme));
                offerOperations.Start();
                intentSettlement=new TradeIntentSettlement(intentJournal,inventoryReadApi,inventoryReservationApi,recoveryTasks.ForModule(this),completionEndpoint,()=>sessionContext.Uuid,offerOperations,
                    message=>hostLog?.Invoke(message,LogLevel.WARNING));
                inventoryRecovery.Start(inventoryReadApi,inventoryReservationApi);
                inventoryRecovery.ConfigureCompletions(completionJournal,()=>sessionContext?.Uuid,completionEndpoint);
                inventoryRecovery.ConfigureIntents(intentJournal, () => intentAdapter.CanQueryOperation, intentAdapter.QueryOperation);
                hostContext.TryResolveApi<IInventorySavedReceiptApi>(out var savedReceipts);
                deliveryRecovery=new TradeDeliveryRecovery(completionJournal,inventoryReadApi,inventoryDeposit,savedReceipts,recoveryTasks.ForModule(this),deliveryReceipts,
                    completionEndpoint,()=>sessionContext.Uuid,message=>hostLog?.Invoke(message,LogLevel.WARNING));
                intentSettlement.Start();deliveryRecovery.Start();
                composition.Resolve<TradeMainTabProvider>().Start(this,hostContext.GetRequiredService<IClientPresentationService>(),
                    hostContext.GetRequiredService<IClientExtensionPointService>(),currentTheme,offerOperations,inventoryRecovery);

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
                defaultTradeBehaviour?.ConfigureCompletionJournal(completionJournal,completionEndpoint);
                // Missing host support retains the inbox without binding/delivering nonempty batches.
                hostContext.TryGetService<IClientGameScopeService>(out var gameScopes);
                defaultTradeBehaviour?.ConfigureGameScopes(gameScopes);
                ((PhinixFrameworkTradeClientService)tradeApi).ConfigureGameScopes(gameScopes);
                hostContext.TryResolveApi<IInventoryReceiveScopeApi>(out var receiveScopes);
                defaultTradeBehaviour?.ConfigureReceiveScopes(receiveScopes);
                ((PhinixFrameworkTradeClientService)tradeApi).ConfigureReceiveScopes(receiveScopes);
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
            if (currentTheme != null && themeChanged != null) currentTheme.Changed -= themeChanged;
            currentTheme = null; themeChanged = null;
            TradeTheme.Release();
            themeRegistration?.Dispose(); themeRegistration = null;
            System.Threading.Interlocked.Increment(ref activationGeneration);
            var failures = new List<Exception>();
            TradeLifetimeCleanup.Try(()=>deliveryRecovery?.Dispose(),failures);deliveryRecovery=null;
            TradeLifetimeCleanup.Try(()=>intentSettlement?.Dispose(),failures);intentSettlement=null;
            TradeLifetimeCleanup.Try(() => inventoryRecovery?.Dispose(), failures);
            TradeLifetimeCleanup.Try(() => offerOperations?.Dispose(), failures);
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
            basicTermsNoticeShown = 0;
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
            IClientGameWindowService windowService = hostContext.GetRequiredService<IClientGameWindowService>();

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

        private static void RegisterThemeDefaults(IClientThemeRegistration theme)
        {
            theme.RegisterColor("trade.ourOfferAccent", new ClientRgba(0.30f, 0.65f, 0.35f, 0.70f), label: "Phinix_theme_label_trade_ourOfferAccent", group: "Phinix_theme_group_offers", description: "Phinix_theme_desc_trade_ourOfferAccent");
            theme.RegisterColor("trade.theirOfferAccent", new ClientRgba(0.45f, 0.75f, 1.00f, 0.70f), label: "Phinix_theme_label_trade_theirOfferAccent", group: "Phinix_theme_group_offers", description: "Phinix_theme_desc_trade_theirOfferAccent");
            theme.RegisterColor("trade.ourOfferBg", new ClientRgba(0.30f, 0.65f, 0.35f, 0.04f), label: "Phinix_theme_label_trade_ourOfferBg", group: "Phinix_theme_group_offers", description: "Phinix_theme_desc_trade_ourOfferBg");
            theme.RegisterColor("trade.theirOfferBg", new ClientRgba(0.45f, 0.75f, 1.00f, 0.04f), label: "Phinix_theme_label_trade_theirOfferBg", group: "Phinix_theme_group_offers", description: "Phinix_theme_desc_trade_theirOfferBg");
            theme.RegisterColor("trade.cancelButton", new ClientRgba(0.85f, 0.30f, 0.25f, 1.00f), fallbackKey: "ui.error", label: "Phinix_theme_label_trade_cancelButton", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_trade_cancelButton");
            theme.RegisterColor("trade.acceptedBadge", new ClientRgba(0.30f, 0.65f, 0.35f, 1.00f), fallbackKey: "ui.success", label: "Phinix_theme_label_trade_acceptedBadge", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_trade_acceptedBadge");
            theme.RegisterColor("trade.pendingBadge", new ClientRgba(0.55f, 0.52f, 0.48f, 0.80f), fallbackKey: "ui.pending", label: "Phinix_theme_label_trade_pendingBadge", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_trade_pendingBadge");
            theme.RegisterColor("trade.rowHoverBg", new ClientRgba(1.00f, 1.00f, 1.00f, 0.04f), fallbackKey: "ui.hover", label: "Phinix_theme_label_trade_rowHoverBg", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_trade_rowHoverBg");
            theme.RegisterColor("trade.panelBg", new ClientRgba(1.00f, 1.00f, 1.00f, 0.03f), fallbackKey: "ui.surface", label: "Phinix_theme_label_trade_panelBg", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_trade_panelBg");
            theme.RegisterColor("trade.searchPlaceholder", new ClientRgba(0.55f, 0.52f, 0.48f, 0.50f), fallbackKey: "ui.text.secondary", label: "Phinix_theme_label_trade_searchPlaceholder", group: "Phinix_theme_group_status", description: "Phinix_theme_desc_trade_searchPlaceholder");
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
            yield return TradeOperationQueryProtocol.Request;
            yield return TradeOperationQueryProtocol.Response;
            yield return TradeDeliveryProtocol.Request;
            yield return TradeDeliveryProtocol.Response;
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
                    command.MessageType == FrameworkTradeProtocol.CompletionAckResponseType ||
                    command.MessageType == TradeOperationQueryProtocol.Response || command.MessageType==TradeDeliveryProtocol.Response);
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            if (!((FrameworkClientTradeServiceAdapter)tradeFacade).RecordCommandResult(command))
                return new ClientIncomingCommandResult { Action = MessageHandlingResultAction.Handled };
            switch (command.MessageType)
            {
                case TradeDeliveryProtocol.Response:
                    deliveryReceipts?.Handle(command);
                    // Server receipt response never settles assets or changes a game-save result.
                    break;
                case TradeOperationQueryProtocol.Response:
                    ((FrameworkClientTradeServiceAdapter)tradeFacade).HandleOperationQueryResponse(command);
                    break;
                case FrameworkTradeProtocol.SnapshotType:
                    ShowBasicTermsNotice(command);
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

        private void ShowBasicTermsNotice(FrameworkPacket command)
        {
            if (!command.TryGetMetadataValue("trade_basic_terms_v1", out string ids) ||
                string.IsNullOrEmpty(ids) || ids.Length > 4096 || tradeUiHostContext == null) return;
            var marked = new HashSet<string>(ids.Split(','), StringComparer.OrdinalIgnoreCase);
            var snapshot = FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(command.PayloadJson);
            if (snapshot?.Trades == null || !snapshot.Trades.Any(t => marked.Contains(t.TradeId)) ||
                System.Threading.Interlocked.CompareExchange(ref basicTermsNoticeShown, 1, 0) != 0) return;
            // Presentation only. No protocol switch, settlement or asset mutation here.
            tradeUiHostContext.RunOnMainThread(() => Messages.Message(
                "Phinix_trade_basicCompatibilityNotice".Translate(), RimWorld.MessageTypeDefOf.CautionInput, false));
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
            (tradeFacade as FrameworkClientTradeServiceAdapter)?.AcknowledgeCompletion(tradeId, cancelled);
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
