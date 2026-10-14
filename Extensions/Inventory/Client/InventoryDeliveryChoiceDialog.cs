using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    // Owner choice/setting semantics retained; all contents use the same public Host renderer.
    internal sealed class InventoryDeliveryChoiceDialog : Window, IDisposable
    {
        private readonly Action<string> choose;
        private readonly Action dismiss;
        private readonly IClientPresentationSession view;
        private readonly IClientExtensionPointScope scope;
        private readonly IClientExtensionPointService points;
        private readonly IClientActionPoint<InventoryPresentationActionContext> point;
        private bool choiceMade, closed;
        private object language;
        private long revision, pointRevision = -1;
        private ClientActionExecutionResult result;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(620, UiScreenSafeArea.Current.width), Mathf.Min(500, UiScreenSafeArea.Current.height));
        public InventoryDeliveryChoiceDialog(IPhinixExtensionModule owner, IClientPresentationService renderer,
            IClientExtensionPointService points, Action<string> choose, Action dismiss)
        {
            this.choose = choose; this.dismiss = dismiss; this.points = points;
            doCloseX = true; closeOnAccept = false; closeOnCancel = true; closeOnClickedOutside = false;
            absorbInputAroundWindow = true; draggable = true; resizeable = true;
            view = renderer.Open(owner, "inventory-delivery-choice");
            try
            {
                scope = points.ForModule(owner);
                point = scope.DefineActions<InventoryPresentationActionContext>(InventoryPresentationActionContext.DeliveryChoicePointId);
                scope.ContributeActions<InventoryPresentationActionContext>(InventoryPresentationActionContext.DeliveryChoicePointId, context => new[] {
                    new ClientActionDefinition("direct", L("deliveryDirectButton"), () => Select(InventoryDeliveryPreference.DirectDrop)),
                    new ClientActionDefinition("inventory", L("deliveryInventoryButton"), () => Select(InventoryDeliveryPreference.Inventory))
                });
                Refresh();
            }
            catch { scope?.Dispose(); view.Dispose(); throw; }
        }
        private static string L(string key) => ("Phinix_inventory_" + key).Translate();
        protected override void SetInitialSizeAndPosition() { base.SetInitialSizeAndPosition(); Clamp(); }
        public override void WindowUpdate()
        {
            base.WindowUpdate(); Clamp();
            if (view.IsClosed) { Close(); return; }
            Refresh();
        }
        private void Clamp() { windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(360, 400)); }
        private void Refresh()
        {
            var nextLanguage = LanguageDatabase.activeLanguage; long nextPoints = points.Revision;
            var nextResult = view.LastActionResult;
            if (ReferenceEquals(language, nextLanguage) && nextPoints == pointRevision && ReferenceEquals(nextResult, result)) return;
            Func<bool> current = () => !closed && !choiceMade && !view.IsClosed && points.Revision == nextPoints && ReferenceEquals(LanguageDatabase.activeLanguage, nextLanguage);
            var set = point.Collect(new InventoryPresentationActionContext(0, "DeliveryChoice", null, 0, 1, "", ""), current);
            try
            {
                var direct = new List<ClientPresentationAction>(); var stored = new List<ClientPresentationAction>(); var extras = new List<ClientPresentationAction>();
                foreach (var action in set.Actions)
                {
                    var binding = new ClientPresentationAction(action.OwnerId + "." + action.Id, action);
                    if (action.Id == "direct" && action.OwnerId == "builtin.inventory") direct.Add(binding);
                    else if (action.Id == "inventory" && action.OwnerId == "builtin.inventory") stored.Add(binding);
                    else extras.Add(binding);
                }
                view.Publish(new ClientPresentationPage("inventory-delivery-choice", L("deliveryChoiceTitle"), ++revision, new[] {
                    new ClientPresentationSection("intro", "", L("deliveryChoiceIntro")),
                    new ClientPresentationSection("direct", L("deliveryDirect"), L("deliveryDirectDesc"), direct, surfaceKey: "inventory.choiceBackground", summaryColorKey: "inventory.directDeliveryAccent"),
                    new ClientPresentationSection("stored", L("deliveryInventory"), L("deliveryInventoryDesc"), stored, surfaceKey: "inventory.choiceBackground", summaryColorKey: "inventory.storedDeliveryAccent"),
                    new ClientPresentationSection("footer", "", L("deliveryChoiceFooter"), extras)
                }), set, current);
            }
            catch { set.Dispose(); throw; }
            language = nextLanguage; pointRevision = nextPoints; result = nextResult;
        }
        public override void DoWindowContents(Rect rect) { view.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height)); }
        private void Select(string value) { choiceMade = true; choose?.Invoke(value); Close(); }
        public override void PostClose() { try { Dispose(); } finally { base.PostClose(); } }
        public void Dispose()
        {
            if (closed) return; closed = true;
            try { view.Dispose(); }
            finally { try { scope.Dispose(); } finally { if (!choiceMade) dismiss?.Invoke(); } }
        }
    }
}
