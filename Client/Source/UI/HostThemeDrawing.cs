using System;
using PhinixClient.Framework;

namespace PhinixClient
{
    // Host shell and retained host displayables share the current draw palette.
    // Plugin views receive their own IClientTheme through the ordinary host services.
    internal static class HostThemeDrawing
    {
        private static IClientThemeReader drawing;
        internal static IClientThemeReader Current => drawing ?? Client.Instance.Theme.Capture();
        internal static Scope BeginDraw() => new Scope(true);
        internal struct Scope : IDisposable
        {
            private readonly IClientThemeReader previous;
            internal Scope(bool capture)
            { previous = drawing; drawing = previous ?? Client.Instance.Theme.Capture(); }
            public void Dispose() { drawing = previous; }
        }
    }
}
