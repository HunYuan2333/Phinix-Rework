#if !PHINIX_RIMWORLD_17
using UnityEngine;
namespace PhinixClient.Framework
{
    // The maintained 1.6 engine implementation; not an old client API branch.
    internal static class ClientGameDrawing
    {
        // Unity 1.6 owns a different scrollbar geometry/skin. Preserve its native
        // controls rather than painting a 1.7 handle over a different hit target.
        internal static void Scrollbars(Rect viewport,Vector2 scroll,Rect content,Color track,Color thumb,Color hover) { }
        internal static void RoundedSurface(Rect rect, Color color, float radius)
        { UnityEngine.GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, Vector4.zero, new Vector4(radius, radius, radius, radius)); }
        internal static void RoundedBorder(Rect rect, Color color, float radius, float thickness)
        { UnityEngine.GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, new Vector4(thickness, thickness, thickness, thickness), new Vector4(radius, radius, radius, radius)); }
    }
}
#endif
