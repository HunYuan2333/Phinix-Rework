using System;
using System.Collections.Generic;

namespace Phinix.TradeExtension.Client
{
    internal static class TradeLifetimeCleanup
    {
        internal static void Try(Action release, List<Exception> failures)
        {
            try { release(); }
            catch (Exception error) { global::Phinix.TradeExtension.Client.TradeDiagnostics.Report("TradeLifetimeCleanup/Try", error);  failures.Add(error); }
        }

        internal static void ThrowIfFailed(List<Exception> failures)
        {
            if (failures.Count != 0) throw new AggregateException("Trade lifetime cleanup failed.", failures);
        }
    }
}
