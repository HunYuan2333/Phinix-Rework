using System;
using System.Collections.Generic;

namespace Phinix.InventoryExtension
{
    // Immutable owner projection for actions; quantities are codec units, not Host-inferred items.
    public sealed class InventoryPresentationActionContext
    {
        public const string PointId = "builtin.inventory.actions";
        public const string DeliveryChoicePointId = "builtin.inventory.delivery-choice.actions";
        public const string SettingsPointId = "builtin.inventory.settings.actions";
        public InventoryPresentationActionContext(long version, string mode, string entryId, long availableUnits,
            long itemsPerUnit, string quantityText, string dayTickText, string groupKey = null, int groupRecords = 0)
        { Version = version; Mode = mode; EntryId = entryId; AvailableUnits = availableUnits; ItemsPerUnit = itemsPerUnit;
          QuantityText = quantityText; DayTickText = dayTickText; GroupKey = groupKey; GroupRecords = groupRecords; }
        public string GroupKey { get; }
        public int GroupRecords { get; }
        public long Version { get; }
        public string Mode { get; }
        public string EntryId { get; }
        public long AvailableUnits { get; }
        public long ItemsPerUnit { get; }
        public string QuantityText { get; }
        public string DayTickText { get; }
    }

    // Runtime queue only: every legal stack uses the existing durable extraction transaction.
    public interface IInventoryBatchExtractionApi
    {
        bool StartBatchExtraction(IReadOnlyList<InventoryBatchItem> items, long expectedVersion, out string reason);
        InventoryBatchProgress BatchProgress { get; }
        void StopBatchExtraction();
    }
    public sealed class InventoryBatchItem
    {
        public InventoryBatchItem(string entryId, long quantity) { EntryId = entryId; Quantity = quantity; }
        public string EntryId { get; }
        public long Quantity { get; }
    }
    public sealed class InventoryBatchProgress
    {
        public InventoryBatchProgress(bool running, int records, int completedRecords, long completedUnits, string reason)
        { Running = running; Records = records; CompletedRecords = completedRecords; CompletedUnits = completedUnits; Reason = reason; }
        public bool Running { get; }
        public int Records { get; }
        public int CompletedRecords { get; }
        public long CompletedUnits { get; }
        public string Reason { get; }
    }

    public static class InventoryContract
    {
        public const int Version = 6;
    }

    public static class InventoryDeliveryPreference
    {
        public const string SettingKey = "inventory.deliveryMode";
        public const string DirectDrop = "direct";
        public const string Inventory = "inventory";

        public static bool IsValid(string value)
        {
            return string.Equals(value, DirectDrop, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, Inventory, StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesInventory(string value)
        {
            return string.Equals(value, Inventory, StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum InventoryAvailability
    {
        Inactive,
        UnsavedGame,
        Ready,
        RecoveryRequired,
        ExtractionReconciliationRequired,
        ReadOnlyFault
    }

    public enum InventoryFailureCode
    {
        None,
        InvalidRequest,
        Unavailable,
        Conflict,
        CodecUnavailable,
        CapacityExceeded,
        PersistenceFailed,
        DeliveryFailed,
        DeliveryUncertain
    }

    public sealed class InventoryCapabilities
    {
        public int ContractVersion { get; set; }
        public bool SupportsProvenance { get; set; }
        public bool SupportsScopedRegistration { get; set; }
        public bool SupportsSourcePresentation { get; set; }
        public bool SupportsChangeEvents { get; set; }
        public bool SupportsVerifiedDelivery { get; set; }
        public bool SupportsReservations { get; set; }
        public bool SupportsItemPresentation { get; set; }
        public int MaximumEntryCount { get; set; }
        public int MaximumDepositItemCount { get; set; }
        public int MaximumItemPayloadBytes { get; set; }
        public int MaximumDepositPayloadBytes { get; set; }
    }

    public sealed class InventoryStatus
    {
        public InventoryAvailability Availability { get; set; }
        public string Reason { get; set; }
        public bool CanWrite => Availability == InventoryAvailability.Ready;
    }

    public sealed class InventorySourcePresentation
    {
        public string Summary { get; set; }
        public IList<string> Details { get; set; } = new List<string>();
    }

    /// <summary>Display-only metadata. Grouping never authorizes payload merging or partial extraction.</summary>
    public sealed class InventoryItemPresentation
    {
        public string Label { get; set; }
        // Optional codec-provided Def name for a base UI icon. Never infer it from Label or GroupKey.
        // Display only; it does not authorize decoding, materialization or extraction.
        public string IconDefName { get; set; }
        // Stable, non-localized identity within this codec (for example def, stuff, quality).
        // Null keeps this entry separate. Do not infer equivalence from a localized label.
        public string GroupKey { get; set; }
        public long ItemsPerUnit { get; set; } = 1;
    }

    /// <summary>Optional codec capability, called on the game thread when UI caches are refreshed.
    /// Read metadata only: do not materialize Things or change the entry.</summary>
    public interface IInventoryItemPresentationCodec
    {
        InventoryItemPresentation PresentItem(InventoryEntry entry);
    }

    /// <summary>Opaque, versioned item data. A producer must retain every byte needed to restore the item.</summary>
    public sealed class InventoryItem
    {
        public string CodecId { get; set; }
        public int CodecVersion { get; set; }
        public byte[] Payload { get; set; }
        public long Quantity { get; set; }
        public string Label { get; set; }

        public InventoryItem Clone()
        {
            return new InventoryItem
            {
                CodecId = CodecId,
                CodecVersion = CodecVersion,
                Payload = Payload == null ? null : (byte[])Payload.Clone(),
                Quantity = Quantity,
                Label = Label
            };
        }
    }

    public sealed class InventoryDeposit
    {
        public string DepositId { get; set; }
        public string Source { get; set; }
        // Stable, non-localized provenance. Producers should use a durable event ID
        // and only set the user fields when another player is the source.
        public string OriginKind { get; set; }
        public string OriginId { get; set; }
        public string OriginUserId { get; set; }
        public string OriginDisplayName { get; set; }
        public IList<InventoryItem> Items { get; set; }
    }

    public enum InventoryDepositStatus
    {
        Committed,
        AlreadyCommitted,
        Conflict,
        Rejected,
        Unavailable,
        NotCommitted
    }

    public sealed class InventoryDepositResult
    {
        public InventoryDepositStatus Status { get; set; }
        public InventoryFailureCode FailureCode { get; set; }
        public string Reason { get; set; }
        public bool Succeeded => Status == InventoryDepositStatus.Committed || Status == InventoryDepositStatus.AlreadyCommitted;
    }

    public enum InventoryDeliveryStatus
    {
        Delivered,
        Rejected,
        Uncertain
    }

    public sealed class InventoryEntry
    {
        public string EntryId { get; set; }
        public string CodecId { get; set; }
        public int CodecVersion { get; set; }
        public byte[] Payload { get; set; }
        public long Quantity { get; set; }
        public string Label { get; set; }
        public string Source { get; set; }
        public string OriginKind { get; set; }
        public string OriginId { get; set; }
        public string OriginUserId { get; set; }
        public string OriginDisplayName { get; set; }
        public string AggregationKey { get; set; }

        public InventoryEntry Clone()
        {
            return new InventoryEntry
            {
                EntryId = EntryId,
                CodecId = CodecId,
                CodecVersion = CodecVersion,
                Payload = Payload == null ? null : (byte[])Payload.Clone(),
                Quantity = Quantity,
                Label = Label,
                Source = Source,
                OriginKind = OriginKind,
                OriginId = OriginId,
                OriginUserId = OriginUserId,
                OriginDisplayName = OriginDisplayName,
                AggregationKey = AggregationKey
            };
        }
    }

    public sealed class InventorySchedule
    {
        public string EntryId { get; set; }
        public long QuantityPerRun { get; set; }
        public int DayTick { get; set; }
        public int LastTriggeredDay { get; set; }
        public int PendingDueDay { get; set; }
        public long RemainingQuantity { get; set; }

        public InventorySchedule Clone()
        {
            return new InventorySchedule
            {
                EntryId = EntryId, QuantityPerRun = QuantityPerRun, DayTick = DayTick,
                LastTriggeredDay = LastTriggeredDay, PendingDueDay = PendingDueDay,
                RemainingQuantity = RemainingQuantity
            };
        }
    }

    public enum InventoryReservationState
    {
        Reserved,
        Uncertain,
        Committed,
        Restored
    }

    public sealed class InventoryReservationLine
    {
        public string EntryId { get; set; }
        public long Quantity { get; set; }

        public InventoryReservationLine Clone()
        {
            return new InventoryReservationLine { EntryId = EntryId, Quantity = Quantity };
        }
    }

    public sealed class InventoryReservationRequest
    {
        public string OperationId { get; set; }
        public string OwnerExtensionId { get; set; }
        public string Purpose { get; set; }
        public IList<InventoryReservationLine> Lines { get; set; }
    }

    public sealed class InventoryReservedItem
    {
        public string EntryId { get; set; }
        public long Quantity { get; set; }
        public InventoryEntry Entry { get; set; }
    }

    public sealed class InventoryReservation
    {
        public string ReservationId { get; set; }
        public string OperationId { get; set; }
        public string OwnerExtensionId { get; set; }
        public string Purpose { get; set; }
        public InventoryReservationState State { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string Evidence { get; set; }
        public IList<InventoryReservedItem> Items { get; set; }
    }

    public enum InventoryReservationResultStatus
    {
        Reserved,
        AlreadyReserved,
        Conflict,
        Rejected,
        Unavailable
    }

    public sealed class InventoryReservationResult
    {
        public InventoryReservationResultStatus Status { get; set; }
        public InventoryFailureCode FailureCode { get; set; }
        public string Reason { get; set; }
        public InventoryReservation Reservation { get; set; }
        public bool Succeeded => Status == InventoryReservationResultStatus.Reserved ||
                                 Status == InventoryReservationResultStatus.AlreadyReserved;
    }

    public enum InventoryReservationResolution
    {
        Commit,
        Restore,
        MarkUncertain
    }

    public sealed class InventoryReservationResolutionResult
    {
        public bool Succeeded { get; set; }
        public InventoryFailureCode FailureCode { get; set; }
        public string Reason { get; set; }
        public InventoryReservation Reservation { get; set; }
    }

    /// <summary>Formats one producer's persisted provenance on the game main thread.</summary>
    public interface IInventorySourcePresenter
    {
        string SourceId { get; }
        InventorySourcePresentation Present(InventoryEntry entry);
    }

    public interface IInventoryRegistrationApi
    {
        IDisposable RegisterCodecScoped(IInventoryCodec codec);
        IDisposable RegisterSourcePresenter(IInventorySourcePresenter presenter);
    }

    public interface IInventoryDepositApi
    {
        InventoryDepositResult CheckDeposit(InventoryDeposit deposit);
        InventoryDepositResult TryDeposit(InventoryDeposit deposit);
    }

    public interface IInventoryReadApi
    {
        event EventHandler InventoryChanged;
        event EventHandler AvailabilityChanged;
        IReadOnlyList<InventoryEntry> GetSnapshot();
        InventoryStatus GetStatus();
        InventoryCapabilities GetCapabilities();
    }

    public interface IInventoryExtractionApi
    {
        bool TryExtract(string entryId, long quantity, out string reason);
        IReadOnlyList<InventorySchedule> GetSchedules();
        bool SetDailySchedule(string entryId, long quantityPerRun, int dayTick, out string reason);
        bool RemoveDailySchedule(string entryId);
    }

    /// <summary>
    /// Reserves client-owned inventory for another plugin. Consumers must commit
    /// only after an authoritative matching success, restore only after a proven
    /// rejection, and retain uncertain operations for reconciliation.
    /// </summary>
    public interface IInventoryReservationApi
    {
        IReadOnlyList<InventoryEntry> GetAvailableSnapshot();
        IReadOnlyList<InventoryReservation> GetReservations();
        InventoryReservationResult TryReserve(InventoryReservationRequest request);
        InventoryMaterializationResult CreatePreview(string entryId);
        InventoryMaterializationResult MaterializeReservation(string reservationId);
        InventoryReservationResolutionResult ResolveReservation(
            string reservationId, InventoryReservationResolution resolution, string evidence);
    }

    public interface IInventoryApi
    {
        void RegisterCodec(IInventoryCodec codec);
        InventoryDepositResult CheckDeposit(InventoryDeposit deposit);
        InventoryDepositResult TryDeposit(InventoryDeposit deposit);
        IReadOnlyList<InventoryEntry> GetSnapshot();
        bool TryExtract(string entryId, long quantity, out string reason);
        IReadOnlyList<InventorySchedule> GetSchedules();
        bool SetDailySchedule(string entryId, long quantityPerRun, int dayTick, out string reason);
        bool RemoveDailySchedule(string entryId);
    }
}
