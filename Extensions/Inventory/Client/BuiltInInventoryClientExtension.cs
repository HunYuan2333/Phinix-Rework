using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using PhinixClient;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using Utils;
using Utils.Framework;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    [PhinixExtension("builtin.inventory")]
    public sealed partial class BuiltInInventoryClientExtension : ClientExtensionModule, IActivatablePhinixExtensionModule,
        IInventoryApi, IInventoryRegistrationApi, IInventoryDepositApi, IInventoryReadApi, IInventoryExtractionApi,
        IInventoryBatchExtractionApi, IInventoryReservationApi, IInventoryReceiveScopeApi, IInventorySavedReceiptApi
    {
        private static BuiltInInventoryClientExtension active;
        private IClientCompositionScope composition;
        private InventoryTab tab;
        private InventorySettingsPanel settingsPanel;
        private InventoryTheme theme;
        private readonly Dictionary<string, IInventoryCodec> codecs = new Dictionary<string, IInventoryCodec>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IInventorySourcePresenter> sourcePresenters = new Dictionary<string, IInventorySourcePresenter>(StringComparer.OrdinalIgnoreCase);
        private readonly object registrationLock = new object();
        private readonly InventoryLedger ledger = new InventoryLedger();
        private InventoryDatabaseStore store;
        private InventoryGameComponent attachedSave;
        private string attachedIdentity;
        private ReceiveScope publishedReceiveScope;
        private ExtensionHostContext host;
        private IClientErrorReporter errorReporter;
        private void ReportFailure(string operation,Exception error,bool notify=true)
            => errorReporter.ReportSensitive(error,"InventoryFailure",operation,notify);
        private IClientSettingsContext settings;
        private IClientMainThreadDispatcher dispatcher;
        private IClientShellEventStream shellEvents;
        private IClientGameWindowService windowService;
        private bool deliveryChoiceOpen;
        private InventoryDeliveryChoiceDialog deliveryChoiceDialog;
        private volatile int mainThreadId;
        private int activationGeneration;
        private string fault;
        internal string StorageNotice { get; private set; }
        private Harmony harmony;

        public event EventHandler InventoryChanged;
        public event EventHandler AvailabilityChanged;

        public override string ExtensionId => "builtin.inventory";
        internal static bool IsAttached(InventoryGameComponent component) => active != null && active.attachedSave == component;
        internal static string ExportSnapshot() => FrameworkSerialization.SerializePayload(active.ledger.Export());

        public override void Compose(IExtensionBuilder builder)
        {
            if (composition != null) throw new InvalidOperationException("Inventory is already composed.");
            ExtensionHostContext context = builder.HostContext;
            composition = context.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
            {
                // The registry owns this facade and the host owns these services.
                local.Borrow(this);
                local.Borrow(context.GetRequiredService<IClientSettingsContext>());
                local.Borrow(context.GetRequiredService<IClientMainThreadDispatcher>());
                local.Borrow(context.GetRequiredService<IClientShellEventStream>());
                local.Borrow(context.GetRequiredService<IClientGameWindowService>());
                local.Borrow(context.GetRequiredService<IClientTheme>());
                local.Borrow(context.GetRequiredService<IClientThemeRegistry>());
                local.Register<InventoryTheme, InventoryTheme>();
                local.Register<InventoryTab, InventoryTab>();
                local.Register<InventorySettingsPanel, InventorySettingsPanel>();
            });
            tab = composition.Resolve<InventoryTab>();
            settingsPanel = composition.Resolve<InventorySettingsPanel>();
            theme = composition.Resolve<InventoryTheme>();
            settings = composition.Resolve<IClientSettingsContext>();
            dispatcher = composition.Resolve<IClientMainThreadDispatcher>();
            shellEvents = composition.Resolve<IClientShellEventStream>();
            windowService = composition.Resolve<IClientGameWindowService>();
            builder.RegisterApi<IInventoryApi>(this);
            builder.RegisterApi<IInventoryRegistrationApi>(this);
            builder.RegisterApi<IInventoryDepositApi>(this);
            builder.RegisterApi<IInventoryReadApi>(this);
            builder.RegisterApi<IInventoryReceiveScopeApi>(this);
            builder.RegisterApi<IInventorySavedReceiptApi>(this);
            builder.RegisterApi<IInventoryExtractionApi>(this);
            builder.RegisterApi<IInventoryBatchExtractionApi>(this);
            builder.RegisterApi<IInventoryReservationApi>(this);
            builder.RegisterApi<IMainTabProvider>(tab);
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanel);
            builder.RegisterApi<IClientQuickSettingsPanelProvider>(settingsPanel);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            host = hostContext;
            IClientErrorService moduleErrorService;
            InventoryDiagnostics.Scope.Bind(hostContext.TryGetService(out moduleErrorService)?moduleErrorService.ForModule(this):null);
            if(hostContext.TryGetService<IClientErrorService>(out var errors))errorReporter=errors.ForModule(this);
            theme.Start(this);
            tab.Start(this, hostContext.GetRequiredService<IClientPresentationService>(), hostContext.GetRequiredService<IClientExtensionPointService>(), windowService);
            settingsPanel.Start(this, hostContext.GetRequiredService<IClientPresentationService>(), hostContext.GetRequiredService<IClientExtensionPointService>(), settings);
            active = this;
            int generation = Interlocked.Increment(ref activationGeneration);
            harmony = new Harmony("phinix.inventory.save-identity");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            shellEvents.MainWindowOpened += OnMainWindowOpened;
            dispatcher.Enqueue(() =>
            {
                if (!ReferenceEquals(active, this) || generation != activationGeneration) return;
                BindGameMainThread();
                if (Current.Game != null)
                {
                    InventoryGameComponent component = InventoryGameComponent.EnsureFor(Current.Game);
                    if (component != null) Attach(component);
                }
            });
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            try {

            StopBatchExtraction();
            extractionBatch = null; ReleaseBatchContext();
            InvalidateReceiveScope();
            // Invalidate queued work before releasing any owned resource.
            Interlocked.Increment(ref activationGeneration);
            if (active == this) active = null;
            var failures = new List<Exception>();
            InventoryDeliveryChoiceDialog oldDialog = deliveryChoiceDialog;
            deliveryChoiceDialog = null;
            Release(() => oldDialog?.Close(), failures);
            InventoryDatabaseStore oldJournal = store;
            store = null;
            attachedSave = null;
            attachedIdentity = null;
            ledger.Restore(new InventoryState());
            fault = null;
            Release(() => oldJournal?.Dispose(), failures);
            codecs.Clear();
            sourcePresenters.Clear();
            Harmony oldHarmony = harmony;
            harmony = null;
            Release(() => oldHarmony?.UnpatchAll("phinix.inventory.save-identity"), failures);
            InventorySaveIdentityPatch.Clear();
            mainThreadId = 0;
            dispatcher = null;
            IClientShellEventStream oldShell = shellEvents;
            shellEvents = null;
            Release(() => { if (oldShell != null) oldShell.MainWindowOpened -= OnMainWindowOpened; }, failures);
            windowService = null;
            deliveryChoiceOpen = false;
            host = null;
            settings = null;
            Release(RaiseAvailabilityChanged, failures);
            IClientCompositionScope oldComposition = composition;
            composition = null;
            tab = null;
            settingsPanel = null;
            theme = null;
            Release(() => oldComposition?.Dispose(), failures);
            if (failures.Count != 0) throw new AggregateException("Inventory shutdown failed.", failures);

            } finally { InventoryDiagnostics.Scope.Unbind(); }
        }

        private static void Release(Action release, List<Exception> failures)
        {
            try { release(); }
            catch (Exception error) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/Release", error);  failures.Add(error); }
        }

        private void OnMainWindowOpened(object sender, EventArgs args)
        {
            string current = settings?.Get<string>(InventoryDeliveryPreference.SettingKey, null);
            if (InventoryDeliveryPreference.IsValid(current) || deliveryChoiceOpen || windowService == null) return;
            deliveryChoiceOpen = true;
            try
            {
                deliveryChoiceDialog = new InventoryDeliveryChoiceDialog(
                    this, host.GetRequiredService<IClientPresentationService>(), host.GetRequiredService<IClientExtensionPointService>(),
                    SetDeliveryPreference,
                    () => { deliveryChoiceOpen = false; deliveryChoiceDialog = null; }, host.GetRequiredService<IClientTheme>());
                windowService.Open(deliveryChoiceDialog);
            }
            catch (System.Exception boundaryError35)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/OnMainWindowOpened", boundaryError35);
                var failedDialog = deliveryChoiceDialog;
                deliveryChoiceDialog = null; deliveryChoiceOpen = false;
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/OnMainWindowOpened/Cleanup", () => { failedDialog?.Dispose(); });
                throw;
            }
        }

        private void SetDeliveryPreference(string value)
        {
            settings?.Set(InventoryDeliveryPreference.SettingKey, value);
            deliveryChoiceOpen = false;
            deliveryChoiceDialog = null;
        }

        internal static void AttachSave(InventoryGameComponent component)
        {
            active?.QueueAttach(component);
        }

        internal static void RefreshAfterSave()
        {
            var module = active; var component = InventoryGameComponent.Current;
            if (module == null || component == null || module.dispatcher == null) return;
            int generation = module.activationGeneration;
            module.dispatcher.Enqueue(() =>
            {
                if (!ReferenceEquals(active, module) || generation != module.activationGeneration ||
                    !InventoryGameComponent.BelongsTo(component, Current.Game)) return;
                module.BindGameMainThread(); module.Attach(component); module.RaiseEvent(module.SavedCheckpoint);
            });
        }

        private void QueueAttach(InventoryGameComponent component)
        {
            if (component == null || dispatcher == null) return;
            if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                Attach(component);
                return;
            }
            int generation = activationGeneration;
            dispatcher.Enqueue(() =>
            {
                if (!ReferenceEquals(active, this) || generation != activationGeneration) return;
                BindGameMainThread();
                Attach(component);
            });
        }

        internal static void ObserveGameLifetime()
        {
            BuiltInInventoryClientExtension module = active;
            if (module == null) return;
            module.BindGameMainThread();
            Game game = Current.Game;
            module.SynchronizeGame(game);
            InventoryGameComponent component = InventoryGameComponent.Current;
            if (module.attachedSave == null && component != null && component.Initialized &&
                InventoryGameComponent.BelongsTo(component, game)) module.Attach(component);
            module.PublishReceiveScope();
            module.AdvanceBatchExtraction();
        }

        private void SynchronizeGame(Game game)
        {
            if (attachedSave == null || InventoryGameComponent.BelongsTo(attachedSave, game)) return;
            InvalidateReceiveScope();
            StopBatchExtraction();
            extractionBatch = null; ReleaseBatchContext();
            InventoryDatabaseStore oldJournal = store;
            store = null;
            attachedSave = null;
            attachedIdentity = null;
            fault = null;
            // Durable transactions remain in the old store; never carry its display state into another game.
            ledger.Restore(new InventoryState());
            try { oldJournal?.Dispose(); }
            catch (Exception error) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/SynchronizeGame", error);  global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/SynchronizeGame/LogObserver", () => { host?.Log?.Invoke("[Inventory] Save store release failed: " + error.GetType().Name, LogLevel.ERROR); }); }
            finally
            {
                RaiseInventoryChanged();
                RaiseAvailabilityChanged();
            }
        }

        private void BindGameMainThread()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void Attach(InventoryGameComponent component)
        {
            if (!ReferenceEquals(active, this) || component == null || !component.Initialized || string.IsNullOrEmpty(component.SaveId) || !InventoryGameComponent.BelongsTo(component, Current.Game)) return;
            string identity = ResolveSaveIdentity();
            if (component == attachedSave && store != null && fault == null && !store.Faulted)
            { attachedIdentity=identity; PublishReceiveScope(); return; }
            bool changedGame = component != attachedSave;
            InvalidateReceiveScope();
            attachedSave = component; attachedIdentity = identity; fault = null;
            bool showMigrationNotice = false;
            try
            {
                if (store == null) store = new InventoryDatabaseStore(host.GetRequiredService<PhinixClient.Storage.IClientDatabaseService>(),(operation,error)=>ReportFailure(operation,error));
                if (component.StorageVersion > 1)
                    throw new InvalidDataException("This save uses a newer inventory upgrade format.");
                if(component.StorageVersion==1 && component.DatabaseIdentity!=store.Identity)
                {
                    // A copied world does not select or replace the local shared balance.
                    NotifyStorage("Phinix_inventory_databaseChanged");
                    component.CompleteDatabaseMigration(store.Identity);
                }
                store.Load(component.SaveId);
                if (component.StorageVersion == 0)
                {
                    bool hadLegacy = component.HasLegacySnapshot;
                    try
                    {
                        if (store.Import(component.SaveId, component.ReadSnapshot(), out string notice, out bool firstImport))
                        {
                            component.CompleteDatabaseMigration(store.Identity);
                            showMigrationNotice = hadLegacy && firstImport;
                        }
                        if (notice != null) NotifyStorage(notice);
                        else if (hadLegacy) NotifyStorage("Phinix_inventory_legacyImported");
                    }
                    catch (Exception legacyError) when (legacyError is InvalidDataException || legacyError is System.Runtime.Serialization.SerializationException || legacyError is ArgumentException)
                    { ReportFailure("InventoryLegacyImportFailed",legacyError);NotifyStorage("Phinix_inventory_legacyImportConflict"); }
                }
                ledger.Restore(store.Load(component.SaveId));
                if (changedGame && store.AbandonInterruptedExtraction())
                { ledger.Restore(store.Load(component.SaveId)); NotifyStorage("Phinix_inventory_interruptedAbandoned"); }
            }
            catch (Exception error)
            {
                ReportFailure("InventoryAttachFailed",error);
                fault = "Inventory database is unavailable; existing data was retained.";
            }
            PublishReceiveScope(); RaiseInventoryChanged(); RaiseAvailabilityChanged();
            if (showMigrationNotice && fault == null)
            {
                try
                {
                    windowService?.Open(new Dialog_MessageBox(
                        title: "Phinix_inventory_migrationTitle".Translate(),
                        text: "Phinix_inventory_migrationMessage".Translate()));
                }
                catch (Exception error) { ReportFailure("InventoryMigrationNoticeFailed",error,false); }
            }
        }

        private void NotifyStorage(string key)
        {
            StorageNotice=key;
            try {StorageNotice=key.Translate().ToString();}catch (System.Exception boundaryError40) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/NotifyStorage", boundaryError40);  }
            try { host?.Log?.Invoke("[Inventory] " + key.Translate().ToString(), LogLevel.WARNING); } catch (System.Exception boundaryError41) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/NotifyStorage", boundaryError41);  }
            try { Messages.Message(key.Translate(), MessageTypeDefOf.CautionInput, false); } catch (System.Exception boundaryError42) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/NotifyStorage", boundaryError42);  }
        }
        private bool CommitInventory(InventoryState state)
        {
            bool committed = store != null && store.TryAppend(state);
            if (!committed) RaiseAvailabilityChanged();
            return committed;
        }

        public void RegisterCodec(IInventoryCodec codec)
        {
            RegisterCodecCore(codec);
        }

        public IDisposable RegisterCodecScoped(IInventoryCodec codec)
        {
            RegisterCodecCore(codec);
            return new InventoryRegistration(() =>
            {
                lock (registrationLock)
                {
                    if (codecs.TryGetValue(codec.CodecId, out IInventoryCodec current) && ReferenceEquals(current, codec))
                        codecs.Remove(codec.CodecId);
                }
                RaiseAvailabilityChanged();
            });
        }

        public IDisposable RegisterSourcePresenter(IInventorySourcePresenter presenter)
        {
            if (presenter == null || string.IsNullOrWhiteSpace(presenter.SourceId))
                throw new ArgumentException("Invalid inventory source presenter.", nameof(presenter));
            lock (registrationLock)
            {
                if (sourcePresenters.TryGetValue(presenter.SourceId, out IInventorySourcePresenter existing) &&
                    !ReferenceEquals(existing, presenter))
                    throw new InvalidOperationException("Inventory source presenter is already registered: " + presenter.SourceId);
                sourcePresenters[presenter.SourceId] = presenter;
            }
            RaiseInventoryChanged();
            return new InventoryRegistration(() =>
            {
                lock (registrationLock)
                {
                    if (sourcePresenters.TryGetValue(presenter.SourceId, out IInventorySourcePresenter current) &&
                        ReferenceEquals(current, presenter))
                        sourcePresenters.Remove(presenter.SourceId);
                }
                RaiseInventoryChanged();
            });
        }

        private void RegisterCodecCore(IInventoryCodec codec)
        {
            if (codec == null || string.IsNullOrWhiteSpace(codec.CodecId) || codec.Version < 1)
                throw new ArgumentException("Invalid inventory codec.", nameof(codec));
            lock (registrationLock)
            {
                if (codecs.TryGetValue(codec.CodecId, out IInventoryCodec existing) && !ReferenceEquals(existing, codec))
                    throw new InvalidOperationException("Inventory codec ID is already registered: " + codec.CodecId);
                codecs[codec.CodecId] = codec;
            }
            RaiseAvailabilityChanged();
        }

        public InventoryDepositResult TryDeposit(InventoryDeposit deposit)
        {
            if (!Ready(out string reason))
                return new InventoryDepositResult { Status = InventoryDepositStatus.Unavailable, FailureCode = InventoryFailureCode.Unavailable, Reason = reason };
            try
            {
                InventoryDepositResult result = ledger.PrepareDeposit(deposit, codecs, out InventoryState proposed);
                if (result.Status != InventoryDepositStatus.Committed) return result;
                if (!CommitInventory(proposed))
                    return new InventoryDepositResult { Status = InventoryDepositStatus.Unavailable, FailureCode = InventoryFailureCode.PersistenceFailed, Reason = "Inventory database could not confirm the deposit." };
                ledger.Publish(proposed);
                RaiseInventoryChanged();
                return result;
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/TryDeposit", ex);
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/TryDeposit/LogObserver", () => { host?.Log?.Invoke("[Inventory] Deposit failed: " + ex.GetType().Name, LogLevel.ERROR); });
                return new InventoryDepositResult { Status = InventoryDepositStatus.Rejected, FailureCode = InventoryFailureCode.InvalidRequest, Reason = "Operation failed; see the error report." };
            }
        }

        public InventoryDepositResult CheckDeposit(InventoryDeposit deposit)
        {
            if (!ReadyWithoutPending(out string reason))
                return new InventoryDepositResult { Status = InventoryDepositStatus.Unavailable, FailureCode = InventoryFailureCode.Unavailable, Reason = reason };
            try { return ledger.CheckDeposit(deposit); }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/CheckDeposit", ex, true);
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/CheckDeposit/LogObserver", () => { host?.Log?.Invoke("[Inventory] Deposit receipt check failed: " + ex.GetType().Name, LogLevel.ERROR); });
                return new InventoryDepositResult { Status = InventoryDepositStatus.Rejected, FailureCode = InventoryFailureCode.InvalidRequest, Reason = "Operation failed; see the error report." };
            }
        }

        public IReadOnlyList<InventoryEntry> GetSnapshot()
        {
            return ledger.Snapshot();
        }

        public IReadOnlyList<InventoryEntry> GetAvailableSnapshot()
        {
            return ledger.AvailableSnapshot();
        }

        public IReadOnlyList<InventoryReservation> GetReservations()
        {
            return ledger.ReservationSnapshot();
        }

        public InventoryReservationResult TryReserve(InventoryReservationRequest request)
        {
            if (!Ready(out string reason))
                return new InventoryReservationResult
                {
                    Status = InventoryReservationResultStatus.Unavailable,
                    FailureCode = InventoryFailureCode.Unavailable,
                    Reason = reason
                };
            try
            {
                InventoryReservationResult result = ledger.PrepareReservation(request, out InventoryState proposed);
                if (result.Status != InventoryReservationResultStatus.Reserved) return result;
                if (!CommitInventory(proposed))
                    return new InventoryReservationResult
                    {
                        Status = InventoryReservationResultStatus.Unavailable,
                        FailureCode = InventoryFailureCode.PersistenceFailed,
                        Reason = "Inventory database could not confirm the reservation."
                    };
                ledger.Publish(proposed);
                RaiseInventoryChanged();
                return result;
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/TryReserve", ex);
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/TryReserve/LogObserver", () => { host?.Log?.Invoke("[Inventory] Reservation failed: " + ex.GetType().Name, LogLevel.ERROR); });
                return new InventoryReservationResult
                {
                    Status = InventoryReservationResultStatus.Rejected,
                    FailureCode = InventoryFailureCode.InvalidRequest,
                    Reason = "Operation failed; see the error report."
                };
            }
        }

        public InventoryMaterializationResult CreatePreview(string entryId)
        {
            if (!ReadyWithoutPending(out string reason)) return MaterializationFailure(reason,
                InventoryFailureCode.Unavailable);
            InventoryEntry entry = ledger.AvailableSnapshot().FirstOrDefault(candidate =>
                candidate.EntryId == entryId);
            if (entry == null) return MaterializationFailure("The inventory entry is unavailable.",
                InventoryFailureCode.InvalidRequest);
            if (!codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec) ||
                codec.Version != entry.CodecVersion)
                return MaterializationFailure("The item codec is unavailable.",
                    InventoryFailureCode.CodecUnavailable);
            try
            {
                if (!TryGetMaximumStackCount(codec, entry, out int maximumStack, out reason))
                    return MaterializationFailure(reason, InventoryFailureCode.CodecUnavailable);
                int count = (int)Math.Min(entry.Quantity, maximumStack);
                Thing thing = codec.Materialize(entry, count);
                if (!IsValidMaterialization(entry, count, thing))
                {
                    DiscardUnclaimed(thing);
                    return MaterializationFailure("The item codec returned an invalid preview.",
                        InventoryFailureCode.CodecUnavailable);
                }
                return new InventoryMaterializationResult
                {
                    Succeeded = true,
                    Things = new[] { thing }
                };
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/CreatePreview", ex, true);
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/CreatePreview/LogObserver", () => { host?.Log?.Invoke("[Inventory] Preview materialization failed: " + ex.GetType().Name, LogLevel.WARNING); });
                return MaterializationFailure("Operation failed; see the error report.", InventoryFailureCode.CodecUnavailable);
            }
        }

        public InventoryMaterializationResult MaterializeReservation(string reservationId)
        {
            if (!ReadyWithoutPending(out string reason)) return MaterializationFailure(reason,
                InventoryFailureCode.Unavailable);
            InventoryReservation reservation = ledger.ReservationSnapshot().FirstOrDefault(candidate =>
                candidate.ReservationId == reservationId);
            if (reservation == null || (reservation.State != InventoryReservationState.Reserved &&
                reservation.State != InventoryReservationState.Uncertain))
                return MaterializationFailure("The active inventory reservation was not found.",
                    InventoryFailureCode.InvalidRequest);

            List<Thing> materialized = new List<Thing>();
            try
            {
                foreach (InventoryReservedItem reserved in reservation.Items)
                {
                    InventoryEntry entry = reserved.Entry;
                    if (entry == null || !codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec) ||
                        codec.Version != entry.CodecVersion)
                        throw new InvalidOperationException("A reserved item codec is unavailable.");
                    if (!TryGetMaximumStackCount(codec, entry, out int maximumStack, out reason))
                        throw new InvalidOperationException(reason);
                    long remaining = reserved.Quantity;
                    while (remaining > 0)
                    {
                        int count = (int)Math.Min(remaining, maximumStack);
                        Thing thing = codec.Materialize(entry, count);
                        if (!IsValidMaterialization(entry, count, thing))
                        {
                            DiscardUnclaimed(thing);
                            throw new InvalidOperationException("An item codec returned an invalid transfer object.");
                        }
                        materialized.Add(thing);
                        remaining -= count;
                    }
                }
                return new InventoryMaterializationResult { Succeeded = true, Things = materialized };
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/MaterializeReservation", ex, true);
                foreach (Thing thing in materialized) global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/MaterializeReservation/Cleanup", () => { DiscardUnclaimed(thing); });
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/MaterializeReservation/LogObserver", () => { host?.Log?.Invoke("[Inventory] Reservation materialization failed: " + ex.GetType().Name, LogLevel.ERROR); });
                return MaterializationFailure("Operation failed; see the error report.", InventoryFailureCode.CodecUnavailable);
            }
        }

        public InventoryReservationResolutionResult ResolveReservation(string reservationId,
            InventoryReservationResolution resolution, string evidence)
        {
            if (!ReadyWithoutPending(out string reason))
                return new InventoryReservationResolutionResult
                {
                    Succeeded = false,
                    FailureCode = InventoryFailureCode.Unavailable,
                    Reason = reason
                };
            try
            {
                InventoryReservationResolutionResult result = ledger.PrepareReservationResolution(
                    reservationId, resolution, evidence, out InventoryState proposed);
                if (!result.Succeeded || proposed == null) return result;
                if (!CommitInventory(proposed))
                    return new InventoryReservationResolutionResult
                    {
                        Succeeded = false,
                        FailureCode = InventoryFailureCode.PersistenceFailed,
                        Reason = "Inventory database did not persist the reservation result.",
                        Reservation = result.Reservation
                    };
                ledger.Publish(proposed);
                RaiseInventoryChanged();
                return result;
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/ResolveReservation", ex, true);
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/ResolveReservation/LogObserver", () => { host?.Log?.Invoke("[Inventory] Reservation resolution failed: " + ex.GetType().Name, LogLevel.ERROR); });
                return new InventoryReservationResolutionResult
                {
                    Succeeded = false,
                    FailureCode = InventoryFailureCode.InvalidRequest,
                    Reason = "Operation failed; see the error report."
                };
            }
        }

        public bool TryExtract(string entryId, long quantity, out string reason)
        {
            bool success = TryExtractForPresentation(entryId, quantity, out long delivered, out reason);
            if (!success && delivered > 0) reason = "Phinix_inventory_extractPartial".Translate(delivered, quantity, reason).ToString();
            return success;
        }

        // Same transaction path; report only fully verified stacks, never infer delivery from a balance delta.
        internal bool TryExtractForPresentation(string entryId, long quantity, out long delivered, out string reason)
        {
            delivered = 0;
            if (!Ready(out reason)) return false;
            InventoryEntry entry = ledger.AvailableSnapshot().FirstOrDefault(candidate => candidate.EntryId == entryId);
            if (entry == null || quantity < 1)
            {
                reason = "Phinix_inventory_extractInvalid".Translate().ToString();
                return false;
            }
            if (quantity > entry.Quantity)
            {
                reason = "Phinix_inventory_extractExceedsBalance".Translate(entry.Quantity).ToString();
                return false;
            }
            if (!codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec) || codec.Version != entry.CodecVersion)
            {
                reason = "The item codec is unavailable.";
                return false;
            }
            if (!TryGetMaximumStackCount(codec, entry, out int maximumStack, out reason) || maximumStack < 1)
                return false;

            while (delivered < quantity)
            {
                long stackQuantity = Math.Min(quantity - delivered, maximumStack);
                if (!TryExtractSingleStack(entryId, stackQuantity, null, -1, out string stackReason))
                {
                    reason = stackReason;
                    return false;
                }
                delivered += stackQuantity;
            }

            reason = null;
            return true;
        }

        private bool TryExtractSingleStack(string entryId, long quantity, string scheduleEntryId, int scheduleDay, out string reason)
        {
            if (!Ready(out reason)) return false;
            Map map = settings != null && settings.Get("trade.dropCurrentMap", false)
                ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (!InventoryDropDelivery.TryResolveTradeDropTarget(map, out IntVec3 spot, out reason))
                return false;
            if (!ledger.PrepareExtraction(entryId, quantity, scheduleEntryId, scheduleDay,
                out InventoryState reserved, out InventoryEntry entry))
            {
                reason = "Invalid quantity or an extraction is already pending.";
                return false;
            }
            if (!codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec) || codec.Version != entry.CodecVersion)
            {
                reason = "The item codec is unavailable.";
                return false;
            }
            if (!TryGetMaximumStackCount(codec, entry, out int maxStack, out reason)) return false;
            if (quantity > int.MaxValue || maxStack < 1 || quantity > maxStack)
            {
                reason = "The extraction batch exceeds the codec stack limit.";
                return false;
            }
            Thing thing;
            try { thing = codec.Materialize(entry, (int)quantity); }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/TryExtractSingleStack", ex, true);
                reason = "Item could not be restored: " + "Operation failed; see the error report.";
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/TryExtractSingleStack/LogObserver", () => { host?.Log?.Invoke("[Inventory] Materialize failed: " + ex.GetType().Name, LogLevel.ERROR); });
                return false;
            }
            bool invalidStack = entry.AggregationKey == null
                ? quantity != 1 || thing != null && thing.stackCount < 1
                : thing != null && thing.stackCount != quantity;
            if (thing == null || thing.def == null || invalidStack)
            {
                DiscardUnclaimed(thing);
                reason = "Item codec returned an invalid object.";
                return false;
            }
            if (!CommitInventory(reserved))
            {
                DiscardUnclaimed(thing);
                reason = "Inventory database could not confirm the extraction reservation. " + store.LastError;
                return false;
            }
            ledger.Publish(reserved);
            try
            {
                InventoryDeliveryResult delivery = codec.DeliverVerified(thing, map, spot);

                if (delivery == null || delivery.Status == InventoryDeliveryStatus.Uncertain)
                {
                    reason = delivery?.Reason ?? "Delivery status is uncertain; reconcile the pending extraction.";
                    return false;
                }
                if (delivery.Status == InventoryDeliveryStatus.Rejected)
                    return RestoreRejectedDelivery(thing, delivery.Reason, out reason);

                InventoryState completed = ledger.PrepareExtractionResolution(true);
                if (!CommitInventory(completed))
                {
                    reason = "Delivery needs reconciliation; its inventory balance is locked.";
                    return false;
                }
                ledger.Publish(completed);
                RaiseInventoryChanged();
                reason = null;
                return true;
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/TryExtractSingleStack", ex, true);
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/TryExtractSingleStack/LogObserver", () => { host?.Log?.Invoke("[Inventory] Delivery failed: " + ex.GetType().Name, LogLevel.ERROR); });
                InventoryDeliveryResult custody = InventoryDropDelivery.InspectCustody(new[] { thing }, map, spot);
                if (custody.Status == InventoryDeliveryStatus.Rejected)
                    return RestoreRejectedDelivery(thing, "Operation failed; see the error report.", out reason);
                reason = "Delivery status is uncertain; reconcile the pending extraction.";
                return false;
            }
        }

        private bool RestoreRejectedDelivery(Thing thing, string deliveryReason, out string reason)
        {
            InventoryState restored = ledger.PrepareExtractionResolution(false);
            if (restored == null || !CommitInventory(restored))
            {
                DiscardUnclaimed(thing);
                reason = "Delivery failed and its inventory reservation needs reconciliation.";
                return false;
            }
            ledger.Publish(restored);
            DiscardUnclaimed(thing);
            RaiseInventoryChanged();
            RaiseAvailabilityChanged();
            reason = string.IsNullOrWhiteSpace(deliveryReason)
                ? "Delivery was rejected; the inventory balance was restored."
                : deliveryReason;
            return false;
        }

        internal bool HasPendingRecovery => false;
        internal bool HasPendingExtraction => ledger.PendingExtraction != null;
        internal bool HasUncertainReservations => ledger.ReservationSnapshot().Any(item=>item.State==InventoryReservationState.Uncertain);
        internal bool AbandonUncertainReservations()
        {
            if(!ReadyWithoutPending(out _))return false;
            foreach(var reservation in ledger.ReservationSnapshot().Where(item=>item.State==InventoryReservationState.Uncertain).ToArray())
                if(!ResolveReservation(reservation.ReservationId,InventoryReservationResolution.Abandon,"user-abandoned; no remote outcome asserted").Succeeded)return false;
            NotifyStorage("Phinix_inventory_reservationsAbandoned");return true;
        }
        internal string Fault => fault ?? (store != null && store.Faulted
            ? (store.FaultReason ?? "Inventory store is unavailable.") + " Inventory is read only."
            : null);
        internal long Version => ledger.Sequence;

        public IReadOnlyList<InventorySchedule> GetSchedules() => ledger.ScheduleSnapshot();

        // Shared preflight: the editor and the write path must report the same rejection.
        internal bool CanSetDailySchedule(string entryId, long quantityPerRun, int dayTick, out string reason)
        {
            if (!Ready(out reason)) return false;
            InventoryEntry entry = ledger.AvailableSnapshot().FirstOrDefault(candidate => candidate.EntryId == entryId);
            if (entry == null)
                reason = "Phinix_inventory_planMissingItem".Translate().ToString();
            else if (quantityPerRun < 1 || quantityPerRun > entry.Quantity)
                reason = "Phinix_inventory_inlineQuantityInvalid".Translate(entry.Quantity).ToString();
            else if (dayTick < 0 || dayTick >= 60000)
                reason = "Phinix_inventory_planInvalid".Translate().ToString();
            else
            {
                IInventoryCodec codec;
                lock (registrationLock) codecs.TryGetValue(entry.CodecId, out codec);
                if (codec == null)
                    reason = "Phinix_inventory_planCodecMissing".Translate().ToString();
                else if (codec.Version != entry.CodecVersion)
                    reason = "Phinix_inventory_planCodecVersion".Translate().ToString();
                else if (!TryGetMaximumStackCount(codec, entry, out _, out _))
                    reason = "Phinix_inventory_planItemUnreadable".Translate().ToString();
            }
            return reason == null;
        }

        public bool SetDailySchedule(string entryId, long quantityPerRun, int dayTick, out string reason)
        {
            if (!CanSetDailySchedule(entryId, quantityPerRun, dayTick, out reason)) return false;
            InventoryState proposed = ledger.PrepareSchedule(entryId, quantityPerRun, dayTick, false);
            if (proposed == null || !CommitInventory(proposed))
            {
                reason = "Phinix_inventory_planSaveFailed".Translate().ToString();
                return false;
            }
            ledger.Publish(proposed);
            RaiseInventoryChanged();
            return true;
        }

        public bool RemoveDailySchedule(string entryId)
        {
            if (!Ready(out _)) return false;
            if (!ledger.ScheduleSnapshot().Any(schedule => schedule.EntryId == entryId)) return true;
            InventoryState proposed = ledger.PrepareSchedule(entryId, 0, 0, true);
            if (proposed == null || !CommitInventory(proposed)) return false;
            ledger.Publish(proposed);
            RaiseInventoryChanged();
            return true;
        }

        internal static void TickSchedules()
        {
            active?.ProcessSchedules();
        }

        private void ProcessSchedules()
        {
            if (!Ready(out _) || Find.TickManager == null) return;
            int ticks = Find.TickManager.TicksGame;
            int day = ticks / 60000;
            int dayTick = ticks % 60000;
            foreach (InventorySchedule schedule in ledger.ScheduleSnapshot())
            {
                if (schedule.PendingDueDay < 0 && dayTick >= schedule.DayTick &&
                    schedule.LastTriggeredDay < day)
                {
                    InventoryState due = ledger.PrepareScheduleDue(schedule.EntryId, day);
                    if (due == null || !CommitInventory(due)) return;
                    ledger.Publish(due);
                    RaiseInventoryChanged();
                    schedule.PendingDueDay = day;
                    schedule.RemainingQuantity = due.Schedules.First(item => item.EntryId == schedule.EntryId).RemainingQuantity;
                }
                if (schedule.PendingDueDay < 0) continue;
                InventoryEntry entry = ledger.AvailableSnapshot().FirstOrDefault(item => item.EntryId == schedule.EntryId);
                if (entry == null || !codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec)) continue;
                if (!TryGetMaximumStackCount(codec, entry, out int maximumStack, out _)) continue;
                long quantity = Math.Min(schedule.RemainingQuantity, Math.Min(entry.Quantity, maximumStack));
                if (quantity > 0 && TryExtractSingleStack(schedule.EntryId, quantity, schedule.EntryId, schedule.PendingDueDay, out _))
                    return; // One stack per budgeted tick.
            }
        }

        internal bool TryExtractOneStack(string entryId, out string reason)
        {
            InventoryEntry entry = ledger.AvailableSnapshot().FirstOrDefault(candidate => candidate.EntryId == entryId);
            if (entry == null || !codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec))
            {
                reason = "The item or its codec is unavailable.";
                return false;
            }
            if (!TryGetMaximumStackCount(codec, entry, out int maximumStack, out reason)) return false;
            return TryExtract(entryId, Math.Min(entry.Quantity, maximumStack), out reason);
        }

        internal void RetryDatabase()
        {
            if(!ReferenceEquals(active,this)||mainThreadId!=Thread.CurrentThread.ManagedThreadId)return;
            store?.Dispose();store=null;fault=null;Attach(InventoryGameComponent.Current);
        }
        internal bool CanDiscardUnrecoverableHistory => false;
        internal bool DiscardUnrecoverableHistory(out string reason)
        { reason = "Shared inventory cannot be reset from a game save. Restore its database backup instead."; return false; }
        internal bool ApproveRecovery() => false;
        internal bool RejectRecovery() => false;

        internal bool AbandonPendingExtraction()
        {
            if(!ResolvePendingExtraction(true))return false;
            NotifyStorage("Phinix_inventory_interruptedAbandoned");return true;
        }

        internal bool ResolvePendingExtraction(bool delivered)
        {
            if (!ReadyWithoutPending(out _)) return false;
            InventoryState resolved = ledger.PrepareExtractionResolution(delivered);
            if (resolved == null || !CommitInventory(resolved)) return false;
            ledger.Publish(resolved);
            RaiseInventoryChanged();
            RaiseAvailabilityChanged();
            return true;
        }

        public InventoryCapabilities GetCapabilities()
        {
            return new InventoryCapabilities
            {
                ContractVersion = InventoryContract.Version,
                SupportsProvenance = true,
                SupportsScopedRegistration = true,
                SupportsSourcePresentation = true,
                SupportsChangeEvents = true,
                SupportsVerifiedDelivery = true,
                SupportsReservations = true,
                SupportsItemPresentation = true,
                MaximumEntryCount = InventoryLedger.MaxEntryCount,
                MaximumDepositItemCount = InventoryLedger.MaxDepositItemCount,
                MaximumItemPayloadBytes = InventoryLedger.MaxPayloadBytes,
                MaximumDepositPayloadBytes = InventoryLedger.MaxBatchBytes
            };
        }

        public IInventoryReceiveScope CaptureReceiveScope() => Volatile.Read(ref publishedReceiveScope);

        private void InvalidateReceiveScope()
        { Interlocked.Exchange(ref publishedReceiveScope,null)?.Invalidate(); }

        private void PublishReceiveScope()
        {
            if(mainThreadId==0) { InvalidateReceiveScope();return; }
            if(Thread.CurrentThread.ManagedThreadId!=mainThreadId)
                throw new InvalidOperationException("Inventory receive scope publication requires the main thread.");
            var previous=Volatile.Read(ref publishedReceiveScope);
            // Stable frames compare in-memory ownership only; disk/hash validation belongs to actual use.
            if(previous!=null && previous.MatchesLive()) return;
            int epoch=InventorySaveIdentityPatch.Generation;
            string identity=GetStatus().SaveIdentity;
            if(identity==null || InventorySaveIdentityPatch.ReceiveScopeSuspended || epoch!=InventorySaveIdentityPatch.Generation) { InvalidateReceiveScope();return; }
            InvalidateReceiveScope();
            Volatile.Write(ref publishedReceiveScope,new ReceiveScope(this,Current.Game,attachedSave,identity,epoch));
        }

        private sealed class ReceiveScope : IInventoryReceiveScope
        {
            private readonly BuiltInInventoryClientExtension owner;
            private readonly Game game;
            private readonly InventoryGameComponent component;
            private readonly int mainThread;
            private readonly string lineage;
            private int invalid;
            public string SaveIdentity { get; }
            internal ReceiveScope(BuiltInInventoryClientExtension owner,Game game,InventoryGameComponent component,string identity,int epoch)
            {this.owner=owner;this.game=game;this.component=component;SaveIdentity=identity;mainThread=owner.mainThreadId;lineage=component.SaveId;}
            internal void Invalidate() { Interlocked.Exchange(ref invalid,1); }
            internal bool MatchesLive() => Volatile.Read(ref invalid)==0 && owner.mainThreadId==mainThread &&
                ReferenceEquals(active,owner) && ReferenceEquals(this,Volatile.Read(ref owner.publishedReceiveScope)) &&
                ReferenceEquals(game,Current.Game) && ReferenceEquals(component,owner.attachedSave) &&
                InventoryGameComponent.BelongsTo(component,game) && component.SaveId==lineage &&
                !InventorySaveIdentityPatch.ReceiveScopeSuspended;
            public bool IsCurrent()
            {
                if(Thread.CurrentThread.ManagedThreadId!=mainThread)
                    throw new InvalidOperationException("Inventory receive scope validation requires the main thread.");
                if(!MatchesLive() || owner.GetStatus().SaveIdentity!=SaveIdentity || !MatchesLive()) Invalidate();
                return Volatile.Read(ref invalid)==0;
            }
        }

        public InventoryStatus GetStatus()
        {
            InventoryStatus status = new InventoryStatus();
            if (active != this || mainThreadId == 0)
                status.Availability = InventoryAvailability.Inactive;
            else if (!InventoryGameComponent.BelongsTo(attachedSave, Current.Game))
                status.Availability = InventoryAvailability.UnsavedGame;
            else if (!string.IsNullOrEmpty(Fault) || store == null || store.Faulted)
                status.Availability = InventoryAvailability.ReadOnlyFault;
            else
                status.Availability = InventoryAvailability.Ready;
            Ready(out string reason);
            status.Reason = reason;
            status.JournalIdentity=store!=null&&!store.Faulted?store.Identity:null;
            if (status.Availability != InventoryAvailability.Inactive && store != null && !store.Faulted)
                status.SaveIdentity = Hash(store.Identity);
            return status;
        }

        internal InventoryItemPresentation PresentItem(InventoryEntry entry)
        {
            IInventoryCodec codec;
            lock (registrationLock) codecs.TryGetValue(entry.CodecId, out codec);
            if (codec != null && codec.Version == entry.CodecVersion && codec is IInventoryItemPresentationCodec presenter)
            {
                try
                {
                    InventoryItemPresentation result = presenter.PresentItem(entry.Clone());
                    if (result != null && result.ItemsPerUnit > 0 &&
                        entry.Quantity <= long.MaxValue / result.ItemsPerUnit &&
                        !string.IsNullOrWhiteSpace(result.Label)) return result;
                }
                catch (Exception ex)
                { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/PresentItem", ex);
                    global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/PresentItem/LogObserver", () => { host?.Log?.Invoke("[Inventory] Item presentation failed for '" + entry.CodecId + "': " + ex.GetType().Name, LogLevel.ERROR); });
                }
            }
            return new InventoryItemPresentation { Label = entry.Label ?? entry.CodecId };
        }

        internal InventorySourcePresentation PresentSource(InventoryEntry entry)
        {
            if (entry == null) return null;
            IInventorySourcePresenter presenter = null;
            lock (registrationLock)
                sourcePresenters.TryGetValue(entry.Source ?? string.Empty, out presenter);
            if (presenter != null)
            {
                try
                {
                    InventorySourcePresentation presentation = presenter.Present(entry);
                    if (presentation != null && !string.IsNullOrWhiteSpace(presentation.Summary)) return presentation;
                }
                catch (Exception ex)
                { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/PresentSource", ex);
                    global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/PresentSource/LogObserver", () => { host?.Log?.Invoke("[Inventory] Source presenter failed for '" + entry.Source + "': " + ex.GetType().Name, LogLevel.ERROR); });
                }
            }
            InventorySourcePresentation fallback = new InventorySourcePresentation
            {
                Summary = string.IsNullOrWhiteSpace(entry.Source) ? null : "Phinix_inventory_originSource".Translate(entry.Source).ToString()
            };
            if (!string.IsNullOrWhiteSpace(entry.OriginId))
                fallback.Details.Add("Phinix_inventory_originEventId".Translate(entry.OriginId).ToString());
            if (!string.IsNullOrWhiteSpace(entry.OriginUserId))
                fallback.Details.Add("Phinix_inventory_originUserId".Translate(entry.OriginUserId).ToString());
            return fallback;
        }

        private bool Ready(out string reason)
        {
            if (!ReadyWithoutPending(out reason)) return false;
            return true;
        }

        private bool ReadyWithoutPending(out string reason)
        {
            reason = null;
            if (mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId != mainThreadId)
                reason = "Inventory operations must run on the game main thread.";
            else if (Current.Game == null || attachedSave == null || !InventoryGameComponent.BelongsTo(attachedSave, Current.Game))
                reason = "No inventory save is attached.";
            else if (!string.IsNullOrEmpty(Fault))
                reason = Fault;
            else if (store == null || store.Faulted)
                reason = "Inventory store is unavailable.";
            return reason == null;
        }

        private static string ResolveSaveIdentity()
        {
            if (Current.Game == null) return null;
            string path = InventorySaveIdentityPatch.CurrentPath;
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }

        private static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        private static void DiscardUnclaimed(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing.Spawned || thing.holdingOwner != null) return;
            try { thing.Destroy(); }
            catch (Exception boundaryError53) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/DiscardUnclaimed", boundaryError53);  /* The store still owns the original payload. */ }
        }

        private static bool IsValidMaterialization(InventoryEntry entry, int count, Thing thing)
        {
            if (thing == null || thing.def == null || thing.Destroyed || thing.Spawned ||
                thing.ParentHolder != null || thing.stackCount < 1) return false;
            return entry.AggregationKey == null ? count == 1 : thing.stackCount == count;
        }

        private static InventoryMaterializationResult MaterializationFailure(string reason,
            InventoryFailureCode failureCode)
        {
            return new InventoryMaterializationResult
            {
                Succeeded = false,
                FailureCode = failureCode,
                Reason = reason
            };
        }

        private bool TryGetMaximumStackCount(IInventoryCodec codec, InventoryEntry entry,
            out int maximumStack, out string reason)
        {
            maximumStack = 0;
            reason = null;
            try
            {
                maximumStack = codec.MaximumStackCount(entry);
                if (maximumStack > 0) return true;
                reason = "The item codec returned an invalid stack limit.";
            }
            catch (Exception ex)
            { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/TryGetMaximumStackCount", ex);
                reason = "The item codec could not determine a stack limit: " + "Operation failed; see the error report.";
                global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/TryGetMaximumStackCount/LogObserver", () => { host?.Log?.Invoke("[Inventory] Stack limit lookup failed: " + ex.GetType().Name, LogLevel.ERROR); });
            }
            return false;
        }

        private void RaiseInventoryChanged()
        {
            RaiseEvent(InventoryChanged);
        }

        private void RaiseAvailabilityChanged()
        {
            RaiseEvent(AvailabilityChanged);
        }

        private void RaiseEvent(EventHandler handlers)
        {
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { global::Phinix.InventoryExtension.InventoryDiagnostics.Report("BuiltInInventoryClientExtension/RaiseEvent", ex);  global::Phinix.InventoryExtension.InventoryDiagnostics.Cleanup("BuiltInInventoryClientExtension/RaiseEvent/LogObserver", () => { host?.Log?.Invoke("[Inventory] Extension event subscriber failed: " + ex.GetType().Name, LogLevel.ERROR); }); }
            }
        }

        private sealed class InventoryRegistration : IDisposable
        {
            private Action dispose;
            public InventoryRegistration(Action dispose) { this.dispose = dispose; }
            public void Dispose()
            {
                Action action = Interlocked.Exchange(ref dispose, null);
                action?.Invoke();
            }
        }
    }
}
