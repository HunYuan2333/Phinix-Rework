using System;
using UnityEngine;

namespace PhinixClient
{
    public struct ResponsiveToolbarResult
    {
        public ResponsiveToolbarResult(int visibleActionCount, int rowCount, float height, bool hasOverflow, Rect overflowButtonRect)
        {
            VisibleActionCount = visibleActionCount;
            RowCount = rowCount;
            Height = height;
            HasOverflow = hasOverflow;
            OverflowButtonRect = overflowButtonRect;
        }

        public int VisibleActionCount { get; private set; }
        public int RowCount { get; private set; }
        public float Height { get; private set; }
        public bool HasOverflow { get; private set; }
        public Rect OverflowButtonRect { get; private set; }
    }

    /// <summary>
    /// Allocation-free toolbar geometry. Actions must be ordered by priority; hidden trailing actions belong in a FloatMenu.
    /// </summary>
    public static class ResponsiveToolbarLayout
    {
        public static ResponsiveToolbarResult Calculate(
            Rect container,
            float[] desiredWidths,
            int actionCount,
            int primaryActionCount,
            float rowHeight,
            float spacing,
            int maximumRows,
            float overflowButtonWidth,
            Rect[] actionRects)
        {
            container = UiScreenSafeArea.Normalize(container);
            rowHeight = Mathf.Max(0f, rowHeight);
            spacing = Mathf.Max(0f, spacing);
            maximumRows = Math.Max(1, maximumRows);
            overflowButtonWidth = Mathf.Max(0f, overflowButtonWidth);
            actionCount = Math.Max(0, actionCount);
            if (desiredWidths == null || actionRects == null)
            {
                actionCount = 0;
            }
            else
            {
                actionCount = Math.Min(actionCount, Math.Min(desiredWidths.Length, actionRects.Length));
            }
            primaryActionCount = Math.Min(actionCount, Math.Max(0, primaryActionCount));

            for (int i = 0; i < actionCount; i++)
            {
                actionRects[i] = new Rect(container.xMin, container.yMin, 0f, 0f);
            }

            if (actionCount == 0 || container.width <= 0f || container.height <= 0f || rowHeight <= 0f)
            {
                return new ResponsiveToolbarResult(0, 0, 0f, false, new Rect(container.xMin, container.yMin, 0f, 0f));
            }

            int rowsByHeight = Math.Max(1, (int)Math.Floor((container.height + spacing) / (rowHeight + spacing)));
            maximumRows = Math.Min(maximumRows, rowsByHeight);

            int rows;
            if (TryPlace(container, desiredWidths, actionCount, rowHeight, spacing, maximumRows, actionRects, out rows))
            {
                return new ResponsiveToolbarResult(actionCount, rows, HeightForRows(rows, rowHeight, spacing), false, new Rect(container.xMin, container.yMin, 0f, 0f));
            }

            for (int i = 0; i < actionCount; i++)
            {
                actionRects[i] = new Rect(container.xMin, container.yMin, 0f, 0f);
            }

            int visible = primaryActionCount;
            Rect overflowRect;
            while (visible >= 0)
            {
                if (TryPlaceWithOverflow(
                    container,
                    desiredWidths,
                    visible,
                    rowHeight,
                    spacing,
                    maximumRows,
                    overflowButtonWidth,
                    actionRects,
                    out rows,
                    out overflowRect))
                {
                    for (int i = visible; i < actionCount; i++)
                    {
                        actionRects[i] = new Rect(container.xMin, container.yMin, 0f, 0f);
                    }
                    return new ResponsiveToolbarResult(visible, rows, HeightForRows(rows, rowHeight, spacing), true, overflowRect);
                }
                visible--;
            }

            overflowRect = new Rect(container.xMin, container.yMin, Mathf.Min(container.width, overflowButtonWidth), rowHeight);
            return new ResponsiveToolbarResult(0, 1, rowHeight, true, overflowRect);
        }

        private static bool TryPlaceWithOverflow(
            Rect container,
            float[] widths,
            int visibleCount,
            float rowHeight,
            float spacing,
            int maximumRows,
            float overflowWidth,
            Rect[] output,
            out int rows,
            out Rect overflowRect)
        {
            rows = 1;
            float x = container.xMin;
            float y = container.yMin;
            for (int i = 0; i < visibleCount; i++)
            {
                float width = Mathf.Min(container.width, Mathf.Max(0f, widths[i]));
                if (x > container.xMin && x + width > container.xMax)
                {
                    rows++;
                    x = container.xMin;
                    y += rowHeight + spacing;
                }
                if (rows > maximumRows) return Fail(out overflowRect, container);
                output[i] = new Rect(x, y, width, rowHeight);
                x += width + spacing;
            }

            float finalWidth = Mathf.Min(container.width, overflowWidth);
            if (x > container.xMin && x + finalWidth > container.xMax)
            {
                rows++;
                x = container.xMin;
                y += rowHeight + spacing;
            }
            if (rows > maximumRows) return Fail(out overflowRect, container);
            overflowRect = new Rect(x, y, finalWidth, rowHeight);
            return true;
        }

        private static bool TryPlace(
            Rect container,
            float[] widths,
            int count,
            float rowHeight,
            float spacing,
            int maximumRows,
            Rect[] output,
            out int rows)
        {
            rows = 1;
            float x = container.xMin;
            float y = container.yMin;
            for (int i = 0; i < count; i++)
            {
                float width = Mathf.Min(container.width, Mathf.Max(0f, widths[i]));
                if (x > container.xMin && x + width > container.xMax)
                {
                    rows++;
                    x = container.xMin;
                    y += rowHeight + spacing;
                }
                if (rows > maximumRows) return false;
                output[i] = new Rect(x, y, width, rowHeight);
                x += width + spacing;
            }
            return true;
        }

        private static bool Fail(out Rect rect, Rect container)
        {
            rect = new Rect(container.xMin, container.yMin, 0f, 0f);
            return false;
        }

        private static float HeightForRows(int rows, float rowHeight, float spacing)
        {
            return rows <= 0 ? 0f : rows * rowHeight + (rows - 1) * spacing;
        }
    }
}
