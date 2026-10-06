using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    /// <summary>Host-owned extension controls, available to every extension through a general service.</summary>
    internal sealed class ExtensionManagerWindow : Window
    {
        private readonly ExtensionManagerTab content = new ExtensionManagerTab();

        public ExtensionManagerWindow()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(860f, UI.screenWidth), Mathf.Min(680f, UI.screenHeight));

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            Clamp();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            Clamp();
        }

        public override void PostClose()
        { content.Close(); base.PostClose(); }

        private void Clamp()
        {
            windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(320f, 320f));
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = UnityEngine.GUI.color;
            bool oldEnabled = UnityEngine.GUI.enabled;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.WordWrap = true;
                UnityEngine.GUI.color = Color.white;
                float titleHeight = Mathf.Min(32f, Mathf.Max(0f, inRect.height));
                Widgets.Label(new Rect(inRect.x, inRect.y, Mathf.Max(0f, inRect.width), titleHeight),
                    "Phinix_extensions_management".Translate());
                content.Draw(new Rect(inRect.x, inRect.y + titleHeight, Mathf.Max(0f, inRect.width),
                    Mathf.Max(0f, inRect.height - titleHeight)));
            }
            finally
            {
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
                UnityEngine.GUI.color = oldColor;
                UnityEngine.GUI.enabled = oldEnabled;
            }
        }
    }
}
