using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace PhinixClient.GUI
{
    public class HorizontalFlexContainer : Displayable
    {
        /// <inheritdoc />
        public override bool IsFluidWidth => false;

        /// <summary>
        /// List of contents to draw.
        /// </summary>
        public readonly List<Displayable> Contents;

        /// <summary>
        /// Spacing between elements.
        /// </summary>
        private float spacing;

        /// <summary>
        /// Creates a new <see cref="HorizontalFlexContainer"/> with the given spacing.
        /// </summary>
        public HorizontalFlexContainer(float spacing = 10f)
        {
            this.spacing = spacing;

            this.Contents = new List<Displayable>();
        }

        /// <summary>
        /// Creates a new <see cref="HorizontalFlexContainer"/> with the given contents and spacing.
        /// </summary>
        /// <param name="contents">Drawable contents</param>
        /// <param name="spacing">Spacing between elements</param>
        public HorizontalFlexContainer(IEnumerable<Displayable> contents, float spacing = 10f)
        {
            this.Contents = contents.ToList();
            this.spacing = spacing;
        }

        /// <inheritdoc />
        public override void Draw(Rect container)
        {
            // Don't do anything if there's nothing to draw
            int count = Contents.Count;
            if (count == 0) return;

            container.width = Mathf.Max(0f, container.width);
            container.height = Mathf.Max(0f, container.height);
            float effectiveSpacing = Mathf.Max(0f, spacing);

            // Fixed content uses a clip fallback when its requested size exceeds the container.
            float fixedWidth = 0f;
            int fluidItems = 0;
            for (int i = 0; i < count; i++)
            {
                Displayable item = Contents[i];
                if (item.IsFluidWidth)
                    fluidItems++;
                else
                    fixedWidth += Mathf.Max(0f, item.CalcWidth(container.height));
            }

            // Divvy out the remaining width to each fluid element
            float remainingWidth = container.width - fixedWidth;
            remainingWidth -= (count - 1) * effectiveSpacing;
            float widthPerFluid = fluidItems > 0
                ? Mathf.Max(0f, remainingWidth) / fluidItems
                : 0f;

            // Draw each item
            float xOffset = 0f;
            for (int i = 0; i < count; i++)
            {
                Displayable item = Contents[i];
                Rect rect;

                // Give fluid items special treatment
                if (item.IsFluidWidth)
                {
                    // Give the item a container with a share of the remaining width
                    rect = new Rect(
                        x: container.xMin + xOffset,
                        y: container.yMin,
                        width: widthPerFluid,
                        height: container.height
                    );
                }
                else
                {
                    // Give the item a container with fixed height and dynamic width
                    rect = new Rect(
                        x: container.xMin + xOffset,
                        y: container.yMin,
                        width: Mathf.Min(
                            Mathf.Max(0f, item.CalcWidth(container.height)),
                            Mathf.Max(0f, container.width - xOffset)),
                        height: container.height
                    );
                }

                // Draw the item
                item.Draw(rect);

                // Increment the x offset by the item's width
                xOffset = Mathf.Min(container.width, xOffset + rect.width);

                // Add spacing to the x offset if applicable
                if (i < Contents.Count - 1)
                    xOffset = Mathf.Min(container.width, xOffset + effectiveSpacing);
            }
        }

        /// <inheritdoc />
        public override float CalcHeight(float width)
        {
            return FLUID;
        }

        /// <inheritdoc />
        public override float CalcWidth(float height)
        {
            // Return the sum of each item's width, ignoring fluid items, and the spacing between each
            float total = 0f;
            foreach (Displayable item in Contents)
            {
                if (!item.IsFluidWidth)
                    total += item.CalcWidth(height);
            }
            return Mathf.Max(0f, total + (Mathf.Max(0f, spacing) * Mathf.Max(0, Contents.Count - 1)));
        }

        /// <inheritdoc />
        public override void Update()
        {
            foreach (Displayable item in Contents)
            {
                item.Update();
            }
        }

        /// <summary>
        /// Adds an <see cref="Displayable"/> item to the container.
        /// </summary>
        /// <param name="item">Drawable item to add</param>
        public void Add(Displayable item)
        {
            Contents.Add(item);
        }
    }
}
