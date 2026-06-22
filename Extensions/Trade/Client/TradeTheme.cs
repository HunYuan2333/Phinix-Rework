using PhinixClient.Framework;
using UnityEngine;

namespace Phinix.TradeExtension.Client
{
    internal static class TradeTheme
    {
        public static Color OurOfferAccent;
        public static Color TheirOfferAccent;
        public static Color OurOfferBg;
        public static Color TheirOfferBg;
        public static Color CancelButton;
        public static Color AcceptedBadge;
        public static Color PendingBadge;
        public static Color RowHoverBg;
        public static Color PanelBg;
        public static Color SearchPlaceholder;

        internal static void Refresh(IUiTheme theme)
        {
            OurOfferAccent = theme.GetColor("trade.ourOfferAccent");
            TheirOfferAccent = theme.GetColor("trade.theirOfferAccent");
            OurOfferBg = theme.GetColor("trade.ourOfferBg");
            TheirOfferBg = theme.GetColor("trade.theirOfferBg");
            CancelButton = theme.GetColor("trade.cancelButton");
            AcceptedBadge = theme.GetColor("trade.acceptedBadge");
            PendingBadge = theme.GetColor("trade.pendingBadge");
            RowHoverBg = theme.GetColor("trade.rowHoverBg");
            PanelBg = theme.GetColor("trade.panelBg");
            SearchPlaceholder = theme.GetColor("trade.searchPlaceholder");
        }
    }
}
