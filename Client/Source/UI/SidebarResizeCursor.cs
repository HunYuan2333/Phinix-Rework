using UnityEngine;
using Verse;

namespace PhinixClient
{
    // Only the active shell divider borrows the process cursor. ContentFinder owns the texture.
    internal sealed class SidebarResizeCursor
    {
        private Texture2D texture;
        private bool loaded, owned;
        internal void Update(bool active)
        {
            if (!active) { Release(); return; }
            if (owned) return;
            if (!loaded)
            {
                loaded = true;
                texture = ContentFinder<Texture2D>.Get("UI/Phinix/ResizeHorizontal", false);
                if (texture == null) Log.Warning("[Phinix] Sidebar resize cursor texture is unavailable.");
            }
            if (texture == null) return;
            Cursor.SetCursor(texture, new Vector2(16, 16), CursorMode.Auto);
            owned = true;
        }
        internal void Release()
        {
            if (!owned) return;
            owned = false;
            if (Prefs.CustomCursorEnabled) CustomCursor.Activate();
            else CustomCursor.Deactivate();
        }
    }
}
