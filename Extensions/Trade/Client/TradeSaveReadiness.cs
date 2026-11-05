using Phinix.InventoryExtension;

namespace Phinix.TradeExtension.Client
{
    // A saved local recipient must exist before preparing an asset operation.
    // Reading readiness never saves the game, adopts a receipt or settles assets.
    internal static class TradeSaveReadiness
    {
        internal static string BlockReason(InventoryStatus status)
        {
            if(status==null || string.IsNullOrEmpty(status.SaveIdentity) ||
                status.Availability==InventoryAvailability.Inactive || status.Availability==InventoryAvailability.UnsavedGame)
                return "Phinix_trade_saveBeforeTrading";
            return status.CanWrite?null:"Phinix_trade_inventoryUnavailableBeforeTrading";
        }
    }
}
