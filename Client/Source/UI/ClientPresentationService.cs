using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;
using static PhinixClient.ClientPresentationFeedMetrics;

namespace PhinixClient
{
    // Neutral Host implementation. It never references a business plugin or reads package/theme files.
    internal sealed partial class ClientPresentationService : IClientPresentationService
    {
        private readonly IClientLifetimeService lifetimes;
        private readonly IClientTheme theme;
        private readonly Func<bool> mainThread;
        private readonly Action<string> log;
        private readonly HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);

        internal ClientPresentationService(IClientLifetimeService lifetimes, IClientTheme theme, Func<bool> mainThread, Action<string> log)
        { this.lifetimes = lifetimes; this.theme = theme; this.mainThread = mainThread; this.log = log; }
        private void MainThread()
        { if (!mainThread()) throw new InvalidOperationException("PresentationRequiresMainThread"); }

        public IClientPresentationSession Open(IPhinixExtensionModule module, string pageId)
        {
            MainThread();
            // Validate using the public model, before acquiring module-owned resources.
            new ClientPresentationPage(pageId, "", 0, null);
            var lifetime = lifetimes.ForModule(module); // Validates the actual active module instance.
            string identity = lifetime.OwnerId + "/" + pageId;
            if (!identities.Add(identity)) { lifetime.Dispose(); throw new InvalidOperationException("PresentationPageAlreadyOpen"); }
            try
            {
                var session = new Session(this, lifetime, pageId, identity);
                lifetime.Own(session);
                return session;
            }
            catch { identities.Remove(identity); lifetime.Dispose(); throw; }
        }

        private static readonly Regex IntegerSyntax = new Regex(@"^-?[0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static Regex Validator(ClientPresentationTextField field) => field.InputKind == ClientPresentationTextInputKind.Integer ? IntegerSyntax : null;

        private static string L(string key) => ("Phinix_presentation_" + key).Translate();

        private sealed partial class Session : IClientPresentationSession
        {
            private readonly ClientPresentationService service;
            private readonly IClientLifetimeScope lifetime;
            private readonly string identity;
            private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<ClientPresentationAction> attempted = new HashSet<ClientPresentationAction>();
            private ClientPresentationPage page;
            private IClientActionSet actions;
            private Func<bool> isCurrent;
            private ClientThemeSnapshot palette;
            private long generation;
            private Vector2 scroll;
            private float measuredWidth = -1, contentHeight;
            private float composerWidth = -1, composerHeight, composerButtonWidth, composerButtonHeight;
            private bool composerStacked;
            private List<Block> blocks;
            private readonly Dictionary<ClientPresentationBadge, Vector2> badgeHeights = new Dictionary<ClientPresentationBadge, Vector2>();
            private readonly Dictionary<ClientPresentationBadge, float> badgeWidths = new Dictionary<ClientPresentationBadge, float>();
            private sealed class PaneState
            { internal Vector2 Scroll; internal float Width = -1, Height; internal List<Block> Blocks; internal bool Following = true; }
            private readonly PaneState[] panes = { new PaneState(), new PaneState(), new PaneState(), new PaneState(), new PaneState(), new PaneState(), new PaneState(), new PaneState(), new PaneState() };
            private int narrowPane, sectionStart, sectionEnd = int.MaxValue;
            private void InvalidatePaneMeasurements() { composerWidth = -1; badgeWidths.Clear(); badgeHeights.Clear(); foreach (var pane in panes) pane.Blocks = null; }
            private ConfirmationWindow confirmation;
            private int composerFocusAttempts;
            private bool feedFollowing = true, forceEnd;
            private string scrollFeed, scrollRow, highlightedFeed, highlightedRow;
            private float highlightUntil;
            private readonly HashSet<string> mediaVisibility = new HashSet<string>(StringComparer.Ordinal);
            public string ComposerControlName => identity + "." + page?.Composer?.Field.Id;
            public bool ComposerFocused { get; private set; }
            public string ComposerDraft => page?.Composer == null ? "" : drafts[page.Composer.Field.Id];
            private readonly Queue<ClientPresentationInput> inputs = new Queue<ClientPresentationInput>();
            private readonly Dictionary<string, string> drafts = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string,bool> colorDragging=new Dictionary<string,bool>(StringComparer.Ordinal);
            private readonly HashSet<string> failedRegions = new HashSet<string>(StringComparer.Ordinal);

            internal Session(ClientPresentationService service, IClientLifetimeScope lifetime, string pageId, string identity)
            { this.service = service; this.lifetime = lifetime; this.identity = identity; OwnerId = lifetime.OwnerId; PageId = pageId; }
            public string OwnerId { get; }
            public string PageId { get; }
            public bool IsClosed { get; private set; }
            public string FailureCode { get; private set; }
            public ClientActionExecutionResult LastActionResult { get; private set; }
            private void Require()
            { service.MainThread(); if (IsClosed || lifetime.IsClosed) throw new ObjectDisposedException("PresentationSession"); }

            public void Publish(ClientPresentationPage next, IClientActionSet nextActions, Func<bool> current)
            {
                Require();
                if (next == null || current == null) throw new ArgumentNullException("presentation");
                if (next.Id != PageId) throw new ArgumentException("PresentationPageIdentityChanged");
                if (page != null && next.Revision <= page.Revision) throw new ArgumentException("PresentationRevisionMustIncrease");
                if (nextActions != null && ReferenceEquals(actions, nextActions)) throw new ArgumentException("PresentationRequiresNewActionSet");
                var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
                var uniqueActions = new HashSet<IClientContributedAction>();
                int count = 0;
                foreach (var section in next.Sections) count += section.Actions.Count;
                if (next.Composer != null) count += 1 + next.Composer.Actions.Count;
                if (count > 32) throw new ArgumentException("PresentationActionLimitOrDuplicate");
                count = 0;
                foreach (var binding in Bindings(next))
                {
                    if (++count > 544 || !uniqueIds.Add(binding.Id) || !uniqueActions.Add(binding.Action)) throw new ArgumentException("PresentationActionLimitOrDuplicate");
                    if (nextActions == null) throw new ArgumentException("PresentationActionSetRequired");
                    bool member = false;
                    foreach (var action in nextActions.Actions) if (ReferenceEquals(action, binding.Action)) { member = true; break; }
                    if (!member) throw new ArgumentException("PresentationActionNotInOwnedSet");
                }
                var contentIds = new HashSet<string>(StringComparer.Ordinal);
                int fields = 0, rowFields = 0, rows = 0;
                foreach (var section in next.Sections)
                {
                    foreach (var field in section.Fields)
                        if (++fields > 32 || !contentIds.Add(field.Id)) throw new ArgumentException("PresentationFieldLimitOrDuplicate");
                    if (section.List != null)
                    {
                        foreach (var row in section.List.Rows)
                        {
                            if (section.List.IsReadOnly && (row.Field != null || row.Actions.Count > 0)) throw new ArgumentException("PresentationReadOnlyControls");
                            if (row.Field != null && (++rowFields > 256 || !contentIds.Add(row.Field.Id))) throw new ArgumentException("PresentationRowFieldLimitOrDuplicate");
                        }
                        rows += section.List.Rows.Count;
                        if (rows > 20000) throw new ArgumentException("PresentationRowLimit");
                        if (!contentIds.Add(section.List.Id)) throw new ArgumentException("PresentationContentDuplicate");
                    }
                    if (section.Feed != null)
                    {
                        rows += section.Feed.Rows.Count;
                        if (rows > 20000 || !contentIds.Add(section.Feed.Id)) throw new ArgumentException("PresentationFeedLimitOrDuplicate");
                    }
                    if (section.Region != null && !contentIds.Add(section.Region.Id)) throw new ArgumentException("PresentationContentDuplicate");
                }
                bool selectionChanged = false;
                // Shared lower editors do not navigate or reset either comparison pane.
                int selectionSections = next.Sections.Count - (next.SplitView?.SharedFooterSections ?? 0);
                if (page != null) for (int i = 0; i < selectionSections; i++)
                    if (next.Sections[i].List != null) foreach (var previousSection in page.Sections)
                        if (previousSection.List != null && previousSection.List.Id == next.Sections[i].List.Id && previousSection.List.SelectedId != next.Sections[i].List.SelectedId) selectionChanged = true;
                if (selectionChanged || next.SplitView?.SecondaryContextId != page?.SplitView?.SecondaryContextId)
                {
                    colorDragging.Clear();
                    if (next.SplitView == null) { if (next.FixedHeaderSectionCount > 0) panes[0].Scroll.y = 0; else scroll.y = 0; }
                    else
                    {
                        // Keep the catalogue position while opening a new detail projection.
                        panes[1].Scroll.y = 0;
                        if (page?.SplitView != null && Event.current != null && Event.current.type != EventType.Layout) narrowPane = 1;
                    }
                }
                if (next.Composer != null && (++fields > 32 || !contentIds.Add(next.Composer.Field.Id))) throw new ArgumentException("PresentationContentDuplicate");
                // Validation completed; ownership now transfers, including when cleanup callbacks fail.
                var previous = actions;
                page = next; actions = nextActions; isCurrent = current; generation++;
                attempted.Clear(); blocks = null; InvalidatePaneMeasurements(); FailureCode = null; inputs.Clear(); failedRegions.Clear(); mediaVisibility.Clear();
                var pickerIds=new HashSet<string>();
                drafts.Clear(); foreach (var field in Fields(next)) {drafts.Add(field.Id, field.Value);if(field.ColorPicker!=null)pickerIds.Add(field.Id);}
                foreach(var id in new List<string>(colorDragging.Keys))if(!pickerIds.Contains(id))colorDragging.Remove(id);
                if (next.Composer != null) drafts.Add(next.Composer.Field.Id, next.Composer.Field.Value);
                CloseConfirmation(); Release(previous);
            }

            private bool Current(long ticket)
            {
                if (IsClosed || lifetime.IsClosed || ticket != generation || isCurrent == null) return false;
                try { return isCurrent(); }
                catch (Exception error) { Fail("ContextFailed", error); return false; }
            }

            private void Fail(string code, Exception error = null)
            {
                FailureCode = code;
                if (error == null || reported.Count >= 16 || !reported.Add(code + "/" + error.GetType().FullName)) return;
                try { service.log?.Invoke("Presentation: owner=" + OwnerId + ", page=" + PageId + ", stage=" + code + ", exception=" + error.GetType().Name); }
                catch { }
            }
            private void Release(IDisposable resource)
            { try { resource?.Dispose(); } catch (Exception error) { Fail("ReleaseFailed", error); } }

            private void UpdatePalette()
            {
                if (palette != null && palette.Version == service.theme.Version) return;
                palette = service.theme.Capture(); blocks = null; InvalidatePaneMeasurements();
            }
            private static Color OptionalColor(IClientThemeReader reader, string key, Color fallback)
            { return key != null && reader.TryGetColor(key, out var value) ? value.ToUnityColor() : fallback; }
            private static Color Tone(IClientThemeReader reader, ClientPresentationTone tone)
            {
                switch (tone)
                {
                    case ClientPresentationTone.Pending: return reader.Pending.ToUnityColor();
                    case ClientPresentationTone.Warning: return reader.Warning.ToUnityColor();
                    case ClientPresentationTone.Error: return reader.Error.ToUnityColor();
                    case ClientPresentationTone.Success: return reader.Success.ToUnityColor();
                    default: return reader.SecondaryText.ToUnityColor();
                }
            }

            private sealed class Block
            {
                internal Rect Rect;
                internal string Text;
                internal ClientPresentationSection Section;
                internal ClientPresentationAction Action;
                internal ClientPresentationTextField Field;
                internal ClientPresentationSwatch Swatch;
                internal ClientPresentationList List;
                internal ClientPresentationFeed Feed;
                internal float[] RowOffsets, MetadataOffsets, MetadataWidths;
                internal ClientPresentationRegion Region;
                internal ClientPresentationActionMenu Menu;
                internal IReadOnlyList<ClientPresentationBadge> Badges;
                internal bool Surface, Secondary, Summary;
                internal ClientPresentationTone Tone;
            }
            private void Measure(float width)
            {
                if (blocks != null && measuredWidth == width) return;
                measuredWidth = width; blocks = new List<Block>();
                float y = string.IsNullOrEmpty(page.Title) && page.Sections.Count == 1 && page.Sections[0].Feed != null ? 0 : ClientWidgets.Gap;
                if (page.SplitView == null && page.FixedHeaderSectionCount == 0) AddText(page.Title, width, ref y);
                for (int sectionIndex = sectionStart; sectionIndex < Math.Min(sectionEnd, page.Sections.Count); sectionIndex++)
                {
                    var section = page.Sections[sectionIndex];
                    if (string.IsNullOrEmpty(section.Title) && string.IsNullOrEmpty(section.Summary) && string.IsNullOrEmpty(section.Status?.Text) &&
                        section.Actions.Count == 0 && section.Fields.Count == 0 && section.List == null && section.Feed == null && section.Region == null && section.Badges.Count == 0 && section.Swatches.Count == 0) continue;
                    float padding = section.Plain ? 4 : ClientWidgets.CardPadding;
                    float start = y; int index = blocks.Count;
                    blocks.Add(new Block { Surface = section.Feed == null && !section.Plain, Section = section }); y += section.Feed == null ? padding : 6;
                    if (section.InlineToolbar && section.Feed == null && section.List == null && section.Region == null && section.Badges.Count == 0 && section.Swatches.Count == 0 && !HasSlider(section))
                    {
                        MeasureToolbar(section, width, padding, ref y);
                        blocks[index].Rect = new Rect(0, start, width, y - start + padding);
                        y += padding + 4;
                        continue;
                    }
                    if (section.Feed != null)
                    {
                        var feed = section.Feed; float innerWidth = Mathf.Max(1, width - 16);
                        var offsets = new float[feed.Rows.Count + 1];
                        var metadataOffsets = new float[feed.Rows.Count]; var metadataWidths = new float[feed.Rows.Count];
                        for (int i = 0; i < feed.Rows.Count; i++)
                        {
                            offsets[i + 1] = offsets[i] + FeedRowHeight(feed.Rows[i], innerWidth);
                            metadataWidths[i] = MetadataWidth(feed.Rows[i], Mathf.Max(1, innerWidth - 24 - (feed.Rows[i].Indented ? 16 : 0)));
                            metadataOffsets[i] = FeedMetadataRect(feed.Rows[i], 0, 0, Mathf.Max(1, innerWidth - 24 - (feed.Rows[i].Indented ? 16 : 0))).x;
                        }
                        blocks.Add(new Block { Rect = new Rect(8, y, innerWidth, offsets[feed.Rows.Count]), Feed = feed, RowOffsets = offsets, MetadataOffsets = metadataOffsets, MetadataWidths = metadataWidths });
                        if (feed.Rows.Count == 0) AddText(feed.EmptyText, innerWidth, ref y, section, true);
                        else y += offsets[feed.Rows.Count];
                    }
                    AddText(string.IsNullOrEmpty(section.Title) ? "" : "<b>" + section.Title + "</b>", width - 2 * padding, ref y, section);
                    if (section.Badges.Count > 0)
                    {
                        blocks.Add(new Block { Rect = new Rect(padding, y, Mathf.Max(1, width - 2 * padding), BadgeHeight(section.Badges, width - 2 * padding)), Badges = section.Badges });
                        y += blocks[blocks.Count - 1].Rect.height + 6;
                    }
                    if (section.Swatches.Count > 0)
                    {
                        float swatchX = padding; float available = Mathf.Max(1, width - 2 * padding);
                        foreach (var swatch in section.Swatches)
                        {
                            float cellWidth = Mathf.Min(available, Mathf.Max(72, Text.CalcSize(swatch.Label).x + 40));
                            if (swatchX > padding && swatchX + cellWidth > width - padding) { swatchX = padding; y += 38; }
                            blocks.Add(new Block { Rect = new Rect(swatchX, y, cellWidth, 32), Swatch = swatch }); swatchX += cellWidth + 6;
                        }
                        y += 38;
                    }
                    AddText(section.Summary, width - 2 * padding, ref y, section, true, summary: true);
                    if (section.Status != null) AddText(section.Status.Text, width - 2 * padding, ref y, section, true, section.Status.Tone);
                    float fieldSpace = Mathf.Max(1, width - 2 * padding);
                    int columns = Math.Max(1, Math.Min(section.FieldColumns, (int)((fieldSpace + ClientWidgets.Gap) / (section.FieldMinimumWidth + ClientWidgets.Gap))));
                    float fieldWidth = (fieldSpace - (columns - 1) * ClientWidgets.Gap) / columns;
                    for (int first = 0; first < section.Fields.Count; first += columns)
                    {
                        float rowBottom = y;
                        for (int column = 0; column < columns && first + column < section.Fields.Count; column++)
                        {
                            var field = section.Fields[first + column]; float cellY = y;
                            int firstBlock = blocks.Count;
                            AddText(field.Label, fieldWidth, ref cellY, section);
                            blocks.Add(new Block { Rect = new Rect(padding, cellY, fieldWidth, (field.ColorPicker!=null?ColorPickerHeight(fieldWidth):ClientWidgets.ControlHeight + (field.Slider == null ? 0 : 36))), Field = field });
                            cellY += (field.ColorPicker!=null?ColorPickerHeight(fieldWidth):ClientWidgets.ControlHeight + (field.Slider == null ? 0 : 36)) + ClientWidgets.Gap;
                            AddText(field.ValidationText, fieldWidth, ref cellY, section, true, ClientPresentationTone.Warning);
                            for (int b = firstBlock; b < blocks.Count; b++) { var cell = blocks[b].Rect; cell.x += column * (fieldWidth + ClientWidgets.Gap); blocks[b].Rect = cell; }
                            rowBottom = Mathf.Max(rowBottom, cellY);
                        }
                        y = rowBottom;
                    }
                    if (section.List != null)
                    {
                        var list = section.List;
                        if (list.Rows.Count == 0) AddText(list.EmptyText, width - 2 * padding, ref y, section, true);
                        else
                        {
                            float[] offsets = new float[list.Rows.Count + 1];
                            for (int i = 0; i < list.Rows.Count; i++)
                            {
                                var row = list.Rows[i];
                                offsets[i + 1] = offsets[i] + RowHeight(row, Mathf.Max(1, width - 2 * padding), list.CompactRows);
                            }
                            blocks.Add(new Block { Rect = new Rect(padding, y, Mathf.Max(1, width - 2 * padding), offsets[list.Rows.Count]), List = list, RowOffsets = offsets });
                            y += offsets[list.Rows.Count] + 8;
                        }
                    }
                    if (section.Region != null)
                    {
                        float height = 32;
                        try
                        {
                            using (new ClientGuiScope(true)) height = section.Region.Content.Measure(Mathf.Max(1, width - 2 * padding), palette);
                            if (float.IsNaN(height) || float.IsInfinity(height) || height < 0 || height > 4096) throw new ArgumentException("PresentationRegionHeightInvalid");
                        }
                        catch (Exception error) { failedRegions.Add(section.Region.Id); Fail("RegionMeasureFailed", error); height = 32; }
                        blocks.Add(new Block { Rect = new Rect(padding, y, Mathf.Max(1, width - 2 * padding), height), Region = section.Region }); y += height + 8;
                    }
                    // Bounded toolbar: wrap each action to available width and keep every action scroll-accessible.
                    float actionX = padding;
                    float actionInnerWidth = Mathf.Max(1, width - 2 * padding);
                    bool wrappedRow = false; float rowHeight = ClientWidgets.ControlHeight;
                    var grouped = new HashSet<string>();
                    foreach (var menu in section.ActionMenus) foreach (var id in menu.ActionIds) grouped.Add(id);
                    foreach (var binding in section.Actions)
                    {
                        if (grouped.Contains(binding.Id)) continue;
                        string label = binding.Action.Label ?? "";
                        if (label.Length > 4096) throw new ArgumentException("PresentationActionLabelLimit");
                        var buttonSize = ClientWidgets.MeasureButton(label, actionInnerWidth, minimumWidth: section.WrapActions ? 96 : actionInnerWidth);
                        float actionWidth = section.WrapActions ? buttonSize.x : actionInnerWidth;
                        if (section.WrapActions && actionX > padding && actionX + actionWidth > width - padding)
                        { actionX = padding; y += rowHeight + 6; rowHeight = ClientWidgets.ControlHeight; }
                        float height = buttonSize.y;
                        blocks.Add(new Block { Rect = new Rect(actionX, y, actionWidth, height), Action = binding, Text = label, Section = section });
                        if (section.WrapActions) { actionX += actionWidth + 6; rowHeight = Mathf.Max(rowHeight, height); wrappedRow = true; }
                        else y += height + 6;
                    }
                    foreach (var menu in section.ActionMenus)
                    {
                        var menuSize = ClientWidgets.MeasureButton(menu.Label, actionInnerWidth, dropdown: true);
                        float menuWidth = menuSize.x;
                        if (actionX > padding && actionX + menuWidth > width - padding) { actionX = padding; y += rowHeight + 6; rowHeight = ClientWidgets.ControlHeight; }
                        blocks.Add(new Block { Rect = new Rect(actionX, y, menuWidth, menuSize.y), Menu = menu, Section = section, Text = menu.Label });
                        actionX += menuWidth + 6; rowHeight = Mathf.Max(rowHeight, menuSize.y); wrappedRow = true;
                    }
                    if (wrappedRow) y += rowHeight + 6;
                    float contentEnd = start + padding;
                    for (int i = index + 1; i < blocks.Count; i++) contentEnd = Mathf.Max(contentEnd, blocks[i].Rect.yMax);
                    y = contentEnd + padding;
                    blocks[index].Rect = new Rect(0, start, width, y - start); y += section.Plain ? 4 : ClientWidgets.Gap;
                }
                contentHeight = y;
            }
            private static bool HasSlider(ClientPresentationSection section)
            { foreach (var field in section.Fields) if (field.Slider != null||field.ColorPicker!=null) return true; return false; }
            private void MeasureToolbar(ClientPresentationSection section, float width, float padding, ref float y)
            {
                float available = Mathf.Max(1, width - 2 * padding), x = padding, rowY = y, rowHeight = 0;
                Func<float, float, Rect> cell = (w, h) => {
                    w = Mathf.Min(w, available);
                    if (x > padding && x + w > width - padding) { x = padding; rowY += rowHeight + 6; rowHeight = 0; }
                    var rect = new Rect(x, rowY, w, h); x += w + 6; rowHeight = Mathf.Max(rowHeight, h); return rect;
                };
                if (!string.IsNullOrEmpty(section.Title))
                {
                    string title = "<b>" + section.Title + "</b>"; float w = Mathf.Min(240, available);
                    blocks.Add(new Block { Rect = cell(w, Mathf.Max(32, Text.CalcHeight(title, w))), Text = title, Section = section });
                }
                foreach (var field in section.Fields)
                {
                    float w = Mathf.Min(256, available);
                    float label = string.IsNullOrEmpty(field.Label) ? 0 : Text.CalcHeight(field.Label, w) + 4;
                    float validation = string.IsNullOrEmpty(field.ValidationText) ? 0 : Text.CalcHeight(field.ValidationText, w) + 4;
                    var rect = cell(w, label + ClientWidgets.ControlHeight + validation);
                    if (label > 0) blocks.Add(new Block { Rect = new Rect(rect.x, rect.y, w, label - 4), Text = field.Label, Section = section });
                    blocks.Add(new Block { Rect = new Rect(rect.x, rect.y + label, w, ClientWidgets.ControlHeight), Field = field, Section = section });
                    if (validation > 0) blocks.Add(new Block { Rect = new Rect(rect.x, rect.y + label + ClientWidgets.ControlHeight + 4, w, validation - 4), Text = field.ValidationText, Section = section, Secondary = true, Tone = ClientPresentationTone.Warning });
                }
                var grouped = new HashSet<string>(); foreach (var menu in section.ActionMenus) foreach (var id in menu.ActionIds) grouped.Add(id);
                foreach (var binding in section.Actions)
                {
                    if (grouped.Contains(binding.Id)) continue;
                    var size = ClientWidgets.MeasureButton(binding.Action.Label, Mathf.Min(180, available), minimumWidth: 80);
                    blocks.Add(new Block { Rect = cell(size.x, size.y), Action = binding, Text = binding.Action.Label, Section = section });
                }
                foreach (var menu in section.ActionMenus)
                {
                    var size = ClientWidgets.MeasureButton(menu.Label, Mathf.Min(240, available), dropdown: true);
                    blocks.Add(new Block { Rect = cell(size.x, size.y), Menu = menu, Text = menu.Label, Section = section });
                }
                y = rowY + rowHeight + (rowHeight > 0 ? 4 : 0);
                AddText(section.Summary, available, ref y, section, true, summary: true);
                if (section.Status != null) AddText(section.Status.Text, available, ref y, section, true, section.Status.Tone);
            }
            private void AddText(string text, float width, ref float y, ClientPresentationSection section = null,
                bool secondary = false, ClientPresentationTone tone = ClientPresentationTone.Neutral, bool summary = false)
            {
                if (string.IsNullOrEmpty(text)) return;
                float height = Mathf.Max(24, Text.CalcHeight(text, Mathf.Max(1, width)));
                blocks.Add(new Block { Rect = new Rect(section == null ? 0 : section.Plain ? 4 : ClientWidgets.CardPadding, y, Mathf.Max(1, width), height),
                    Text = text, Section = section, Secondary = secondary, Summary = summary, Tone = tone });
                y += height + 6;
            }

            public void Draw(ClientPresentationBounds bounds)
            {
                var rect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                service.MainThread();
                if (IsClosed || lifetime.IsClosed || page == null) return;
                if (page.SplitView == null && page.FixedHeaderSectionCount == 0) { sectionStart = 0; sectionEnd = int.MaxValue; DrawContent(rect); return; }
                if (float.IsNaN(rect.width) || float.IsNaN(rect.height) || float.IsInfinity(rect.width) || float.IsInfinity(rect.height) || rect.width < 24 || rect.height < 48) return;
                using (new ClientGuiScope(true))
                {
                    UpdatePalette(); Text.Font = GameFont.Small; Text.WordWrap = true;
                    if (page.Composer != null)
                    {
                        float dockHeight = Mathf.Min(rect.height, ComposerHeight(rect.width));
                        DrawComposer(new Rect(rect.x, rect.yMax - dockHeight, rect.width, dockHeight));
                        rect.height -= dockHeight + 8;
                    }
                    if (!string.IsNullOrEmpty(page.Title))
                    {
                        ClientWidgets.LabelEllipses(new Rect(rect.x, rect.y, rect.width, 28), page.Title, palette);
                        TooltipHandler.TipRegion(new Rect(rect.x, rect.y, rect.width, 28), page.Title);
                        rect.y += 32; rect.height -= 32;
                    }
                    var split = page.SplitView;
                    long headerGeneration = generation;
                    if (split == null)
                    {
                        DrawFixedHeader(4, 0, page.FixedHeaderSectionCount, ref rect);
                        if (generation != headerGeneration || IsClosed) return;
                        DrawRange(0, page.FixedHeaderSectionCount, page.Sections.Count, rect, true);
                        return;
                    }
                    if (split.GlobalHeaderSections > 0) DrawFixedHeader(4, 0, split.GlobalHeaderSections, ref rect);
                    if (generation != headerGeneration || IsClosed) return;
                    Rect sharedFooter = default(Rect);
                    if (split.SharedFooterSections > 0)
                    {
                        float height = Mathf.Max(48, rect.height * split.SharedFooterFraction);
                        height = Mathf.Min(height, Mathf.Max(0, rect.height - 100));
                        sharedFooter = new Rect(rect.x, rect.yMax - height, rect.width, height);
                        rect.height -= height + 8;
                    }
                    if (rect.width < split.MinimumWidth)
                    {
                        float half = Mathf.Max(1, (rect.width - 8) / 2);
                        if (ClientWidgets.Button(new Rect(rect.x, rect.y, half, ClientWidgets.ControlHeight), split.PrimaryTitle, palette, narrowPane == 0 ? ClientButtonKind.Primary : ClientButtonKind.Normal)) narrowPane = 0;
                        if (ClientWidgets.Button(new Rect(rect.x + half + 8, rect.y, half, ClientWidgets.ControlHeight), split.SecondaryTitle, palette, narrowPane == 1 ? ClientButtonKind.Primary : ClientButtonKind.Normal)) narrowPane = 1;
                        rect.y += ClientWidgets.ControlHeight + 8; rect.height -= ClientWidgets.ControlHeight + 8;
                        DrawPane(narrowPane, rect);
                    }
                    else
                    {
                        float left = (rect.width - 12) * split.PrimaryFraction;
                        long splitGeneration = generation;
                        float aligned = -1;
                        if (split.AlignHeaders)
                            aligned = Mathf.Max(HeaderHeight(2, split.GlobalHeaderSections, split.GlobalHeaderSections + split.PrimaryHeaderSections, left, rect.height),
                                HeaderHeight(3, split.PrimarySectionCount, split.PrimarySectionCount + split.SecondaryHeaderSections, rect.width - left - 12, rect.height));
                        DrawPane(0, new Rect(rect.x, rect.y, left, rect.height), aligned);
                        if (generation != splitGeneration || IsClosed) return;
                        DrawPane(1, new Rect(rect.x + left + 12, rect.y, rect.width - left - 12, rect.height), aligned);
                        if (split.AlignHeaders) ClientWidgets.Fill(new Rect(rect.x + left + 6, rect.y, 1, Mathf.Max(0, rect.height)), palette.Separator.ToUnityColor());
                    }
                    if (split.SharedFooterSections > 0 && generation == headerGeneration && !IsClosed)
                    {
                        ClientWidgets.Fill(new Rect(sharedFooter.x, sharedFooter.y - 4, sharedFooter.width, 1), palette.Separator.ToUnityColor());
                        int footerStart = page.Sections.Count - split.SharedFooterSections;
                        if (split.SharedFooterHeaderSections > 0) DrawFixedHeader(8, footerStart, footerStart + split.SharedFooterHeaderSections, ref sharedFooter);
                        if (generation != headerGeneration || IsClosed) return;
                        DrawRange(7, footerStart + split.SharedFooterHeaderSections, page.Sections.Count, sharedFooter, true);
                    }
                }
            }
            private void DrawPane(int index, Rect rect, float aligned = -1)
            {
                var split = page.SplitView; long ticket = generation;
                int start = index == 0 ? split.GlobalHeaderSections : split.PrimarySectionCount;
                int end = index == 0 ? split.PrimarySectionCount : page.Sections.Count - split.SharedFooterSections;
                int header = index == 0 ? split.PrimaryHeaderSections : split.SecondaryHeaderSections;
                if (header > 0) DrawFixedHeader(index + 2, start, start + header, ref rect, aligned);
                if (generation != ticket || IsClosed) return;
                int footer = index == 0 ? split.PrimaryFooterSections : split.SecondaryFooterSections;
                if (footer > 0)
                {
                    float height = HeaderHeight(index + 5, end - footer, end, rect.width, rect.height);
                    height = Mathf.Min(height, Mathf.Max(0, rect.height - 52));
                    var dock = new Rect(rect.x, rect.yMax - height, rect.width, height);
                    // Footer has a separate scroll only when the available height cannot fit it.
                    if (height > 0) DrawRange(index + 5, end - footer, end, dock, true, false);
                    if (generation != ticket || IsClosed) return;
                    rect.height -= height + 4;
                    ClientWidgets.Fill(new Rect(rect.x, dock.y - 2, rect.width, 1), palette.Separator.ToUnityColor());
                }
                DrawRange(index, start + header, end - footer, rect, true);
            }
            private void LoadPane(PaneState pane)
            { feedFollowing = pane.Following; scroll = pane.Scroll; blocks = pane.Blocks; measuredWidth = pane.Width; contentHeight = pane.Height; }
            private void SavePane(PaneState pane, long ticket)
            {
                if (generation == ticket)
                { pane.Following = feedFollowing; pane.Scroll = scroll; pane.Blocks = blocks; pane.Width = measuredWidth; pane.Height = contentHeight; }
                else pane.Blocks = null;
                blocks = null;
            }
            private float HeaderHeight(int index, int start, int end, float width, float height)
            {
                var pane = panes[index]; long ticket = generation; LoadPane(pane); sectionStart = start; sectionEnd = end;
                Measure(Mathf.Max(1, width - 20)); float result = Mathf.Min(contentHeight, height * .5f); SavePane(pane, ticket); return result;
            }
            private void DrawFixedHeader(int paneIndex, int start, int end, ref Rect rect, float aligned = -1)
            {
                var pane = panes[paneIndex]; long ticket = generation;
                LoadPane(pane); sectionStart = start; sectionEnd = end;
                Measure(Mathf.Max(1, rect.width - 20));
                // At very small heights the header gains its own scroll rather than hiding controls.
                float height = Mathf.Min(Mathf.Max(contentHeight, aligned), Mathf.Max(0, rect.height * .5f));
                bool scrollHeader = contentHeight > height;
                SavePane(pane, ticket);
                if (height > 0) DrawRange(paneIndex, start, end, new Rect(rect.x, rect.y, rect.width, height), scrollHeader, false);
                // A header action can synchronously close or republish its owner view.
                if (generation != ticket || IsClosed) return;
                if (page.SplitView?.AlignHeaders == true && height > 0) ClientWidgets.Fill(new Rect(rect.x, rect.y + height, rect.width, 1), palette.Separator.ToUnityColor());
                rect.y += height + 4; rect.height -= height + 4;
            }
            private void DrawRange(int index, int start, int end, Rect rect, bool scrolling, bool feedback = true)
            {
                var pane = panes[index]; long ticket = generation;
                LoadPane(pane); sectionStart = start; sectionEnd = end;
                if (!scrolling) scroll = new Vector2(0, 0);
                try { DrawContent(rect, false, scrolling, feedback); }
                finally { SavePane(pane, ticket); }
            }
            private void DrawContent(Rect rect, bool drawComposer = true, bool scrolling = true, bool feedback = true)
            {
                service.MainThread(); if (IsClosed || lifetime.IsClosed || page == null) return;
                if (float.IsNaN(rect.x) || float.IsNaN(rect.y) || float.IsNaN(rect.width) || float.IsNaN(rect.height) ||
                    float.IsInfinity(rect.x) || float.IsInfinity(rect.y) || float.IsInfinity(rect.width) || float.IsInfinity(rect.height)) return;
                if (rect.width < 24 || rect.height < 48) return;
                if (drawComposer && page.Composer != null)
                {
                    float dockHeight = Mathf.Min(rect.height, ComposerHeight(rect.width));
                    DrawComposer(new Rect(rect.x, rect.yMax - dockHeight, rect.width, dockHeight));
                    if (page.Sections.Count == 0) return;
                    rect.height -= dockHeight + 8;
                    if (rect.height < 48) return;
                }
                ClientPresentationAction pending = null; ClientPresentationSection menuSection = null; ClientPresentationActionMenu pendingMenu = null;
                long ticket = generation;
                try
                {
                    using (new ClientGuiScope(true))
                    {
                        UpdatePalette(); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                        bool fresh = Current(ticket);
                        Measure(Mathf.Max(1, rect.width - 20));
                        UnityEngine.GUI.BeginGroup(rect);
                        try
                        {
                            bool showFeedback = feedback && (!fresh || FailureCode != null || inputs.Count != 0);
                            var viewport = new Rect(0, 0, rect.width, Mathf.Max(0, rect.height - (showFeedback ? 32 : 0)));
                            float maxScroll = Mathf.Max(0, contentHeight - viewport.height);
                            bool follows = false, hasFeed = false;
                            for (int i = sectionStart; i < Math.Min(sectionEnd, page.Sections.Count); i++)
                            { follows |= page.Sections[i].Feed?.FollowEnd == true; hasFeed |= page.Sections[i].Feed != null; }
                            if (forceEnd && hasFeed || follows && feedFollowing) scroll.y = maxScroll;
                            if (scrollFeed != null)
                            {
                                foreach (var block in blocks)
                                {
                                    if (block.Feed?.Id == scrollFeed)
                                        for (int i = 0; i < block.Feed.Rows.Count; i++) if (block.Feed.Rows[i].Id == scrollRow)
                                            scroll.y = block.Rect.y + block.RowOffsets[i] - viewport.height / 2f;
                                    if (block.List?.Id == scrollFeed)
                                        for (int i = 0; i < block.List.Rows.Count; i++) if (block.List.Rows[i].Id == scrollRow)
                                            scroll.y = block.Rect.y + block.RowOffsets[i];
                                }
                                bool foundFeed = false;
                                foreach (var block in blocks) foundFeed |= block.Feed?.Id == scrollFeed || block.List?.Id == scrollFeed;
                                if (foundFeed) { scrollFeed = scrollRow = null; feedFollowing = false; }
                            }
                            if (hasFeed) forceEnd = false;
                            scroll.y = Mathf.Clamp(scroll.y, 0, maxScroll); float beforeScroll = scroll.y;
                            if (scrolling) Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, measuredWidth, contentHeight));
                            try
                            {
                                foreach (var block in blocks)
                                {
                                    if (block.Rect.yMax < scroll.y || block.Rect.y > scroll.y + viewport.height) continue;
                                    if (block.Surface)
                                    {
                                        ClientWidgets.RoundedSurface(block.Rect, OptionalColor(palette, block.Section.SurfaceKey, palette.Surface.ToUnityColor()));
                                        ClientWidgets.RoundedBorder(block.Rect, palette.Separator.ToUnityColor());
                                    }
                                    else if (block.Swatch != null)
                                    {
                                        var chip = new Rect(block.Rect.x, block.Rect.y + 5, 22, 22);
                                        ClientWidgets.Fill(chip, palette.Surface.ToUnityColor()); ClientWidgets.Fill(chip, new Color(block.Swatch.Value.Red, block.Swatch.Value.Green, block.Swatch.Value.Blue, block.Swatch.Value.Alpha)); ClientWidgets.RoundedBorder(chip, palette.Separator.ToUnityColor(), 2);
                                        ClientWidgets.ClippedLabel(new Rect(block.Rect.x + 30, block.Rect.y, Mathf.Max(1, block.Rect.width - 30), 32), block.Swatch.Label, palette.PrimaryText.ToUnityColor(), TextAnchor.MiddleLeft);
                                        var c = block.Swatch.Value;
                                        TooltipHandler.TipRegion(block.Rect, block.Swatch.Label + "\nRGBA " + c.Red + ", " + c.Green + ", " + c.Blue + ", " + c.Alpha);
                                    }
                                    else if (block.Field != null)
                                    {
                                        var field = block.Field;
                                        using (new ClientGuiScope(true))
                                        {
                                            UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && fresh && field.DisabledReason == null;
                                            if(field.ColorPicker!=null){DrawColorPicker(block.Rect,field);continue;}
                                            var textRect = new Rect(block.Rect.x, block.Rect.y, block.Rect.width, ClientWidgets.ControlHeight);
                                            string value = ClientWidgets.TextField(textRect, drafts[field.Id], palette, identity + "." + field.Id, field.Placeholder, field.MaxLength, Validator(field));
                                            bool textChanged = value != drafts[field.Id];
                                            if (UnityEngine.GUI.enabled && textChanged)
                                            { drafts[field.Id] = value; Enqueue(field.Id, ClientPresentationInputKind.TextChanged, value); }
                                            if (field.Slider != null)
                                            {
                                                var slider = field.Slider;
                                                float next = ClientWidgets.Slider(new Rect(block.Rect.x, block.Rect.y + 38, block.Rect.width, 28), slider.Value, slider.Minimum, slider.Maximum, palette);
                                                if (UnityEngine.GUI.enabled && !textChanged && InputEvent() && next != slider.Value)
                                                { string raw = next.ToString("R", System.Globalization.CultureInfo.InvariantCulture); drafts[field.Id] = raw; Enqueue(field.Id, ClientPresentationInputKind.TextChanged, raw); }
                                            }
                                            if (field.DisabledReason != null) TooltipHandler.TipRegion(block.Rect, field.DisabledReason);
                                        }
                                    }
                                    else if (block.Menu != null)
                                    {
                                        if (ClientWidgets.Button(block.Rect, block.Text, palette, wrap: true, dropdown: true, enabled: fresh && inputs.Count == 0, cornerRadius: block.Section.Plain ? 8 : 12)) { pendingMenu = block.Menu; menuSection = block.Section; }
                                    }
                                    else if (block.Badges != null) DrawBadges(block.Rect, block.Badges, viewport.height);
                                    else if (block.Feed != null) DrawFeed(block, fresh, viewport.height);
                                    else if (block.List != null) { var rowAction = DrawList(block, fresh, viewport.height); if (rowAction != null) pending = rowAction; }
                                    else if (block.Region != null) DrawRegion(block);
                                    else if (block.Action != null)
                                    {
                                        var action = block.Action.Action;
                                        string reason = action.DisabledReason;
                                        bool enabled = fresh && inputs.Count == 0 && reason == null && !attempted.Contains(block.Action);
                                        TooltipHandler.TipRegion(block.Rect, block.Text);
                                        if (!string.IsNullOrEmpty(reason)) TooltipHandler.TipRegion(block.Rect, reason);
                                        else if (inputs.Count != 0) TooltipHandler.TipRegion(block.Rect, L("inputPending"));
                                        if (ClientWidgets.Button(block.Rect, block.Text, palette, block.Action.Kind, enabled, cornerRadius: block.Section?.Plain == true ? 8 : 12, wrap: true)) pending = block.Action;
                                        if (block.Action.IsSelected) ClientWidgets.RoundedBorder(block.Rect, palette.GetColor("ui.accent").ToUnityColor(), radius: block.Section?.Plain == true ? 8 : 12, thickness: 2);
                                    }
                                    else if (block.Text != null)
                                    {
                                        Color color = block.Secondary ? Tone(palette, block.Tone) : palette.PrimaryText.ToUnityColor();
                                        if (block.Summary)
                                            color = OptionalColor(palette, block.Section.SummaryColorKey, color);
                                        ClientWidgets.Label(block.Rect, block.Text, color);
                                    }
                                }
                            }
                            finally { if (scrolling) Widgets.EndScrollView(); }
                            if(scrolling) ClientWidgets.ScrollbarDecoration(viewport,scroll,new Rect(0,0,measuredWidth,contentHeight),palette);
                            if (beforeScroll != scroll.y) feedFollowing = Math.Abs(scroll.y - maxScroll) < 1;
                            string feedbackText = !fresh ? L("stale") : FailureCode != null ? L("failed") : inputs.Count != 0 ? L("inputPending") : "";
                            if (showFeedback) ClientWidgets.LabelEllipses(new Rect(0, rect.height - 28, rect.width, 28), feedbackText, palette, true);
                            if (feedback && FailureCode != null) TooltipHandler.TipRegion(new Rect(0, rect.height - 28, rect.width, 28), FailureCode);
                        }
                        finally { UnityEngine.GUI.EndGroup(); }
                    }
                }
                catch (Exception error)
                {
                    Fail("RenderFailed", error);
                    using (new ClientGuiScope(true)) { Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                        ClientWidgets.Label(rect, L("failed"), service.theme.Error.ToUnityColor()); }
                }
                if (pendingMenu != null && Current(ticket))
                {
                    var options = new List<ClientPresentationMenuItem>();
                    foreach (var id in pendingMenu.ActionIds) foreach (var binding in menuSection.Actions) if (binding.Id == id)
                    { var captured = binding; options.Add(new ClientPresentationMenuItem((binding.IsSelected ? "✓ " : "") + binding.Action.Label,
                        Current(ticket) && binding.Action.DisabledReason == null ? (Action)(() => Request(captured, ticket)) : null)); }
                    if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(ClientPresentationMenus.ToGameOptions(options)));
                }
                // Commands run after all render enumerations/groups finish. Reentrant Publish cannot corrupt drawing.
                if (pending != null)
                {
                    try { Request(pending, ticket); }
                    catch (Exception error) { Fail("ActionRequestFailed", error); CloseConfirmation(); }
                }
            }

            private static IEnumerable<ClientPresentationAction> Bindings(ClientPresentationPage model)
            {
                foreach (var section in model.Sections)
                {
                    foreach (var binding in section.Actions) yield return binding;
                    if (section.List != null) foreach (var row in section.List.Rows) foreach (var binding in row.Actions) yield return binding;
                }
                if (model.Composer != null) { yield return model.Composer.Submit; foreach (var action in model.Composer.Actions) yield return action; }
            }
            private static IEnumerable<ClientPresentationTextField> Fields(ClientPresentationPage model)
            {
                foreach (var section in model.Sections)
                {
                    foreach (var field in section.Fields) yield return field;
                    if (section.List != null) foreach (var row in section.List.Rows) if (row.Field != null) yield return row.Field;
                }
            }
            public void InvokeAction(string actionId)
            {
                Require(); if (page == null) return;
                foreach (var binding in Bindings(page)) if (binding.Id == actionId)
                { if (binding.Action.DisabledReason == null) { if (binding.Confirmation != null) throw new InvalidOperationException("UseConfirmationView"); Execute(binding, generation); } return; }
                throw new ArgumentException("PresentationActionMissing");
            }
            public IReadOnlyList<ClientPresentationMenuItem> CreateActionMenuItems()
            {
                Require();
                var options = new List<ClientPresentationMenuItem>();
                if (page == null) return options.AsReadOnly();
                long ticket = generation;
                foreach (var action in Bindings(page))
                {
                    var binding = action;
                    options.Add(new ClientPresentationMenuItem(binding.Action.Label,
                        Current(ticket) && binding.Action.DisabledReason == null ? (Action)(() => Request(binding, ticket)) : null));
                }
                return options.AsReadOnly();
            }
            public void SetComposerDraft(string value)
            {
                Require(); if (page?.Composer == null) throw new InvalidOperationException("ComposerMissing");
                value = value ?? ""; if (value.Length > page.Composer.Field.MaxLength) throw new ArgumentException("ComposerDraftLimit");
                drafts[page.Composer.Field.Id] = value; Enqueue(page.Composer.Field.Id, ClientPresentationInputKind.TextChanged, value);
            }
            public void RequestComposerFocus() { Require(); composerFocusAttempts = 2; }
            public float MeasureComposerHeight(float width)
            {
                Require(); if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0) throw new ArgumentException("PresentationWidthInvalid");
                UpdatePalette(); return page?.Composer == null ? 0 : ComposerHeight(width);
            }
            private float ComposerHeight(float width)
            {
                if (composerWidth == width) return composerHeight;
                composerWidth = width;
                float inner = Mathf.Max(1, width - 34), overflow = page.Composer.Actions.Count > 0 ? 36 : 0;
                var size = ClientWidgets.MeasureButton(page.Composer.Submit.Action.Label, Mathf.Max(1, inner - overflow), minimumWidth: 80);
                composerButtonWidth = Mathf.Min(Mathf.Max(1, inner - overflow), size.x + (page.Composer.Image == null ? 0 : 18));
                var full = ClientWidgets.MeasureButton(page.Composer.Submit.Action.Label, Mathf.Max(1, composerButtonWidth - (page.Composer.Image == null ? 0 : 18)), minimumWidth: 1);
                composerButtonHeight = full.y;
                composerStacked = inner - composerButtonWidth - overflow - 8 < 160;
                composerHeight = 34 + (composerStacked ? 32 + 6 + composerButtonHeight : Mathf.Max(32, composerButtonHeight));
                return composerHeight;
            }
            private void DrawComposer(Rect rect)
            {
                try
                {
                    using (new ClientGuiScope(true))
                    {
                        UpdatePalette(); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = false;
                        var descriptor = page.Composer; var field = descriptor.Field;
                        bool fresh = Current(generation);
                        var dock = rect.ContractedBy(8);
                        ClientWidgets.RoundedSurface(dock, palette.GetColor("ui.input.background").ToUnityColor(), 16);
                        ClientWidgets.RoundedBorder(dock, ComposerFocused ? palette.GetColor("ui.input.focus").ToUnityColor() : palette.Separator.ToUnityColor(), 16, ComposerFocused ? 2 : 1);
                        var inner = dock.ContractedBy(9);
                        ComposerHeight(rect.width);
                        bool stacked = composerStacked;
                        float overflowWidth = descriptor.Actions.Count > 0 ? 36 : 0;
                        float buttonWidth = Mathf.Min(Mathf.Max(1, inner.width - overflowWidth), composerButtonWidth);
                        var button = new Rect(inner.xMax - buttonWidth, stacked ? inner.y + 38 : inner.y, buttonWidth, composerButtonHeight);
                        var input = new Rect(inner.x, inner.y, Mathf.Max(1, stacked ? inner.width : inner.width - buttonWidth - 8 - overflowWidth), 32);
                        if (overflowWidth > 0 && ClientWidgets.Button(new Rect(button.x - 36, button.y, 32, 32), "…", palette))
                        {
                            var menu = new List<ClientPresentationMenuItem>(); var ticket = generation;
                            foreach (var action in descriptor.Actions)
                            {
                                var binding = action;
                                menu.Add(new ClientPresentationMenuItem(binding.Action.Label, binding.Action.DisabledReason == null ? (Action)(() => Request(binding, ticket)) : null));
                            }
                            Find.WindowStack.Add(new FloatMenu(ClientPresentationMenus.ToGameOptions(menu)));
                        }
                        var ev = Event.current;
                        // Single-line text fields may consume Return even without editing
                        // text. Capture the intent while the event still has its key data.
                        bool enterBefore = ev != null && ev.type == EventType.KeyDown &&
                            (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) &&
                            UnityEngine.GUI.GetNameOfFocusedControl() == ComposerControlName;
                        // Text bridges can commit during Layout/Repaint. Preserve returned text in
                        // the view draft; only explicit submit intents may execute owner actions.
                        bool composingBefore = !string.IsNullOrEmpty(Input.compositionString);
                        string draftBefore = ComposerDraft;
                        GUIUtility.GetControlID(0x50484349, FocusType.Passive);
                        using (new ClientGuiScope(true))
                        {
                            UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && fresh && field.DisabledReason == null;
                            string value = ClientWidgets.EmbeddedTextField(input, ComposerDraft, palette, ComposerControlName, field.Placeholder, field.MaxLength, Validator(field));
                            if (UnityEngine.GUI.enabled && value != ComposerDraft) SetComposerDraft(value);
                        }
                        ComposerFocused = UnityEngine.GUI.GetNameOfFocusedControl() == ComposerControlName;
                        // Let the field/IME commit first. Consuming the key alone is not an
                        // IME commit; composition or changed text still suppresses sending.
                        bool enter = enterBefore &&
                            ComposerFocused && !composingBefore && string.IsNullOrEmpty(Input.compositionString) &&
                            draftBefore == ComposerDraft && Find.WindowStack.FloatMenu == null;
                        if (composerFocusAttempts > 0 && ev != null && ev.type == EventType.Repaint && Find.WindowStack.FloatMenu == null &&
                            Find.WindowStack.currentlyDrawnWindow != null && Find.WindowStack.GetsInput(Find.WindowStack.currentlyDrawnWindow))
                        { UnityEngine.GUI.FocusControl(ComposerControlName); composerFocusAttempts--; }
                        bool enabled = fresh && field.DisabledReason == null && descriptor.Submit.Action.DisabledReason == null;
                        bool click;
                        using (new ClientGuiScope(true))
                        {
                            Text.Font = GameFont.Small;
                            var lease = BorrowImage(descriptor.Image, out var texture);
                            try { click = ClientWidgets.IconButton(button, descriptor.Submit.Action.Label, texture, new Vector2(11.875f, 10), palette, ClientButtonKind.Primary, enabled, wrap: true,
                                reserveIcon: descriptor.Image != null, onImageError: ReportImageFailure); }
                            finally { ReturnImage(lease); }
                        }
                        if (enabled && (enter || click))
                        {
                            Enqueue(field.Id, ClientPresentationInputKind.SubmitRequested, ComposerDraft);
                            if (enter && ev.type != EventType.Used) ev.Use();
                        }
                        if (!enabled) TooltipHandler.TipRegion(button, descriptor.Submit.Action.DisabledReason ?? field.DisabledReason ?? L("stale"));
                    }
                }
                catch (Exception error) { Fail("ComposerDrawFailed", error); }
            }

            private const int PresetSize=24,PresetGap=4;
            private static readonly List<Color> colorPresets=CreateColorPresets();
            private static List<Color> CreateColorPresets()
            {
                var result=new List<Color>();
                for(int i=0;i<10;i++){float shade=i/9f;result.Add(new Color(shade,shade,shade));}
                for(int row=0;row<5;row++)for(int hue=0;hue<10;hue++)
                    result.Add(Color.HSVToRGB(hue/10f,row==1||row==3?.45f:1f,row==2||row==3?.6f:1f));
                return result;
            }
            private static int PresetColumns(float width)=>Math.Max(1,Math.Min(10,(int)(width/(PresetSize+PresetGap))));
            private static float ColorPickerHeight(float width)
            {
                float paletteWidth=width>=480?width-176:width;
                int columns=PresetColumns(paletteWidth);float paletteHeight=((colorPresets.Count+columns-1)/columns)*(PresetSize+PresetGap);
                return (width>=480?Mathf.Max(160,paletteHeight):Mathf.Min(160,width)+12+paletteHeight)+60;
            }
            private void DrawColorPicker(Rect rect,ClientPresentationTextField field)
            {
                var picker=field.ColorPicker;var value=picker.Value;
                var original=new Color(value.Red,value.Green,value.Blue,value.Alpha);var color=original;
                bool input=InputEvent(),dragging=colorDragging.TryGetValue(field.Id,out var drag)&&drag;
                float hue,saturation,brightness;Color.RGBToHSV(color,out hue,out saturation,out brightness);
                // Keep the wheel bright and readable even for dark/black backgrounds.
                var wheelColor=Color.HSVToRGB(hue,saturation,1);var beforeWheel=wheelColor;
                float size=Mathf.Min(160,rect.width);var wheelRect=new Rect(rect.x,rect.y,size,size);
                Widgets.HSVColorWheel(wheelRect,ref wheelColor,ref dragging,null,identity+"."+field.Id+".wheel");
                colorDragging[field.Id]=dragging;
                if(input&&(wheelColor.r!=beforeWheel.r||wheelColor.g!=beforeWheel.g||wheelColor.b!=beforeWheel.b))
                {
                    Color.RGBToHSV(wheelColor,out hue,out saturation,out var ignored);
                    color=Color.HSVToRGB(hue,saturation,brightness==0?1:brightness);
                }
                bool alongside=rect.width>=480;float paletteWidth=alongside?rect.width-176:rect.width;
                float paletteX=alongside?rect.x+176:rect.x,paletteY=alongside?rect.y:rect.y+size+12;
                int columns=PresetColumns(paletteWidth);
                for(int i=0;i<colorPresets.Count;i++)
                {
                    var cell=new Rect(paletteX+(i%columns)*(PresetSize+PresetGap),paletteY+(i/columns)*(PresetSize+PresetGap),PresetSize,PresetSize);
                    Widgets.ColorBox(cell,ref color,colorPresets[i],PresetSize,0);
                }
                Color.RGBToHSV(color,out hue,out saturation,out brightness);
                float bottom=rect.y+rect.height-60;
                ClientWidgets.Label(new Rect(rect.x,bottom,rect.width,24),picker.BrightnessLabel,palette);
                float next=ClientWidgets.Slider(new Rect(rect.x,bottom+26,rect.width,28),brightness,0,1,palette);
                if(input&&next!=brightness)color=Color.HSVToRGB(hue,saturation,next);
                color.a=original.a; // Palette and wheel never silently remove transparency.
                if(UnityEngine.GUI.enabled&&input&&(color.r!=original.r||color.g!=original.g||color.b!=original.b))
                {
                    var culture=System.Globalization.CultureInfo.InvariantCulture;
                    string raw=string.Join(",",new[]{color.r.ToString("R",culture),color.g.ToString("R",culture),color.b.ToString("R",culture),color.a.ToString("R",culture)});
                    drafts[field.Id]=raw;Enqueue(field.Id,ClientPresentationInputKind.TextChanged,raw);
                }
            }

            private static bool InputEvent() => Event.current != null && Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint;
            private bool Enqueue(string id, ClientPresentationInputKind kind, string value, string target = null)
            {
                if (inputs.Count >= 32) { Fail("InputQueueFull"); return false; }
                inputs.Enqueue(new ClientPresentationInput(page.Revision, id, kind, value, target));
                return true;
            }
            public bool TryDequeueInput(out ClientPresentationInput input)
            {
                Require(); input = null;
                if (!Current(generation)) { inputs.Clear(); return false; }
                if (inputs.Count == 0) return false;
                input = inputs.Dequeue(); return true;
            }
            public void ScrollToRow(string feedId, string rowId)
            {
                Require(); if (page == null) return;
                foreach (var section in page.Sections)
                {
                    if (section.List?.Id == feedId)
                        foreach (var row in section.List.Rows) if (row.Id == rowId) { scrollFeed = feedId; scrollRow = rowId; forceEnd = false; feedFollowing = false; return; }
                    if (section.Feed?.Id == feedId)
                        foreach (var row in section.Feed.Rows) if (row.Id == rowId) { scrollFeed = feedId; scrollRow = rowId; forceEnd = false; feedFollowing = false; highlightedFeed = feedId; highlightedRow = rowId; highlightUntil = Time.realtimeSinceStartup + 1.2f; return; }
                }
            }
            public void ScrollToEnd() { Require(); forceEnd = true; feedFollowing = true; scrollFeed = scrollRow = null; }
            private float BadgeWidth(ClientPresentationBadge badge, float width)
            {
                if (!badgeWidths.TryGetValue(badge, out float natural))
                { natural = Text.CalcSize(badge.Label).x + (badge.Image == null ? 16 : 38); badgeWidths.Add(badge, natural); }
                return Mathf.Min(Mathf.Max(1, width), natural);
            }
            private float BadgeCellHeight(ClientPresentationBadge badge, float width)
            {
                float textWidth = Mathf.Max(1, width - (badge.Image == null ? 16 : 36));
                if (badgeHeights.TryGetValue(badge, out var cached) && cached.x == textWidth) return cached.y;
                float height;
                using (new ClientGuiScope(true)) { Text.Font = GameFont.Small; Text.WordWrap = true; height = Mathf.Max(28, Text.CalcHeight(badge.Label, textWidth) + 4); }
                badgeHeights[badge] = new Vector2(textWidth, height); return height;
            }
            private float BadgeHeight(IReadOnlyList<ClientPresentationBadge> badges, float width)
            {
                float x = 0, y = 0, row = 28;
                foreach (var badge in badges)
                {
                    float w = BadgeWidth(badge, width);
                    if (x > 0 && x + w > width) { x = 0; y += row + 4; row = 28; }
                    row = Mathf.Max(row, BadgeCellHeight(badge, w)); x += w + 6;
                }
                return y + row;
            }
            private void DrawBadges(Rect rect, IReadOnlyList<ClientPresentationBadge> badges, float viewportHeight)
            {
                float x = rect.x, y = rect.y, row = 28;
                foreach (var badge in badges)
                {
                    float width = BadgeWidth(badge, rect.width);
                    if (x > rect.x && x + width > rect.xMax) { x = rect.x; y += row + 4; row = 28; }
                    float height = BadgeCellHeight(badge, width); row = Mathf.Max(row, height);
                    var cell = new Rect(x, y, width, height);
                    Color ink = OptionalColor(palette, badge.ColorKey, palette.PrimaryText.ToUnityColor());
                    ClientWidgets.Fill(cell, new Color(ink.r, ink.g, ink.b, .15f * ink.a));
                    float inset = 8;
                    if (badge.Image != null) { if (cell.yMax > scroll.y && cell.y < scroll.y + viewportHeight) DrawImage(new Rect(x + 7, y + (height - 18) / 2, Mathf.Min(18, Mathf.Max(0, width - 14)), 18), badge.Image); inset = 30; }
                    ClientWidgets.ClippedLabel(new Rect(x + inset, y + 2, Mathf.Max(0, width - inset - 6), height - 4), badge.Label, ink, TextAnchor.MiddleLeft, true);
                    TooltipHandler.TipRegion(cell, badge.Label); x += width + 6;
                }
            }

            private float RowControlWidth(ClientPresentationRow row)
            {
                float width = row.Field == null ? 0 : 136;
                foreach (var binding in row.Actions) width += Mathf.Min(140, ClientWidgets.MeasureButton(binding.Action.Label, 140, minimumWidth: 80).x) + 6;
                return width;
            }
            private sealed class RowControl
            { internal Rect Rect; internal ClientPresentationTextField Field; internal ClientPresentationAction Action; }
            private List<RowControl> RowControls(ClientPresentationRow row, float available, out float height)
            {
                available = Mathf.Max(1, available);
                var result = new List<RowControl>(); float x = 0, y = 0, lineHeight = 0;
                if (row.Field != null)
                {
                    float width = Mathf.Min(136, available);
                    float h = (string.IsNullOrEmpty(row.Field.Label) ? 0 : Text.CalcHeight(row.Field.Label, width) + 4) + ClientWidgets.ControlHeight +
                        (string.IsNullOrEmpty(row.Field.ValidationText) ? 0 : Text.CalcHeight(row.Field.ValidationText, width) + 4);
                    result.Add(new RowControl { Rect = new Rect(0, 0, width, h), Field = row.Field }); x = width + 6; lineHeight = h;
                }
                foreach (var binding in row.Actions)
                {
                    var size = ClientWidgets.MeasureButton(binding.Action.Label, Mathf.Min(140, available), minimumWidth: 80);
                    if (x > 0 && x + size.x > available) { x = 0; y += lineHeight + 6; lineHeight = 0; }
                    result.Add(new RowControl { Rect = new Rect(x, y, Mathf.Min(size.x, available), size.y), Action = binding });
                    x += size.x + 6; lineHeight = Mathf.Max(lineHeight, size.y);
                }
                height = y + lineHeight; return result;
            }
            private sealed class RowTextLayout
            { internal float Padding, Inset, Width, LabelWidth, DetailWidth, TitleHeight, DetailHeight, BodyHeight; internal bool Inline, Alongside; internal float Controls; }
            private RowTextLayout RowText(ClientPresentationRow row, float width, bool compact)
            {
                var layout = new RowTextLayout { Padding = compact ? 4 : 8, Controls = RowControlWidth(row) };
                layout.Inset = layout.Padding + (row.HasIcon ? (compact ? 30 : 40) : 0);
                layout.Alongside = layout.Controls > 0 && width >= (compact ? 440 : 560) && layout.Controls < width * .55f;
                layout.Width = Mathf.Max(1, width - layout.Inset - layout.Padding - (layout.Alongside ? layout.Controls + 8 : 0));
                layout.Inline = compact && width >= 640 && row.Badges.Count == 0 && !string.IsNullOrEmpty(row.Detail);
                layout.LabelWidth = layout.Inline ? Mathf.Min(layout.Width * .6f, Mathf.Max(120, Text.CalcSize(row.Label).x)) : layout.Width;
                layout.DetailWidth = layout.Inline ? Mathf.Max(1, layout.Width - layout.LabelWidth - 8) : layout.Width;
                layout.TitleHeight = Text.CalcHeight(row.Label, layout.LabelWidth);
                layout.DetailHeight = string.IsNullOrEmpty(row.Detail) ? 0 : Text.CalcHeight(row.Detail, layout.DetailWidth);
                layout.BodyHeight = Mathf.Max(row.HasIcon ? (compact ? 24 : 32) : (compact ? 24 : 16), layout.Inline ? Mathf.Max(layout.TitleHeight, layout.DetailHeight) : layout.TitleHeight + layout.DetailHeight) + 2 * layout.Padding +
                    (row.Badges.Count == 0 ? 0 : BadgeHeight(row.Badges, layout.Width) + 6);
                return layout;
            }
            private float RowHeight(ClientPresentationRow row, float width, bool compact)
            {
                var layout = RowText(row, width, compact);
                if (layout.Controls == 0) return layout.BodyHeight;
                RowControls(row, layout.Alongside ? layout.Controls : Mathf.Max(1, width - 2 * layout.Padding), out float controlHeight);
                return layout.Alongside ? Mathf.Max(layout.BodyHeight, controlHeight + 2 * layout.Padding) : layout.BodyHeight + controlHeight + 8;
            }
            private ClientPresentationAction DrawList(Block block, bool fresh, float viewportHeight)
            {
                ClientPresentationAction pending = null;
                var list = block.List;
                var range = VirtualListLayout.GetDynamicRange(block.RowOffsets, list.Rows.Count, Mathf.Max(0, scroll.y - block.Rect.y),
                    Mathf.Max(0, scroll.y + viewportHeight - Mathf.Max(scroll.y, block.Rect.y)), 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    var row = list.Rows[i];
                    var rect = new Rect(block.Rect.x, block.Rect.y + block.RowOffsets[i], block.Rect.width, block.RowOffsets[i + 1] - block.RowOffsets[i]);
                    bool selected = list.SelectedId == row.Id;
                    if (selected || !list.IsReadOnly && Mouse.IsOver(rect)) ClientWidgets.RoundedSurface(rect, OptionalColor(palette, list.HoverColorKey,
                        selected?ClientWidgets.Role(palette,"ui.navigation.selected","ui.hover"):palette.HoverHighlight.ToUnityColor()));
                    if (selected) ClientWidgets.RoundedSurface(new Rect(rect.x, rect.y + 6, 3, Mathf.Max(0, rect.height - 12)), OptionalColor(palette, list.SelectionColorKey, palette.GetColor("ui.accent").ToUnityColor()), 1);
                    var layout = RowText(row, rect.width, list.CompactRows);
                    float inset = layout.Inset, controls = layout.Controls; bool alongside = layout.Alongside;
                    if (row.HasIcon && rect.yMax > scroll.y && rect.y < scroll.y + viewportHeight) DrawImage(new Rect(rect.x + layout.Padding, rect.y + layout.Padding, list.CompactRows ? 24 : 32, list.CompactRows ? 24 : 32), row.Image);
                    ClientWidgets.Label(new Rect(rect.x + inset, rect.y + layout.Padding, layout.LabelWidth, layout.TitleHeight), row.Label, OptionalColor(palette, row.ColorKey, selected ? OptionalColor(palette, list.SelectionColorKey, palette.GetColor("ui.accent").ToUnityColor()) : palette.PrimaryText.ToUnityColor()));
                    if (!string.IsNullOrEmpty(row.Detail)) ClientWidgets.Label(new Rect(rect.x + inset + (layout.Inline ? layout.LabelWidth + 8 : 0), rect.y + layout.Padding + (layout.Inline ? 0 : layout.TitleHeight), layout.DetailWidth, layout.DetailHeight), row.Detail, palette, true);
                    if (row.Badges.Count > 0 && rect.yMax > scroll.y && rect.y < scroll.y + viewportHeight) DrawBadges(new Rect(rect.x + inset, rect.y + layout.Padding + layout.TitleHeight + layout.DetailHeight + 6, layout.Width, BadgeHeight(row.Badges, layout.Width)), row.Badges, viewportHeight);
                    var controlsLayout = RowControls(row, alongside ? controls : Mathf.Max(1, rect.width - 2 * layout.Padding), out float controlsHeight);
                    float controlY = alongside ? rect.y + layout.Padding : rect.yMax - controlsHeight - layout.Padding;
                    var selectionRect = alongside ? new Rect(rect.x, rect.y, rect.width - controls - 8, rect.height) : new Rect(rect.x, rect.y, rect.width, controls == 0 ? rect.height : Mathf.Max(0, controlY - rect.y - 4));
                    TooltipHandler.TipRegion(selectionRect, row.DisabledReason ?? row.Label);
                    float controlX = alongside ? rect.xMax - controls - layout.Padding : rect.x + layout.Padding;
                    foreach (var control in controlsLayout)
                    {
                        var cell = new Rect(controlX + control.Rect.x, controlY + control.Rect.y, control.Rect.width, control.Rect.height);
                        if (control.Field != null)
                        {
                            var field = control.Field;
                            float labelHeight = string.IsNullOrEmpty(field.Label) ? 0 : Text.CalcHeight(field.Label, cell.width);
                            ClientWidgets.Label(new Rect(cell.x, cell.y, cell.width, labelHeight), field.Label, palette, true);
                            var inputRect = new Rect(cell.x, cell.y + (labelHeight == 0 ? 0 : labelHeight + 4), cell.width, ClientWidgets.ControlHeight);
                            using (new ClientGuiScope(true))
                            {
                                UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && fresh && row.DisabledReason == null && field.DisabledReason == null;
                                string value = ClientWidgets.TextField(inputRect, drafts[field.Id], palette, identity + "." + field.Id, field.Placeholder, field.MaxLength, Validator(field));
                                if (UnityEngine.GUI.enabled && value != drafts[field.Id]) { drafts[field.Id] = value; Enqueue(field.Id, ClientPresentationInputKind.TextChanged, value); }
                            }
                            if (!string.IsNullOrEmpty(field.ValidationText)) ClientWidgets.Label(new Rect(cell.x, inputRect.yMax + 4, cell.width, Text.CalcHeight(field.ValidationText, cell.width)), field.ValidationText, palette.Error.ToUnityColor());
                            if (field.DisabledReason != null) TooltipHandler.TipRegion(inputRect, field.DisabledReason);
                        }
                        else
                        {
                            var binding = control.Action;
                            bool enabled = fresh && inputs.Count == 0 && row.DisabledReason == null && binding.Action.DisabledReason == null && !attempted.Contains(binding);
                            TooltipHandler.TipRegion(cell, binding.Action.DisabledReason ?? row.DisabledReason ?? binding.Action.Label);
                            if (ClientWidgets.Button(cell, binding.Action.Label, palette, binding.Kind, enabled, wrap: true)) pending = binding;
                        }
                    }
                    using (new ClientGuiScope(true))
                    {
                        UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && fresh && row.DisabledReason == null;
                        if (!list.IsReadOnly && Widgets.ButtonInvisible(selectionRect) && UnityEngine.GUI.enabled && InputEvent()) Enqueue(list.Id, ClientPresentationInputKind.RowSelected, row.Id);
                    }
                }
                return pending;
            }
            private void ReportImageFailure(Exception error)
            {
                if (reported.Add("ImageReadFailed")) service.log?.Invoke("PresentationImageReadFailed: " + error.GetType().Name);
            }
            private void ReturnImage(IClientPresentationImageLease lease)
            { try { lease?.Dispose(); } catch (Exception error) { ReportImageFailure(error); } }
            private IClientPresentationImageLease BorrowImage(IClientPresentationImage image, out Texture texture)
            {
                texture = null;
                if (image == null || Event.current?.type != EventType.Repaint) return null;
                IClientPresentationImageLease lease = null;
                try
                {
                    lease = image.Acquire();
                    if (lease is IClientTextureImageLease textureLease)
                    { if (!textureLease.TryGetTexture(out texture)) texture = null; }
                    else if (lease != null) ReportImageFailure(new InvalidOperationException("ImageAdapterUnavailable"));
                    return lease;
                }
                catch (Exception error) { texture = null; ReturnImage(lease); ReportImageFailure(error); return null; }
            }
            private bool DrawImage(Rect rect, IClientPresentationImage image)
            {
                var lease = BorrowImage(image, out var texture);
                try
                {
                    if (texture == null) return false;
                    using (new ClientGuiScope(true)) { UnityEngine.GUI.color = Color.white; Widgets.DrawTextureFitted(rect, texture, 1); }
                    return true;
                }
                catch (Exception error) { ReportImageFailure(error); return false; }
                finally { ReturnImage(lease); }
            }

            private void DrawRegion(Block block)
            {
                if (Event.current?.type != EventType.Repaint) return;
                if (failedRegions.Contains(block.Region.Id)) { ClientWidgets.Label(block.Rect, L("failed"), palette.Error.ToUnityColor()); return; }
                using (new ClientGuiScope(true))
                {
                    UnityEngine.GUI.enabled = false; // Passive region; interactive commands use the shared fields/actions.
                    UnityEngine.GUI.BeginGroup(block.Rect);
                    var context = new RegionDrawContext(this, new ClientPresentationBounds(0, 0, block.Rect.width, block.Rect.height));
                    try { block.Region.Content.Draw(context); }
                    catch (Exception error) { failedRegions.Add(block.Region.Id); Fail("RegionDrawFailed", error); }
                    finally { context.Close(); UnityEngine.GUI.EndGroup(); }
                }
            }

            private sealed class RegionDrawContext : IClientPresentationDrawContext
            {
                private Session owner;
                private ClientPresentationPage projection;
                private IClientThemeReader theme;
                private readonly ClientPresentationBounds bounds;
                internal RegionDrawContext(Session owner, ClientPresentationBounds bounds)
                { this.owner = owner; projection = owner.page; theme = owner.palette; this.bounds = bounds; }
                private void Check()
                {
                    if (owner == null) throw new ObjectDisposedException(nameof(IClientPresentationDrawContext));
                    owner.service.MainThread();
                    if (owner.IsClosed || !ReferenceEquals(projection, owner.page) || Event.current?.type != EventType.Repaint)
                        throw new InvalidOperationException("PresentationDrawContextExpired");
                }
                public ClientPresentationBounds Bounds { get { Check(); return bounds; } }
                public IClientThemeReader Theme { get { Check(); return theme; } }
                public void Progress(ClientPresentationBounds area, float fraction)
                {
                    Check();
                    foreach (float value in new[] { area.X, area.Y, area.Width, area.Height, area.X + area.Width, area.Y + area.Height, fraction })
                        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("PresentationDrawValueInvalid");
                    if (area.Width < 0 || area.Height < 0) throw new ArgumentException("PresentationDrawBoundsInvalid");
                    if (area.Width == 0 || area.Height == 0 || area.X >= bounds.Width || area.Y >= bounds.Height || area.X + area.Width <= 0 || area.Y + area.Height <= 0) return;
                    // The enclosing Host group clips partial intersections without changing progress geometry.
                    ClientWidgets.Progress(new Rect(area.X, area.Y, area.Width, area.Height), fraction, theme);
                }
                internal void Close() { owner = null; projection = null; theme = null; }
            }

            private void Request(ClientPresentationAction binding, long ticket)
            {
                service.MainThread();
                if (Event.current == null || Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) return;
                if (!Current(ticket) || inputs.Count != 0) { Fail(inputs.Count != 0 ? "InputPending" : "ContextStale"); return; }
                if (binding.Action.DisabledReason != null) return;
                if (binding.Confirmation == null) { Execute(binding, ticket); return; }
                CloseConfirmation();
                confirmation = new ConfirmationWindow(this, binding, ticket);
                Find.WindowStack.Add(confirmation);
            }
            private void Execute(ClientPresentationAction binding, long ticket)
            {
                service.MainThread();
                if (!Current(ticket) || inputs.Count != 0) { Fail(inputs.Count != 0 ? "InputPending" : "ContextStale"); return; }
                if (binding.Action.DisabledReason != null || !attempted.Add(binding)) return;
                try
                {
                    using (new ClientGuiScope(true)) LastActionResult = binding.Action.TryExecute();
                    if (LastActionResult == null || !LastActionResult.Completed) Fail(LastActionResult?.Code ?? "ActionResultMissing");
                }
                catch (Exception error) { Fail("ActionFailed", error); }
                // Completed means callback completion only. The next owner projection supplies actual business status.
            }
            private void CloseConfirmation()
            { var previous = confirmation; confirmation = null; if (previous != null) Release(previous); }
            public void Dispose()
            {
                service.MainThread(); if (IsClosed) return;
                IsClosed = true; ComposerFocused = false; composerFocusAttempts = 0; generation++; isCurrent = null; page = null; blocks = null; InvalidatePaneMeasurements(); palette = null; colorDragging.Clear();
                CloseConfirmation(); var previous = actions; actions = null; Release(previous); attempted.Clear();
                inputs.Clear(); drafts.Clear(); failedRegions.Clear(); mediaVisibility.Clear(); scrollFeed = scrollRow = null;
                service.identities.Remove(identity); lifetime.Dispose();
            }

            private sealed class ConfirmationWindow : Window, IDisposable
            {
                private readonly Session session;
                private readonly ClientPresentationAction binding;
                private readonly long ticket;
                private Vector2 bodyScroll, buttonScroll;
                private float cachedWidth = -1, cachedBodyHeight; private Vector2 cachedAccept, cachedCancel; private long cachedTheme = -1;
                private readonly string frozenText;
                private bool closed;
                internal ConfirmationWindow(Session session, ClientPresentationAction binding, long ticket)
                    : base(new ClientThemeWindowDrawing(() => { session.UpdatePalette(); return session.palette; }))
                {
                    this.session = session; this.binding = binding; this.ticket = ticket;
                    frozenText = binding.Confirmation.Title + "\n\n" + binding.Confirmation.Text;
                    doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true;
                }
                public override Vector2 InitialSize => new Vector2(Mathf.Min(560, UiScreenSafeArea.Current.width), Mathf.Min(360, UiScreenSafeArea.Current.height));
                public override void WindowUpdate()
                {
                    base.WindowUpdate(); windowRect = UiScreenSafeArea.ClampWindow(windowRect, new Vector2(240, 180));
                    if (!session.Current(ticket)) Close();
                }
                public override void PostClose()
                { closed = true; if (ReferenceEquals(session.confirmation, this)) session.confirmation = null; base.PostClose(); }
                public void Dispose() { if (!closed) Close(); }
                public override void DoWindowContents(Rect rect)
                {
                    if (closed || session.IsClosed || rect.width < 24 || rect.height < 96) return;
                    bool accept = false, cancel = false;
                    try
                    {
                        using (new ClientGuiScope(true))
                        {
                            session.UpdatePalette(); Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true;
                            var descriptor = binding.Confirmation;
                            float width = Mathf.Max(1, rect.width - 20);
                            string text = frozenText;
                            if (cachedWidth != width || cachedTheme != session.palette.Version)
                            {
                                cachedWidth = width; cachedTheme = session.palette.Version;
                                cachedBodyHeight = Text.CalcHeight(text, width);
                                cachedAccept = ClientWidgets.MeasureButton(descriptor.AcceptLabel, width, minimumWidth: width);
                                cachedCancel = ClientWidgets.MeasureButton(descriptor.CancelLabel, width, minimumWidth: width);
                            }
                            float height = cachedBodyHeight; var acceptSize = cachedAccept; var cancelSize = cachedCancel;
                            float buttonContentHeight = acceptSize.y + cancelSize.y + 6;
                            float footerHeight = Mathf.Min(buttonContentHeight, rect.height * .5f);
                            var body = new Rect(rect.x, rect.y, rect.width, Mathf.Max(0, rect.height - footerHeight - 8));
                            Widgets.BeginScrollView(body, ref bodyScroll, new Rect(0, 0, width, height));
                            try { ClientWidgets.Label(new Rect(0, 0, width, height), text, session.palette); }
                            finally { Widgets.EndScrollView(); }
                            ClientWidgets.ScrollbarDecoration(body,bodyScroll,new Rect(0,0,width,height),session.palette);
                            bool fresh = session.Current(ticket);
                            Widgets.BeginScrollView(new Rect(rect.x, rect.yMax - footerHeight, rect.width, footerHeight), ref buttonScroll, new Rect(0, 0, width, buttonContentHeight));
                            try
                            {
                                accept = ClientWidgets.Button(new Rect(0, 0, width, acceptSize.y), descriptor.AcceptLabel, session.palette, binding.Kind, fresh, wrap: true);
                                cancel = ClientWidgets.Button(new Rect(0, acceptSize.y + 6, width, cancelSize.y), descriptor.CancelLabel, session.palette, wrap: true);
                            }
                            finally { Widgets.EndScrollView(); }
                            ClientWidgets.ScrollbarDecoration(new Rect(rect.x,rect.yMax-footerHeight,rect.width,footerHeight),buttonScroll,new Rect(0,0,width,buttonContentHeight),session.palette);
                        }
                    }
                    catch (Exception error) { session.Fail("ConfirmationFailed", error); Close(); return; }
                    if (accept || cancel) Close();
                    if (accept && Event.current != null && Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint)
                        session.Execute(binding, ticket);
                }
            }
        }
    }
}
