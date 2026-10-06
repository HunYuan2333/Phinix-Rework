using UnityEngine;

namespace Phinix.PluginStore
{
    internal struct StoreBrowserLayout
    {
        public Rect Management;
        public Rect Selection;
        public Rect Plan;
        public Rect Feedback;
        public Rect Body;

        public static StoreBrowserLayout Calculate(Rect container, float selectionHeight, float feedbackHeight)
        {
            Rect remaining = new Rect(container.x, container.y, Mathf.Max(0f, container.width), Mathf.Max(0f, container.height));
            var result = new StoreBrowserLayout();
            result.Management = Take(ref remaining, 30f);
            result.Selection = Take(ref remaining, Mathf.Clamp(selectionHeight, 24f, 48f));
            result.Plan = Take(ref remaining, 30f);
            result.Feedback = Take(ref remaining, Mathf.Clamp(feedbackHeight, 24f, 72f));
            result.Body = remaining;
            return result;
        }

        public static float PackageListHeight(int count)
        {
            return Mathf.Clamp(Mathf.Max(1f, count) * 32f, 32f, 220f);
        }

        private static Rect Take(ref Rect remaining, float desiredHeight)
        {
            float height = Mathf.Min(desiredHeight, remaining.height);
            Rect row = new Rect(remaining.x, remaining.y, remaining.width, height);
            float used = Mathf.Min(remaining.height, height + 4f);
            remaining = new Rect(remaining.x, remaining.y + used, remaining.width,
                Mathf.Max(0f, remaining.height - used));
            return row;
        }
    }
}
