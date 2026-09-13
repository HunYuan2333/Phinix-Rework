using UnityEngine;

namespace PhinixClient
{
    public enum ResponsiveSplitMode
    {
        Horizontal,
        Vertical,
        SinglePane
    }

    public struct ResponsiveSplitResult
    {
        public ResponsiveSplitResult(ResponsiveSplitMode mode, Rect firstRect, Rect secondRect, Rect dividerRect)
        {
            Mode = mode;
            FirstRect = firstRect;
            SecondRect = secondRect;
            DividerRect = dividerRect;
        }

        public ResponsiveSplitMode Mode { get; private set; }
        public Rect FirstRect { get; private set; }
        public Rect SecondRect { get; private set; }
        public Rect DividerRect { get; private set; }
    }

    /// <summary>Allocation-free two-pane layout selected from content minimum sizes.</summary>
    public static class ResponsiveSplitLayout
    {
        public static ResponsiveSplitResult Calculate(
            Rect container,
            Vector2 firstMinimum,
            Vector2 secondMinimum,
            Vector2 firstPreferred,
            float spacing,
            bool showFirstInSinglePane)
        {
            container = UiScreenSafeArea.Normalize(container);
            spacing = Mathf.Max(0f, spacing);
            firstMinimum = NonNegative(firstMinimum);
            secondMinimum = NonNegative(secondMinimum);
            firstPreferred = new Vector2(
                Mathf.Max(firstMinimum.x, firstPreferred.x),
                Mathf.Max(firstMinimum.y, firstPreferred.y));

            if (container.width >= firstMinimum.x + spacing + secondMinimum.x)
            {
                float firstWidth = Mathf.Clamp(
                    firstPreferred.x,
                    firstMinimum.x,
                    container.width - spacing - secondMinimum.x);
                Rect first = new Rect(container.xMin, container.yMin, firstWidth, container.height);
                Rect divider = new Rect(first.xMax, container.yMin, spacing, container.height);
                Rect second = new Rect(divider.xMax, container.yMin, Mathf.Max(0f, container.xMax - divider.xMax), container.height);
                return new ResponsiveSplitResult(ResponsiveSplitMode.Horizontal, first, second, divider);
            }

            if (container.height >= firstMinimum.y + spacing + secondMinimum.y)
            {
                float firstHeight = Mathf.Clamp(
                    firstPreferred.y,
                    firstMinimum.y,
                    container.height - spacing - secondMinimum.y);
                Rect first = new Rect(container.xMin, container.yMin, container.width, firstHeight);
                Rect divider = new Rect(container.xMin, first.yMax, container.width, spacing);
                Rect second = new Rect(container.xMin, divider.yMax, container.width, Mathf.Max(0f, container.yMax - divider.yMax));
                return new ResponsiveSplitResult(ResponsiveSplitMode.Vertical, first, second, divider);
            }

            Rect hidden = new Rect(container.xMin, container.yMin, 0f, 0f);
            return showFirstInSinglePane
                ? new ResponsiveSplitResult(ResponsiveSplitMode.SinglePane, container, hidden, hidden)
                : new ResponsiveSplitResult(ResponsiveSplitMode.SinglePane, hidden, container, hidden);
        }

        private static Vector2 NonNegative(Vector2 value)
        {
            return new Vector2(Mathf.Max(0f, value.x), Mathf.Max(0f, value.y));
        }
    }
}
