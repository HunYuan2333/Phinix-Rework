// Original file provided by Longwelwind (https://github.com/Longwelwind)
// as a part of the RimWorld mod Phi (https://github.com/Longwelwind/Phi)

using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient.GUI
{
    internal class ButtonWidget : Displayable
    {
        /// <inheritdoc />
        public override bool IsFluidHeight => false;

        /// <summary>
        /// The button label.
        /// </summary>
        public string label;

        /// <summary>
        /// Whether to draw the default chunky, brown button or a transparent context menu-esque one.
        /// </summary>
        public bool drawBackground;

        /// <summary>
        /// Callback invoked when the button is clicked.
        /// </summary>
        public Action clickAction;

        public ButtonWidget(string label, Action clickAction, bool drawBackground = true)
        {
            this.label = label;
            this.drawBackground = drawBackground;
            this.clickAction = clickAction;
        }

        /// <inheritdoc />
        public override void Draw(Rect inRect)
        {
            using (HostThemeDrawing.BeginDraw())
            using (new ClientGuiScope(true))
            {
                bool clicked = drawBackground
                    ? ClientWidgets.Button(inRect, label, HostThemeDrawing.Current)
                    : ClientWidgets.Link(inRect, label, HostThemeDrawing.Current);
                if (clicked) clickAction?.Invoke();
            }
        }

        /// <inheritdoc />
        public override float CalcHeight(float width)
        {
            return Text.CalcHeight(label, width);
        }
    }
}
