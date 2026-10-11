using System;
using PhinixClient.Framework;
using UnityEngine;

namespace Phinix.TradeExtension.Client
{
    internal static class TradeTheme
    {
        public static Color OurOfferAccent => Current?.GetColor("trade.ourOfferAccent") ?? default(Color);
        public static Color TheirOfferAccent => Current?.GetColor("trade.theirOfferAccent") ?? default(Color);
        public static Color OurOfferBg => Current?.GetColor("trade.ourOfferBg") ?? default(Color);
        public static Color TheirOfferBg => Current?.GetColor("trade.theirOfferBg") ?? default(Color);
        public static Color CancelButton => Current?.GetColor("trade.cancelButton") ?? default(Color);
        public static Color AcceptedBadge => Current?.GetColor("trade.acceptedBadge") ?? default(Color);
        public static Color PendingBadge => Current?.GetColor("trade.pendingBadge") ?? default(Color);
        public static Color RowHoverBg => Current?.GetColor("trade.rowHoverBg") ?? default(Color);
        public static Color PanelBg => Current?.GetColor("trade.panelBg") ?? default(Color);
        public static Color SearchPlaceholder => Current?.GetColor("trade.searchPlaceholder") ?? default(Color);

        private static IClientTheme source;
        private static IClientThemeReader latest, drawing;
        internal static IClientThemeReader Current => drawing ?? latest;
        internal static void Refresh(IClientTheme theme)
        { source = theme; latest = theme.Capture(); }
        internal static void Release() { source = null; latest = null; }
        internal static DrawScope BeginDraw() => new DrawScope(true);
        internal struct DrawScope : IDisposable
        {
            private readonly IClientThemeReader previous;
            internal DrawScope(bool capture)
            { previous = drawing; drawing = previous ?? source?.Capture() ?? latest; }
            public void Dispose() { drawing = previous; }
        }
    }
}
