// Original file provided by Longwelwind (https://github.com/Longwelwind)
// as a part of the RimWorld mod Phi (https://github.com/Longwelwind/Phi)

using System;
using UnityEngine;
using Verse;

namespace PhinixClient.GUI
{
    internal class HorizontalScrollContainer : Displayable
    {
        /// <inheritdoc />
        public override bool IsFluidHeight => child.IsFluidHeight;

        private const float SCROLL_BAR_WIDTH = 16f;

        /// <summary>
        /// Contents of the container.
        /// </summary>
        private Displayable child;

        /// <summary>
        /// Callback invoked when the scroll position is changed.
        /// </summary>
        private Action<Vector2> onScroll;

        /// <summary>
        /// Scroll position of the container.
        /// </summary>
        private Vector2 scrollPosition = Vector2.zero;

        public HorizontalScrollContainer(Displayable child, Action<Vector2> onScroll = null)
        {
            this.child = child;
            this.onScroll = onScroll;
        }

        /// <inheritdoc />
        public override void Draw(Rect inRect)
        {
            inRect.width = Mathf.Max(0f, inRect.width);
            inRect.height = Mathf.Max(0f, inRect.height);

            // We calculate the overflowed size the children will take
            // Only supports x-overflow at the moment
            float widthChild = child.CalcWidth(inRect.height);
            if (widthChild == FLUID || widthChild <= inRect.width)
            {
                ResetHorizontalScroll();
                child.Draw(inRect);
                return;
            }

            float heightChild = Mathf.Max(0f, inRect.height - SCROLL_BAR_WIDTH);
            widthChild = Mathf.Max(inRect.width, child.CalcWidth(heightChild));

            // Create an inner container that will hold the scrollable content
            Rect viewRect = new Rect(inRect.xMin, inRect.yMin, widthChild, heightChild);

            // Get a copy of the current scroll position
            Vector2 previousScrollPosition = new Vector2(scrollPosition.x, scrollPosition.y);

            // Begin scrolling
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

            // Draw the contents
            child.Draw(viewRect);

            // Stop scrolling
            Widgets.EndScrollView();

            // Check if the scroll position changed
            if (!scrollPosition.Equals(previousScrollPosition))
            {
                // Invoke the scroll callback
                onScroll?.Invoke(scrollPosition);
            }
        }

        private void ResetHorizontalScroll()
        {
            if (scrollPosition.x == 0f) return;
            scrollPosition.x = 0f;
            onScroll?.Invoke(scrollPosition);
        }

        /// <inheritdoc />
        public override void Update()
        {
            child.Update();
        }

        /// <inheritdoc />
        public override float CalcHeight(float width)
        {
            // We can't determine a height if the child is fluid
            if (child.IsFluidHeight) return FLUID;

            // Compensate for the scrollbar width
            float childHeight = Mathf.Max(0f, child.CalcHeight(Mathf.Max(0f, width)));
            float childWidth = child.CalcWidth(childHeight);
            return childWidth != FLUID && childWidth > width
                ? childHeight + SCROLL_BAR_WIDTH
                : childHeight;
        }
    }
}
