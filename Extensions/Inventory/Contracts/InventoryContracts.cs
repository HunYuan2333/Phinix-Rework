using System;
using System.Collections.Generic;

namespace Phinix.InventoryExtension
{
    public static class InventoryContract
    {
        public const int Version = 5;
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

    public sealed class InventoryDeliveryResult
    {
        public InventoryDeliveryStatus Status { get; set; }
        public string Reason { get; set; }
        public Verse.Map Map { get; set; }
        public Verse.IntVec3 DropSpot { get; set; }
        public bool Succeeded => Status == InventoryDeliveryStatus.Delivered;
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

    public sealed class InventoryMaterializationResult
    {
        public bool Succeeded { get; set; }
        public InventoryFailureCode FailureCode { get; set; }
        public string Reason { get; set; }
        public IList<Verse.Thing> Things { get; set; } = new List<Verse.Thing>();
    }

    /// <summary>Inventory invokes codecs on the RimWorld main thread only.</summary>
    public interface IInventoryCodec
    {
        string CodecId { get; }
        int Version { get; }
        bool CanStore(InventoryItem item);
        // Null means the payload cannot be split or combined without losing state.
        string GetAggregationKey(InventoryItem item);
        int MaximumStackCount(InventoryEntry entry);
        Verse.Thing Materialize(InventoryEntry entry, int count);
        // Throw if delivery fails or is uncertain; inventory retains the reservation.
        void Deliver(Verse.Thing thing, Verse.Map map, Verse.IntVec3 dropSpot);
    }

    /// <summary>
    /// Additive delivery contract for codecs that can report ownership transfer.
    /// Older codecs remain supported and are verified by the inventory host after
    /// their legacy Deliver call returns.
    /// </summary>
    public interface IInventoryVerifiedDeliveryCodec
    {
        InventoryDeliveryResult DeliverVerified(Verse.Thing thing, Verse.Map map, Verse.IntVec3 dropSpot);
    }

    public static class InventoryDropDelivery
    {
        public static bool TryResolveTradeDropTarget(Verse.Map map, out Verse.IntVec3 dropSpot, out string reason)
        {
            dropSpot = Verse.IntVec3.Invalid;
            if (map == null || map.Disposed)
            {
                reason = "The delivery map is unavailable.";
                return false;
            }
            if (Verse.Current.Game != null && Verse.Current.Game.Gravship != null)
            {
                reason = "Wait until the gravship has finished landing before requesting a delivery.";
                return false;
            }

            try
            {
                Verse.IntVec3 requested = RimWorld.DropCellFinder.TradeDropSpot(map);
                if (!requested.IsValid || !IsInBounds(requested, map) ||
                    !RimWorld.DropCellFinder.TryFindDropSpotNear(requested, map, out dropSpot,
                        allowFogged: true, canRoofPunch: false))
                {
                    dropSpot = Verse.IntVec3.Invalid;
                    reason = "No valid drop-pod landing cell is available.";
                    return false;
                }
            }
            catch (Exception exception)
            {
                reason = "A safe drop-pod landing cell could not be resolved: " + exception.Message;
                return false;
            }

            reason = null;
            return true;
        }

        public static InventoryDeliveryResult DeliverThings(
            IEnumerable<Verse.Thing> things, Verse.Map map, Verse.IntVec3 dropSpot)
        {
            List<Verse.Thing> materialized = things == null
                ? new List<Verse.Thing>()
                : new List<Verse.Thing>(things);
            if (materialized.Count == 0 || materialized.Exists(thing => thing == null || thing.def == null))
                return Rejected("There are no valid items to deliver.", map, dropSpot);
            if (map == null || map.Disposed || !dropSpot.IsValid || !IsInBounds(dropSpot, map))
                return Rejected("The delivery target is unavailable.", map, dropSpot);
            if (Verse.Current.Game != null && Verse.Current.Game.Gravship != null)
                return Rejected("Wait until the gravship has finished landing before requesting a delivery.", map, dropSpot);

            try
            {
                RimWorld.DropPodUtility.DropThingsNear(dropSpot, map, materialized, canRoofPunch: false);
            }
            catch (Exception exception)
            {
                InventoryDeliveryResult custody = InspectCustody(materialized, map, dropSpot);
                custody.Status = custody.Status == InventoryDeliveryStatus.Rejected
                    ? InventoryDeliveryStatus.Rejected
                    : InventoryDeliveryStatus.Uncertain;
                custody.Reason = "Drop-pod delivery threw an exception: " + exception.Message;
                return custody;
            }

            return InspectCustody(materialized, map, dropSpot);
        }

        public static InventoryDeliveryResult InspectCustody(
            IEnumerable<Verse.Thing> things, Verse.Map map, Verse.IntVec3 dropSpot)
        {
            bool anyCustody = false;
            bool allOnTargetMap = true;
            bool anyDestroyed = false;
            foreach (Verse.Thing thing in things ?? new Verse.Thing[0])
            {
                if (thing == null || thing.Destroyed)
                {
                    anyDestroyed = true;
                    allOnTargetMap = false;
                    continue;
                }
                bool hasCustody = thing.ParentHolder != null || thing.SpawnedOrAnyParentSpawned;
                anyCustody |= hasCustody;
                if (!hasCustody || thing.MapHeld != map) allOnTargetMap = false;
            }

            if (anyCustody && allOnTargetMap && !anyDestroyed)
                return Delivered(map, dropSpot);
            if (!anyCustody && !anyDestroyed)
                return Rejected("The drop-pod API did not take custody of the items.", map, dropSpot);
            return new InventoryDeliveryResult
            {
                Status = InventoryDeliveryStatus.Uncertain,
                Reason = "Item custody could not be verified after drop-pod delivery.",
                Map = map,
                DropSpot = dropSpot
            };
        }

        private static InventoryDeliveryResult Delivered(Verse.Map map, Verse.IntVec3 dropSpot)
        {
            return new InventoryDeliveryResult
            {
                Status = InventoryDeliveryStatus.Delivered,
                Map = map,
                DropSpot = dropSpot
            };
        }

        private static bool IsInBounds(Verse.IntVec3 cell, Verse.Map map)
        {
            return map != null && cell.x >= 0 && cell.z >= 0 &&
                   cell.x < map.Size.x && cell.z < map.Size.z;
        }

        private static InventoryDeliveryResult Rejected(string reason, Verse.Map map, Verse.IntVec3 dropSpot)
        {
            return new InventoryDeliveryResult
            {
                Status = InventoryDeliveryStatus.Rejected,
                Reason = reason,
                Map = map,
                DropSpot = dropSpot
            };
        }
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
