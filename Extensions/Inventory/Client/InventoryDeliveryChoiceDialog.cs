using System;
using PhinixClient;
using UnityEngine;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    internal sealed class InventoryDeliveryChoiceDialog : Window
    {
        private const float PreferredWidth = 620f;
        private const float PreferredHeight = 500f;
        private const float MinimumWidth = 360f;
        private const float MinimumHeight = 400f;
        private readonly Action<string> choose;
        private readonly Action dismiss;
        private bool choiceMade;
        private Vector2 scroll;

        public override Vector2 InitialSize
        {
            get
            {
                Rect safe = UiScreenSafeArea.Current;
                return new Vector2(Mathf.Min(PreferredWidth, safe.width), Mathf.Min(PreferredHeight, safe.height));
            }
        }

        public InventoryDeliveryChoiceDialog(Action<string> choose, Action dismiss)
        {
            this.choose = choose;
            this.dismiss = dismiss;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ClampToSafeArea();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ClampToSafeArea();
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWrap = Text.WordWrap;
            Color oldColor = GUI.color;
            try
            {
                string intro = "Phinix_inventory_deliveryChoiceIntro".Translate();
                string directDescription = "Phinix_inventory_deliveryDirectDesc".Translate();
                string inventoryDescription = "Phinix_inventory_deliveryInventoryDesc".Translate();
                string footer = "Phinix_inventory_deliveryChoiceFooter".Translate();

                float viewWidth = Mathf.Max(1f, inRect.width - 16f);
                Text.Font = GameFont.Small;
                float introHeight = Mathf.Max(44f, Text.CalcHeight(intro, viewWidth));
                Text.Font = GameFont.Tiny;
                float descriptionWidth = Mathf.Max(1f, viewWidth - 24f);
                float descriptionHeight = Mathf.Max(
                    Text.CalcHeight(directDescription, descriptionWidth),
                    Text.CalcHeight(inventoryDescription, descriptionWidth));
                float cardHeight = Mathf.Max(120f, descriptionHeight + 84f);
                float footerHeight = Mathf.Max(42f, Text.CalcHeight(footer, viewWidth));
                float contentHeight = 40f + introHeight + 12f + cardHeight * 2f + 20f + footerHeight;
                if (contentHeight <= inRect.height)
                {
                    viewWidth = inRect.width;
                    Text.Font = GameFont.Small;
                    introHeight = Mathf.Max(44f, Text.CalcHeight(intro, viewWidth));
                    Text.Font = GameFont.Tiny;
                    descriptionWidth = Mathf.Max(1f, viewWidth - 24f);
                    descriptionHeight = Mathf.Max(
                        Text.CalcHeight(directDescription, descriptionWidth),
                        Text.CalcHeight(inventoryDescription, descriptionWidth));
                    cardHeight = Mathf.Max(120f, descriptionHeight + 84f);
                    footerHeight = Mathf.Max(42f, Text.CalcHeight(footer, viewWidth));
                    contentHeight = 40f + introHeight + 12f + cardHeight * 2f + 20f + footerHeight;
                }

                Rect view = new Rect(0f, 0f, viewWidth, Mathf.Max(contentHeight, inRect.height));
                Widgets.BeginScrollView(inRect, ref scroll, view);
                try
                {
                    float y = 0f;
                    Text.Font = GameFont.Medium;
                    Text.Anchor = TextAnchor.UpperLeft;
                    Widgets.Label(new Rect(0f, y, viewWidth, 34f),
                        "Phinix_inventory_deliveryChoiceTitle".Translate());
                    y += 40f;

                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(0f, y, viewWidth, introHeight), intro);
                    y += introHeight + 12f;

                    DrawChoiceCard(new Rect(0f, y, viewWidth, cardHeight),
                        "Phinix_inventory_deliveryDirect".Translate(),
                        directDescription,
                        "Phinix_inventory_deliveryDirectButton".Translate(),
                        new Color(0.28f, 0.62f, 0.95f, 1f),
                        InventoryDeliveryPreference.DirectDrop);
                    y += cardHeight + 10f;
                    DrawChoiceCard(new Rect(0f, y, viewWidth, cardHeight),
                        "Phinix_inventory_deliveryInventory".Translate(),
                        inventoryDescription,
                        "Phinix_inventory_deliveryInventoryButton".Translate(),
                        new Color(0.35f, 0.75f, 0.45f, 1f),
                        InventoryDeliveryPreference.Inventory);
                    y += cardHeight + 10f;

                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                    Widgets.Label(new Rect(0f, y, viewWidth, footerHeight), footer);
                }
                finally { Widgets.EndScrollView(); }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
                Text.WordWrap = oldWrap;
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (!choiceMade) dismiss?.Invoke();
        }

        private void DrawChoiceCard(Rect rect, string title, string description, string buttonText,
            Color accent, string value)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f, 0.72f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, rect.height), accent);
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Widgets.DrawBox(rect, 1);

            Rect inner = rect.ContractedBy(12f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = accent;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), title);

            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            float buttonWidth = Mathf.Min(180f, inner.width);
            Rect buttonRect = new Rect(inner.xMax - buttonWidth, inner.yMax - 32f, buttonWidth, 30f);
            Rect descriptionRect = new Rect(inner.x, inner.y + 28f, inner.width,
                Mathf.Max(0f, buttonRect.y - inner.y - 32f));
            Widgets.Label(descriptionRect, description);
            TooltipHandler.TipRegion(rect, description);
            if (Widgets.ButtonText(buttonRect, buttonText)) Select(value);
        }

        private void Select(string value)
        {
            choiceMade = true;
            choose?.Invoke(value);
            Close();
        }

        private void ClampToSafeArea()
        {
            windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(MinimumWidth, MinimumHeight));
        }
    }
}
