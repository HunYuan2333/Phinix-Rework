using UnityEngine;

namespace PhinixClient.Framework
{
    public interface IUiTheme
    {
        Color PrimaryText { get; }
        Color SecondaryText { get; }
        Color Background { get; }
        Color Surface { get; }
        Color Separator { get; }
        Color HoverHighlight { get; }
        Color Pending { get; }
        Color Error { get; }
        Color Success { get; }
        Color Warning { get; }

        void RegisterColor(string key, Color defaultColor);
        Color GetColor(string key);
        bool TryGetColor(string key, out Color color);

        /// <summary>
        /// Registers a themeable float parameter (e.g. HSV saturation, layout dimensions) with a default value.
        /// </summary>
        void RegisterFloat(string key, float defaultValue);

        /// <summary>
        /// Gets a themeable float parameter by key, or returns <paramref name="defaultValue"/> if not found.
        /// </summary>
        float GetFloat(string key, float defaultValue = 0f);

        void Reload();
    }
}
