using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    // Adapter for the existing quick/full settings surfaces. One public Host page, one setting key.
    internal sealed class InventorySettingsPanel : IClientSettingsPanelProvider, IClientQuickSettingsPanelProvider, IDisposable
    {
        private readonly InventoryTheme theme;
        private IClientPresentationSession view;
        private IClientExtensionPointScope scope;
        private IClientActionPoint<InventoryPresentationActionContext> point;
        private IClientExtensionPointService points;
        private IClientSettingsContext settings;
        private string mode;
        private object language;
        private long revision, pointRevision = -1;
        private ClientActionExecutionResult result;
        public InventorySettingsPanel(InventoryTheme theme) { this.theme = theme; }
        public string SectionId => "inventory.delivery";
        public float Order => 115;
        public bool IsVisible(IClientSettingsContext settings) => true;
        internal void Start(IPhinixExtensionModule owner, IClientPresentationService renderer, IClientExtensionPointService points, IClientSettingsContext settings)
        {
            this.points = points; this.settings = settings;
            view = renderer.Open(owner, "inventory-settings");
            try
            {
                scope = points.ForModule(owner);
                point = scope.DefineActions<InventoryPresentationActionContext>(InventoryPresentationActionContext.SettingsPointId);
                scope.ContributeActions<InventoryPresentationActionContext>(InventoryPresentationActionContext.SettingsPointId, context => new[] {
                    new ClientActionDefinition("direct", L("deliveryDirectButton"), () => settings.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.DirectDrop), context.Mode == InventoryDeliveryPreference.DirectDrop ? L("deliveryDirect") : null),
                    new ClientActionDefinition("inventory", L("deliveryInventoryButton"), () => settings.Set(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.Inventory), context.Mode == InventoryDeliveryPreference.Inventory ? L("deliveryInventory") : null)
                });
            }
            catch { Dispose(); throw; }
        }
        private static string L(string key) => ("Phinix_inventory_" + key).Translate();
        private string CurrentMode => settings.Get(InventoryDeliveryPreference.SettingKey, InventoryDeliveryPreference.DirectDrop);
        // Existing provider contract only has Draw. Refresh pure projection here on change; no settings write until explicit Host action.
        private void Refresh()
        {
            if (view == null || view.IsClosed) return;
            string nextMode = CurrentMode; var nextLanguage = LanguageDatabase.activeLanguage; long nextPoints = points.Revision; var nextResult = view.LastActionResult;
            if (nextMode == mode && ReferenceEquals(nextLanguage, language) && nextPoints == pointRevision && ReferenceEquals(nextResult, result)) return;
            Func<bool> current = () => !view.IsClosed && nextMode == CurrentMode && ReferenceEquals(LanguageDatabase.activeLanguage, nextLanguage) && points.Revision == nextPoints && ReferenceEquals(view.LastActionResult, nextResult);
            var actions = point.Collect(new InventoryPresentationActionContext(0, nextMode, null, 0, 1, "", ""), current);
            try
            {
                var bindings = new List<ClientPresentationAction>();
                foreach (var action in actions.Actions) bindings.Add(new ClientPresentationAction(action.OwnerId + "." + action.Id, action));
                view.Publish(new ClientPresentationPage("inventory-settings", "", ++revision, new[] {
                    new ClientPresentationSection("choice", L("deliverySettingTitle"),
                        L(InventoryDeliveryPreference.UsesInventory(nextMode) ? "deliveryInventory" : "deliveryDirect") + "\n\n" + L("deliveryDirectDesc") + "\n\n" + L("deliveryInventoryDesc") + "\n\n" + L("deliverySettingDesc"), bindings, nextResult != null && !nextResult.Completed ? new ClientPresentationStatus(L("operationFailed"), ClientPresentationTone.Error) : null)
                }), actions, current);
            }
            catch { actions.Dispose(); throw; }
            mode = nextMode; language = nextLanguage; pointRevision = nextPoints; result = nextResult;
        }
        public float GetQuickSettingsHeight(float width) => 320;
        public void DrawQuickSettings(Rect rect, IClientSettingsContext settings) { Refresh(); view?.Draw(rect); }
        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        { var rect = listing.GetRect(GetQuickSettingsHeight(listing.ColumnWidth)); DrawQuickSettings(rect, settings); listing.Gap(8); }
        public void Dispose() { var old = view; view = null; old?.Dispose(); scope?.Dispose(); scope = null; point = null; }
    }
}
