using System;

namespace PhinixClient
{
    // Host-shell geometry, independent of sidebar business and native GUI state.
    internal static class SidebarSplitLayout
    {
        internal const float Gap = 8, MainMinimum = 480;
        private static float Minimum(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 120 : Math.Max(120, value);
        internal static bool TryWidth(float total, float preferred, float minimum, float ratio, out float width)
        {
            width = 0;
            float available = total - Gap, min = Minimum(minimum);
            if (float.IsNaN(total) || float.IsInfinity(total) || available < MainMinimum + min) return false;
            float desired = ratio > 0 && ratio < 1 ? available * ratio : preferred;
            if (float.IsNaN(desired) || float.IsInfinity(desired)) desired = 270;
            width = Math.Max(min, Math.Min(available - MainMinimum, desired));
            return true;
        }
        internal static float RatioForWidth(float total, float minimum, float desired)
        {
            if (!TryWidth(total, desired, minimum, 0, out var width)) return 0;
            return width / (total - Gap);
        }
    }
}
