using PhinixClient.GUI;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace PhinixClient
{
    public class PlaceholderWidget : Displayable
    {
        /// <summary>
        /// Text displayed in the centre of the placeholder.
        /// </summary>
        private string text;

        /// <summary>
        /// Creates a <see cref="PlaceholderWidget"/> with the given text.
        /// </summary>
        /// <param name="text">Text to display</param>
        public PlaceholderWidget(string text = null)
        {
            this.text = text;
        }

        /// <inheritdoc />
        public override void Draw(Rect inRect)
        {
            using (HostThemeDrawing.BeginDraw())
            using (new ClientGuiScope(true))
            {
                ClientWidgets.Surface(inRect, HostThemeDrawing.Current);
                ClientWidgets.Border(inRect, HostThemeDrawing.Current);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (text != null) ClientWidgets.Label(inRect, text, HostThemeDrawing.Current, secondary: true);
            }
        }
    }
}