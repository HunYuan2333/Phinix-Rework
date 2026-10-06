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
    public sealed class BuiltInInventoryClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule,
        IInventoryApi, IInventoryRegistrationApi, IInventoryDepositApi, IInventoryReadApi, IInventoryExtractionApi,
        IInventoryReservationApi
    {
        private static BuiltInInventoryClientExtension active;
        private readonly Dictionary<string, IInventoryCodec> codecs = new Dictionary<string, IInventoryCodec>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IInventorySourcePresenter> sourcePresenters = new Dictionary<string, IInventorySourcePresenter>(StringComparer.OrdinalIgnoreCase);
        private readonly object registrationLock = new object();
        private readonly InventoryLedger ledger = new InventoryLedger();
        private InventoryJournal journal;
        private InventoryGameComponent attachedSave;
        private string attachedIdentity;
        private ExtensionHostContext host;
        private IClientSettingsContext settings;
        private IClientMainThreadDispatcher dispatcher;
        private IClientShellEventStream shellEvents;
        private IClientWindowService windowService;
        private bool deliveryChoiceOpen;
        private volatile int mainThreadId;
        private string fault;
        private Harmony harmony;

        public event EventHandler InventoryChanged;
        public event EventHandler AvailabilityChanged;

        public string ExtensionId => "builtin.inventory";
        internal static bool IsAttached(InventoryGameComponent component) => active != null && active.attachedSave == component;
        internal static string ExportSnapshot() => FrameworkSerialization.SerializePayload(active.ledger.Export());

        public void Register(IExtensionBuilder builder)
        {
            InventorySettingsPanel settingsPanel = new InventorySettingsPanel();
            builder.RegisterApi<IInventoryApi>(this);
            builder.RegisterApi<IInventoryRegistrationApi>(this);
            builder.RegisterApi<IInventoryDepositApi>(this);
            builder.RegisterApi<IInventoryReadApi>(this);
            builder.RegisterApi<IInventoryExtractionApi>(this);
            builder.RegisterApi<IInventoryReservationApi>(this);
            builder.RegisterApi<IMainTabProvider>(new InventoryTab(this));
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanel);
            builder.RegisterApi<IClientQuickSettingsPanelProvider>(settingsPanel);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            host = hostContext;
            settings = hostContext.GetRequiredService<IClientSettingsContext>();
            active = this;
            harmony = new Harmony("phinix.inventory.save-identity");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            dispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            shellEvents = hostContext.GetRequiredService<IClientShellEventStream>();
            windowService = hostContext.GetRequiredService<IClientWindowService>();
            shellEvents.MainWindowOpened += OnMainWindowOpened;
            dispatcher.Enqueue(() =>
            {
                if (!ReferenceEquals(active, this)) return;
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
            journal?.Dispose();
            journal = null;
            attachedSave = null;
            attachedIdentity = null;
            codecs.Clear();
            sourcePresenters.Clear();
            harmony?.UnpatchAll("phinix.inventory.save-identity");
            harmony = null;
            InventorySaveIdentityPatch.Clear();
            if (active == this) active = null;
            mainThreadId = 0;
            dispatcher = null;
            if (shellEvents != null) shellEvents.MainWindowOpened -= OnMainWindowOpened;
            shellEvents = null;
            windowService = null;
            deliveryChoiceOpen = false;
            host = null;
            settings = null;
            RaiseAvailabilityChanged();
        }

        private void OnMainWindowOpened(object sender, EventArgs args)
        {
            string current = settings?.Get<string>(InventoryDeliveryPreference.SettingKey, null);
            if (InventoryDeliveryPreference.IsValid(current) || deliveryChoiceOpen || windowService == null) return;
            deliveryChoiceOpen = true;
            windowService.Open(new InventoryDeliveryChoiceDialog(
                SetDeliveryPreference,
                () => deliveryChoiceOpen = false));
        }

        private void SetDeliveryPreference(string value)
        {
            settings?.Set(InventoryDeliveryPreference.SettingKey, value);
            deliveryChoiceOpen = false;
        }

        internal static void AttachSave(InventoryGameComponent component)
        {
            active?.QueueAttach(component);
        }

        internal static void RefreshAfterSave()
        {
            if (active != null && InventoryGameComponent.Current != null)
                active.QueueAttach(InventoryGameComponent.Current);
        }

        private void QueueAttach(InventoryGameComponent component)
        {
            if (component == null || dispatcher == null) return;
            if (mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                Attach(component);
                return;
            }
            dispatcher.Enqueue(() =>
            {
                if (!ReferenceEquals(active, this)) return;
                BindGameMainThread();
                Attach(component);
            });
        }

        private void BindGameMainThread()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void Attach(InventoryGameComponent component)
        {
            string identity = ResolveSaveIdentity();
            if (component == null || (component == attachedSave && identity == attachedIdentity && journal != null)) return;
            journal?.Dispose();
            journal = null;
            attachedSave = component;
            attachedIdentity = identity;
            fault = null;
            try
            {
                ledger.Restore(component.ReadSnapshot());
                if (string.IsNullOrEmpty(attachedIdentity) || !File.Exists(attachedIdentity) ||
                    string.IsNullOrEmpty(component.SaveId))
                {
                    fault = "Save file identity is unavailable. Save this game before using inventory.";
                    return;
                }
                string key = Hash(component.SaveId + "|" + attachedIdentity);
                string path = host.GetStoragePath(ExtensionId, key + ".journal");
                journal = new InventoryJournal(path, ledger.Export());
                if (journal.Faulted)
                {
                    fault = (journal.FaultReason ?? "Inventory journal is damaged or locked.") + " Inventory is read only.";
                    host?.Log?.Invoke("[Inventory] Journal load failed: " + journal.FaultReason +
                        " (snapshot sequence " + ledger.Sequence + ", journal " + path + ")", LogLevel.ERROR);
                }
            }
            catch (Exception ex)
            {
                fault = "Inventory could not load safely: " + ex.Message;
                host?.Log?.Invoke("[Inventory] " + ex, LogLevel.ERROR);
            }
            finally
            {
                RaiseAvailabilityChanged();
            }
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
                if (!journal.TryAppend(proposed))
                    return new InventoryDepositResult { Status = InventoryDepositStatus.Unavailable, FailureCode = InventoryFailureCode.PersistenceFailed, Reason = "Inventory journal did not commit." };
                ledger.Publish(proposed);
                RaiseInventoryChanged();
                return result;
            }
            catch (Exception ex)
            {
                host?.Log?.Invoke("[Inventory] Deposit failed: " + ex, LogLevel.ERROR);
                return new InventoryDepositResult { Status = InventoryDepositStatus.Rejected, FailureCode = InventoryFailureCode.InvalidRequest, Reason = ex.Message };
            }
        }

        public InventoryDepositResult CheckDeposit(InventoryDeposit deposit)
        {
            if (!ReadyWithoutPending(out string reason))
                return new InventoryDepositResult { Status = InventoryDepositStatus.Unavailable, FailureCode = InventoryFailureCode.Unavailable, Reason = reason };
            try { return ledger.CheckDeposit(deposit); }
            catch (Exception ex)
            {
                host?.Log?.Invoke("[Inventory] Deposit receipt check failed: " + ex, LogLevel.ERROR);
                return new InventoryDepositResult { Status = InventoryDepositStatus.Rejected, FailureCode = InventoryFailureCode.InvalidRequest, Reason = ex.Message };
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
                if (!journal.TryAppend(proposed))
                    return new InventoryReservationResult
                    {
                        Status = InventoryReservationResultStatus.Unavailable,
                        FailureCode = InventoryFailureCode.PersistenceFailed,
                        Reason = "Inventory journal did not persist the reservation."
                    };
                ledger.Publish(proposed);
                RaiseInventoryChanged();
                return result;
            }
            catch (Exception ex)
            {
                host?.Log?.Invoke("[Inventory] Reservation failed: " + ex, LogLevel.ERROR);
                return new InventoryReservationResult
                {
                    Status = InventoryReservationResultStatus.Rejected,
                    FailureCode = InventoryFailureCode.InvalidRequest,
                    Reason = ex.Message
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
            {
                host?.Log?.Invoke("[Inventory] Preview materialization failed: " + ex, LogLevel.WARNING);
                return MaterializationFailure(ex.Message, InventoryFailureCode.CodecUnavailable);
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
            {
                foreach (Thing thing in materialized) DiscardUnclaimed(thing);
                host?.Log?.Invoke("[Inventory] Reservation materialization failed: " + ex, LogLevel.ERROR);
                return MaterializationFailure(ex.Message, InventoryFailureCode.CodecUnavailable);
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
                if (!journal.TryAppend(proposed))
                    return new InventoryReservationResolutionResult
                    {
                        Succeeded = false,
                        FailureCode = InventoryFailureCode.PersistenceFailed,
                        Reason = "Inventory journal did not persist the reservation result.",
                        Reservation = result.Reservation
                    };
                ledger.Publish(proposed);
                RaiseInventoryChanged();
                return result;
            }
            catch (Exception ex)
            {
                host?.Log?.Invoke("[Inventory] Reservation resolution failed: " + ex, LogLevel.ERROR);
                return new InventoryReservationResolutionResult
                {
                    Succeeded = false,
                    FailureCode = InventoryFailureCode.InvalidRequest,
                    Reason = ex.Message
                };
            }
        }

        public bool TryExtract(string entryId, long quantity, out string reason)
        {
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

            long delivered = 0;
            while (delivered < quantity)
            {
                long stackQuantity = Math.Min(quantity - delivered, maximumStack);
                if (!TryExtractSingleStack(entryId, stackQuantity, null, -1, out string stackReason))
                {
                    reason = delivered == 0
                        ? stackReason
                        : "Phinix_inventory_extractPartial".Translate(delivered, quantity, stackReason).ToString();
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
            {
                reason = "Item could not be restored: " + ex.Message;
                host?.Log?.Invoke("[Inventory] Materialize failed: " + ex, LogLevel.ERROR);
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
            if (!journal.TryAppend(reserved))
            {
                DiscardUnclaimed(thing);
                reason = "Inventory journal did not reserve the extraction.";
                return false;
            }
            ledger.Publish(reserved);
            try
            {
                InventoryDeliveryResult delivery;
                if (codec is IInventoryVerifiedDeliveryCodec verifiedCodec)
                    delivery = verifiedCodec.DeliverVerified(thing, map, spot);
                else
                {
                    codec.Deliver(thing, map, spot);
                    delivery = InventoryDropDelivery.InspectCustody(new[] { thing }, map, spot);
                }

                if (delivery == null || delivery.Status == InventoryDeliveryStatus.Uncertain)
                {
                    reason = delivery?.Reason ?? "Delivery status is uncertain; reconcile the pending extraction.";
                    return false;
                }
                if (delivery.Status == InventoryDeliveryStatus.Rejected)
                    return RestoreRejectedDelivery(thing, delivery.Reason, out reason);

                InventoryState completed = ledger.PrepareExtractionResolution(true);
                if (!journal.TryAppend(completed))
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
            {
                host?.Log?.Invoke("[Inventory] Delivery failed: " + ex, LogLevel.ERROR);
                InventoryDeliveryResult custody = InventoryDropDelivery.InspectCustody(new[] { thing }, map, spot);
                if (custody.Status == InventoryDeliveryStatus.Rejected)
                    return RestoreRejectedDelivery(thing, ex.Message, out reason);
                reason = "Delivery status is uncertain; reconcile the pending extraction.";
                return false;
            }
        }

        private bool RestoreRejectedDelivery(Thing thing, string deliveryReason, out string reason)
        {
            InventoryState restored = ledger.PrepareExtractionResolution(false);
            if (restored == null || !journal.TryAppend(restored))
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

        internal bool HasPendingRecovery => journal != null && journal.HasPendingRecovery;
        internal bool HasPendingExtraction => ledger.PendingExtraction != null;
        internal string Fault => fault ?? (journal != null && journal.Faulted
            ? (journal.FaultReason ?? "Inventory journal is unavailable.") + " Inventory is read only."
            : null);
        internal long Version => ledger.Sequence;

        public IReadOnlyList<InventorySchedule> GetSchedules() => ledger.ScheduleSnapshot();

        public bool SetDailySchedule(string entryId, long quantityPerRun, int dayTick, out string reason)
        {
            if (!Ready(out reason)) return false;
            InventoryEntry entry = ledger.AvailableSnapshot().FirstOrDefault(candidate => candidate.EntryId == entryId);
            if (entry == null || !codecs.TryGetValue(entry.CodecId, out IInventoryCodec codec) ||
                quantityPerRun < 1 || quantityPerRun > entry.Quantity ||
                !TryGetMaximumStackCount(codec, entry, out int maximumStack, out reason) || maximumStack < 1)
            {
                reason = "Daily quantity must fit the available balance.";
                return false;
            }
            InventoryState proposed = ledger.PrepareSchedule(entryId, quantityPerRun, dayTick, false);
            if (proposed == null || !journal.TryAppend(proposed))
            {
                reason = "Daily schedule could not be saved.";
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
            if (proposed == null || !journal.TryAppend(proposed)) return false;
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
                    if (due == null || !journal.TryAppend(due)) return;
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

        internal bool ApproveRecovery()
        {
            if (journal == null || journal.Faulted || !journal.HasPendingRecovery) return false;
            InventoryState recovered = journal.ApproveRecovery();
            if (recovered == null) return false;
            ledger.Restore(recovered);
            RaiseInventoryChanged();
            RaiseAvailabilityChanged();
            return true;
        }

        internal bool RejectRecovery()
        {
            bool rejected = journal != null && !journal.Faulted && journal.HasPendingRecovery && journal.RejectRecovery();
            if (rejected) RaiseAvailabilityChanged();
            return rejected;
        }

        internal bool ResolvePendingExtraction(bool delivered)
        {
            if (!ReadyWithoutPending(out _)) return false;
            InventoryState resolved = ledger.PrepareExtractionResolution(delivered);
            if (resolved == null || !journal.TryAppend(resolved)) return false;
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

        public InventoryStatus GetStatus()
        {
            InventoryStatus status = new InventoryStatus();
            if (active != this || mainThreadId == 0)
                status.Availability = InventoryAvailability.Inactive;
            else if (Current.Game == null || attachedSave == null || string.IsNullOrEmpty(attachedIdentity) ||
                !File.Exists(attachedIdentity))
                status.Availability = InventoryAvailability.UnsavedGame;
            else if (!string.IsNullOrEmpty(Fault) || journal == null || journal.Faulted)
                status.Availability = InventoryAvailability.ReadOnlyFault;
            else if (journal.HasPendingRecovery)
                status.Availability = InventoryAvailability.RecoveryRequired;
            else if (ledger.PendingExtraction != null)
                status.Availability = InventoryAvailability.ExtractionReconciliationRequired;
            else
                status.Availability = InventoryAvailability.Ready;
            Ready(out string reason);
            status.Reason = reason;
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
                {
                    host?.Log?.Invoke("[Inventory] Item presentation failed for '" + entry.CodecId + "': " + ex, LogLevel.ERROR);
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
                {
                    host?.Log?.Invoke("[Inventory] Source presenter failed for '" + entry.Source + "': " + ex, LogLevel.ERROR);
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
            if (ledger.PendingExtraction != null)
            {
                reason = "An earlier extraction needs reconciliation.";
                return false;
            }
            return true;
        }

        private bool ReadyWithoutPending(out string reason)
        {
            reason = null;
            if (mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId != mainThreadId)
                reason = "Inventory operations must run on the game main thread.";
            else if (Current.Game == null || attachedSave == null || !ReferenceEquals(attachedSave, InventoryGameComponent.Current))
                reason = "No inventory save is attached.";
            else if (!string.Equals(attachedIdentity, ResolveSaveIdentity(), StringComparison.Ordinal))
                reason = "Save file changed; reload before using inventory.";
            else if (!File.Exists(attachedIdentity))
                reason = "Save file is unavailable; save before using inventory.";
            else if (!string.IsNullOrEmpty(Fault))
                reason = Fault;
            else if (journal == null || journal.Faulted)
                reason = "Inventory journal is unavailable.";
            else if (journal.HasPendingRecovery)
                reason = "Journal recovery requires your approval.";
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
            catch (Exception) { /* The journal still owns the original payload. */ }
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
            {
                reason = "The item codec could not determine a stack limit: " + ex.Message;
                host?.Log?.Invoke("[Inventory] Stack limit lookup failed: " + ex, LogLevel.ERROR);
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
                catch (Exception ex) { host?.Log?.Invoke("[Inventory] Extension event subscriber failed: " + ex, LogLevel.ERROR); }
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
