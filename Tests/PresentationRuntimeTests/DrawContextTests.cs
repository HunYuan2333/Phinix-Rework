using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private sealed class DrawProbe : IClientPresentationRegion
    {
        internal int Calls;
        internal Action<IClientPresentationDrawContext> Paint;
        public float Measure(float width, IClientThemeReader theme) => 12;
        public void Draw(IClientPresentationDrawContext context) { Calls++; Paint?.Invoke(context); }
    }
    private static ClientPresentationPage RegionPage(string id, long revision, params DrawProbe[] probes)
    {
        var sections = new ClientPresentationSection[probes.Length];
        for (int i = 0; i < probes.Length; i++) sections[i] = new ClientPresentationSection("section." + i, "", "", region: new ClientPresentationRegion("region." + i, probes[i])).WithPlainLayout();
        return new ClientPresentationPage(id, "", revision, sections);
    }
    private static void CheckDrawContext()
    {
        bool main = true;
        var theme = new Theme();
        var service = new ClientPresentationService(new Lifetime(), theme, () => main, null);
        var probe = new DrawProbe(); IClientPresentationDrawContext saved = null;
        int depthBefore = GUI.GroupDepth;
        using (var session = service.Open(new Module(), "draw-context"))
        {
            probe.Paint = context =>
            {
                saved = context; var bounds = context.Bounds;
                Check(bounds.X == 0 && bounds.Y == 0 && bounds.Width > 0 && bounds.Height == 12, "Region context uses local logical bounds");
                Check(!GUI.enabled && GUI.GroupDepth > depthBefore, "Host owns the passive clip group and disables input");
                long version = context.Theme.Version; theme.Change();
                Check(context.Theme.Version == version, "Drawing uses one immutable theme snapshot even during a theme change");
                int before = Widgets.FilledBoxes.Count;
                context.Progress(bounds, .25f);
                var fill = Widgets.FilledBoxes[before + 1];
                Check(Widgets.FilledBoxes.Count == before + 2 && fill.Rect.width == bounds.Width * .25f && fill.Rect.height == bounds.Height, "Controlled progress draws the expected geometry");
                Check(fill.GroupDepth == GUI.GroupDepth && !fill.Enabled, "Primitive drawing stays in the Host clip and passive state");
                before = Widgets.FilledBoxes.Count;
                context.Progress(new ClientPresentationBounds(bounds.Width, 0, 8, 8), .5f);
                context.Progress(new ClientPresentationBounds(0, 0, 0, 8), .5f);
                Check(Widgets.FilledBoxes.Count == before, "Empty or wholly outside primitives never enter native drawing");
                context.Progress(new ClientPresentationBounds(-5, 0, 20, 8), .5f);
                Check(Widgets.FilledBoxes[before + 1].Rect.x == -5 && Widgets.FilledBoxes[before + 1].Rect.width == 10, "Partial clipping preserves original progress geometry");
                Reject(() => context.Progress(new ClientPresentationBounds(0, 0, -1, 1), .5f), "Invalid draw bounds are rejected");
                Reject(() => context.Progress(bounds, float.NaN), "Nonfinite draw fractions are rejected");
                Reject(() => context.Progress(new ClientPresentationBounds(float.MaxValue, 0, float.MaxValue, 1), .5f), "Overflowing draw extents are rejected");
                main = false; Reject(() => context.Progress(bounds, .5f), "Context cannot draw off the main thread"); main = true;
            };
            session.Publish(RegionPage("draw-context", 1, probe), null, () => true);
            Event.current.type = EventType.Layout; session.Draw(new ClientPresentationBounds(0, 0, 600, 300));
            Event.current.type = EventType.MouseDown; session.Draw(new ClientPresentationBounds(0, 0, 600, 300));
            Check(probe.Calls == 0, "Layout and input do not invoke passive region drawing");
            Event.current.type = EventType.Repaint; session.Draw(new ClientPresentationBounds(0, 0, 600, 300));
            Check(probe.Calls == 1 && session.FailureCode == null && GUI.GroupDepth == depthBefore, "Normal draw returns its group and preserves the page");
            Reject(() => saved.Progress(default(ClientPresentationBounds), .5f), "Retained context is revoked after the callback");
            Reject(() => { var value = saved.Bounds; }, "Retained context cannot expose later view geometry");
            Reject(() => { var value = saved.Theme; }, "Retained context releases the captured theme");
            probe.Paint = context =>
            {
                session.Publish(RegionPage("draw-context", 2), null, () => true);
                Reject(() => context.Progress(context.Bounds, .5f), "Reentrant Publish revokes an in-flight draw context");
            };
            session.Draw(new ClientPresentationBounds(0, 0, 600, 300));
            Check(session.FailureCode == null && GUI.GroupDepth == depthBefore, "Caught expired context does not alter owner state");
        }
        Reject(() => saved.Progress(default(ClientPresentationBounds), .5f), "Closed views cannot reactivate a retained context");

        var broken = new DrawProbe(); var healthy = new DrawProbe(); IClientPresentationDrawContext failedContext = null;
        using (var session = new ClientPresentationService(new Lifetime(), new Theme(), () => true, null).Open(new Module(), "draw-errors"))
        {
            broken.Paint = context => { failedContext = context; GUI.color = new Color(.2f, .3f, .4f); GUI.enabled = true; Text.Font = GameFont.Tiny; throw new InvalidOperationException("region"); };
            healthy.Paint = context => { Check(!GUI.enabled && GUI.color.r != .2f && Text.Font != GameFont.Tiny, "A throwing region cannot corrupt the next region's drawing state"); context.Progress(context.Bounds, .5f); };
            session.Publish(RegionPage("draw-errors", 1, broken, healthy), null, () => true);
            Event.current.type = EventType.Repaint; session.Draw(new ClientPresentationBounds(0, 0, 600, 300));
            Check(broken.Calls == 1 && healthy.Calls == 1 && session.FailureCode == "RegionDrawFailed" && GUI.GroupDepth == depthBefore, "Region failure is reported and the next consumer still draws");
            Reject(() => failedContext.Progress(default(ClientPresentationBounds), 1), "Throwing callbacks also revoke their context");
            session.Draw(new ClientPresentationBounds(0, 0, 600, 300));
            Check(broken.Calls == 1 && healthy.Calls == 2, "Failed region uses fallback while healthy content remains available");
        }
        Event.current.type = EventType.Layout;
    }
}
