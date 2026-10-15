#if PHINIX_RIMWORLD_17
using System;
using UnityEngine;
using UnityStubs.Gui;
namespace PhinixClient.Framework
{
    // Direct rev1248 backend API. No GUI.DrawTexture border/radius emulation or business state.
    internal static class ClientGameDrawing
    {
        private static IGuiBackend PaintingBackend()
        { return Event.current != null && Event.current.type == EventType.Repaint ? GuiBackend.Current : null; }
        // Paint only after the native scroll group ends. Native controls retain IDs,
        // wheel routing, dragging and clipping; no global skin/backend mutation.
        internal static void Scrollbars(Rect viewport,Vector2 scroll,Rect content,Color track,Color thumb,Color hover)
        {
            var backend=PaintingBackend();
            if(backend==null || viewport.width<=0 || viewport.height<=0) return;
            for(int axis=0;axis<2;axis++)
            {
                bool vertical=axis==0;
                float visible=vertical?viewport.height:viewport.width,total=vertical?content.height:content.width;
                if(total<=visible || float.IsNaN(total) || float.IsInfinity(total)) continue;
                float length=Mathf.Min(visible,Mathf.Max(20,visible*visible/total));
                float offset=Mathf.Clamp(vertical?scroll.y:scroll.x,0,total-visible)/(total-visible)*(visible-length);
                var rail=vertical?new Rect(viewport.xMax-16,viewport.y,16,visible):new Rect(viewport.x,viewport.yMax-16,visible,16);
                var handle=vertical?new Rect(rail.x+2,rail.y+offset,12,length):new Rect(rail.x+offset,rail.y+2,length,12);
                backend.FillRect(rail,track);
                backend.FillRectRounded(handle,rail.Contains(Event.current.mousePosition)?hover:thumb,6);
            }
        }
        internal static void RoundedSurface(Rect rect, Color color, float radius)
        {
            var backend = PaintingBackend();
            if (backend != null && rect.width > 0 && rect.height > 0) backend.FillRectRounded(rect, color, radius);
        }
        internal static void RoundedBorder(Rect rect, Color color, float radius, float thickness)
        {
            var backend = PaintingBackend();
            if (backend == null || rect.width <= 0 || rect.height <= 0 || thickness <= 0) return;
            float innerWidth = rect.width - 2 * thickness, innerHeight = rect.height - 2 * thickness;
            if (innerWidth <= 0 || innerHeight <= 0) { backend.FillRectRounded(rect, color, radius); return; }
            int segments = (int)Math.Max(4, Math.Min(32, Math.Ceiling(Math.PI / 2 * Math.Sqrt(Math.Max(0, radius) / .5))));
            int count = 4 * (segments + 1);
            var outer = new Vector2[count]; var inner = new Vector2[count];
            var inset = new Rect(rect.x + thickness, rect.y + thickness, innerWidth, innerHeight);
            float innerRadius = Math.Max(0, radius - thickness);
            for (int corner = 0; corner < 4; corner++)
            {
                var oc = Center(rect, radius, corner); var ic = Center(inset, innerRadius, corner);
                for (int step = 0; step <= segments; step++)
                {
                    double angle = (-1 + corner) * Math.PI / 2 + step * Math.PI / (2 * segments);
                    float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle); int index = corner * (segments + 1) + step;
                    outer[index] = new Vector2(oc.x + dx * radius, oc.y + dy * radius);
                    inner[index] = new Vector2(ic.x + dx * innerRadius, ic.y + dy * innerRadius);
                }
            }
            // Immutable after submission: do not reuse a list a backend might retain.
            var vertices = new GuiVertex[count * 6]; Color32 packed = color;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count, offset = i * 6;
                vertices[offset] = Vertex(outer[i], packed); vertices[offset + 1] = Vertex(outer[next], packed); vertices[offset + 2] = Vertex(inner[next], packed);
                vertices[offset + 3] = Vertex(outer[i], packed); vertices[offset + 4] = Vertex(inner[next], packed); vertices[offset + 5] = Vertex(inner[i], packed);
            }
            // rev1248 PrimPosition(false) uses ToScreen, the same transform as
            // rounded fills. GuiToScreenPoint applies group offsets in a
            // different order and must not substitute for that drawing path.
            backend.DrawPrimitives(GL.TRIANGLES, vertices, null, false);
        }
        private static Vector2 Center(Rect rect, float radius, int corner)
        { return new Vector2(corner == 0 || corner == 1 ? rect.xMax - radius : rect.xMin + radius,
            corner == 0 || corner == 3 ? rect.yMin + radius : rect.yMax - radius); }
        private static GuiVertex Vertex(Vector2 point, Color32 color)
        { return new GuiVertex { position = point, uv = Vector2.zero, color = color }; }
    }
}
#endif
