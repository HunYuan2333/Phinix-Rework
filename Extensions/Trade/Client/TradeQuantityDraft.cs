using System.Globalization;
namespace Phinix.TradeExtension.Client
{
    // Draft validation only: no reservation, item materialization or server intent.
    internal static class TradeQuantityDraft
    {
        internal static bool TryParse(string text,int available,out int quantity)
        {
            quantity=0;
            return available>=0&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out quantity)&&quantity>=0&&quantity<=available;
        }
    }
}
