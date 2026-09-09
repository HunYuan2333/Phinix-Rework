using System;

namespace PhinixClient
{
    public struct VirtualListRange
    {
        public VirtualListRange(int firstIndex, int endIndexExclusive)
        {
            FirstIndex = firstIndex;
            EndIndexExclusive = endIndexExclusive;
        }

        public int FirstIndex { get; private set; }
        public int EndIndexExclusive { get; private set; }
        public int Count => EndIndexExclusive - FirstIndex;
    }

    /// <summary>Visible-range calculations for fixed and cached dynamic row heights.</summary>
    public static class VirtualListLayout
    {
        public static VirtualListRange GetFixedRange(int itemCount, float rowHeight, float scrollY, float viewportHeight, int overscan)
        {
            itemCount = Math.Max(0, itemCount);
            overscan = Math.Max(0, overscan);
            if (itemCount == 0 || rowHeight <= 0f || viewportHeight <= 0f)
            {
                return new VirtualListRange(0, 0);
            }

            float visibleStart = Math.Max(0f, scrollY);
            float visibleEnd = visibleStart + viewportHeight;
            int first = Math.Max(0, (int)Math.Floor(visibleStart / rowHeight) - overscan);
            int end = Math.Min(itemCount, (int)Math.Ceiling(visibleEnd / rowHeight) + overscan);
            return new VirtualListRange(first, Math.Max(first, end));
        }

        /// <param name="prefixOffsets">Cumulative offsets with length itemCount + 1 and prefixOffsets[0] == 0.</param>
        public static VirtualListRange GetDynamicRange(float[] prefixOffsets, int itemCount, float scrollY, float viewportHeight, int overscan)
        {
            itemCount = Math.Max(0, itemCount);
            overscan = Math.Max(0, overscan);
            if (prefixOffsets == null || prefixOffsets.Length < itemCount + 1 || itemCount == 0 || viewportHeight <= 0f)
            {
                return new VirtualListRange(0, 0);
            }

            float visibleStart = Math.Max(0f, scrollY);
            float visibleEnd = visibleStart + viewportHeight;
            int first = FirstRowEndingAfter(prefixOffsets, itemCount, visibleStart);
            int end = FirstRowStartingAtOrAfter(prefixOffsets, itemCount, visibleEnd);
            first = Math.Max(0, first - overscan);
            end = Math.Min(itemCount, end + overscan);
            return new VirtualListRange(first, Math.Max(first, end));
        }

        private static int FirstRowEndingAfter(float[] offsets, int itemCount, float position)
        {
            int low = 0;
            int high = itemCount;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (offsets[middle + 1] <= position) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private static int FirstRowStartingAtOrAfter(float[] offsets, int itemCount, float position)
        {
            int low = 0;
            int high = itemCount;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (offsets[middle] < position) low = middle + 1;
                else high = middle;
            }
            return low;
        }
    }
}
