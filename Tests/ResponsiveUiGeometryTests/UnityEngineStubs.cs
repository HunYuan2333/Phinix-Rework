namespace UnityEngine
{
    public struct Vector2
    {
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float x;
        public float y;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float x;
        public float y;
        public float width;
        public float height;
        public float xMin { get => x; set { float oldMax = xMax; x = value; width = oldMax - x; } }
        public float yMin { get => y; set { float oldMax = yMax; y = value; height = oldMax - y; } }
        public float xMax { get => x + width; set => width = value - x; }
        public float yMax { get => y + height; set => height = value - y; }
    }

    public static class Mathf
    {
        public static float Max(float first, float second) => first > second ? first : second;
        public static float Min(float first, float second) => first < second ? first : second;
        public static float Clamp(float value, float minimum, float maximum)
            => value < minimum ? minimum : value > maximum ? maximum : value;
    }
}

namespace Verse
{
    public static class UI
    {
        public static int screenWidth;
        public static int screenHeight;
    }
}
