using System;
using PhinixClient;
using PhinixClient.Framework;
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
        private readonly InventoryTheme theme;
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

        public InventoryDeliveryChoiceDialog(InventoryTheme theme, Action<string> choose, Action dismiss)
        {
            this.theme = theme;
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
            using (theme.BeginDraw())
            using (new ClientGuiScope(true))
            {
                GUI.color = theme.Current.PrimaryText;
                DrawContents(inRect);
            }
        }

        private void DrawContents(Rect inRect)
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
                    ClientWidgets.Label(new Rect(0f, y, viewWidth, 34f),
                        "Phinix_inventory_deliveryChoiceTitle".Translate(), theme.Current);
                    y += 40f;

                    Text.Font = GameFont.Small;
                    ClientWidgets.Label(new Rect(0f, y, viewWidth, introHeight), intro, theme.Current);
                    y += introHeight + 12f;

                    DrawChoiceCard(new Rect(0f, y, viewWidth, cardHeight),
                        "Phinix_inventory_deliveryDirect".Translate(),
                        directDescription,
                        "Phinix_inventory_deliveryDirectButton".Translate(),
                        theme.DirectDeliveryAccent,
                        InventoryDeliveryPreference.DirectDrop);
                    y += cardHeight + 10f;
                    DrawChoiceCard(new Rect(0f, y, viewWidth, cardHeight),
                        "Phinix_inventory_deliveryInventory".Translate(),
                        inventoryDescription,
                        "Phinix_inventory_deliveryInventoryButton".Translate(),
                        theme.StoredDeliveryAccent,
                        InventoryDeliveryPreference.Inventory);
                    y += cardHeight + 10f;

                    Text.Font = GameFont.Tiny;
                    ClientWidgets.Label(new Rect(0f, y, viewWidth, footerHeight), footer, theme.Current, secondary: true);
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
            ClientWidgets.Fill(rect, theme.ChoiceBackground);
            ClientWidgets.Fill(new Rect(rect.x, rect.y, 4f, rect.height), accent);
            if (Mouse.IsOver(rect)) ClientWidgets.Fill(rect, theme.Current.HoverHighlight);
            ClientWidgets.Border(rect, theme.Current);

            Rect inner = rect.ContractedBy(12f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            ClientWidgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), title, accent);
            Text.Font = GameFont.Tiny;
            float buttonWidth = Mathf.Min(180f, inner.width);
            Rect buttonRect = new Rect(inner.xMax - buttonWidth, inner.yMax - 32f, buttonWidth, 30f);
            Rect descriptionRect = new Rect(inner.x, inner.y + 28f, inner.width,
                Mathf.Max(0f, buttonRect.y - inner.y - 32f));
            ClientWidgets.Label(descriptionRect, description, theme.Current, secondary: true);
            TooltipHandler.TipRegion(rect, description);
            if (ClientWidgets.Button(buttonRect, buttonText, theme.Current)) Select(value);
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
