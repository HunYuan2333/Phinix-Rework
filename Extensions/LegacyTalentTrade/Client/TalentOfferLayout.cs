using PhinixClient;
using UnityEngine;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    internal struct TalentOfferLayout
    {
        public Rect Header;
        public Rect Viewport;
        public Rect ScrollContent;
        public Rect Controls;
        public bool ControlsScroll;

        // Controls never depend on the unit count in normal-height panes.
        // In short panes controls belong to the scroll content after the units.
        public static TalentOfferLayout Calculate(Rect inner, int count, bool mine)
        {
            inner.width = Mathf.Max(0f, inner.width);
            inner.height = Mathf.Max(0f, inner.height);
            count = System.Math.Max(0, count);
            float controlsHeight = mine ? 78f : 32f;
            float headerHeight = Mathf.Min(26f, inner.height);
            bool scrollControls = inner.height < headerHeight + controlsHeight + 50f;
            float viewportHeight = Mathf.Max(0f, inner.height - headerHeight - (scrollControls ? 0f : controlsHeight));
            float listHeight = count * 56f;
            Rect viewport = new Rect(inner.x, inner.y + headerHeight, inner.width, viewportHeight);
            float contentWidth = Mathf.Max(0f, inner.width - 16f);
            return new TalentOfferLayout
            {
                Header = new Rect(inner.x, inner.y, inner.width, headerHeight),
                Viewport = viewport,
                ScrollContent = new Rect(0f, 0f, contentWidth, listHeight + (scrollControls ? controlsHeight : 0f)),
                Controls = scrollControls
                    ? new Rect(0f, listHeight, contentWidth, controlsHeight)
                    : new Rect(inner.x, viewport.yMax, inner.width, controlsHeight),
                ControlsScroll = scrollControls
            };
        }
    }
}
