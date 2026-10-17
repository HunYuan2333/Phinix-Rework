using System;
using System.Globalization;
using PhinixClient.Framework;

namespace PhinixClient
{
    internal static class NicknameDisplayMarkup
    {
        private static string Channel(float value) => ((int)Math.Round((value > 0 ? Math.Min(1, value) : 0) * 255, MidpointRounding.AwayFromZero)).ToString("X2", CultureInfo.InvariantCulture);
        internal static string Name(string raw, ClientRgba color) => "<color=#" + Channel(color.Red) + Channel(color.Green) + Channel(color.Blue) + Channel(color.Alpha) + ">" + (raw ?? "") + "</color>";
        // Nested explicit color tags override the display fallback. Never persist this markup.
        internal static string Identity(string prefix, string raw, ClientRgba text, ClientRgba accent) => Name(prefix, text) + " " + Name(raw, accent);
    }
}
