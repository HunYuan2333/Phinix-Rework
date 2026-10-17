using System;
using System.Collections.Generic;
using System.Globalization;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    // Owner projection through the existing public renderer/action/confirmation/window services.
    internal sealed class InventoryPlansWindow : Window
    {
        private const int PageSize = 10;
        private readonly BuiltInInventoryClientExtension inventory;
        private readonly IPhinixExtensionModule module;
        private readonly IClientPresentationService renderer;
        private readonly IClientExtensionPointService points;
        private readonly IClientActionPoint<InventoryPresentationActionContext> point;
        private readonly Func<bool> ownerClosed;
        private readonly Action closed;
        private readonly string pageId = "inventory-plans." + Guid.NewGuid().ToString("N");
        private readonly InventoryIconCache icons = new InventoryIconCache();
        private IClientPresentationSession view;
        private IReadOnlyList<InventoryEntry> entries = Array.Empty<InventoryEntry>();
        private IReadOnlyList<InventorySchedule> schedules = Array.Empty<InventorySchedule>();
        private readonly Dictionary<string, InventoryEntry> entryIndex = new Dictionary<string, InventoryEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> available = new Dictionary<string, long>(StringComparer.Ordinal);
        private long loadedVersion = -1;
        private object loadedLanguage;
        private bool dataDirty = true;
        private readonly Dictionary<string, InventoryItemPresentation> presentations = new Dictionary<string, InventoryItemPresentation>(StringComparer.Ordinal);
        private long revision, publishedVersion = -1, publishedPoints = -1, selectedUnits;
        private object game, language;
        private ClientActionExecutionResult result;
        private bool dirty = true, released, details, unitReview, scrollTop = true, publishedBlocked, noticeFailed;
        private string selected, quantity = "1", tick = "30000", notice;
        private int page;
        internal InventoryPlansWindow(BuiltInInventoryClientExtension inventory, InventoryTheme theme, IPhinixExtensionModule module,
            IClientPresentationService renderer, IClientExtensionPointService points, IClientActionPoint<InventoryPresentationActionContext> point,
            Func<bool> ownerClosed, Action closed) : base(new ClientThemeWindowDrawing(() => theme?.Current))
        {
            this.inventory = inventory; this.module = module; this.renderer = renderer; this.points = points; this.point = point;
            this.ownerClosed = ownerClosed; this.closed = closed;
            doCloseX = true; closeOnAccept = false; closeOnCancel = true; absorbInputAroundWindow = true; draggable = true; resizeable = true;
        }
        public override Vector2 InitialSize => new Vector2(Mathf.Min(700, UiScreenSafeArea.Current.width), Mathf.Min(selected == null ? 520 : 360, UiScreenSafeArea.Current.height));
        private static string L(string key) => ("Phinix_inventory_" + key).Translate();
        internal void Invalidate() { dirty = true; dataDirty = true; }
        internal void Select(string entryId)
        {
            selected = entryId; scrollTop = true; details = false; selectedUnits = 0; unitReview = false; notice = null; noticeFailed = false;
            if (entryId != null)
            {
                InventorySchedule existing = null;
                foreach (var plan in inventory.GetSchedules()) if (plan.EntryId == entryId) { existing = plan; break; }
                quantity = (existing?.QuantityPerRun ?? 1).ToString(CultureInfo.InvariantCulture);
                tick = (existing?.DayTick ?? 30000).ToString(CultureInfo.InvariantCulture);
            }
            dirty = true;
        }
        public override void PreOpen()
        {
            base.PreOpen(); game = Current.Game;
            try { view = renderer.Open(module, pageId); Refresh(); }
            catch { Release(); throw; }
        }
        public override void WindowUpdate()
        {
            base.WindowUpdate(); windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(360, 260));
            if (ownerClosed() || released || !ReferenceEquals(game, Current.Game) || view == null || view.IsClosed) { Close(); return; }
            var pending = new List<ClientPresentationInput>();
            while (view.TryDequeueInput(out var input)) pending.Add(input);
            foreach (var input in pending)
            {
                if (input.Revision != revision) continue;
                if (input.Id == "quantity") { if (quantity != input.Value) unitReview = false; quantity = input.Value; }
                else if (input.Id == "tick") tick = input.Value;
                dirty = true;
            }
            Refresh();
        }
        public override void DoWindowContents(Rect rect) => view?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height));
        public override void PostClose() { try { Release(); } finally { closed(); base.PostClose(); } }
        internal void Release() { if (released) return; released = true; var old = view; view = null; old?.Dispose(); icons.Clear(); }
        private InventoryEntry Entry(string id) => id != null && entryIndex.TryGetValue(id, out var entry) ? entry : null;
        private InventoryItemPresentation Presentation(InventoryEntry entry)
        {
            if (!presentations.TryGetValue(entry.EntryId, out var value)) { value = inventory.PresentItem(entry); presentations.Add(entry.EntryId, value); }
            return value;
        }
        private InventorySchedule Plan(string id) { foreach (var plan in schedules) if (plan.EntryId == id) return plan; return null; }
        private long Available(string id) => id != null && available.TryGetValue(id, out long value) ? value : 0;
        private bool IsBlocked => inventory.HasPendingRecovery || inventory.HasPendingExtraction || inventory.Fault != null || inventory.BatchProgress?.Running == true;
        private string Blocked => IsBlocked ? L("plansBlocked") : null;
        private string QuantityError(long available) => unitReview ? L("inlineUnitChanged") : available < 1 ? L("inlineUnavailable") :
            !long.TryParse(quantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out long q) || q < 1 || q > available ? "Phinix_inventory_inlineQuantityInvalid".Translate(available).ToString() : null;
        private bool ValidTick => int.TryParse(tick, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0 && value < 60000;
        internal List<ClientActionDefinition> BuildActions(InventoryPresentationActionContext context)
        {
            var actions = new List<ClientActionDefinition>();
            string blocked = Blocked;
            if (context.Mode == "PlanEdit")
            {
                long.TryParse(context.QuantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long q);
                int.TryParse(context.DayTickText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t);
                string id = context.EntryId;
                actions.Add(new ClientActionDefinition("save-plan", L("savePlan"), () => {
                    bool ok = inventory.SetDailySchedule(id, q, t, out string reason);
                    notice = ok ? L("planSaved") : reason ?? L("planInvalid"); noticeFailed = !ok;
                    if (ok)
                    {
                        int index = 0; foreach (var plan in inventory.GetSchedules()) { if (plan.EntryId == id) break; index++; }
                        page = index / PageSize; selected = null; selectedUnits = 0; details = false; scrollTop = true;
                    }
                    dirty = true;
                }, blocked ?? (Entry(id) == null ? L("planMissingItem") : QuantityError(context.AvailableUnits) ?? (!ValidTick ? L("planInvalid") : inventory.CanSetDailySchedule(id, q, t, out string why) ? null : why))));
                actions.Add(new ClientActionDefinition("plan-back", L("plansBack"), () => { selected = null; selectedUnits = 0; details = false; scrollTop = true; dirty = true; }));
                actions.Add(new ClientActionDefinition("plan-details", L(details ? "plansHideDetails" : "plansDetails"), () => { details = !details; dirty = true; }));
                if (Plan(id) != null) AddRemove(actions, "remove-selected", id, blocked);
            }
            else
            {
                if (page > 0) actions.Add(new ClientActionDefinition("plans-previous", L("previousPage"), () => { page--; scrollTop = true; dirty = true; }));
                if ((page + 1) * PageSize < schedules.Count) actions.Add(new ClientActionDefinition("plans-next", L("nextPage"), () => { page++; scrollTop = true; dirty = true; }));
                for (int i = page * PageSize; i < Math.Min(schedules.Count, (page + 1) * PageSize); i++)
                {
                    string id = schedules[i].EntryId; string suffix = i.ToString(CultureInfo.InvariantCulture);
                    actions.Add(new ClientActionDefinition("edit." + suffix, L("plansEdit"), () => Select(id), blocked ?? (Entry(id) == null ? L("planMissingItem") : null)));
                    AddRemove(actions, "remove." + suffix, id, blocked);
                }
            }
            return actions;
        }
        private void AddRemove(List<ClientActionDefinition> actions, string actionId, string entryId, string blocked)
        { actions.Add(new ClientActionDefinition(actionId, L("removePlan"), () => { notice = inventory.RemoveDailySchedule(entryId) ? L("planRemoved") : L("operationFailed"); selected = null; selectedUnits = 0; scrollTop = true; dirty = true; }, blocked)); }
        private void Refresh()
        {
            if (view == null || view.IsClosed || released) return;
            long version = inventory.Version, pointsVersion = points.Revision; object lang = LanguageDatabase.activeLanguage; bool blockedNow = IsBlocked;
            if (!dirty && publishedBlocked == blockedNow && publishedVersion == version && publishedPoints == pointsVersion && ReferenceEquals(language, lang) && ReferenceEquals(result, view.LastActionResult)) return;
            if (dataDirty || loadedVersion != version || !ReferenceEquals(loadedLanguage, lang))
            {
                entries = inventory.GetSnapshot(); schedules = inventory.GetSchedules(); icons.Clear(); presentations.Clear(); entryIndex.Clear(); available.Clear();
                foreach (var entry in entries) entryIndex.Add(entry.EntryId, entry);
                foreach (var entry in inventory.GetAvailableSnapshot()) available[entry.EntryId] = entry.Quantity;
                loadedVersion = version; loadedLanguage = lang; dataDirty = false;
            }
            page = Math.Max(0, Math.Min(page, Math.Max(0, (schedules.Count - 1) / PageSize)));
            var item = Entry(selected);
            long units = item == null ? 1 : Presentation(item).ItemsPerUnit;
            if (selected != null && item != null && selectedUnits > 0 && units != selectedUnits) { unitReview = true; quantity = "1"; }
            selectedUnits = units;
            long ticket = ++revision; string mode = selected == null ? "PlanList" : "PlanEdit", selection = selected;
            var context = new InventoryPresentationActionContext(version, mode, selected, Available(selected), units, quantity, tick);
            Func<bool> current = () => !released && !ownerClosed() && view != null && !view.IsClosed && !dirty && revision == ticket && inventory.Version == version &&
                points.Revision == pointsVersion && IsBlocked == blockedNow && ReferenceEquals(Current.Game, game) && ReferenceEquals(LanguageDatabase.activeLanguage, lang) && selection == selected;
            var owned = point.Collect(context, current);
            try
            {
                var bindings = new Dictionary<string, ClientPresentationAction>(StringComparer.Ordinal);
                var extra = new List<ClientPresentationAction>();
                foreach (var action in owned.Actions)
                {
                    bool ours = action.OwnerId == module.ExtensionId, remove = ours && action.Id.StartsWith("remove", StringComparison.Ordinal);
                    var binding = new ClientPresentationAction(action.OwnerId + "." + action.Id, action, remove ? ClientButtonKind.Danger : ours && action.Id == "save-plan" ? ClientButtonKind.Primary : ClientButtonKind.Normal,
                        confirmation: remove ? new ClientPresentationConfirmation(L("removePlan"), L("plansRemoveConfirm"), L("removePlan"), L("cancel")) : null);
                    if (ours) bindings.Add(action.Id, binding); else extra.Add(binding);
                }
                var sections = new List<ClientPresentationSection>();
                string preflight = mode == "PlanEdit" && bindings.TryGetValue("save-plan", out var saveBinding) ? saveBinding.Action.DisabledReason : null;
                string status = Blocked ?? preflight ?? notice;
                if (mode == "PlanEdit")
                {
                    string label = item == null ? L("planMissingItem") : Presentation(item).Label;
                    sections.Add(new ClientPresentationSection("editor", label, item == null ? "" : "Phinix_inventory_balance".Translate(checked(context.AvailableUnits * units)).ToString(), bindings.Values,
                        status: new ClientPresentationStatus(status ?? "", Blocked == null && preflight == null && !noticeFailed ? ClientPresentationTone.Neutral : ClientPresentationTone.Warning),
                        fields: new[] {
                            ClientPresentationTextField.Integer("quantity", units > 1 ? "Phinix_inventory_scheduleStackQuantity".Translate(units).ToString() : L("scheduleQuantity"), quantity, QuantityError(context.AvailableUnits), Blocked ?? (item == null ? L("planMissingItem") : null)),
                            ClientPresentationTextField.Integer("tick", L("scheduleTime"), tick, ValidTick ? null : L("planInvalid"), Blocked)
                        }).WithPlainLayout().WithFieldColumns(2));
                    if (details && item != null)
                    {
                        var source = inventory.PresentSource(item); string summary = source?.Summary ?? "";
                        if (source?.Details != null) foreach (string line in source.Details) summary += "\n" + line;
                        sections.Add(new ClientPresentationSection("source", L("plansDetails"), summary).WithPlainLayout());
                    }
                }
                else
                {
                    var navigation = new List<ClientPresentationAction>();
                    foreach (var pair in bindings) if (!pair.Key.StartsWith("edit.", StringComparison.Ordinal) && !pair.Key.StartsWith("remove.", StringComparison.Ordinal)) navigation.Add(pair.Value);
                    sections.Add(new ClientPresentationSection("tools", L("plansTitle"), "Phinix_inventory_plansCount".Translate(schedules.Count).ToString() + (schedules.Count > PageSize ? " · " + "Phinix_inventory_page".Translate(page + 1, (schedules.Count + PageSize - 1) / PageSize).ToString() : ""), navigation,
                        status: new ClientPresentationStatus(status ?? "", Blocked == null && preflight == null && !noticeFailed ? ClientPresentationTone.Neutral : ClientPresentationTone.Warning)).WithPlainLayout().WithInlineToolbar());
                    var rows = new List<ClientPresentationRow>();
                    for (int i = page * PageSize; i < Math.Min(schedules.Count, (page + 1) * PageSize); i++)
                    {
                        var plan = schedules[i]; var entry = Entry(plan.EntryId); var presentation = entry == null ? new InventoryItemPresentation { Label = L("planMissingItem") } : Presentation(entry);
                        string suffix = i.ToString(CultureInfo.InvariantCulture), summary = "Phinix_inventory_planSummary".Translate(checked(plan.QuantityPerRun * presentation.ItemsPerUnit), plan.DayTick).ToString();
                        if (plan.RemainingQuantity > 0) summary += " · " + "Phinix_inventory_planRemaining".Translate(checked(plan.RemainingQuantity * presentation.ItemsPerUnit));
                        rows.Add(new ClientPresentationRow("plan." + suffix, presentation.Label, summary).WithImage(icons.Get(presentation.IconDefName))
                            .WithControls(new[] { bindings["edit." + suffix], bindings["remove." + suffix] }));
                    }
                    sections.Add(new ClientPresentationSection("plans", "", "", list: new ClientPresentationList("plans", rows, L("plansEmpty")).WithCompactRows()).WithPlainLayout());
                }
                if (extra.Count > 0) sections.Add(new ClientPresentationSection("contributions", "", "", extra).WithPlainLayout().WithInlineToolbar());
                dirty = false;
                view.Publish(new ClientPresentationPage(pageId, "", ticket, sections).WithFixedHeaderSections(mode == "PlanList" ? 1 : 0), owned, current);
                if (mode == "PlanList" && scrollTop)
                {
                    foreach (var section in sections) if (section.List?.Rows.Count > 0) view.ScrollToRow(section.List.Id, section.List.Rows[0].Id);
                    scrollTop = false;
                }
            }
            catch { dirty = true; owned.Dispose(); throw; }
            publishedBlocked = blockedNow; publishedVersion = version; publishedPoints = pointsVersion; language = lang; result = view.LastActionResult;
        }
    }
}
