using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    internal sealed class InventoryTab : IMainTabProvider, IResponsiveMainTabProvider, IDisposable
    {
        private enum EditorMode { None, Extract, DailySchedule }

        private const float RowHeight = 60f;
        private const float Spacing = 8f;
        private static readonly UiLayoutHints Hints = new UiLayoutHints(
            new Vector2(360f, 300f), new Vector2(780f, 560f), true);
        private static readonly Color InfoBackground = new Color(0.18f, 0.38f, 0.58f, 0.16f);
        private static readonly Color InfoAccent = new Color(0.32f, 0.66f, 0.95f, 1f);
        private static readonly Color EditorBackground = new Color(0.12f, 0.12f, 0.12f, 0.66f);
        private static readonly Color NoticeBackground = new Color(0.55f, 0.42f, 0.12f, 0.2f);

        private readonly BuiltInInventoryClientExtension inventory;
        private bool disposed;
        private IReadOnlyList<InventoryEntry> entries = Array.Empty<InventoryEntry>();
        private string[] rowTitles = Array.Empty<string>();
        private string[] rowDetails = Array.Empty<string>();
        private string[] rowTooltips = Array.Empty<string>();
        private long[] rowAvailable = Array.Empty<long>();
        private InventoryItemPresentation[] itemPresentations = Array.Empty<InventoryItemPresentation>();
        private List<InventoryDisplayGroup> groups = new List<InventoryDisplayGroup>();
        private readonly Dictionary<InventoryDisplayGroup, string[]> groupText = new Dictionary<InventoryDisplayGroup, string[]>();
        private readonly HashSet<string> expandedGroups = new HashSet<string>(StringComparer.Ordinal);
        private List<DisplayRow> visibleRows = new List<DisplayRow>();
        private object shownLanguage;
        private object shownGame;
        private sealed class DisplayRow
        {
            public InventoryDisplayGroup Group;
            public int EntryIndex;
            public bool IsHeader;
        }
        private int pendingTransferCount;
        private long shownVersion = -1;
        private Vector2 scroll;
        private string notice;
        private string selectedEntryId;
        private EditorMode editorMode;
        private string extractQuantityText = "1";
        private string scheduleQuantityText = "1";
        private string dayTickText = "30000";

        public InventoryTab(BuiltInInventoryClientExtension inventory)
        {
            this.inventory = inventory;
            // The tab and inventory share one extension lifetime. Also refresh on codec activation changes.
            inventory.InventoryChanged += InvalidateRows;
            inventory.AvailabilityChanged += InvalidateRows;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            inventory.InventoryChanged -= InvalidateRows;
            inventory.AvailabilityChanged -= InvalidateRows;
        }

        private void InvalidateRows(object sender, EventArgs args) { shownVersion = -1; }
        public string TabLabel => "Phinix_inventory_tab".Translate();
        public float TabOrder => 2f;
        public UiLayoutHints LayoutHints => Hints;

        public void Draw(Rect inRect)
        {
            if (inventory.HasPendingRecovery)
            {
                DrawResolutionPanel(inRect,
                    "Phinix_inventory_recoveryTitle".Translate(),
                    "Phinix_inventory_recoveryWarning".Translate(),
                    "Phinix_inventory_recover".Translate(),
                    "Phinix_inventory_keepSave".Translate(),
                    () => inventory.ApproveRecovery(), () => inventory.RejectRecovery());
                return;
            }
            if (inventory.HasPendingExtraction)
            {
                DrawResolutionPanel(inRect,
                    "Phinix_inventory_pendingTitle".Translate(),
                    "Phinix_inventory_pendingWarning".Translate(),
                    "Phinix_inventory_markDelivered".Translate(),
                    "Phinix_inventory_restoreBalance".Translate(),
                    () => inventory.ResolvePendingExtraction(true),
                    () => inventory.ResolvePendingExtraction(false));
                return;
            }
            if (inventory.Fault != null)
            {
                DrawMessageCard(inRect, "Phinix_inventory_unavailableTitle".Translate(), inventory.Fault,
                    new Color(0.75f, 0.28f, 0.25f, 1f));
                return;
            }

            RefreshRows();
            float top = inRect.y;
            DrawHeader(new Rect(inRect.x, top, inRect.width, 50f));
            top += 50f + Spacing;

            if (pendingTransferCount > 0)
            {
                string pending = "Phinix_inventory_transferPending".Translate(pendingTransferCount);
                float pendingHeight = Mathf.Max(36f,
                    Text.CalcHeight(pending, Mathf.Max(1f, inRect.width - 24f)) + 12f);
                Rect pendingRect = new Rect(inRect.x, top, inRect.width, pendingHeight);
                Widgets.DrawBoxSolid(pendingRect, NoticeBackground);
                Widgets.DrawBoxSolid(new Rect(pendingRect.x, pendingRect.y, 3f, pendingRect.height),
                    new Color(0.95f, 0.72f, 0.2f, 1f));
                Widgets.Label(pendingRect.ContractedBy(8f), pending);
                top += pendingHeight + Spacing;
            }

            if (!string.IsNullOrEmpty(notice))
            {
                float noticeHeight = Mathf.Max(36f,
                    Text.CalcHeight(notice, Mathf.Max(1f, inRect.width - 24f)) + 12f);
                Rect noticeRect = new Rect(inRect.x, top, inRect.width, noticeHeight);
                Widgets.DrawBoxSolid(noticeRect, NoticeBackground);
                Widgets.DrawBoxSolid(new Rect(noticeRect.x, noticeRect.y, 3f, noticeRect.height),
                    new Color(0.95f, 0.72f, 0.2f, 1f));
                Widgets.Label(noticeRect.ContractedBy(8f), notice);
                top += noticeHeight + Spacing;
            }

            InventoryEntry selected = FindEntry(selectedEntryId);
            if (selected == null) CloseEditor();
            else if (editorMode == EditorMode.Extract) DrawExtractionEditor(inRect, selected, ref top);
            else if (editorMode == EditorMode.DailySchedule) DrawScheduleEditor(inRect, selected, ref top);

            Rect viewport = new Rect(inRect.x, top, inRect.width, Mathf.Max(0f, inRect.yMax - top));
            if (entries.Count == 0)
            {
                DrawEmptyState(viewport);
                return;
            }

            List<DisplayRow> drawingRows = visibleRows;
            Rect content = new Rect(0f, 0f, Mathf.Max(0f, viewport.width - 16f), drawingRows.Count * RowHeight);
            Widgets.BeginScrollView(viewport, ref scroll, content);
            try
            {
                VirtualListRange range = VirtualListLayout.GetFixedRange(
                    drawingRows.Count, RowHeight, scroll.y, viewport.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    DisplayRow row = drawingRows[i];
                    Rect rect = new Rect(0f, i * RowHeight, content.width, RowHeight - 4f);
                    if (row.IsHeader) DrawGroupRow(row.Group, rect);
                    else
                    {
                        if (row.Group.EntryIndices.Count > 1) { rect.x += 16f; rect.width -= 16f; }
                        DrawRow(row.EntryIndex, entries[row.EntryIndex], rect);
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void RefreshRows()
        {
            if (shownVersion == inventory.Version && ReferenceEquals(shownLanguage, LanguageDatabase.activeLanguage) &&
                ReferenceEquals(shownGame, Current.Game)) return;
            if (!ReferenceEquals(shownGame, Current.Game))
            {
                expandedGroups.Clear();
                CloseEditor();
                scroll = Vector2.zero;
            }
            entries = inventory.GetSnapshot();
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
            rowAvailable = new long[entries.Count];
            itemPresentations = new InventoryItemPresentation[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                InventoryEntry entry = entries[i];
                availableByEntry.TryGetValue(entry.EntryId, out rowAvailable[i]);
                InventorySourcePresentation source = inventory.PresentSource(entry);
                itemPresentations[i] = inventory.PresentItem(entry);
                rowTitles[i] = itemPresentations[i].Label;
                string detail = source?.Summary ?? string.Empty;
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
            RebuildVisibleRows();
            shownVersion = inventory.Version;
            shownLanguage = LanguageDatabase.activeLanguage;
            shownGame = Current.Game;
        }

        private void RebuildVisibleRows()
        {
            List<DisplayRow> rows = new List<DisplayRow>();
            foreach (InventoryDisplayGroup group in groups)
            {
                bool multiple = group.EntryIndices.Count > 1;
                if (multiple) rows.Add(new DisplayRow { Group = group, IsHeader = true });
                if (!multiple || expandedGroups.Contains(group.Key))
                    foreach (int index in group.EntryIndices)
                        rows.Add(new DisplayRow { Group = group, EntryIndex = index });
            }
            visibleRows = rows;
        }

        private void DrawGroupRow(InventoryDisplayGroup group, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, InfoBackground);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), InfoAccent);
            float buttonWidth = Mathf.Clamp(rect.width * 0.23f, 80f, 126f);
            Rect button = new Rect(rect.xMax - buttonWidth - 6f, rect.y + 10f, buttonWidth, 34f);
            Rect text = new Rect(rect.x + 10f, rect.y + 4f, Mathf.Max(0f, button.x - rect.x - 20f), rect.height - 8f);
            string[] labels = groupText[group];
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.LabelEllipses(new Rect(text.x, text.y, text.width, 27f), labels[0]);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.76f, 0.76f, 0.76f, 1f);
                Widgets.LabelEllipses(new Rect(text.x, text.y + 25f, text.width, 20f), labels[1]);
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
            TooltipHandler.TipRegion(text, labels[2]);
            bool expanded = expandedGroups.Contains(group.Key);
            if (Widgets.ButtonText(button, (expanded ? "Phinix_inventory_collapse" : "Phinix_inventory_expand").Translate()))
            {
                if (expanded) expandedGroups.Remove(group.Key); else expandedGroups.Add(group.Key);
                RebuildVisibleRows();
            }
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, InfoBackground);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), InfoAccent);
            Rect inner = rect.ContractedBy(8f);
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(inner.x, inner.y, Mathf.Max(0f, inner.width - 110f), 24f),
                    "Phinix_inventory_currentSaveTitle".Translate());
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inner.x, inner.y + 23f, inner.width, 20f),
                    "Phinix_inventory_saveOnly".Translate());
                Text.Anchor = TextAnchor.MiddleRight;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(inner.xMax - 110f, inner.y, 110f, 24f),
                    "Phinix_inventory_entryCount".Translate(groups.Count));
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; }
        }

        private void DrawRow(int index, InventoryEntry entry, Rect rect)
        {
            if ((index & 1) != 0) Widgets.DrawHighlight(rect);
            if (string.Equals(entry.EntryId, selectedEntryId, StringComparison.Ordinal))
                Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.55f, 0.82f, 0.14f));
            else if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);
            Widgets.DrawBox(rect, 1);

            float actionWidth = Mathf.Clamp(rect.width * 0.17f, 68f, 98f);
            float actionsWidth = actionWidth * 2f + 6f;
            Rect textRect = new Rect(rect.x + 10f, rect.y + 4f,
                Mathf.Max(0f, rect.width - actionsWidth - 22f), rect.height - 8f);
            Rect planRect = new Rect(rect.xMax - actionsWidth - 6f, rect.y + 10f, actionWidth, 34f);
            Rect extractRect = new Rect(planRect.xMax + 6f, planRect.y, actionWidth, 34f);

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                float balanceWidth = Mathf.Min(100f, textRect.width * 0.34f);
                Rect titleRect = new Rect(textRect.x, textRect.y,
                    Mathf.Max(0f, textRect.width - balanceWidth - 6f), 27f);
                Widgets.LabelEllipses(titleRect, rowTitles[index]);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(titleRect.xMax + 6f, titleRect.y, balanceWidth, 27f),
                    "Phinix_inventory_balance".Translate(checked(rowAvailable[index] * itemPresentations[index].ItemsPerUnit)));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.LowerLeft;
                GUI.color = new Color(0.76f, 0.76f, 0.76f, 1f);
                Widgets.LabelEllipses(new Rect(textRect.x, textRect.y + 25f, textRect.width, 20f),
                    string.IsNullOrEmpty(rowDetails[index])
                        ? "Phinix_inventory_originUnavailable".Translate().ToString() : rowDetails[index]);
            }
            finally { GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor; }

            TooltipHandler.TipRegion(textRect, rowTooltips[index]);
            if (Widgets.ButtonText(planRect, "Phinix_inventory_plan".Translate())) OpenScheduleEditor(entry);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && rowAvailable[index] > 0;
            try
            {
                if (Widgets.ButtonText(extractRect, "Phinix_inventory_extractStack".Translate())) OpenExtractionEditor(entry);
            }
            finally { GUI.enabled = previousEnabled; }
        }

        private void DrawExtractionEditor(Rect inRect, InventoryEntry entry, ref float top)
        {
            const float height = 92f;
            Rect card = new Rect(inRect.x, top, inRect.width, height);
            DrawEditorBackground(card);
            Rect inner = card.ContractedBy(10f);
            DrawEditorTitle(inner,
                GetExtractionTitle(entry));

            float fieldWidth = Mathf.Clamp(inner.width * 0.22f, 70f, 130f);
            float buttonWidth = Mathf.Max(70f, (inner.width - fieldWidth - 12f) / 2f);
            float rowY = inner.y + 38f;
            extractQuantityText = Widgets.TextField(new Rect(inner.x, rowY, fieldWidth, 30f), extractQuantityText);
            float buttonX = inner.x + fieldWidth + 6f;
            if (Widgets.ButtonText(new Rect(buttonX, rowY, buttonWidth, 30f),
                "Phinix_inventory_extractConfirm".Translate()))
            {
                if (!long.TryParse(extractQuantityText, out long quantity) || quantity < 1)
                    notice = "Phinix_inventory_extractInvalid".Translate();
                else if (!inventory.TryExtract(selectedEntryId, quantity, out string reason))
                    notice = string.IsNullOrEmpty(reason)
                        ? "Phinix_inventory_operationFailed".Translate().ToString() : reason;
                else
                {
                    notice = "Phinix_inventory_extractSucceeded".Translate(checked(quantity * GetPresentation(entry.EntryId).ItemsPerUnit));
                    CloseEditor();
                    shownVersion = -1;
                }
            }
            if (Widgets.ButtonText(new Rect(buttonX + buttonWidth + 6f, rowY, buttonWidth, 30f),
                "Phinix_inventory_cancel".Translate())) CloseEditor();
            top += height + Spacing;
        }

        private void DrawScheduleEditor(Rect inRect, InventoryEntry entry, ref float top)
        {
            const float height = 132f;
            Rect card = new Rect(inRect.x, top, inRect.width, height);
            DrawEditorBackground(card);
            Rect inner = card.ContractedBy(10f);
            DrawEditorTitle(inner,
                "Phinix_inventory_scheduleEditorTitle".Translate(GetPresentation(entry.EntryId).Label));

            float columnWidth = (inner.width - 6f) / 2f;
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y + 31f, columnWidth, 20f),
                GetPresentation(entry.EntryId).ItemsPerUnit > 1
                    ? "Phinix_inventory_scheduleStackQuantity".Translate(GetPresentation(entry.EntryId).ItemsPerUnit)
                    : "Phinix_inventory_scheduleQuantity".Translate());
            Widgets.Label(new Rect(inner.x + columnWidth + 6f, inner.y + 31f, columnWidth, 20f),
                "Phinix_inventory_scheduleTime".Translate());
            Text.Font = oldFont;
            scheduleQuantityText = Widgets.TextField(new Rect(inner.x, inner.y + 52f, columnWidth, 28f),
                scheduleQuantityText);
            dayTickText = Widgets.TextField(new Rect(inner.x + columnWidth + 6f, inner.y + 52f, columnWidth, 28f),
                dayTickText);

            float buttonY = inner.y + 88f;
            bool hasSchedule = FindSchedule(entry.EntryId) != null;
            if (Widgets.ButtonText(new Rect(inner.x, buttonY, columnWidth, 30f),
                "Phinix_inventory_savePlan".Translate()))
            {
                if (!long.TryParse(scheduleQuantityText, out long quantity) ||
                    !int.TryParse(dayTickText, out int tick) ||
                    !inventory.SetDailySchedule(selectedEntryId, quantity, tick, out string reason))
                    notice = "Phinix_inventory_planInvalid".Translate();
                else { notice = "Phinix_inventory_planSaved".Translate(); shownVersion = -1; }
            }
            if (Widgets.ButtonText(new Rect(inner.x + columnWidth + 6f, buttonY, columnWidth, 30f),
                hasSchedule ? "Phinix_inventory_removePlan".Translate() : "Phinix_inventory_cancel".Translate()))
            {
                if (!hasSchedule)
                {
                    CloseEditor();
                }
                else if (inventory.RemoveDailySchedule(selectedEntryId))
                {
                    notice = "Phinix_inventory_planRemoved".Translate();
                    CloseEditor();
                    shownVersion = -1;
                }
                else notice = "Phinix_inventory_operationFailed".Translate();
            }
            top += height + Spacing;
        }

        private void DrawResolutionPanel(Rect inRect, string title, string description,
            string firstButton, string secondButton, Func<bool> firstAction, Func<bool> secondAction)
        {
            float descriptionHeight = Mathf.Max(52f,
                Text.CalcHeight(description, Mathf.Max(1f, inRect.width - 28f)));
            Rect card = new Rect(inRect.x, inRect.y, inRect.width,
                Mathf.Min(descriptionHeight + 104f, inRect.height));
            Widgets.DrawBoxSolid(card, NoticeBackground);
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, 4f, card.height),
                new Color(0.95f, 0.72f, 0.2f, 1f));
            Rect inner = card.ContractedBy(14f);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 32f), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y + 38f, inner.width, descriptionHeight), description);
            Text.Font = oldFont;

            float buttonWidth = (inner.width - 8f) / 2f;
            float buttonY = inner.y + 42f + descriptionHeight;
            if (Widgets.ButtonText(new Rect(inner.x, buttonY, buttonWidth, 34f), firstButton))
                notice = firstAction() ? null : "Phinix_inventory_operationFailed".Translate();
            if (Widgets.ButtonText(new Rect(inner.x + buttonWidth + 8f, buttonY, buttonWidth, 34f), secondButton))
                notice = secondAction() ? null : "Phinix_inventory_operationFailed".Translate();
            if (!string.IsNullOrEmpty(notice))
                Widgets.Label(new Rect(inner.x, buttonY + 40f, inner.width, 28f), notice);
            shownVersion = -1;
        }

        private static void DrawMessageCard(Rect rect, string title, string description, Color accent)
        {
            Widgets.DrawBoxSolid(rect, new Color(accent.r, accent.g, accent.b, 0.13f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, rect.height), accent);
            Rect inner = rect.ContractedBy(14f);
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 34f), title);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y + 42f, inner.width, inner.height - 42f), description);
            Text.Font = oldFont;
        }

        private static void DrawEditorBackground(Rect card)
        {
            Widgets.DrawBoxSolid(card, EditorBackground);
            Widgets.DrawBoxSolid(new Rect(card.x, card.y, 3f, card.height), InfoAccent);
            Widgets.DrawBox(card, 1);
        }

        private static void DrawEditorTitle(Rect inner, string title)
        {
            GameFont oldFont = Text.Font;
            Text.Font = GameFont.Small;
            Widgets.LabelEllipses(new Rect(inner.x, inner.y, inner.width, 28f), title);
            TooltipHandler.TipRegion(new Rect(inner.x, inner.y, inner.width, 28f), title);
            Text.Font = oldFont;
        }

        private static void DrawEmptyState(Rect rect)
        {
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
                Widgets.Label(rect, "Phinix_inventory_empty".Translate());
            }
            finally { Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor; }
        }

        private void OpenExtractionEditor(InventoryEntry entry)
        {
            selectedEntryId = entry.EntryId;
            editorMode = EditorMode.Extract;
            extractQuantityText = "1";
            notice = null;
        }

        private void OpenScheduleEditor(InventoryEntry entry)
        {
            selectedEntryId = entry.EntryId;
            editorMode = EditorMode.DailySchedule;
            InventorySchedule schedule = FindSchedule(entry.EntryId);
            scheduleQuantityText = schedule == null ? "1" : schedule.QuantityPerRun.ToString();
            dayTickText = schedule == null ? "30000" : schedule.DayTick.ToString();
            notice = null;
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
            selectedEntryId = null;
            editorMode = EditorMode.None;
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
