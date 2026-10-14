using UnityEngine;

namespace PhinixClient.Framework
{
    // Explicit engine bridge for current drawing consumers. No validation or color-space conversion.
    public static class ClientColorValues
    {
        public static Color ToUnityColor(this ClientRgba value) => new Color(value.Red, value.Green, value.Blue, value.Alpha);
        public static ClientRgba ToClientRgba(this Color value) => new ClientRgba(value.r, value.g, value.b, value.a);
    }
}
