using System;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class PluginStoreWindow : Window
    {
        private readonly ManagedPluginStoreView managedView;

        internal PluginStoreWindow(ManagedPluginStoreView view)
        { managedView=view; doCloseX=true; doCloseButton=false; draggable=true; resizeable=true; }


        public override Vector2 InitialSize => new Vector2(Mathf.Min(780f, UI.screenWidth), Mathf.Min(720f, UI.screenHeight));
        protected override void SetInitialSizeAndPosition() { base.SetInitialSizeAndPosition(); Clamp(); }
        public override void WindowUpdate() { base.WindowUpdate(); Clamp(); }
        private void Clamp() { windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(320f, 320f)); }
        public override void DoWindowContents(Rect inRect) { managedView?.Draw(inRect); }
    }
}
