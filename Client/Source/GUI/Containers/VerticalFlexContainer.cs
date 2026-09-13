using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhinixClient.GUI
{
    [System.Obsolete("Use responsive geometry helpers and direct RimWorld widgets instead.")]
    public class VerticalFlexContainer : Displayable
    {
        /// <inheritdoc />
        public override bool IsFluidHeight => false;

        /// <summary>
        /// List of contents to draw.
        /// </summary>
        public readonly List<Displayable> Contents;

        /// <summary>
        /// Spacing between elements.
        /// </summary>
        private float spacing;

        /// <summary>
        /// Creates a new <see cref="VerticalFlexContainer"/> with the given width.
        /// </summary>
        public VerticalFlexContainer(float spacing = 10f)
        {
            this.spacing = spacing;

            this.Contents = new List<Displayable>();
        }

        /// <summary>
        /// Creates a new <see cref="VerticalFlexContainer"/> with the given width and contents.
        /// </summary>
        /// <param name="contents">Drawable contents</param>
        /// <param name="spacing">Spacing between elements</param>
        public VerticalFlexContainer(IEnumerable<Displayable> contents, float spacing = 10f)
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
            float fixedHeight = 0f;
            int fluidItems = 0;
            for (int i = 0; i < count; i++)
            {
                Displayable item = Contents[i];
                if (item.IsFluidHeight)
                    fluidItems++;
                else
                    fixedHeight += Mathf.Max(0f, item.CalcHeight(container.width));
            }

            // Divvy out the remaining height to each fluid element
            float remainingHeight = container.height - fixedHeight;
            remainingHeight -= (count - 1) * effectiveSpacing;
            float heightPerFluid = fluidItems > 0
                ? Mathf.Max(0f, remainingHeight) / fluidItems
                : 0f;

            // Draw each item
            float yOffset = 0f;
            for (int i = 0; i < count; i++)
            {
                Displayable item = Contents[i];
                Rect rect;

                // Give fluid items special treatment
                if (item.IsFluidHeight)
                {
                    // Give the item a container with a share of the remaining height
                    rect = new Rect(
                        x: container.xMin,
                        y: container.yMin + yOffset,
                        width: container.width,
                        height: heightPerFluid
                    );
                }
                else
                {
                    // Give the item a container with fixed width and dynamic height
                    rect = new Rect(
                        x: container.xMin,
                        y: container.yMin + yOffset,
                        width: container.width,
                        height: Mathf.Min(
                            Mathf.Max(0f, item.CalcHeight(container.width)),
                            Mathf.Max(0f, container.height - yOffset))
                    );
                }

                // Draw the item
                item.Draw(rect);

                // Increment the y offset by the item's height
                yOffset = Mathf.Min(container.height, yOffset + rect.height);

                // Add spacing to the y offset if applicable
                if (i < Contents.Count - 1)
                    yOffset = Mathf.Min(container.height, yOffset + effectiveSpacing);
            }
        }

        /// <inheritdoc />
        public override void Update()
        {
            foreach (Displayable item in Contents)
            {
                item.Update();
            }
        }

        /// <inheritdoc />
        public override float CalcHeight(float width)
        {
            // Return the sum of each item's height, ignoring fluid items, and the spacing between each
            float total = 0f;
            foreach (Displayable item in Contents)
            {
                if (!item.IsFluidHeight)
                    total += item.CalcHeight(width);
            }
            return Mathf.Max(0f, total + (Mathf.Max(0f, spacing) * Mathf.Max(0, Contents.Count - 1)));
        }

        /// <inheritdoc />
        public override float CalcWidth(float height)
        {
            return FLUID;
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
