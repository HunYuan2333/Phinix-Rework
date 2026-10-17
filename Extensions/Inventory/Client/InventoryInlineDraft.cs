using System.Globalization;

namespace Phinix.InventoryExtension.Client
{
    // Session-local input/result for one authoritative EntryId. Never persisted or used as a balance.
    internal sealed class InventoryInlineDraft
    {
        internal string Text = "1", Result, ReviewKey = "inlineReview";
        private long itemsPerUnit;
        internal void BindUnits(long value)
        {
            if (itemsPerUnit > 0 && itemsPerUnit != value) { Text = "1"; NeedsReview = true; ReviewKey = "inlineUnitChanged"; }
            itemsPerUnit = value;
        }
        internal bool Failed, NeedsReview;
        internal bool TryQuantity(long available, out long quantity) =>
            long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) && quantity > 0 && quantity <= available && !NeedsReview;
        internal void Edit(string value)
        {
            value = value ?? "";
            if (value == Text) return;
            Text = value; NeedsReview = false; Result = null; Failed = false;
        }
        internal void Complete(bool success, long requested, long delivered, string result, bool uncertain)
        {
            Result = result; Failed = !success; NeedsReview = uncertain; ReviewKey = "inlineReview";
            // Only units explicitly confirmed by the extraction loop reduce the remaining request.
            if (!success && delivered > 0 && delivered < requested)
                Text = (requested - delivered).ToString(CultureInfo.InvariantCulture);
        }
    }
}
