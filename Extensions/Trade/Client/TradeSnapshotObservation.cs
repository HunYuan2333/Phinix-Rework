using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Utils.Framework;
using PhinixClient.Trade;

namespace Phinix.TradeExtension.Client
{
    // Attach the revision to the immutable view actually displayed, not the newest repository entry.
    internal static class TradeSnapshotObservation
    {
        private sealed class Revision { public long Value; public string Fingerprint; }
        private static readonly ConditionalWeakTable<ClientTradeSnapshot, Revision> revisions = new ConditionalWeakTable<ClientTradeSnapshot, Revision>();
        public static ClientTradeSnapshot Bind(ClientTradeSnapshot snapshot, long version, FrameworkTradeStateSnapshot source = null)
        {
            revisions.Add(snapshot, new Revision { Value = version, Fingerprint = version != 0 || source == null ? null : Fingerprint(source) });
            return snapshot;
        }
        internal static bool MatchesSnapshot(ClientTradeSnapshot snapshot, FrameworkTradeStateSnapshot current)
        {
            Revision revision;
            return snapshot != null && revisions.TryGetValue(snapshot, out revision) && revision.Value == 0 && revision.Fingerprint != null &&
                current != null && revision.Fingerprint == Fingerprint(current);
        }

        private static string Fingerprint(FrameworkTradeStateSnapshot snapshot)
        {
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(snapshot))));
        }

        public static long Require(ClientTradeSnapshot snapshot)
        {
            Revision revision;
            if (snapshot == null || !revisions.TryGetValue(snapshot, out revision) || revision.Value < 1)
                throw new InvalidOperationException("Refresh the trade quote before confirming.");
            return revision.Value;
        }
    }
    internal interface IObservedTradeConsentService
    {
        void AcceptObservedTrade(ClientTradeSnapshot snapshot);
    }
}
