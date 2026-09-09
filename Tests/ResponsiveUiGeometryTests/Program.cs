using System;
using PhinixClient;
using UnityEngine;

internal static class Program
{
    private static readonly float[] StableDynamicOffsets = { 0f, 20f, 50f, 90f };

    private static int Main()
    {
        try
        {
            TestLayoutHintsNormalization();
            TestSafeAreaClamp();
            TestSplitModes();
            TestFormModes();
            TestToolbarWrapAndOverflow();
            TestVirtualRanges();
            TestZeroSizeInputs();
            TestStableGeometryDoesNotAllocate();
            Console.WriteLine("All responsive UI geometry tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void TestLayoutHintsNormalization()
    {
        UiLayoutHints hints = new UiLayoutHints(new Vector2(-2f, 50f), new Vector2(-1f, 20f), true);
        Assert(hints.MinimumContentSize.x == 0f && hints.MinimumContentSize.y == 50f, "Layout minimum must be non-negative.");
        Assert(hints.PreferredContentSize.x == 0f && hints.PreferredContentSize.y == 50f, "Preferred size must not be below minimum size.");
        Assert(hints.SupportsCompactLayout, "Compact-layout flag should be preserved.");
    }

    private static void TestSafeAreaClamp()
    {
        Rect clamped = UiScreenSafeArea.ClampWindow(
            new Rect(-50f, 900f, 1200f, 900f),
            new Rect(0f, 0f, 800f, 600f),
            new Vector2(640f, 480f));
        AssertRect(clamped, 0f, 0f, 800f, 600f, "Oversized window should clamp to the complete safe area.");

        Rect tiny = UiScreenSafeArea.ClampWindow(
            new Rect(20f, 20f, 100f, 100f),
            new Rect(0f, 0f, 320f, 240f),
            new Vector2(640f, 480f));
        AssertRect(tiny, 0f, 0f, 320f, 240f, "Screen bounds must win when smaller than the design minimum.");
    }

    private static void TestSplitModes()
    {
        ResponsiveSplitResult horizontal = ResponsiveSplitLayout.Calculate(
            new Rect(0f, 0f, 900f, 500f), new Vector2(300f, 200f), new Vector2(300f, 200f), new Vector2(420f, 240f), 10f, true);
        Assert(horizontal.Mode == ResponsiveSplitMode.Horizontal, "Wide split should be horizontal.");
        AssertContained(horizontal.FirstRect, new Rect(0f, 0f, 900f, 500f));
        AssertContained(horizontal.SecondRect, new Rect(0f, 0f, 900f, 500f));

        ResponsiveSplitResult vertical = ResponsiveSplitLayout.Calculate(
            new Rect(0f, 0f, 500f, 700f), new Vector2(300f, 200f), new Vector2(300f, 200f), new Vector2(400f, 280f), 10f, true);
        Assert(vertical.Mode == ResponsiveSplitMode.Vertical, "Narrow but tall split should be vertical.");

        ResponsiveSplitResult single = ResponsiveSplitLayout.Calculate(
            new Rect(0f, 0f, 200f, 150f), new Vector2(300f, 200f), new Vector2(300f, 200f), new Vector2(400f, 280f), 10f, false);
        Assert(single.Mode == ResponsiveSplitMode.SinglePane && single.FirstRect.width == 0f && single.SecondRect.width == 200f, "Constrained split should select the requested single pane.");
    }

    private static void TestFormModes()
    {
        ResponsiveFormResult inline = ResponsiveFormLayout.Calculate(new Rect(0f, 0f, 600f, 200f), 120f, 200f, 80f, 30f, 10f, 20f);
        Assert(inline.Mode == ResponsiveFormMode.Inline && inline.Height == 60f, "Wide form should remain inline and reserve error height.");

        ResponsiveFormResult stacked = ResponsiveFormLayout.Calculate(new Rect(0f, 0f, 280f, 200f), 120f, 200f, 80f, 30f, 10f, 0f);
        Assert(stacked.Mode == ResponsiveFormMode.Stacked && stacked.Height == 70f, "Narrow form should stack label and controls.");
        Assert(stacked.InputRect.width >= 0f && stacked.ActionRect.width >= 0f, "Stacked form rects must be non-negative.");
    }

    private static void TestToolbarWrapAndOverflow()
    {
        float[] widths = { 100f, 100f, 100f };
        Rect[] rects = new Rect[3];
        ResponsiveToolbarResult wrapped = ResponsiveToolbarLayout.Calculate(new Rect(0f, 0f, 220f, 80f), widths, 3, 2, 30f, 10f, 2, 40f, rects);
        Assert(!wrapped.HasOverflow && wrapped.RowCount == 2 && wrapped.VisibleActionCount == 3, "Toolbar should wrap when two rows fit.");

        ResponsiveToolbarResult overflow = ResponsiveToolbarLayout.Calculate(new Rect(0f, 0f, 220f, 30f), widths, 3, 2, 30f, 10f, 2, 40f, rects);
        Assert(overflow.HasOverflow && overflow.VisibleActionCount == 1, "One-row toolbar should keep a primary action and expose overflow.");
        Assert(rects[1].width == 0f && rects[2].width == 0f, "Hidden toolbar actions must have empty rects.");
    }

    private static void TestVirtualRanges()
    {
        VirtualListRange fixedRange = VirtualListLayout.GetFixedRange(1000, 20f, 200f, 100f, 1);
        Assert(fixedRange.FirstIndex == 9 && fixedRange.EndIndexExclusive == 16, "Fixed-height visible range is incorrect.");

        float[] offsets = { 0f, 10f, 30f, 60f, 100f };
        VirtualListRange dynamicRange = VirtualListLayout.GetDynamicRange(offsets, 4, 10f, 50f, 0);
        Assert(dynamicRange.FirstIndex == 1 && dynamicRange.EndIndexExclusive == 3, "Dynamic-height visible range is incorrect.");

        float[] thousandOffsets = new float[1001];
        for (int i = 0; i < 1000; i++)
        {
            thousandOffsets[i + 1] = thousandOffsets[i] + 18f + i % 5;
        }
        VirtualListRange thousandRange = VirtualListLayout.GetDynamicRange(thousandOffsets, 1000, 10000f, 600f, 2);
        Assert(thousandRange.Count > 0 && thousandRange.Count < 40, "A 1000-row dynamic list should expose only the visible rows plus overscan.");
    }

    private static void TestZeroSizeInputs()
    {
        Rect empty = new Rect(10f, 20f, 0f, 0f);
        ResponsiveSplitResult split = ResponsiveSplitLayout.Calculate(empty, new Vector2(10f, 10f), new Vector2(10f, 10f), new Vector2(20f, 20f), 5f, true);
        AssertContained(split.FirstRect, empty);
        AssertContained(split.SecondRect, empty);

        ResponsiveFormResult form = ResponsiveFormLayout.Calculate(empty, 100f, 100f, 100f, 30f, 10f, 20f);
        AssertContained(form.LabelRect, empty);
        AssertContained(form.InputRect, empty);
        AssertContained(form.ActionRect, empty);
        AssertContained(form.ErrorRect, empty);

        Rect[] rects = new Rect[1];
        ResponsiveToolbarResult toolbar = ResponsiveToolbarLayout.Calculate(empty, new[] { 100f }, 1, 1, 30f, 10f, 1, 40f, rects);
        Assert(toolbar.VisibleActionCount == 0 && toolbar.RowCount == 0, "Zero-size toolbar should not expose actions.");
        Assert(VirtualListLayout.GetFixedRange(10, 20f, 0f, 0f, 0).Count == 0, "Zero-height viewport should return an empty range.");
    }

    private static void TestStableGeometryDoesNotAllocate()
    {
        Rect[] rects = new Rect[3];
        float[] widths = { 100f, 100f, 100f };
        RunStableGeometry(widths, rects, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        RunStableGeometry(widths, rects, 10000);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert(after == before, "Stable geometry calculations should not allocate managed memory.");
    }

    private static void RunStableGeometry(float[] widths, Rect[] rects, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            ResponsiveSplitLayout.Calculate(new Rect(0f, 0f, 900f, 500f), new Vector2(300f, 200f), new Vector2(300f, 200f), new Vector2(420f, 240f), 10f, true);
            ResponsiveFormLayout.Calculate(new Rect(0f, 0f, 600f, 200f), 120f, 200f, 80f, 30f, 10f, 20f);
            ResponsiveToolbarLayout.Calculate(new Rect(0f, 0f, 220f, 80f), widths, 3, 2, 30f, 10f, 2, 40f, rects);
            VirtualListLayout.GetFixedRange(1000, 20f, 200f, 100f, 1);
            VirtualListLayout.GetDynamicRange(StableDynamicOffsets, 3, 20f, 50f, 1);
        }
    }

    private static void AssertContained(Rect value, Rect parent)
    {
        Assert(value.width >= 0f && value.height >= 0f, "Layout returned a negative Rect.");
        Assert(value.xMin >= parent.xMin && value.yMin >= parent.yMin && value.xMax <= parent.xMax && value.yMax <= parent.yMax, "Layout Rect escaped its parent.");
    }

    private static void AssertRect(Rect value, float x, float y, float width, float height, string message)
    {
        Assert(value.x == x && value.y == y && value.width == width && value.height == height, message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
