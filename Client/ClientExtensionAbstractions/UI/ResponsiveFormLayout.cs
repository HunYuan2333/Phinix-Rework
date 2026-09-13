using UnityEngine;

namespace PhinixClient
{
    public enum ResponsiveFormMode
    {
        Inline,
        Stacked
    }

    public struct ResponsiveFormResult
    {
        public ResponsiveFormResult(ResponsiveFormMode mode, Rect labelRect, Rect inputRect, Rect actionRect, Rect errorRect, float height)
        {
            Mode = mode;
            LabelRect = labelRect;
            InputRect = inputRect;
            ActionRect = actionRect;
            ErrorRect = errorRect;
            Height = height;
        }

        public ResponsiveFormMode Mode { get; private set; }
        public Rect LabelRect { get; private set; }
        public Rect InputRect { get; private set; }
        public Rect ActionRect { get; private set; }
        public Rect ErrorRect { get; private set; }
        public float Height { get; private set; }
    }

    public static class ResponsiveFormLayout
    {
        public static ResponsiveFormResult Calculate(
            Rect container,
            float labelWidth,
            float minimumInputWidth,
            float actionWidth,
            float rowHeight,
            float spacing,
            float errorHeight)
        {
            container = UiScreenSafeArea.Normalize(container);
            labelWidth = Mathf.Max(0f, labelWidth);
            minimumInputWidth = Mathf.Max(0f, minimumInputWidth);
            actionWidth = Mathf.Max(0f, actionWidth);
            rowHeight = Mathf.Max(0f, rowHeight);
            spacing = Mathf.Max(0f, spacing);
            errorHeight = Mathf.Max(0f, errorHeight);

            bool hasAction = actionWidth > 0f;
            float inlineWidth = labelWidth + spacing + minimumInputWidth + (hasAction ? spacing + actionWidth : 0f);
            Rect label;
            Rect input;
            Rect action;
            float contentHeight;

            if (container.width >= inlineWidth)
            {
                label = new Rect(container.xMin, container.yMin, labelWidth, rowHeight);
                action = hasAction
                    ? new Rect(container.xMax - actionWidth, container.yMin, actionWidth, rowHeight)
                    : new Rect(container.xMax, container.yMin, 0f, rowHeight);
                float inputRight = hasAction ? action.xMin - spacing : container.xMax;
                input = new Rect(label.xMax + spacing, container.yMin, Mathf.Max(0f, inputRight - label.xMax - spacing), rowHeight);
                contentHeight = rowHeight;
                Rect inlineError = CreateErrorRect(container, contentHeight, spacing, errorHeight);
                return new ResponsiveFormResult(
                    ResponsiveFormMode.Inline,
                    ClipVertically(label, container),
                    ClipVertically(input, container),
                    ClipVertically(action, container),
                    ClipVertically(inlineError, container),
                    contentHeight + ErrorContribution(spacing, errorHeight));
            }

            label = new Rect(container.xMin, container.yMin, container.width, rowHeight);
            float controlsY = label.yMax + spacing;
            action = hasAction
                ? new Rect(container.xMax - Mathf.Min(actionWidth, container.width), controlsY, Mathf.Min(actionWidth, container.width), rowHeight)
                : new Rect(container.xMax, controlsY, 0f, rowHeight);
            float inputRightStacked = hasAction ? action.xMin - spacing : container.xMax;
            input = new Rect(container.xMin, controlsY, Mathf.Max(0f, inputRightStacked - container.xMin), rowHeight);
            contentHeight = rowHeight * 2f + spacing;
            Rect stackedError = CreateErrorRect(container, contentHeight, spacing, errorHeight);
            return new ResponsiveFormResult(
                ResponsiveFormMode.Stacked,
                ClipVertically(label, container),
                ClipVertically(input, container),
                ClipVertically(action, container),
                ClipVertically(stackedError, container),
                contentHeight + ErrorContribution(spacing, errorHeight));
        }

        private static Rect CreateErrorRect(Rect container, float contentHeight, float spacing, float errorHeight)
        {
            return errorHeight > 0f
                ? new Rect(container.xMin, container.yMin + contentHeight + spacing, container.width, errorHeight)
                : new Rect(container.xMin, container.yMin + contentHeight, container.width, 0f);
        }

        private static float ErrorContribution(float spacing, float errorHeight)
        {
            return errorHeight > 0f ? spacing + errorHeight : 0f;
        }

        private static Rect ClipVertically(Rect value, Rect container)
        {
            if (value.y >= container.yMax)
            {
                value.y = container.yMax;
                value.height = 0f;
                return value;
            }

            value.height = Mathf.Min(value.height, Mathf.Max(0f, container.yMax - value.y));
            return value;
        }
    }
}
