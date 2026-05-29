using System;
using UnityEngine;
using Verse;

namespace PhinixClient.GUI
{
    public static class GUIUtils
    {
        private static TextAnchor textAnchor;
        private static GameFont fontSize;

        public static Rect TranslatedBy(this Rect rect, float xOffset = 0f, float yOffset = 0f)
        {
            Vector2 newPosition = new Vector2(rect.x + xOffset, rect.y + yOffset);
            return new Rect(newPosition, rect.size);
        }

        public static void SaveTextFormat()
        {
            textAnchor = Text.Anchor;
            fontSize = Text.Font;
        }

        public static void RestoreTextFormat()
        {
            Text.Anchor = textAnchor;
            Text.Font = fontSize;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static string ToStringSI(this int value, int precision = 2)
        {
            char[] prefixes = { 'k', 'M', 'G', 'T', 'P', 'E', 'Z' };

            if (value < 1000) return value.ToString();

            double quotient = value;
            foreach (char prefix in prefixes)
            {
                quotient /= 1000d;
                if (quotient < 1000) return $"{Math.Round(quotient, precision)}{prefix}";
            }

            return $"{Math.Round(quotient, precision)}{prefixes[prefixes.Length]}";
        }
    }
}
