using UnityEngine;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal struct RedPacketClaimRowLayout
    {
        private const float OUTER_PADDING = 4f;
        private const float COLUMN_SPACING = 6f;
        private const float BEST_TAG_SPACING = 10f;
        private const float MINIMUM_NAME_WIDTH = 80f;

        public Rect NameRect;
        public Rect BestRect;
        public Rect AmountRect;
        public bool ShowBest;

        public static RedPacketClaimRowLayout Calculate(
            Rect rowRect,
            float desiredAmountWidth,
            float desiredBestWidth,
            bool showBest)
        {
            float left = rowRect.xMin + Mathf.Min(OUTER_PADDING, Mathf.Max(0f, rowRect.width));
            float right = Mathf.Max(left, rowRect.xMax - Mathf.Min(OUTER_PADDING, Mathf.Max(0f, rowRect.width)));
            float availableWidth = Mathf.Max(0f, right - left);

            // The amount is authoritative information. It receives its complete measured width
            // before optional metadata or the player name is considered.
            float amountWidth = Mathf.Min(availableWidth, Mathf.Max(0f, desiredAmountWidth));
            Rect amountRect = new Rect(right - amountWidth, rowRect.y, amountWidth, Mathf.Max(0f, rowRect.height));
            float nameRight = amountRect.xMin - (amountWidth > 0f ? COLUMN_SPACING : 0f);

            Rect bestRect = new Rect(nameRight, rowRect.y, 0f, Mathf.Max(0f, rowRect.height));
            bool canShowBest = showBest
                && desiredBestWidth > 0f
                && nameRight - BEST_TAG_SPACING - desiredBestWidth >= left + MINIMUM_NAME_WIDTH;
            if (canShowBest)
            {
                bestRect = new Rect(
                    nameRight - desiredBestWidth,
                    rowRect.y,
                    desiredBestWidth,
                    Mathf.Max(0f, rowRect.height));
                nameRight = bestRect.xMin - BEST_TAG_SPACING;
            }

            Rect nameRect = new Rect(
                left,
                rowRect.y,
                Mathf.Max(0f, nameRight - left),
                Mathf.Max(0f, rowRect.height));

            return new RedPacketClaimRowLayout
            {
                NameRect = nameRect,
                BestRect = bestRect,
                AmountRect = amountRect,
                ShowBest = canShowBest
            };
        }
    }
}
