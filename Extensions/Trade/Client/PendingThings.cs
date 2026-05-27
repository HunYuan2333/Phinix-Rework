using System;
using Verse;

namespace Phinix.TradeExtension.Client
{
    internal struct PendingThings
    {
        /// <summary>
        /// Collection of stacked items that were added to the trade.
        /// </summary>
        public Thing[] Things;

        /// <summary>
        /// Time the trade update was created.
        /// </summary>
        public DateTime Timestamp;
    }
}
