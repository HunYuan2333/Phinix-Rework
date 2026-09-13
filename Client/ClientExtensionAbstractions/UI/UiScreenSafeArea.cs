using UnityEngine;
using Verse;

namespace PhinixClient
{
    /// <summary>Screen-bound window geometry in RimWorld UI coordinates.</summary>
    public static class UiScreenSafeArea
    {
        public static Rect Current => new Rect(0f, 0f, Mathf.Max(0f, UI.screenWidth), Mathf.Max(0f, UI.screenHeight));

        public static Rect ClampWindow(Rect windowRect, Vector2 minimumSize)
        {
            return ClampWindow(windowRect, Current, minimumSize);
        }

        public static Rect ClampWindow(Rect windowRect, Rect safeRect, Vector2 minimumSize)
        {
            safeRect = Normalize(safeRect);
            float minWidth = Mathf.Min(Mathf.Max(0f, minimumSize.x), safeRect.width);
            float minHeight = Mathf.Min(Mathf.Max(0f, minimumSize.y), safeRect.height);
            windowRect.width = Mathf.Clamp(windowRect.width, minWidth, safeRect.width);
            windowRect.height = Mathf.Clamp(windowRect.height, minHeight, safeRect.height);
            windowRect.x = Mathf.Clamp(windowRect.x, safeRect.xMin, safeRect.xMax - windowRect.width);
            windowRect.y = Mathf.Clamp(windowRect.y, safeRect.yMin, safeRect.yMax - windowRect.height);
            return windowRect;
        }

        internal static Rect Normalize(Rect rect)
        {
            if (rect.width < 0f)
            {
                rect.x += rect.width;
                rect.width = -rect.width;
            }
            if (rect.height < 0f)
            {
                rect.y += rect.height;
                rect.height = -rect.height;
            }
            return rect;
        }
    }
}
