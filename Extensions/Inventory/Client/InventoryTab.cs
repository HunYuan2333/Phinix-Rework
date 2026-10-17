using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    // Inventory owns all validation and execution. Host only renders these snapshots and intents.
    internal sealed class InventoryTab : IMainTabProvider, IResponsiveMainTabProvider, IClientViewLifecycle, IClientNavigationMetadata, IDisposable
    {
        private const int PageSize = 20; // Leaves room for navigation/editors under the 32-action contribution budget.
        private int itemPage;
        private bool scrollToPageStart;
        private readonly Dictionary<string, InventoryInlineDraft> rowDrafts = new Dictionary<string, InventoryInlineDraft>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> rowFieldEntries = new Dictionary<string, string>(StringComparer.Ordinal);
        private enum EditorMode { None, Extract, DailySchedule }
        private static readonly UiLayoutHints Hints = new UiLayoutHints(new Vector2(360, 300), new Vector2(780, 560), true);
        private readonly BuiltInInventoryClientExtension inventory;
        private readonly InventoryTheme theme;
        private bool disposed, dirty = true;
        private IReadOnlyList<InventoryEntry> entries = Array.Empty<InventoryEntry>();
        private string[] rowTitles = Array.Empty<string>(), rowDetails = Array.Empty<string>(), rowTooltips = Array.Empty<string>();
        private long[] rowAvailable = Array.Empty<long>();
        private InventoryItemPresentation[] itemPresentations = Array.Empty<InventoryItemPresentation>();
        private readonly InventoryIconCache iconCache = new InventoryIconCache();
        private string searchText = "";
        private string[] searchLabels = Array.Empty<string>();
        private List<InventoryDisplayGroup> groups = new List<InventoryDisplayGroup>();
        private readonly Dictionary<InventoryDisplayGroup, string[]> groupText = new Dictionary<InventoryDisplayGroup, string[]>();
        private readonly HashSet<string> expandedGroups = new HashSet<string>(StringComparer.Ordinal);
        private List<DisplayRow> visibleRows = new List<DisplayRow>();
        private readonly Dictionary<string, DisplayRow> projectedRows = new Dictionary<string, DisplayRow>();
        private sealed class DisplayRow { public InventoryDisplayGroup Group; public int EntryIndex; public bool IsHeader; }
        private object shownLanguage, shownGame, publishedLanguage, publishedGame;
        private long shownVersion = -1, revision, pointsRevision = -1, publishedVersion = -1;
        private int pendingTransferCount;
        private string notice, selectedEntryId, selectedRow, selectedGroupKey;
        private string batchRecordsText = "100";
        private readonly Dictionary<string, ClientPresentationConfirmation> batchConfirmations = new Dictionary<string, ClientPresentationConfirmation>();
        private EditorMode editorMode;
        private string extractQuantityText = "1", scheduleQuantityText = "1", dayTickText = "30000";
        private IPhinixExtensionModule module;
        private IClientPresentationService renderer;
        private IClientExtensionPointService points;
        private IClientExtensionPointScope scope;
        private IClientActionPoint<InventoryPresentationActionContext> point;
        private IClientPresentationSession view;
        private ClientActionExecutionResult publishedResult;
        private IClientGameWindowService windows;
        private InventoryPlansWindow plansWindow;
        public InventoryTab(BuiltInInventoryClientExtension inventory, InventoryTheme theme)
        {
            this.inventory = inventory; this.theme = theme;
            inventory.InventoryChanged += InvalidateRows; inventory.AvailabilityChanged += InvalidateRows;
        }
        internal void Start(IPhinixExtensionModule owner, IClientPresentationService renderer, IClientExtensionPointService points, IClientGameWindowService windows = null)
        {
            module = owner; this.renderer = renderer; this.points = points; this.windows = windows;
            scope = points.ForModule(owner); point = scope.DefineActions<InventoryPresentationActionContext>(InventoryPresentationActionContext.PointId);
            scope.ContributeActions<InventoryPresentationActionContext>(InventoryPresentationActionContext.PointId, BuildActions);
        }
        public string TabLabel => L("tab");
        public float TabOrder => 2;
        public string NavigationId => "inventory";
        public UiLayoutHints LayoutHints => Hints;
        private static string L(string key) => ("Phinix_inventory_" + key).Translate();
        public void ViewOpened() { if (disposed || module == null || view != null && !view.IsClosed) return; view = renderer.Open(module, "inventory"); dirty = true; Refresh(); }
        public void ViewClosed()
        {
            var old = view;
            if (old != null && !old.IsClosed)
            {
                var pendingInputs = new List<ClientPresentationInput>();
                while (old.TryDequeueInput(out var queued)) pendingInputs.Add(queued);
                foreach (var input in pendingInputs)
                {
                    if (rowFieldEntries.TryGetValue(input.Id, out var editedEntry)) Draft(editedEntry).Edit(input.Value);
                    else if (input.Id == "inventory-search") ChangeSearch(input.Value);
                    else if (input.Id == "batch-records") batchRecordsText = input.Value;
                    else if (input.Id == "extract-quantity") extractQuantityText = input.Value;
                    else if (input.Id == "schedule-quantity") scheduleQuantityText = input.Value;
                    else if (input.Id == "schedule-tick") dayTickText = input.Value;
                }
            }
            view = null; old?.Dispose(); iconCache.Clear(); shownVersion = -1; dirty = true;
        }
        public void ViewUpdate()
        {
            if (view == null || view.IsClosed) return;
            var pending = new List<ClientPresentationInput>();
            while (view.TryDequeueInput(out var queued)) pending.Add(queued);
            foreach (var input in pending)
            {
                if (input.Id == "entries" && projectedRows.TryGetValue(input.Value, out var row))
                {
                    if (row.IsHeader) { selectedEntryId = null; selectedGroupKey = row.Group.Key; selectedRow = input.Value; editorMode = EditorMode.None; notice = null; if (!expandedGroups.Remove(row.Group.Key)) expandedGroups.Add(row.Group.Key); RebuildVisibleRows(); }
                    else { OpenPlans(entries[row.EntryIndex].EntryId); }
                    dirty = true;
                }
                else if (rowFieldEntries.TryGetValue(input.Id, out var editedEntry)) { Draft(editedEntry).Edit(input.Value); dirty = true; }
                else if (input.Id == "inventory-search") ChangeSearch(input.Value);
                else if (input.Id == "batch-records") { batchRecordsText = input.Value; dirty = true; }
                else if (input.Id == "extract-quantity") { extractQuantityText = input.Value; dirty = true; }
                else if (input.Id == "schedule-quantity") { scheduleQuantityText = input.Value; dirty = true; }
                else if (input.Id == "schedule-tick") { dayTickText = input.Value; dirty = true; }
            }
            Refresh();
        }
        public void Draw(Rect rect) { if (!disposed) view?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; plansWindow?.Close(); plansWindow = null; ViewClosed();
            inventory.InventoryChanged -= InvalidateRows; inventory.AvailabilityChanged -= InvalidateRows;
            scope?.Dispose(); scope = null; point = null; module = null;
        }
        private void InvalidateRows(object sender, EventArgs args) { shownVersion = -1; dirty = true; plansWindow?.Invalidate(); }
        private static string RowId(string kind, string id)
        { using (var sha = SHA256.Create()) return kind + "." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").ToLowerInvariant(); }
        private string Mode => inventory.HasPendingRecovery ? "recovery" : inventory.HasPendingExtraction ? "reconcile" : inventory.Fault != null ? "fault" : inventory.BatchProgress?.Running == true ? "batch" : selectedGroupKey != null ? "group" : editorMode.ToString();
        private static bool ParseQuantity(string text, out long quantity) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) && quantity > 0;
        private static bool ParseTick(string text, out int tick) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tick) && tick >= 0 && tick < 60000;
        private void Refresh()
        {
            if (view == null || view.IsClosed) return;
            var language = LanguageDatabase.activeLanguage; var game = Current.Game;
            if (!dirty && publishedVersion == inventory.Version && ReferenceEquals(publishedLanguage, language) && ReferenceEquals(publishedGame, game) &&
                pointsRevision == points.Revision && ReferenceEquals(publishedResult, view.LastActionResult)) return;
            string mode = Mode;
            if (mode != "recovery" && mode != "reconcile" && mode != "fault") RefreshRows();
            InventoryEntry selected = FindEntry(selectedEntryId);
            if (selected == null && selectedEntryId != null) CloseEditor();
            mode = Mode;
            itemPage = Math.Max(0, Math.Min(itemPage, Math.Max(0, (visibleRows.Count - 1) / PageSize)));
            long version = inventory.Version, pointVersion = points.Revision, nextRevision = ++revision;
            string groupKey = selectedGroupKey;
            string selectedId = selectedEntryId, quantity = mode == "group" ? batchRecordsText : editorMode == EditorMode.DailySchedule ? scheduleQuantityText : extractQuantityText, tick = dayTickText;
            var context = new InventoryPresentationActionContext(version, mode, selectedId, GetAvailableQuantity(selectedId), GetPresentation(selectedId).ItemsPerUnit, quantity, tick, groupKey, GroupPlan(groupKey, 10001).Count);
            Func<bool> current = () => !disposed && view != null && !view.IsClosed && !dirty && revision == nextRevision && inventory.Version == version &&
                points.Revision == pointVersion && ReferenceEquals(Current.Game, game) && ReferenceEquals(LanguageDatabase.activeLanguage, language) && Mode == mode && selectedEntryId == selectedId && selectedGroupKey == groupKey;
            batchConfirmations.Clear();
            var actions = point.Collect(context, current);
            try
            {
                var bindings = new List<ClientPresentationAction>();
                foreach (var action in actions.Actions)
                {
                    batchConfirmations.TryGetValue(action.Id, out var confirmation);
                    bindings.Add(new ClientPresentationAction(action.OwnerId + "." + action.Id, action,
                        action.OwnerId == module.ExtensionId && (action.Id == "extract" || action.Id == "save-plan" || action.Id == "batch-some" || action.Id == "batch-all" || action.Id.StartsWith("take.", StringComparison.Ordinal)) ? ClientButtonKind.Primary : ClientButtonKind.Normal,
                        confirmation: action.OwnerId == module.ExtensionId ? confirmation : null));
                }
                var rowBindings = new Dictionary<string, ClientPresentationAction>(StringComparer.Ordinal);
                var navigation = new List<ClientPresentationAction>();
                var globalBindings = new List<ClientPresentationAction>();
                foreach (var binding in bindings)
                    if (binding.Action.OwnerId == module.ExtensionId && binding.Action.Id.StartsWith("take.", StringComparison.Ordinal)) rowBindings.Add(binding.Action.Id, binding);
                    else if (binding.Action.OwnerId == module.ExtensionId && (binding.Action.Id == "page-previous" || binding.Action.Id == "page-next" || binding.Action.Id == "manage-plans")) navigation.Add(binding);
                    else globalBindings.Add(binding);
                bindings = globalBindings;
                var sections = new List<ClientPresentationSection>();
                if (mode == "recovery" || mode == "reconcile")
                    sections.Add(new ClientPresentationSection("resolution", L(mode == "recovery" ? "recoveryTitle" : "pendingTitle"),
                        L(mode == "recovery" ? "recoveryWarning" : "pendingWarning"), bindings, new ClientPresentationStatus(notice ?? "", ClientPresentationTone.Warning), "inventory.noticeBackground"));
                else if (mode == "fault") sections.Add(new ClientPresentationSection("fault", L("unavailableTitle"), inventory.Fault, bindings, status: new ClientPresentationStatus(notice ?? "", ClientPresentationTone.Error), surfaceKey: "inventory.errorBackground"));
                else
                {
                    sections.Add(new ClientPresentationSection("save", "", "Phinix_inventory_entryCount".Translate(groups.Count) + " · " + L("saveOnly"), surfaceKey: "inventory.infoBackground").WithPlainLayout());
                    if (pendingTransferCount > 0) sections.Add(new ClientPresentationSection("reserved", "", "Phinix_inventory_transferPending".Translate(pendingTransferCount), status: new ClientPresentationStatus("", ClientPresentationTone.Warning), surfaceKey: "inventory.noticeBackground"));
                    if (!string.IsNullOrEmpty(notice)) sections.Add(new ClientPresentationSection("result", "", notice, surfaceKey: "inventory.noticeBackground"));
                    var progress = inventory.BatchProgress;
                    if (progress != null)
                        sections.Add(new ClientPresentationSection("batch-progress", L("batchProgressTitle"),
                            "Phinix_inventory_batchProgress".Translate(progress.CompletedRecords, progress.Records, progress.CompletedUnits) +
                            (progress.Running ? "\n" + L("batchRunning") : "\n" + (progress.Reason ?? L("batchCompleted"))),
                            mode == "batch" ? bindings : null, surfaceKey: "inventory.noticeBackground"));
                    if (mode == "group")
                        sections.Add(new ClientPresentationSection("group-extract", L("batchTitle"),
                            "Phinix_inventory_batchSelection".Translate(context.GroupRecords), bindings,
                            fields: new[] { ClientPresentationTextField.Integer("batch-records", L("batchRecords"), batchRecordsText,
                                int.TryParse(batchRecordsText, out int n) && n > 0 && n <= 10000 ? null : L("batchInvalid")) }, surfaceKey: "inventory.editorBackground"));
                    else if (mode != "batch" && bindings.Count > 0) sections.Add(new ClientPresentationSection("contributions", "", "", bindings).WithPlainLayout().WithInlineToolbar());
                    sections.Insert(0, new ClientPresentationSection("search", "", "Phinix_inventory_page".Translate(itemPage + 1, Math.Max(1, (visibleRows.Count + PageSize - 1) / PageSize)), navigation, fields:
                        new[] { new ClientPresentationTextField("inventory-search", "", searchText, L("searchPlaceholder")) }).WithPlainLayout().WithInlineToolbar());
                    if (selected != null && searchText.Trim().Length > 0 && !EntryVisible(selected.EntryId))
                        sections.Add(new ClientPresentationSection("hidden-selection", "", "", status:
                            new ClientPresentationStatus(L("searchSelectionHidden"), ClientPresentationTone.Warning)).WithPlainLayout());
                    var listRows = new List<ClientPresentationRow>(); projectedRows.Clear();
                    rowFieldEntries.Clear();
                    for (int i = itemPage * PageSize; i < Math.Min(visibleRows.Count, (itemPage + 1) * PageSize); i++)
                    {
                        var row = visibleRows[i]; string id;
                        if (row.IsHeader)
                        { id = RowId("group", row.Group.Key) + "." + i; var text = groupText[row.Group]; listRows.Add(new ClientPresentationRow(id, (expandedGroups.Contains(row.Group.Key) ? "▾ " : "▸ ") + text[0], text[1]).WithImage(iconCache.Get(GroupIcon(row.Group)))); }
                        else
                        {
                            var entry = entries[row.EntryIndex]; id = RowId("entry", entry.EntryId);
                            var draft = Draft(entry.EntryId); var presentation = itemPresentations[row.EntryIndex]; long available = rowAvailable[row.EntryIndex];
                            string fieldId = RowId("quantity", entry.EntryId); rowFieldEntries.Add(fieldId, entry.EntryId);
                            string error = QuantityError(draft, available);
                            string details = "Phinix_inventory_balance".Translate(checked(available * presentation.ItemsPerUnit)) + " · " + rowDetails[row.EntryIndex];
                            if (draft.Result != null) details += "\n" + draft.Result;
                            var badges = new List<ClientPresentationBadge>();
                            if (entry.EntryId == selectedEntryId) badges.Add(new ClientPresentationBadge("selected", L("inlineSelected")).WithColor("ui.accent"));
                            if (draft.Result != null) badges.Add(new ClientPresentationBadge("result", L(draft.Failed ? "inlineFailed" : "inlineSucceeded")).WithColor(draft.Failed ? "ui.error" : "ui.success"));
                            rowBindings.TryGetValue(TakeId(entry.EntryId), out var take);
                            listRows.Add(new ClientPresentationRow(id, rowTitles[row.EntryIndex], details)
                                .WithControls(take == null ? null : new[] { take }, ClientPresentationTextField.Integer(fieldId,
                                    presentation.ItemsPerUnit > 1 ? "Phinix_inventory_inlineWholeUnits".Translate(presentation.ItemsPerUnit).ToString() : L("inlineQuantity"), draft.Text, error,
                                    mode == "batch" ? L("batchRunning") : null))
                                .WithBadges(badges)
                                .WithImage(iconCache.Get(presentation.IconDefName)));
                        }
                        projectedRows.Add(id, row);
                    }
                    if (!projectedRows.ContainsKey(selectedRow ?? "")) selectedRow = null;
                    sections.Add(new ClientPresentationSection("stock", L("tab"), "", list: new ClientPresentationList("entries", listRows, L(searchText.Trim().Length > 0 ? "searchNoMatches" : "empty")).WithCompactRows()).WithPlainLayout());
                }
                dirty = false;
                view.Publish(new ClientPresentationPage("inventory", "", nextRevision, sections)
                    .WithFixedHeaderSections(mode == "recovery" || mode == "reconcile" || mode == "fault" ? 0 : sections.Count - 1), actions, current);
                if (scrollToPageStart)
                {
                    foreach (var section in sections) if (section.List?.Id == "entries" && section.List.Rows.Count > 0) view.ScrollToRow("entries", section.List.Rows[0].Id);
                    scrollToPageStart = false;
                }
            }
            catch { dirty = true; actions.Dispose(); throw; }
            publishedVersion = version; publishedLanguage = language; publishedGame = game; pointsRevision = pointVersion; publishedResult = view.LastActionResult;
        }
        private IEnumerable<ClientActionDefinition> BuildActions(InventoryPresentationActionContext context)
        {
            if (context.Mode == "PlanList" || context.Mode == "PlanEdit") return plansWindow?.BuildActions(context) ?? new List<ClientActionDefinition>();
            var actions = new List<ClientActionDefinition>();
            Action<string, string, Action, string> add = (id, label, execute, disabled) => actions.Add(new ClientActionDefinition(id, L(label), () => { execute(); dirty = true; }, disabled));
            if (context.Mode == "recovery")
            { add("recover", "recover", () => notice = inventory.ApproveRecovery() ? null : L("operationFailed"), null); add("keep-save", "keepSave", () => notice = inventory.RejectRecovery() ? null : L("operationFailed"), null); }
            else if (context.Mode == "reconcile")
            { add("mark-delivered", "markDelivered", () => notice = inventory.ResolvePendingExtraction(true) ? null : L("operationFailed"), null); add("restore-balance", "restoreBalance", () => notice = inventory.ResolvePendingExtraction(false) ? null : L("operationFailed"), null); }
            else if (context.Mode == "batch")
                add("batch-stop", "batchStop", inventory.StopBatchExtraction, null);
            else if (context.Mode == "group")
            {
                int.TryParse(context.QuantityText, out int count);
                AddBatchAction(actions, context, "batch-some", "batchSome", count);
                AddBatchAction(actions, context, "batch-all", "batchAll", 10000);
            }
            else if (context.Mode != "fault" && context.EntryId != null)
            {
                if (context.Mode == "Extract")
                {
                    bool valid = ParseQuantity(context.QuantityText, out long quantity) && quantity <= context.AvailableUnits;
                    add("extract", "extractConfirm", () =>
                    {
                        if (!inventory.TryExtract(context.EntryId, quantity, out string reason)) notice = string.IsNullOrEmpty(reason) ? L("operationFailed") : reason;
                        else { notice = "Phinix_inventory_extractSucceeded".Translate(checked(quantity * context.ItemsPerUnit)); CloseEditor(); }
                        shownVersion = -1;
                    }, valid ? null : L("extractInvalid"));
                    add("cancel", "cancel", CloseEditor, null);
                }
                else if (context.Mode == "DailySchedule")
                {
                    bool valid = ParseQuantity(context.QuantityText, out long quantity) && ParseTick(context.DayTickText, out _);
                    ParseTick(context.DayTickText, out int tick);
                    add("save-plan", "savePlan", () => { notice = inventory.SetDailySchedule(context.EntryId, quantity, tick, out _) ? L("planSaved") : L("planInvalid"); shownVersion = -1; }, valid ? null : L("planInvalid"));
                    bool scheduled = FindSchedule(context.EntryId) != null;
                    add("remove-plan", scheduled ? "removePlan" : "cancel", () => { if (!scheduled) CloseEditor(); else if (inventory.RemoveDailySchedule(context.EntryId)) { notice = L("planRemoved"); CloseEditor(); shownVersion = -1; } else notice = L("operationFailed"); }, null);
                }
                else
                {
                    add("open-plan", "plan", () => { var entry = FindEntry(context.EntryId); if (entry != null) OpenScheduleEditor(entry); }, null);
                }
            }
            if (context.Mode != "recovery" && context.Mode != "reconcile" && context.Mode != "fault")
            {
                actions.Add(new ClientActionDefinition("manage-plans", "Phinix_inventory_managePlans".Translate(inventory.GetSchedules().Count), () => OpenPlans(null), windows == null ? L("operationFailed") : null));
                if (itemPage > 0) add("page-previous", "previousPage", () => { itemPage--; scrollToPageStart = true; }, null);
                if ((itemPage + 1) * PageSize < visibleRows.Count) add("page-next", "nextPage", () => { itemPage++; scrollToPageStart = true; }, null);
                if (context.Mode != "batch")
                    for (int i = itemPage * PageSize; i < Math.Min(visibleRows.Count, (itemPage + 1) * PageSize); i++)
                    {
                        var row = visibleRows[i]; if (row.IsHeader) continue;
                        string entryId = entries[row.EntryIndex].EntryId;
                        long available = rowAvailable[row.EntryIndex], units = itemPresentations[row.EntryIndex].ItemsPerUnit;
                        string label = itemPresentations[row.EntryIndex].Label;
                        var draft = Draft(entryId); bool valid = draft.TryQuantity(available, out long quantity);
                        actions.Add(new ClientActionDefinition(TakeId(entryId), L("extractStack"), () => ExtractInline(entryId, quantity, units, label, draft),
                            valid ? null : QuantityError(draft, available)));
                    }
            }
            return actions;
        }
        private static string QuantityError(InventoryInlineDraft draft, long available) => draft.NeedsReview ? L(draft.ReviewKey) :
            available < 1 ? L("inlineUnavailable") : draft.TryQuantity(available, out _) ? null : "Phinix_inventory_inlineQuantityInvalid".Translate(available).ToString();
        private InventoryInlineDraft Draft(string entryId)
        {
            if (!rowDrafts.TryGetValue(entryId, out var draft)) { draft = new InventoryInlineDraft(); rowDrafts.Add(entryId, draft); }
            draft.BindUnits(GetPresentation(entryId).ItemsPerUnit);
            return draft;
        }
        private static string TakeId(string entryId) => RowId("take", entryId).Substring(0, 37);
        private void ExtractInline(string entryId, long quantity, long itemsPerUnit, string label, InventoryInlineDraft draft)
        {
            bool success = inventory.TryExtractForPresentation(entryId, quantity, out long delivered, out string reason);
            bool uncertain = inventory.HasPendingExtraction;
            string result = success ? "Phinix_inventory_extractSucceeded".Translate(checked(delivered * itemsPerUnit)).ToString() :
                delivered > 0 ? (uncertain ? "Phinix_inventory_inlineUncertainPartial" : "Phinix_inventory_inlinePartial").Translate(checked(delivered * itemsPerUnit), checked(quantity * itemsPerUnit), reason ?? L("operationFailed")).ToString() : reason ?? L("operationFailed");
            draft.Complete(success, quantity, delivered, result, uncertain);
            notice = label + "：" + result;
            shownVersion = -1; dirty = true;
        }
        private List<InventoryBatchItem> GroupPlan(string key, int maximumRecords)
        {
            var result = new List<InventoryBatchItem>();
            foreach (var group in groups)
            {
                if (group.Key != key) continue;
                foreach (int i in group.EntryIndices)
                {
                    if (result.Count >= maximumRecords) break;
                    if (rowAvailable[i] > 0) result.Add(new InventoryBatchItem(entries[i].EntryId, rowAvailable[i]));
                }
                break;
            }
            return result;
        }
        private void AddBatchAction(List<ClientActionDefinition> actions, InventoryPresentationActionContext context, string id, string label, int maximum)
        {
            var plan = GroupPlan(context.GroupKey, maximum);
            bool valid = maximum > 0 && maximum <= 10000 && plan.Count > 0 &&
                (id != "batch-all" || context.GroupRecords <= 10000);
            string text = "Phinix_inventory_batchConfirm".Translate(plan.Count).ToString();
            long units = 0; foreach (var item in plan) { if (item.Quantity > long.MaxValue - units) { units = long.MaxValue; break; } units += item.Quantity; }
            if (plan.Count >= 100 || units >= 1000) text += "\n\n" + L("batchPerformance");
            batchConfirmations[id] = new ClientPresentationConfirmation(L("batchTitle"), text, L("extractConfirm"), L("cancel"));
            actions.Add(new ClientActionDefinition(id, L(label), () =>
            {
                notice = inventory.StartBatchExtraction(plan, context.Version, out string reason) ? null : reason;
                dirty = true;
            }, valid ? null : L("batchInvalid")));
        }
        private void RefreshRows()
        {
            if (shownVersion == inventory.Version && ReferenceEquals(shownLanguage, LanguageDatabase.activeLanguage) &&
                ReferenceEquals(shownGame, Current.Game)) return;
            // Bound borrowed image wrappers to the current data/language snapshot, not the session's history.
            iconCache.Clear();
            if (!ReferenceEquals(shownGame, Current.Game))
            {
                expandedGroups.Clear();
                searchText = ""; itemPage = 0; scrollToPageStart = true; rowDrafts.Clear(); rowFieldEntries.Clear(); notice = null;
                CloseEditor();
                selectedRow = null;
            }
            entries = inventory.GetSnapshot();
            var entryIds = new HashSet<string>(StringComparer.Ordinal); foreach (var entry in entries) entryIds.Add(entry.EntryId);
            var removed = new List<string>(); foreach (string id in rowDrafts.Keys) if (!entryIds.Contains(id)) removed.Add(id);
            foreach (string id in removed) rowDrafts.Remove(id);
            Dictionary<string, long> availableByEntry = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (InventoryEntry available in inventory.GetAvailableSnapshot())
                availableByEntry[available.EntryId] = available.Quantity;
            pendingTransferCount = 0;
            foreach (InventoryReservation reservation in inventory.GetReservations())
            {
                if (reservation.State == InventoryReservationState.Reserved ||
                    reservation.State == InventoryReservationState.Uncertain) pendingTransferCount++;
            }
            IReadOnlyList<InventorySchedule> schedules = inventory.GetSchedules();
            Dictionary<string, InventorySchedule> schedulesByEntry =
                new Dictionary<string, InventorySchedule>(StringComparer.Ordinal);
            foreach (InventorySchedule schedule in schedules) schedulesByEntry[schedule.EntryId] = schedule;

            rowTitles = new string[entries.Count];
            rowDetails = new string[entries.Count];
            rowTooltips = new string[entries.Count];
            searchLabels = new string[entries.Count];
            rowAvailable = new long[entries.Count];
            itemPresentations = new InventoryItemPresentation[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                InventoryEntry entry = entries[i];
                availableByEntry.TryGetValue(entry.EntryId, out rowAvailable[i]);
                InventorySourcePresentation source = inventory.PresentSource(entry);
                itemPresentations[i] = inventory.PresentItem(entry);
                if (rowDrafts.TryGetValue(entry.EntryId, out var existingDraft)) existingDraft.BindUnits(itemPresentations[i].ItemsPerUnit);
                rowTitles[i] = itemPresentations[i].Label;
                string detail = source?.Summary ?? string.Empty;
                searchLabels[i] = Utils.TextHelper.StripRichText(rowTitles[i] + "\n" + entry.Label + "\n" + detail);
                schedulesByEntry.TryGetValue(entry.EntryId, out InventorySchedule schedule);
                if (schedule != null)
                {
                    string plan = "Phinix_inventory_planSummary".Translate(
                        checked(schedule.QuantityPerRun * itemPresentations[i].ItemsPerUnit), schedule.DayTick);
                    detail = string.IsNullOrEmpty(detail) ? plan : detail + " · " + plan;
                }
                if (itemPresentations[i].ItemsPerUnit > 1)
                    detail += " · " + "Phinix_inventory_wholeStack".Translate(itemPresentations[i].ItemsPerUnit);
                rowDetails[i] = detail;
                rowTooltips[i] = BuildTooltip(rowTitles[i], checked(rowAvailable[i] * itemPresentations[i].ItemsPerUnit), source, schedule,
                    itemPresentations[i].ItemsPerUnit) +
                    "\n" + "Phinix_inventory_entryId".Translate(entry.EntryId);
                if (!string.IsNullOrEmpty(entry.Label) && entry.Label != rowTitles[i])
                    rowTooltips[i] += "\n" + entry.Label;
            }
            groups = InventoryDisplayGrouping.Build(entries, itemPresentations, rowAvailable);
            groupText.Clear();
            foreach (InventoryDisplayGroup group in groups)
            {
                int first = group.EntryIndices[0];
                string title = "Phinix_inventory_groupTitle".Translate(rowTitles[first], group.AvailableItems);
                string detail = "Phinix_inventory_groupSummary".Translate(group.EntryIndices.Count) + " · " +
                    inventory.PresentSource(entries[first])?.Summary;
                groupText[group] = new string[] { title, detail, title + "\n" + detail + "\n" + "Phinix_inventory_groupHint".Translate() };
            }
            HashSet<string> currentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryDisplayGroup group in groups) currentKeys.Add(group.Key);
            expandedGroups.IntersectWith(currentKeys);
            if (selectedGroupKey != null && !currentKeys.Contains(selectedGroupKey)) CloseEditor();
            RebuildVisibleRows();
            shownVersion = inventory.Version;
            shownLanguage = LanguageDatabase.activeLanguage;
            shownGame = Current.Game;
        }

        private void RebuildVisibleRows()
        {
            List<DisplayRow> rows = new List<DisplayRow>();
            bool searching = searchText.Trim().Length > 0;
            var matches = searching ? InventoryDisplaySearch.Find(searchLabels, searchText) : null;
            foreach (InventoryDisplayGroup group in groups)
            {
                if (searching)
                {
                    foreach (int index in group.EntryIndices)
                        if (matches.Contains(index)) rows.Add(new DisplayRow { Group = group, EntryIndex = index });
                    continue;
                }
                bool multiple = group.EntryIndices.Count > 1;
                if (multiple) rows.Add(new DisplayRow { Group = group, IsHeader = true });
                if (!multiple || expandedGroups.Contains(group.Key))
                    foreach (int index in group.EntryIndices)
                        rows.Add(new DisplayRow { Group = group, EntryIndex = index });
            }
            visibleRows = rows;
        }

        private void ChangeSearch(string value)
        {
            value = value ?? "";
            if (searchText == value) return;
            searchText = value; itemPage = 0; scrollToPageStart = true;
            if (selectedGroupKey != null) { CloseEditor(); selectedRow = null; notice = L("searchGroupCleared"); }
            RebuildVisibleRows(); dirty = true;
        }
        private bool EntryVisible(string entryId)
        {
            foreach (var row in visibleRows)
                if (!row.IsHeader && entries[row.EntryIndex].EntryId == entryId) return true;
            return false;
        }
        private string GroupIcon(InventoryDisplayGroup group)
        {
            string first = itemPresentations[group.EntryIndices[0]].IconDefName;
            foreach (int index in group.EntryIndices)
                if (!string.Equals(first, itemPresentations[index].IconDefName, StringComparison.Ordinal)) return null;
            return first;
        }

        private void OpenExtractionEditor(InventoryEntry entry)
        {
            selectedEntryId = entry.EntryId;
            editorMode = EditorMode.Extract;
            extractQuantityText = "1";
            notice = null; dirty = true;
        }

        private void OpenScheduleEditor(InventoryEntry entry) => OpenPlans(entry.EntryId);
        private void OpenPlans(string entryId)
        {
            if (windows == null || disposed) return;
            if (plansWindow?.IsOpen == true) { plansWindow.Select(entryId); return; }
            var next = new InventoryPlansWindow(inventory, theme, module, renderer, points, point, () => disposed, () => plansWindow = null);
            plansWindow = next;
            try { next.Select(entryId); windows.Open(next); }
            catch { plansWindow = null; next.Release(); throw; }
        }

        private static string BuildTooltip(string title, long quantity, InventorySourcePresentation source,
            InventorySchedule schedule, long itemsPerUnit)
        {
            string tooltip = title + "\n" + "Phinix_inventory_balance".Translate(quantity);
            if (source?.Details != null)
                foreach (string detail in source.Details)
                    if (!string.IsNullOrWhiteSpace(detail)) tooltip += "\n" + detail;
            if (schedule != null)
                tooltip += "\n" + "Phinix_inventory_planSummary".Translate(
                    checked(schedule.QuantityPerRun * itemsPerUnit), schedule.DayTick);
            if (itemsPerUnit > 1) tooltip += "\n" + "Phinix_inventory_wholeStack".Translate(itemsPerUnit);
            return tooltip;
        }

        private void CloseEditor()
        {
            selectedEntryId = null; selectedGroupKey = null;
            editorMode = EditorMode.None; dirty = true;
        }

        private InventoryEntry FindEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return null;
            foreach (InventoryEntry entry in entries)
                if (string.Equals(entry.EntryId, entryId, StringComparison.Ordinal)) return entry;
            return null;
        }

        private long GetAvailableQuantity(string entryId)
        {
            for (int index = 0; index < entries.Count && index < rowAvailable.Length; index++)
                if (string.Equals(entries[index].EntryId, entryId, StringComparison.Ordinal))
                    return rowAvailable[index];
            return 0;
        }

        private InventoryItemPresentation GetPresentation(string entryId)
        {
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].EntryId, entryId, StringComparison.Ordinal)) return itemPresentations[i];
            return new InventoryItemPresentation();
        }

        private string GetExtractionTitle(InventoryEntry entry)
        {
            InventoryItemPresentation presentation = GetPresentation(entry.EntryId);
            return presentation.ItemsPerUnit > 1
                ? "Phinix_inventory_extractWholeStackTitle".Translate(presentation.Label,
                    GetAvailableQuantity(entry.EntryId), presentation.ItemsPerUnit).ToString()
                : "Phinix_inventory_extractEditorTitle".Translate(presentation.Label,
                    GetAvailableQuantity(entry.EntryId)).ToString();
        }

        private InventorySchedule FindSchedule(string entryId)
        {
            foreach (InventorySchedule schedule in inventory.GetSchedules())
                if (string.Equals(schedule.EntryId, entryId, StringComparison.Ordinal)) return schedule;
            return null;
        }
    }
}
