using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    internal sealed class InventorySettingsPanel : IClientSettingsPanelProvider, IClientQuickSettingsPanelProvider
    {
        private readonly InventoryTheme theme;
        public InventorySettingsPanel(InventoryTheme theme) { this.theme = theme; }

        private float cachedWidth = -1f;
        private object cachedLanguage;
        private string title;
        private string directTitle;
        private string directDescription;
        private string inventoryTitle;
        private string inventoryDescription;
        private string footer;
        private float titleHeight;
        private float directHeight;
        private float inventoryHeight;
        private float footerHeight;

        public string SectionId => "inventory.delivery";
        public float Order => 115f;
        public bool IsVisible(IClientSettingsContext settings) => true;

        public float GetQuickSettingsHeight(float width)
        {
            Rebuild(width);
            return titleHeight + 4f + directHeight + 4f + inventoryHeight + 6f + footerHeight + 8f;
        }

        public void DrawQuickSettings(Rect rect, IClientSettingsContext settings)
        {
            using (theme.BeginDraw())
            using (new ClientGuiScope(true)) DrawQuickSettingsContents(rect, settings);
        }

        private void DrawQuickSettingsContents(Rect rect, IClientSettingsContext settings)
        {
            Rebuild(rect.width);
            GameFont oldFont = Text.Font;
            Color oldColor = GUI.color;
            try
            {
                float y = rect.y;
                Text.Font = GameFont.Medium;
                ClientWidgets.Label(new Rect(rect.x, y, rect.width, titleHeight), title, theme.Current);
                y += titleHeight + 4f;

                string mode = settings.Get(InventoryDeliveryPreference.SettingKey,
                    InventoryDeliveryPreference.DirectDrop);
                if (DrawOption(new Rect(rect.x, y, rect.width, directHeight), directTitle,
                    directDescription, !InventoryDeliveryPreference.UsesInventory(mode)))
                {
                    settings.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.DirectDrop);
                    mode = InventoryDeliveryPreference.DirectDrop;
                }

                y += directHeight + 4f;
                if (DrawOption(new Rect(rect.x, y, rect.width, inventoryHeight), inventoryTitle,
                    inventoryDescription, InventoryDeliveryPreference.UsesInventory(mode)))
                {
                    settings.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.Inventory);
                }

                y += inventoryHeight + 6f;
                Text.Font = GameFont.Tiny;
                Rect footerRect = new Rect(rect.x, y, rect.width, footerHeight);
                ClientWidgets.Label(footerRect, footer, theme.Current, secondary: true);
                TooltipHandler.TipRegion(footerRect, footer);
            }
            finally
            {
                Text.Font = oldFont;
                GUI.color = oldColor;
            }
        }

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            using (theme.BeginDraw())
            using (new ClientGuiScope(true)) DrawSettingsContents(listing, settings);
        }

        private void DrawSettingsContents(Listing_Standard listing, IClientSettingsContext settings)
        {
            Rebuild(listing.ColumnWidth);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            GUI.color = theme.Current.PrimaryText;
            listing.Label(title);
            Text.Font = oldFont;
            listing.Gap(4f);

            string mode = settings.Get(InventoryDeliveryPreference.SettingKey,
                InventoryDeliveryPreference.DirectDrop);
            if (DrawOption(listing.GetRect(directHeight), directTitle, directDescription,
                !InventoryDeliveryPreference.UsesInventory(mode)))
                settings.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.DirectDrop);

            listing.Gap(4f);
            if (DrawOption(listing.GetRect(inventoryHeight), inventoryTitle, inventoryDescription,
                InventoryDeliveryPreference.UsesInventory(mode)))
                settings.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.Inventory);

            listing.Gap(6f);
            Text.Font = GameFont.Tiny;
            Rect footerRect = listing.GetRect(footerHeight);
            ClientWidgets.Label(footerRect, footer, theme.Current, secondary: true);
            TooltipHandler.TipRegion(footerRect, footer);
            Text.Font = oldFont;
            listing.Gap(8f);
        }

        private bool DrawOption(Rect rect, string optionTitle, string description, bool selected)
        {
            return ClientWidgets.RadioOption(rect, optionTitle, description, selected,
                theme.Current, theme.SelectedBackground);
        }

        private void Rebuild(float width)
        {
            using (theme.BeginDraw())
            using (new ClientGuiScope(true)) RebuildContents(width);
        }

        private void RebuildContents(float width)
        {
            width = Mathf.Max(1f, width);
            object language = LanguageDatabase.activeLanguage;
            if (Mathf.Approximately(width, cachedWidth) && ReferenceEquals(language, cachedLanguage)) return;
            cachedWidth = width;
            cachedLanguage = language;
            title = "Phinix_inventory_deliverySettingTitle".Translate();
            directTitle = "Phinix_inventory_deliveryDirect".Translate();
            directDescription = "Phinix_inventory_deliveryDirectDesc".Translate();
            inventoryTitle = "Phinix_inventory_deliveryInventory".Translate();
            inventoryDescription = "Phinix_inventory_deliveryInventoryDesc".Translate();
            footer = "Phinix_inventory_deliverySettingDesc".Translate();

            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            titleHeight = Mathf.Max(32f, Text.CalcHeight(title, width));
            Text.Font = GameFont.Tiny;
            float descriptionWidth = Mathf.Max(1f, width - 42f);
            directHeight = Mathf.Max(62f, 34f + Text.CalcHeight(directDescription, descriptionWidth));
            inventoryHeight = Mathf.Max(62f, 34f + Text.CalcHeight(inventoryDescription, descriptionWidth));
            footerHeight = Mathf.Max(28f, Text.CalcHeight(footer, width));
            Text.Font = oldFont;
        }
    }
}
