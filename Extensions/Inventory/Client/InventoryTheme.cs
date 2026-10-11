using System;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;

namespace Phinix.InventoryExtension.Client
{
    // One owned registration; draws read the effective palette without copying it into row caches.
    internal sealed class InventoryTheme : IDisposable
    {
        private IClientThemeRegistration registration;
        private readonly IClientThemeRegistry registry;
        private readonly IClientTheme source;
        private IClientThemeReader drawing;
        public InventoryTheme(IClientTheme current, IClientThemeRegistry registry) { source = current; this.registry = registry; }
        public IClientThemeReader Current => drawing ?? source;
        public IDisposable BeginDraw() => new DrawScope(this);
        private sealed class DrawScope : IDisposable
        {
            private readonly InventoryTheme owner;
            private readonly IClientThemeReader previous;
            public DrawScope(InventoryTheme owner)
            { this.owner = owner; previous = owner.drawing; owner.drawing = previous ?? owner.source.Capture(); }
            public void Dispose() { owner.drawing = previous; }
        }
        public Color InfoBackground => Current.GetColor("inventory.infoBackground");
        public Color InfoAccent => Current.GetColor("inventory.infoAccent");
        public Color EditorBackground => Current.GetColor("inventory.editorBackground");
        public Color NoticeBackground => Current.GetColor("inventory.noticeBackground");
        public Color ErrorBackground => Current.GetColor("inventory.errorBackground");
        public Color SelectedBackground => Current.GetColor("inventory.selectedBackground");
        public Color AlternateBackground => Current.GetColor("inventory.alternateBackground");
        public Color ChoiceBackground => Current.GetColor("inventory.choiceBackground");
        public Color DirectDeliveryAccent => Current.GetColor("inventory.directDeliveryAccent");
        public Color StoredDeliveryAccent => Current.GetColor("inventory.storedDeliveryAccent");

        public void Start(IPhinixExtensionModule module)
        {
            if (registration != null) return;
            var owned = registry.RegisterModule(module, "inventory", "Phinix_theme_owner_inventory");
            try
            {
                owned.RegisterColor("inventory.infoBackground", new Color(.18f, .38f, .58f, .16f), label: "Phinix_theme_label_inventory_infoBackground", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_infoBackground");
                owned.RegisterColor("inventory.infoAccent", new Color(.32f, .66f, .95f, 1f), label: "Phinix_theme_label_inventory_infoAccent", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_infoAccent");
                owned.RegisterColor("inventory.editorBackground", new Color(.12f, .12f, .12f, .66f), label: "Phinix_theme_label_inventory_editorBackground", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_editorBackground");
                owned.RegisterColor("inventory.noticeBackground", new Color(.55f, .42f, .12f, .2f), label: "Phinix_theme_label_inventory_noticeBackground", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_noticeBackground");
                owned.RegisterColor("inventory.errorBackground", new Color(.75f, .28f, .25f, .13f), label: "Phinix_theme_label_inventory_errorBackground", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_errorBackground");
                owned.RegisterColor("inventory.selectedBackground", new Color(.25f, .55f, .82f, .14f), fallbackKey: "ui.hover", label: "Phinix_theme_label_inventory_selectedBackground", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_selectedBackground");
                owned.RegisterColor("inventory.alternateBackground", new Color(1f, 1f, 1f, .04f), fallbackKey: "ui.surface", label: "Phinix_theme_label_inventory_alternateBackground", group: "Phinix_theme_group_inventory", description: "Phinix_theme_desc_inventory_alternateBackground");
                owned.RegisterColor("inventory.choiceBackground", new Color(.12f, .12f, .12f, .72f), label: "Phinix_theme_label_inventory_choiceBackground", group: "Phinix_theme_group_delivery", description: "Phinix_theme_desc_inventory_choiceBackground");
                owned.RegisterColor("inventory.directDeliveryAccent", new Color(.28f, .62f, .95f, 1f), fallbackKey: "ui.accent", label: "Phinix_theme_label_inventory_directDeliveryAccent", group: "Phinix_theme_group_delivery", description: "Phinix_theme_desc_inventory_directDeliveryAccent");
                owned.RegisterColor("inventory.storedDeliveryAccent", new Color(.35f, .75f, .45f, 1f), fallbackKey: "ui.success", label: "Phinix_theme_label_inventory_storedDeliveryAccent", group: "Phinix_theme_group_delivery", description: "Phinix_theme_desc_inventory_storedDeliveryAccent");
                owned.Commit();
                registration = owned;
            }
            catch { owned.Dispose(); throw; }
        }

        public void Dispose()
        {
            var owned = registration;
            registration = null;
            owned?.Dispose();
        }
    }
}
