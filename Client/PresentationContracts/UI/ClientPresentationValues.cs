namespace PhinixClient.Framework
{
    public enum ClientButtonKind { Normal, Primary, Danger }

    /// <summary>Logical UI coordinates supplied by a view owner. No engine units or handles.</summary>
    public readonly struct ClientPresentationBounds
    {
        public ClientPresentationBounds(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
    }

    /// <summary>Raw RGBA channels. No color-space conversion or implicit clamping.</summary>
    public readonly struct ClientRgba
    {
        public ClientRgba(float red, float green, float blue, float alpha = 1f)
        { Red = red; Green = green; Blue = blue; Alpha = alpha; }
        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
        public float Alpha { get; }
    }
}
