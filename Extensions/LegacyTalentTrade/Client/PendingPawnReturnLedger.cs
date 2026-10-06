using System;
using System.Collections.Generic;
using System.Linq;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    internal enum PawnReturnOutcome { Deferred, Returned, Uncertain }

    internal static class PawnReturnDelivery
    {
        internal static PawnReturnOutcome Run(Action prepare, Action beginHandoff, Action handoff,
            Action afterDelivery, Action<string, Exception> log)
        {
            try { prepare(); beginHandoff(); }
            catch (Exception ex) { log("prepare", ex); return PawnReturnOutcome.Deferred; }
            try { handoff(); }
            catch (Exception ex) { log("handoff", ex); return PawnReturnOutcome.Uncertain; }
            // Graphics/notification failures after ownership transfer cannot turn success into a retry.
            try { afterDelivery(); }
            catch (Exception ex) { log("after-delivery", ex); }
            return PawnReturnOutcome.Returned;
        }
    }

    /// <summary>Save-local recovery records. Queueing is never a delivery receipt.</summary>
    internal sealed class PendingPawnReturnLedger
    {
        internal sealed class Record
        {
            internal string Id, Data;
            internal bool Queued, Uncertain, Returned;
        }

        private readonly List<Record> records = new List<Record>();

        internal PendingPawnReturnLedger(IList<string> ids, IList<string> data, IEnumerable<string> uncertainIds)
        {
            var uncertain = new HashSet<string>(uncertainIds ?? new string[0], StringComparer.Ordinal);
            int count = Math.Max(ids.Count, data.Count);
            for (int i = 0; i < count; i++)
                records.Add(new Record {
                    Id = i < ids.Count ? ids[i] : null,
                    Data = i < data.Count ? data[i] : null,
                    Uncertain = i < ids.Count && ids[i] != null && uncertain.Contains(ids[i])
                });
        }

        internal Record[] Records { get { return records.Where(r => !r.Returned).ToArray(); } }

        internal bool TryQueue(Record record)
        {
            if (!records.Contains(record) || record.Returned || record.Queued || record.Uncertain ||
                string.IsNullOrEmpty(record.Id) || string.IsNullOrEmpty(record.Data) ||
                records.Count(r => r.Id == record.Id) != 1) return false;
            record.Queued = true;
            return true;
        }

        internal void BeginHandoff(Record record)
        {
            if (!records.Contains(record) || !record.Queued || record.Returned)
                throw new InvalidOperationException("Pending pawn return has no active ticket.");
            // Mark before world ownership transfer; the next save retains ambiguity and raw data.
            record.Uncertain = true;
        }

        internal void Finish(Record record, PawnReturnOutcome outcome)
        {
            if (!records.Contains(record) || !record.Queued) return;
            record.Queued = false;
            if (outcome == PawnReturnOutcome.Returned) record.Returned = true;
            record.Uncertain = outcome == PawnReturnOutcome.Uncertain;
        }

        internal void Snapshot(IEnumerable<KeyValuePair<string, string>> active,
            out List<string> ids, out List<string> data, out List<string> uncertainIds)
        {
            var pending = Records;
            ids = pending.Select(r => r.Id).ToList();
            data = pending.Select(r => r.Data).ToList();
            uncertainIds = pending.Where(r => r.Uncertain && r.Id != null).Select(r => r.Id)
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (var listing in active)
            {
                // Never overwrite a recovery payload with a live listing or silently discard corruption.
                if (string.IsNullOrEmpty(listing.Key) || ids.Contains(listing.Key)) continue;
                ids.Add(listing.Key);
                data.Add(listing.Value);
            }
        }
    }
}
