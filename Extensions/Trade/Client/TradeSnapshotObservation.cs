using System;
using System.Runtime.CompilerServices;
using PhinixClient.Trade;

namespace Phinix.TradeExtension.Client
{
    // Attach the revision to the immutable view actually displayed, not the newest repository entry.
    internal static class TradeSnapshotObservation
    {
        private sealed class Revision { public long Value; }
        private static readonly ConditionalWeakTable<ClientTradeSnapshot, Revision> revisions = new ConditionalWeakTable<ClientTradeSnapshot, Revision>();
        public static ClientTradeSnapshot Bind(ClientTradeSnapshot snapshot, long version)
        {
            revisions.Add(snapshot, new Revision { Value = version });
            return snapshot;
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
