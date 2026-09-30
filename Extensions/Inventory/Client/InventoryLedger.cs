using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Phinix.InventoryExtension;

namespace Phinix.InventoryExtension.Client
{
    [DataContract]
    internal sealed class InventoryCommit
    {
        [DataMember]
        public string DepositId { get; set; }
        [DataMember]
        public string ContentHash { get; set; }
        [DataMember]
        public int HashVersion { get; set; }
    }

    [DataContract]
    internal sealed class InventoryState
    {
        [DataMember]
        public long Sequence { get; set; }
        [DataMember]
        public List<InventoryEntry> Entries { get; set; } = new List<InventoryEntry>();
        [DataMember]
        public List<InventoryCommit> Commits { get; set; } = new List<InventoryCommit>();
        [DataMember]
        public InventoryPendingExtraction PendingExtraction { get; set; }
        [DataMember]
        public List<InventorySchedule> Schedules { get; set; } = new List<InventorySchedule>();
        [DataMember]
        public List<InventoryReservationRecord> Reservations { get; set; } = new List<InventoryReservationRecord>();
    }

    [DataContract]
    internal sealed class InventoryPendingExtraction
    {
        [DataMember]
        public string EntryId { get; set; }
        [DataMember]
        public long Quantity { get; set; }
        [DataMember]
        public string ScheduleEntryId { get; set; }
        [DataMember]
        public int ScheduleDay { get; set; }
    }

    [DataContract]
    internal sealed class InventoryReservationRecord
    {
        [DataMember]
        public string ReservationId { get; set; }
        [DataMember]
        public string OperationId { get; set; }
        [DataMember]
        public string RequestHash { get; set; }
        [DataMember]
        public string OwnerExtensionId { get; set; }
        [DataMember]
        public string Purpose { get; set; }
        [DataMember]
        public InventoryReservationState State { get; set; }
        [DataMember]
        public DateTime CreatedAtUtc { get; set; }
        [DataMember]
        public DateTime UpdatedAtUtc { get; set; }
        [DataMember]
        public string Evidence { get; set; }
        [DataMember]
        public List<InventoryReservationLine> Lines { get; set; } = new List<InventoryReservationLine>();
    }

    internal sealed class InventoryLedger
    {
        private const int CurrentDepositHashVersion = 3;
        internal const int MaxEntryCount = 10000;
        internal const int MaxCommitCount = 100000;
        internal const int MaxDepositItemCount = 1000;
        internal const int MaxReservationCount = 100000;
        internal const int MaxReservationLineCount = 1000;
        // Stateful trade codecs may carry a complete 4 MiB Scribe payload. The
        // inventory envelope uses JSON/base64, so retain bounded headroom for it.
        internal const int MaxPayloadBytes = 8 * 1024 * 1024;
        internal const int MaxBatchBytes = 32 * 1024 * 1024;
        private InventoryState state = new InventoryState();

        public long Sequence => state.Sequence;
        public InventoryPendingExtraction PendingExtraction => state.PendingExtraction;

        public void Restore(InventoryState restored)
        {
            Validate(restored);
            state = Clone(restored);
        }

        internal static void Validate(InventoryState restored)
        {
            if (restored != null && restored.Reservations == null)
                restored.Reservations = new List<InventoryReservationRecord>();
            if (restored == null || restored.Entries == null || restored.Commits == null ||
                restored.Schedules == null || restored.Sequence < 0 ||
                restored.Entries.Count > MaxEntryCount || restored.Commits.Count > MaxCommitCount ||
                restored.Schedules.Count > MaxEntryCount || restored.Reservations.Count > MaxReservationCount)
                throw new InvalidDataException("Inventory snapshot has invalid bounds.");
            HashSet<string> entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryEntry entry in restored.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.EntryId) || !entryIds.Add(entry.EntryId) ||
                    string.IsNullOrWhiteSpace(entry.CodecId) || entry.CodecVersion < 1 || entry.Quantity < 1 ||
                    entry.Payload == null || entry.Payload.Length == 0 || entry.Payload.Length > MaxPayloadBytes ||
                    entry.Label == null || entry.Label.Length > 200 || entry.Source == null || entry.Source.Length > 100 ||
                    TooLong(entry.OriginKind, 100) || TooLong(entry.OriginId, 200) ||
                    TooLong(entry.OriginUserId, 100) || TooLong(entry.OriginDisplayName, 200))
                    throw new InvalidDataException("Inventory snapshot contains an invalid entry.");
            }
            HashSet<string> commitIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryCommit commit in restored.Commits)
            {
                if (commit == null || string.IsNullOrWhiteSpace(commit.DepositId) ||
                    !commitIds.Add(commit.DepositId) || string.IsNullOrWhiteSpace(commit.ContentHash) ||
                    commit.HashVersion < 0 || commit.HashVersion > CurrentDepositHashVersion)
                    throw new InvalidDataException("Inventory snapshot contains an invalid deposit receipt.");
            }
            HashSet<string> scheduledIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventorySchedule schedule in restored.Schedules)
            {
                if (schedule == null || !entryIds.Contains(schedule.EntryId) ||
                    !scheduledIds.Add(schedule.EntryId) || schedule.QuantityPerRun < 1 ||
                    schedule.DayTick < 0 || schedule.DayTick >= 60000 ||
                    schedule.LastTriggeredDay < -1 || schedule.PendingDueDay < -1 ||
                    schedule.RemainingQuantity < 0 ||
                    (schedule.PendingDueDay < 0 && schedule.RemainingQuantity != 0))
                    throw new InvalidDataException("Inventory snapshot contains an invalid schedule.");
            }
            InventoryPendingExtraction pending = restored.PendingExtraction;
            if (pending != null && (!entryIds.Contains(pending.EntryId) || pending.Quantity < 1 ||
                pending.Quantity > restored.Entries.First(entry => entry.EntryId == pending.EntryId).Quantity ||
                (pending.ScheduleEntryId != null && !scheduledIds.Contains(pending.ScheduleEntryId))))
                throw new InvalidDataException("Inventory snapshot contains an invalid pending extraction.");

            HashSet<string> reservationIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> operationIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, long> lockedByEntry = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (InventoryReservationRecord reservation in restored.Reservations)
            {
                if (reservation == null || string.IsNullOrWhiteSpace(reservation.ReservationId) ||
                    !reservationIds.Add(reservation.ReservationId) || string.IsNullOrWhiteSpace(reservation.OperationId) ||
                    !operationIds.Add(reservation.OperationId) || string.IsNullOrWhiteSpace(reservation.RequestHash) ||
                    string.IsNullOrWhiteSpace(reservation.OwnerExtensionId) || reservation.OwnerExtensionId.Length > 100 ||
                    string.IsNullOrWhiteSpace(reservation.Purpose) || reservation.Purpose.Length > 100 ||
                    reservation.Lines == null || reservation.Lines.Count < 1 ||
                    reservation.Lines.Count > MaxReservationLineCount || TooLong(reservation.Evidence, 500))
                    throw new InvalidDataException("Inventory snapshot contains an invalid reservation.");

                HashSet<string> reservationEntries = new HashSet<string>(StringComparer.Ordinal);
                foreach (InventoryReservationLine line in reservation.Lines)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.EntryId) || line.Quantity < 1 ||
                        !reservationEntries.Add(line.EntryId))
                        throw new InvalidDataException("Inventory snapshot contains an invalid reservation line.");
                    if (reservation.State != InventoryReservationState.Reserved &&
                        reservation.State != InventoryReservationState.Uncertain) continue;
                    if (!entryIds.Contains(line.EntryId))
                        throw new InvalidDataException("An active reservation references a missing entry.");
                    lockedByEntry.TryGetValue(line.EntryId, out long locked);
                    lockedByEntry[line.EntryId] = checked(locked + line.Quantity);
                }
            }
            if (pending != null)
            {
                lockedByEntry.TryGetValue(pending.EntryId, out long locked);
                lockedByEntry[pending.EntryId] = checked(locked + pending.Quantity);
            }
            foreach (KeyValuePair<string, long> locked in lockedByEntry)
            {
                InventoryEntry entry = restored.Entries.First(candidate => candidate.EntryId == locked.Key);
                if (locked.Value > entry.Quantity)
                    throw new InvalidDataException("Inventory reservations exceed the stored balance.");
            }
        }

        public InventoryState Export() => Clone(state);

        public IReadOnlyList<InventoryEntry> Snapshot()
        {
            return state.Entries.Select(entry => entry.Clone()).ToArray();
        }

        public IReadOnlyList<InventoryEntry> AvailableSnapshot()
        {
            Dictionary<string, long> locked = ActiveLockedQuantities();
            return state.Entries.Select(entry =>
            {
                InventoryEntry copy = entry.Clone();
                if (locked.TryGetValue(copy.EntryId, out long quantity)) copy.Quantity -= quantity;
                return copy;
            }).Where(entry => entry.Quantity > 0).ToArray();
        }

        public IReadOnlyList<InventorySchedule> ScheduleSnapshot()
        {
            return state.Schedules.Select(schedule => schedule.Clone()).ToArray();
        }

        public InventoryState PrepareSchedule(string entryId, long quantity, int dayTick, bool remove)
        {
            if (state.PendingExtraction != null || !state.Entries.Any(entry => entry.EntryId == entryId)) return null;
            InventoryState proposed = Clone(state);
            InventorySchedule schedule = proposed.Schedules.FirstOrDefault(item => item.EntryId == entryId);
            if (remove)
            {
                if (schedule == null) return null;
                proposed.Schedules.Remove(schedule);
            }
            else
            {
                if (quantity < 1 || dayTick < 0 || dayTick >= 60000) return null;
                if (schedule == null)
                {
                    schedule = new InventorySchedule { EntryId = entryId, LastTriggeredDay = -1, PendingDueDay = -1 };
                    proposed.Schedules.Add(schedule);
                }
                schedule.QuantityPerRun = quantity;
                schedule.DayTick = dayTick;
                schedule.PendingDueDay = -1;
                schedule.RemainingQuantity = 0;
            }
            proposed.Sequence = checked(state.Sequence + 1);
            return proposed;
        }

        public InventoryState PrepareScheduleDue(string entryId, int day)
        {
            InventorySchedule existing = state.Schedules.FirstOrDefault(item => item.EntryId == entryId);
            if (existing == null || existing.PendingDueDay >= 0 || existing.LastTriggeredDay >= day) return null;
            InventoryState proposed = Clone(state);
            InventorySchedule due = proposed.Schedules.First(item => item.EntryId == entryId);
            due.PendingDueDay = day;
            due.RemainingQuantity = Math.Min(due.QuantityPerRun,
                proposed.Entries.First(entry => entry.EntryId == entryId).Quantity);
            proposed.Sequence = checked(state.Sequence + 1);
            return proposed;
        }

        public InventoryDepositResult PrepareDeposit(InventoryDeposit deposit,
            IDictionary<string, IInventoryCodec> codecs, out InventoryState proposed)
        {
            proposed = null;
            InventoryDepositResult existing = CheckDeposit(deposit);
            if (existing.Status != InventoryDepositStatus.NotCommitted) return existing;

            foreach (InventoryItem item in deposit.Items)
            {
                if (!codecs.TryGetValue(item.CodecId, out IInventoryCodec codec) ||
                    codec.Version != item.CodecVersion || !codec.CanStore(item))
                    return Result(InventoryDepositStatus.Rejected, "Item codec or payload is unavailable.", InventoryFailureCode.CodecUnavailable);
            }
            if (state.Commits.Count >= MaxCommitCount)
                return Result(InventoryDepositStatus.Rejected, "Inventory transaction limit reached.", InventoryFailureCode.CapacityExceeded);

            proposed = Clone(state);
            foreach (InventoryItem item in deposit.Items)
            {
                IInventoryCodec codec = codecs[item.CodecId];
                string key = codec.GetAggregationKey(item);
                if (key != null && key.Length > 200)
                    return Result(InventoryDepositStatus.Rejected, "Aggregation key is too long.");
                if (key == null && item.Quantity != 1)
                    return Result(InventoryDepositStatus.Rejected, "An indivisible item must have quantity one.");

                InventoryEntry existingEntry = key == null ? null : proposed.Entries.FirstOrDefault(entry =>
                    entry.CodecId == item.CodecId && entry.CodecVersion == item.CodecVersion &&
                    entry.AggregationKey == key && BytesEqual(entry.Payload, item.Payload) &&
                    entry.Source == deposit.Source && entry.OriginKind == deposit.OriginKind &&
                    entry.OriginId == deposit.OriginId && entry.OriginUserId == deposit.OriginUserId);
                if (existingEntry != null)
                {
                    try { existingEntry.Quantity = checked(existingEntry.Quantity + item.Quantity); }
                    catch (OverflowException) { return Result(InventoryDepositStatus.Rejected, "Inventory quantity overflow."); }
                    continue;
                }
                if (proposed.Entries.Count >= MaxEntryCount)
                    return Result(InventoryDepositStatus.Rejected, "Inventory entry limit reached.", InventoryFailureCode.CapacityExceeded);
                proposed.Entries.Add(new InventoryEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    CodecId = item.CodecId,
                    CodecVersion = item.CodecVersion,
                    Payload = (byte[])item.Payload.Clone(),
                    Quantity = item.Quantity,
                    Label = item.Label,
                    Source = deposit.Source,
                    OriginKind = deposit.OriginKind,
                    OriginId = deposit.OriginId,
                    OriginUserId = deposit.OriginUserId,
                    OriginDisplayName = deposit.OriginDisplayName,
                    AggregationKey = key
                });
            }
            proposed.Commits.Add(new InventoryCommit
            {
                DepositId = deposit.DepositId,
                ContentHash = HashDeposit(deposit, CurrentDepositHashVersion),
                HashVersion = CurrentDepositHashVersion
            });
            proposed.Sequence = checked(state.Sequence + 1);
            return Result(InventoryDepositStatus.Committed, null);
        }

        public InventoryDepositResult CheckDeposit(InventoryDeposit deposit)
        {
            if (deposit == null || string.IsNullOrWhiteSpace(deposit.DepositId) ||
                deposit.DepositId.Length > 200 || deposit.Items == null ||
                deposit.Items.Count == 0 || deposit.Items.Count > MaxDepositItemCount ||
                string.IsNullOrWhiteSpace(deposit.Source) || deposit.Source.Length > 100 ||
                TooLong(deposit.OriginKind, 100) || TooLong(deposit.OriginId, 200) ||
                TooLong(deposit.OriginUserId, 100) || TooLong(deposit.OriginDisplayName, 200))
                return Result(InventoryDepositStatus.Rejected, "Invalid deposit metadata.");

            int totalBytes = 0;
            foreach (InventoryItem item in deposit.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.CodecId) ||
                    item.CodecId.Length > 100 || item.CodecVersion < 1 || item.Quantity < 1 ||
                    item.Payload == null || item.Payload.Length < 1 || item.Payload.Length > MaxPayloadBytes ||
                    item.Label == null || item.Label.Length > 200)
                    return Result(InventoryDepositStatus.Rejected, "Invalid item metadata or payload.");
                totalBytes = checked(totalBytes + item.Payload.Length);
                if (totalBytes > MaxBatchBytes)
                    return Result(InventoryDepositStatus.Rejected, "Deposit exceeds the batch size limit.", InventoryFailureCode.CapacityExceeded);
            }

            InventoryCommit prior = state.Commits.FirstOrDefault(commit => commit.DepositId == deposit.DepositId);
            if (prior == null) return Result(InventoryDepositStatus.NotCommitted, null);
            string priorHash = HashDeposit(deposit, prior.HashVersion);
            return Result(prior.ContentHash == priorHash ? InventoryDepositStatus.AlreadyCommitted : InventoryDepositStatus.Conflict,
                prior.ContentHash == priorHash ? null : "Deposit ID was reused with different content.",
                prior.ContentHash == priorHash ? InventoryFailureCode.None : InventoryFailureCode.Conflict);
        }

        public InventoryReservationResult PrepareReservation(InventoryReservationRequest request,
            out InventoryState proposed)
        {
            proposed = null;
            string invalidReason = ValidateReservationRequest(request);
            if (invalidReason != null)
                return ReservationResult(InventoryReservationResultStatus.Rejected, invalidReason,
                    InventoryFailureCode.InvalidRequest, null);

            string requestHash = HashReservationRequest(request);
            InventoryReservationRecord prior = state.Reservations.FirstOrDefault(candidate =>
                string.Equals(candidate.OperationId, request.OperationId, StringComparison.Ordinal));
            if (prior != null)
            {
                if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
                    return ReservationResult(InventoryReservationResultStatus.Conflict,
                        "Reservation operation ID was reused with different content.",
                        InventoryFailureCode.Conflict, ToReservation(prior));
                if (prior.State == InventoryReservationState.Committed ||
                    prior.State == InventoryReservationState.Restored)
                    return ReservationResult(InventoryReservationResultStatus.Conflict,
                        "Reservation operation is already finalized.",
                        InventoryFailureCode.Conflict, ToReservation(prior));
                return ReservationResult(InventoryReservationResultStatus.AlreadyReserved, null,
                    InventoryFailureCode.None, ToReservation(prior));
            }
            if (state.Reservations.Count >= MaxReservationCount)
                return ReservationResult(InventoryReservationResultStatus.Rejected,
                    "Inventory reservation limit reached.", InventoryFailureCode.CapacityExceeded, null);

            Dictionary<string, long> locked = ActiveLockedQuantities();
            foreach (InventoryReservationLine line in request.Lines)
            {
                InventoryEntry entry = state.Entries.FirstOrDefault(candidate => candidate.EntryId == line.EntryId);
                locked.TryGetValue(line.EntryId, out long lockedQuantity);
                if (entry == null || line.Quantity > entry.Quantity - lockedQuantity)
                    return ReservationResult(InventoryReservationResultStatus.Rejected,
                        "The requested quantity exceeds the available inventory balance.",
                        InventoryFailureCode.Conflict, null);
            }

            DateTime now = DateTime.UtcNow;
            proposed = Clone(state);
            InventoryReservationRecord record = new InventoryReservationRecord
            {
                ReservationId = Guid.NewGuid().ToString("N"),
                OperationId = request.OperationId,
                RequestHash = requestHash,
                OwnerExtensionId = request.OwnerExtensionId,
                Purpose = request.Purpose,
                State = InventoryReservationState.Reserved,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Lines = request.Lines.Select(line => line.Clone()).ToList()
            };
            proposed.Reservations.Add(record);
            proposed.Sequence = checked(state.Sequence + 1);
            return ReservationResult(InventoryReservationResultStatus.Reserved, null,
                InventoryFailureCode.None, ToReservation(record));
        }

        public InventoryReservationResolutionResult PrepareReservationResolution(string reservationId,
            InventoryReservationResolution resolution, string evidence, out InventoryState proposed)
        {
            proposed = null;
            if (string.IsNullOrWhiteSpace(reservationId) || reservationId.Length > 100 || TooLong(evidence, 500))
                return ResolutionResult(false, "Invalid reservation resolution.",
                    InventoryFailureCode.InvalidRequest, null);
            InventoryReservationRecord existing = state.Reservations.FirstOrDefault(candidate =>
                candidate.ReservationId == reservationId);
            if (existing == null)
                return ResolutionResult(false, "Inventory reservation was not found.",
                    InventoryFailureCode.InvalidRequest, null);

            InventoryReservationState target = resolution == InventoryReservationResolution.Commit
                ? InventoryReservationState.Committed
                : resolution == InventoryReservationResolution.Restore
                    ? InventoryReservationState.Restored
                    : InventoryReservationState.Uncertain;
            if (existing.State == target)
                return ResolutionResult(true, null, InventoryFailureCode.None, ToReservation(existing));
            if (existing.State == InventoryReservationState.Committed ||
                existing.State == InventoryReservationState.Restored)
                return ResolutionResult(false, "Inventory reservation is already finalized.",
                    InventoryFailureCode.Conflict, ToReservation(existing));

            proposed = Clone(state);
            InventoryReservationRecord record = proposed.Reservations.First(candidate =>
                candidate.ReservationId == reservationId);
            if (resolution == InventoryReservationResolution.Commit)
            {
                foreach (InventoryReservationLine line in record.Lines)
                {
                    InventoryEntry entry = proposed.Entries.FirstOrDefault(candidate => candidate.EntryId == line.EntryId);
                    if (entry == null || entry.Quantity < line.Quantity)
                        return ResolutionResult(false, "Reserved inventory no longer matches the ledger.",
                            InventoryFailureCode.Conflict, ToReservation(existing));
                    entry.Quantity -= line.Quantity;
                    if (entry.Quantity == 0)
                    {
                        proposed.Entries.Remove(entry);
                        proposed.Schedules.RemoveAll(schedule => schedule.EntryId == entry.EntryId);
                    }
                }
            }
            record.State = target;
            record.UpdatedAtUtc = DateTime.UtcNow;
            record.Evidence = evidence;
            proposed.Sequence = checked(state.Sequence + 1);
            return ResolutionResult(true, null, InventoryFailureCode.None, ToReservation(record));
        }

        public IReadOnlyList<InventoryReservation> ReservationSnapshot()
        {
            return state.Reservations.Select(ToReservation).ToArray();
        }

        public bool PrepareExtraction(string entryId, long quantity, string scheduleEntryId, int scheduleDay,
            out InventoryState proposed, out InventoryEntry entry)
        {
            proposed = null;
            entry = state.Entries.FirstOrDefault(candidate => candidate.EntryId == entryId)?.Clone();
            long available = entry == null ? 0 : entry.Quantity - GetActiveLockedQuantity(entryId);
            if (state.PendingExtraction != null || entry == null || quantity < 1 || quantity > available) return false;
            proposed = Clone(state);
            proposed.PendingExtraction = new InventoryPendingExtraction
            {
                EntryId = entryId, Quantity = quantity,
                ScheduleEntryId = scheduleEntryId, ScheduleDay = scheduleDay
            };
            proposed.Sequence = checked(state.Sequence + 1);
            return true;
        }

        public InventoryState PrepareExtractionResolution(bool delivered)
        {
            if (state.PendingExtraction == null) return null;
            InventoryState proposed = Clone(state);
            if (delivered)
            {
                InventoryEntry target = proposed.Entries.FirstOrDefault(entry =>
                    entry.EntryId == proposed.PendingExtraction.EntryId);
                if (target == null || target.Quantity < proposed.PendingExtraction.Quantity)
                    throw new InvalidDataException("Pending extraction no longer matches the ledger.");
                target.Quantity -= proposed.PendingExtraction.Quantity;
                if (target.Quantity == 0)
                {
                    proposed.Entries.Remove(target);
                    proposed.Schedules.RemoveAll(schedule => schedule.EntryId == target.EntryId);
                }
                else if (proposed.PendingExtraction.ScheduleEntryId != null)
                {
                    InventorySchedule schedule = proposed.Schedules.FirstOrDefault(candidate =>
                        candidate.EntryId == proposed.PendingExtraction.ScheduleEntryId);
                    if (schedule != null)
                    {
                        schedule.RemainingQuantity = Math.Max(0, schedule.RemainingQuantity - proposed.PendingExtraction.Quantity);
                        if (schedule.RemainingQuantity == 0)
                        {
                            schedule.LastTriggeredDay = proposed.PendingExtraction.ScheduleDay;
                            schedule.PendingDueDay = -1;
                        }
                    }
                }
            }
            proposed.PendingExtraction = null;
            proposed.Sequence = checked(state.Sequence + 1);
            return proposed;
        }

        public void Publish(InventoryState committed)
        {
            if (committed == null || committed.Sequence != state.Sequence + 1)
                throw new InvalidOperationException("Inventory state is not the next committed version.");
            state = committed;
        }

        private static InventoryState Clone(InventoryState source)
        {
            return new InventoryState
            {
                Sequence = source.Sequence,
                Entries = source.Entries.Select(entry => entry.Clone()).ToList(),
                Schedules = source.Schedules.Select(schedule => schedule.Clone()).ToList(),
                Commits = source.Commits.Select(commit => new InventoryCommit
                {
                    DepositId = commit.DepositId,
                    ContentHash = commit.ContentHash,
                    HashVersion = commit.HashVersion
                }).ToList(),
                PendingExtraction = source.PendingExtraction == null ? null : new InventoryPendingExtraction
                {
                    EntryId = source.PendingExtraction.EntryId,
                    Quantity = source.PendingExtraction.Quantity,
                    ScheduleEntryId = source.PendingExtraction.ScheduleEntryId,
                    ScheduleDay = source.PendingExtraction.ScheduleDay
                },
                Reservations = source.Reservations.Select(reservation => new InventoryReservationRecord
                {
                    ReservationId = reservation.ReservationId,
                    OperationId = reservation.OperationId,
                    RequestHash = reservation.RequestHash,
                    OwnerExtensionId = reservation.OwnerExtensionId,
                    Purpose = reservation.Purpose,
                    State = reservation.State,
                    CreatedAtUtc = reservation.CreatedAtUtc,
                    UpdatedAtUtc = reservation.UpdatedAtUtc,
                    Evidence = reservation.Evidence,
                    Lines = reservation.Lines.Select(line => line.Clone()).ToList()
                }).ToList()
            };
        }

        private Dictionary<string, long> ActiveLockedQuantities()
        {
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (InventoryReservationRecord reservation in state.Reservations)
            {
                if (reservation.State != InventoryReservationState.Reserved &&
                    reservation.State != InventoryReservationState.Uncertain) continue;
                foreach (InventoryReservationLine line in reservation.Lines)
                {
                    result.TryGetValue(line.EntryId, out long quantity);
                    result[line.EntryId] = checked(quantity + line.Quantity);
                }
            }
            if (state.PendingExtraction != null)
            {
                result.TryGetValue(state.PendingExtraction.EntryId, out long quantity);
                result[state.PendingExtraction.EntryId] = checked(quantity + state.PendingExtraction.Quantity);
            }
            return result;
        }

        private long GetActiveLockedQuantity(string entryId)
        {
            return ActiveLockedQuantities().TryGetValue(entryId, out long quantity) ? quantity : 0;
        }

        private string ValidateReservationRequest(InventoryReservationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.OperationId) || request.OperationId.Length > 200 ||
                string.IsNullOrWhiteSpace(request.OwnerExtensionId) || request.OwnerExtensionId.Length > 100 ||
                string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 100 ||
                request.Lines == null || request.Lines.Count < 1 || request.Lines.Count > MaxReservationLineCount)
                return "Invalid inventory reservation metadata.";
            HashSet<string> entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryReservationLine line in request.Lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.EntryId) || line.EntryId.Length > 100 ||
                    line.Quantity < 1 || !entryIds.Add(line.EntryId))
                    return "Invalid inventory reservation line.";
            }
            return null;
        }

        private static string HashReservationRequest(InventoryReservationRequest request)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(request.OwnerExtensionId);
                writer.Write(request.Purpose);
                writer.Write(request.Lines.Count);
                foreach (InventoryReservationLine line in request.Lines.OrderBy(item => item.EntryId, StringComparer.Ordinal))
                {
                    writer.Write(line.EntryId);
                    writer.Write(line.Quantity);
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }

        private InventoryReservation ToReservation(InventoryReservationRecord record)
        {
            if (record == null) return null;
            return new InventoryReservation
            {
                ReservationId = record.ReservationId,
                OperationId = record.OperationId,
                OwnerExtensionId = record.OwnerExtensionId,
                Purpose = record.Purpose,
                State = record.State,
                CreatedAtUtc = record.CreatedAtUtc,
                UpdatedAtUtc = record.UpdatedAtUtc,
                Evidence = record.Evidence,
                Items = record.Lines.Select(line => new InventoryReservedItem
                {
                    EntryId = line.EntryId,
                    Quantity = line.Quantity,
                    Entry = state.Entries.FirstOrDefault(entry => entry.EntryId == line.EntryId)?.Clone()
                }).ToList()
            };
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            return left != null && right != null && left.SequenceEqual(right);
        }

        private static bool TooLong(string value, int maximumLength)
        {
            return value != null && value.Length > maximumLength;
        }

        private static string HashDeposit(InventoryDeposit deposit, int version)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(deposit.Source);
                if (version >= 3)
                {
                    writer.Write(deposit.OriginKind ?? string.Empty);
                    writer.Write(deposit.OriginId ?? string.Empty);
                    writer.Write(deposit.OriginUserId ?? string.Empty);
                }
                writer.Write(deposit.Items.Count);
                foreach (InventoryItem item in deposit.Items)
                {
                    writer.Write(item.CodecId);
                    writer.Write(item.CodecVersion);
                    writer.Write(item.Quantity);
                    // Version 1 included the localized display label. Version 2
                    // hashes ownership data only so language changes do not turn
                    // a safe retry into a permanent conflict.
                    if (version <= 1) writer.Write(item.Label);
                    writer.Write(item.Payload.Length);
                    writer.Write(item.Payload);
                }
                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                    return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }

        private static InventoryDepositResult Result(InventoryDepositStatus status, string reason,
            InventoryFailureCode failureCode = InventoryFailureCode.None)
        {
            if (failureCode == InventoryFailureCode.None && status == InventoryDepositStatus.Rejected)
                failureCode = InventoryFailureCode.InvalidRequest;
            return new InventoryDepositResult { Status = status, FailureCode = failureCode, Reason = reason };
        }

        private static InventoryReservationResult ReservationResult(InventoryReservationResultStatus status,
            string reason, InventoryFailureCode failureCode, InventoryReservation reservation)
        {
            return new InventoryReservationResult
            {
                Status = status,
                FailureCode = failureCode,
                Reason = reason,
                Reservation = reservation
            };
        }

        private static InventoryReservationResolutionResult ResolutionResult(bool succeeded, string reason,
            InventoryFailureCode failureCode, InventoryReservation reservation)
        {
            return new InventoryReservationResolutionResult
            {
                Succeeded = succeeded,
                FailureCode = failureCode,
                Reason = reason,
                Reservation = reservation
            };
        }
    }
}
