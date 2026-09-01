using System;
namespace Phinix.TradeExtension.Client
{
    internal struct PendingThings
    {
        /// <summary>
        /// Collection of stacked items that were added to the trade.
        /// </summary>
        public PoppedThing[] Things;

        /// <summary>
        /// Time the trade update was created.
        /// </summary>
        public DateTime Timestamp;
    }
}
