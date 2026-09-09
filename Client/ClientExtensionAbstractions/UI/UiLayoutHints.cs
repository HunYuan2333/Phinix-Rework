using UnityEngine;

namespace PhinixClient
{
    /// <summary>
    /// Describes a provider's content-size preferences without imposing a hard window size.
    /// The host may provide less space; providers must keep all output inside the supplied Rect.
    /// </summary>
    public struct UiLayoutHints
    {
        /// <summary>
        /// Conservative fallback used for providers compiled against older abstraction versions.
        /// </summary>
        public static readonly UiLayoutHints Default = new UiLayoutHints(
            new Vector2(480f, 320f),
            new Vector2(700f, 560f),
            false);

        public UiLayoutHints(Vector2 minimumContentSize, Vector2 preferredContentSize, bool supportsCompactLayout)
        {
            MinimumContentSize = new Vector2(
                Mathf.Max(0f, minimumContentSize.x),
                Mathf.Max(0f, minimumContentSize.y));
            PreferredContentSize = new Vector2(
                Mathf.Max(MinimumContentSize.x, preferredContentSize.x),
                Mathf.Max(MinimumContentSize.y, preferredContentSize.y));
            SupportsCompactLayout = supportsCompactLayout;
        }

        /// <summary>Smallest content size at which the provider's normal layout is intended to work.</summary>
        public Vector2 MinimumContentSize { get; private set; }

        /// <summary>Content size the provider would prefer when screen space is available.</summary>
        public Vector2 PreferredContentSize { get; private set; }

        /// <summary>Whether the provider has an explicit compact layout below its normal minimum size.</summary>
        public bool SupportsCompactLayout { get; private set; }
    }
}
