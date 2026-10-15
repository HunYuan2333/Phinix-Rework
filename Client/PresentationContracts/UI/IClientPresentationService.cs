using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Utils.Framework;

namespace PhinixClient.Framework
{
    // Borrow this service. Open during activation/on explicit view open; dispose the returned owned view.
    // All calls require the game main thread. No business operation is implied by rendering or closing.
    public interface IClientPresentationService
    {
        IClientPresentationSession Open(IPhinixExtensionModule module, string pageId);
    }

    public interface IClientPresentationSession : IDisposable
    {
        string OwnerId { get; }
        string PageId { get; }
        bool IsClosed { get; }
        string FailureCode { get; }
        ClientActionExecutionResult LastActionResult { get; }
        // On success, ownership of actions transfers to the session (including an empty set).
        // On failure the caller still owns it. Use a NEW set for every publish. Old confirmations are closed.
        // actions may be null for a read-only page with no action bindings.
        // isCurrent is a pure read guard for this exact projection/context. It is rechecked before execution.
        void Publish(ClientPresentationPage page, IClientActionSet actions, Func<bool> isCurrent);
        void Draw(ClientPresentationBounds bounds);
        // Borrowed layout query for owners that dock the shared composer beside their feed.
        float MeasureComposerHeight(float width);
        // UI drafts/selection only, never business commands. Drain in owner update before Publish.
        // Text bridges may commit during Layout/Repaint; returned text changes are retained too.
        // Old revision input is invalidated on Publish/close/stop. Validation/commit remain owner-owned.
        bool TryDequeueInput(out ClientPresentationInput input);
        string ComposerDraft { get; }
        string ComposerControlName { get; }
        bool ComposerFocused { get; }
        void SetComposerDraft(string value);
        void RequestComposerFocus();
        // Only after explicit user intent and owner validation/publication. Same transferred action set.
        void InvokeAction(string actionId);
        // Neutral compact menu over the same published bindings and revision guards.
        // Keep this session alive while the menu/confirmation is open. No new action registry.
        IReadOnlyList<ClientPresentationMenuItem> CreateActionMenuItems();
        void ScrollToRow(string feedId, string rowId);
        void ScrollToEnd();
    }

    // Request is a guarded UI intent, not business confirmation or a completion receipt.
    // A null request is disabled. The producer owns the callback lifetime.
    public sealed class ClientPresentationMenuItem
    {
        public ClientPresentationMenuItem(string label, Action request)
        { Label = label; Request = request; }
        public string Label { get; }
        public Action Request { get; }
    }

    public enum ClientPresentationTone { Neutral, Pending, Warning, Error, Success }

    public sealed class ClientPresentationStatus
    {
        public ClientPresentationStatus(string text, ClientPresentationTone tone = ClientPresentationTone.Neutral)
        { Text = ClientPresentationLimits.Text(text); Tone = tone; if (!Enum.IsDefined(typeof(ClientPresentationTone), tone)) throw new ArgumentOutOfRangeException(nameof(tone)); }
        public string Text { get; }
        // Tone is a presentation hint supplied by the owner, never a Host-inferred business outcome.
        public ClientPresentationTone Tone { get; }
    }

    public sealed class ClientPresentationConfirmation
    {
        public ClientPresentationConfirmation(string title, string text, string acceptLabel, string cancelLabel)
        { Title = ClientPresentationLimits.Text(title); Text = ClientPresentationLimits.Text(text, 65536); AcceptLabel = ClientPresentationLimits.Text(acceptLabel); CancelLabel = ClientPresentationLimits.Text(cancelLabel);
          if (string.IsNullOrWhiteSpace(AcceptLabel) || string.IsNullOrWhiteSpace(CancelLabel)) throw new ArgumentException("PresentationConfirmationLabelRequired"); }
        public string Title { get; }
        public string Text { get; }
        public string AcceptLabel { get; }
        public string CancelLabel { get; }
    }

    public sealed class ClientPresentationAction
    {
        public ClientPresentationAction(string id, IClientContributedAction action,
            ClientButtonKind kind = ClientButtonKind.Normal, ClientPresentationConfirmation confirmation = null)
        { Id = ClientPresentationLimits.Id(id); Action = action ?? throw new ArgumentNullException(nameof(action)); Kind = kind; Confirmation = confirmation;
          if (!Enum.IsDefined(typeof(ClientButtonKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind)); }
        public string Id { get; }
        // Must be a member of the exact action set transferred with Publish; no parallel operation registry.
        public IClientContributedAction Action { get; }
        public ClientButtonKind Kind { get; }
        public ClientPresentationConfirmation Confirmation { get; }
        public bool IsSelected { get; private set; }
        // Visual projection only; never changes action availability or execution authority.
        public ClientPresentationAction WithSelection(bool selected = true) => selected == IsSelected ? this :
            new ClientPresentationAction(Id, Action, Kind, Confirmation) { IsSelected = selected };
    }

    public enum ClientPresentationInputKind { TextChanged, RowSelected, SubmitRequested, FeedBodySelected, FeedHeadingSelected, FeedReferenceSelected, FeedMediaSelected, FeedMediaVisible }
    public sealed class ClientPresentationInput
    {
        public ClientPresentationInput(long revision, string id, ClientPresentationInputKind kind, string value)
            : this(revision, id, kind, value, null) { }
        public ClientPresentationInput(long revision, string id, ClientPresentationInputKind kind, string value, string target)
        { Revision = revision; Id = id; Kind = kind; Value = value; Target = target; }
        public long Revision { get; }
        public string Id { get; }
        public ClientPresentationInputKind Kind { get; }
        public string Value { get; }
        public string Target { get; }
    }
    public enum ClientPresentationTextInputKind { Text, Integer }

    public sealed class ClientPresentationSlider
    {
        public ClientPresentationSlider(float minimum, float maximum, float value)
        {
            if (float.IsNaN(minimum) || float.IsInfinity(minimum) || float.IsNaN(maximum) || float.IsInfinity(maximum) ||
                float.IsNaN(value) || float.IsInfinity(value) || minimum >= maximum || value < minimum || value > maximum)
                throw new ArgumentException("PresentationSliderInvalid");
            Minimum = minimum; Maximum = maximum; Value = value;
        }
        public float Minimum { get; } public float Maximum { get; } public float Value { get; }
    }
    public sealed class ClientPresentationSwatch
    {
        public ClientPresentationSwatch(string id, string label, ClientRgba value)
        {
            foreach (float channel in new[] { value.Red, value.Green, value.Blue, value.Alpha })
                if (float.IsNaN(channel) || float.IsInfinity(channel) || channel < 0 || channel > 1) throw new ArgumentException("PresentationSwatchInvalid");
            Id = ClientPresentationLimits.Id(id); Label = ClientPresentationLimits.Text(label); Value = value;
        }
        public string Id { get; } public string Label { get; } public ClientRgba Value { get; }
    }
    public sealed class ClientPresentationTextField
    {
        public ClientPresentationTextField(string id, string label, string value, string placeholder = null,
            int maxLength = 256, string disabledReason = null, string validationText = null)
            : this(id, label, value, placeholder, maxLength, disabledReason, validationText, ClientPresentationTextInputKind.Text) { }
        public ClientPresentationTextField(string id, string label, string value, string placeholder,
            int maxLength, string disabledReason, string validationText, ClientPresentationTextInputKind inputKind)
        {
            if (!Enum.IsDefined(typeof(ClientPresentationTextInputKind), inputKind)) throw new ArgumentOutOfRangeException(nameof(inputKind));
            InputKind = inputKind;
            Id = ClientPresentationLimits.Id(id); Label = ClientPresentationLimits.Text(label);
            Value = ClientPresentationLimits.Text(value); Placeholder = ClientPresentationLimits.Text(placeholder);
            if (maxLength < 1 || maxLength > 4096 || Value.Length > maxLength) throw new ArgumentOutOfRangeException(nameof(maxLength));
            MaxLength = maxLength; DisabledReason = disabledReason == null ? null : ClientPresentationLimits.Text(disabledReason);
            ValidationText = ClientPresentationLimits.Text(validationText);
        }
        public ClientPresentationSlider Slider { get; private set; }
        // Slider emits the same raw TextChanged intent; parsing/commit remain with the owner.
        public ClientPresentationTextField WithSlider(ClientPresentationSlider slider)
        {
            if (InputKind != ClientPresentationTextInputKind.Text || slider == null) throw new ArgumentException("PresentationSliderFieldInvalid");
            return new ClientPresentationTextField(Id, Label, Value, Placeholder, MaxLength, DisabledReason, ValidationText, InputKind) { Slider = slider };
        }
        public string Id { get; }
        public string Label { get; }
        public string Value { get; }
        public string Placeholder { get; }
        // Syntax only; parsing/range/business validity remain owner-owned. Empty/minus drafts are permitted.
        public ClientPresentationTextInputKind InputKind { get; }
        public static ClientPresentationTextField Integer(string id, string label, string value, string validationText = null, string disabledReason = null)
        { return new ClientPresentationTextField(id, label, value, null, 20, disabledReason, validationText, ClientPresentationTextInputKind.Integer); }
        public int MaxLength { get; }
        public string DisabledReason { get; }
        public string ValidationText { get; }
    }
    // Borrowed icon; the publisher retains the texture lifetime. Label remains the accessible fallback.
    public sealed class ClientPresentationBadge
    {
        public ClientPresentationBadge(string id, string label, IClientPresentationImage image = null)
        { Id = ClientPresentationLimits.Id(id); Label = ClientPresentationLimits.Text(label); Image = image; }
        public string Id { get; }
        public string Label { get; }
        public IClientPresentationImage Image { get; }
        public string ColorKey { get; private set; }
        public ClientPresentationBadge WithColor(string key) => new ClientPresentationBadge(Id, Label, Image) { ColorKey = ClientPresentationLimits.Id(key) };
    }
    public sealed class ClientPresentationRow
    {
        public ClientPresentationRow(string id, string label, string detail = null, string disabledReason = null, string colorKey = null)
        { Id = ClientPresentationLimits.Id(id); Label = ClientPresentationLimits.Text(label); Detail = ClientPresentationLimits.Text(detail);
          DisabledReason = disabledReason == null ? null : ClientPresentationLimits.Text(disabledReason);
          ColorKey = colorKey == null ? null : ClientPresentationLimits.Id(colorKey); }
        public string Id { get; }
        public string Label { get; }
        public string Detail { get; }
        public string DisabledReason { get; }
        public string ColorKey { get; }
        public IReadOnlyList<ClientPresentationBadge> Badges { get; private set; } = Array.Empty<ClientPresentationBadge>();
        public IClientPresentationImage Image { get; private set; }
        public bool HasIcon => Image != null;
        // Row controls carry owner intents through the same action/field pipeline as sections.
        public IReadOnlyList<ClientPresentationAction> Actions { get; private set; } = Array.Empty<ClientPresentationAction>();
        public ClientPresentationTextField Field { get; private set; }
        public ClientPresentationRow WithControls(IEnumerable<ClientPresentationAction> actions, ClientPresentationTextField field = null)
        {
            if (field?.Slider != null) throw new ArgumentException("PresentationRowSliderUnsupported");
            return new ClientPresentationRow(Id, Label, Detail, DisabledReason, ColorKey) {
                Image = Image, Badges = Badges, Actions = ClientPresentationLimits.Copy(actions, 4, a => a.Id), Field = field };
        }
        public ClientPresentationRow WithImage(IClientPresentationImage image) =>
            new ClientPresentationRow(Id, Label, Detail, DisabledReason, ColorKey) { Image = image, Badges = Badges, Actions = Actions, Field = Field };
        public ClientPresentationRow WithBadges(IEnumerable<ClientPresentationBadge> badges) =>
            new ClientPresentationRow(Id, Label, Detail, DisabledReason, ColorKey) { Image = Image, Badges = ClientPresentationLimits.Copy(badges, 4, b => b.Id), Actions = Actions, Field = Field };
    }
    public sealed class ClientPresentationList
    {
        public ClientPresentationList(string id, IEnumerable<ClientPresentationRow> rows, string emptyText, string selectedId = null)
        { Id = ClientPresentationLimits.Id(id); Rows = ClientPresentationLimits.Copy(rows, 20000, row => row.Id);
          EmptyText = ClientPresentationLimits.Text(emptyText); SelectedId = selectedId == null ? null : ClientPresentationLimits.Id(selectedId);
          if (SelectedId != null && !System.Linq.Enumerable.Any(Rows, row => row.Id == SelectedId)) throw new ArgumentException("PresentationSelectionMissing"); }
        public string Id { get; }
        public IReadOnlyList<ClientPresentationRow> Rows { get; }
        public string EmptyText { get; }
        public string SelectedId { get; }
        public bool CompactRows { get; private set; }
        // Opt-in density; long text and validation still wrap rather than disappear.
        public ClientPresentationList WithCompactRows() => new ClientPresentationList(Id, Rows, EmptyText, SelectedId) {
            CompactRows = true, IsReadOnly = IsReadOnly, HoverColorKey = HoverColorKey, SelectionColorKey = SelectionColorKey };
        public bool IsReadOnly { get; private set; }
        public string HoverColorKey { get; private set; }
        public string SelectionColorKey { get; private set; }
        // Borrowed theme roles; layout/input/authority are unchanged. Unknown keys use Host fallback.
        public ClientPresentationList WithColors(string hoverColorKey, string selectionColorKey) =>
            new ClientPresentationList(Id, Rows, EmptyText, SelectedId) { CompactRows = CompactRows, IsReadOnly = IsReadOnly,
                HoverColorKey = hoverColorKey == null ? null : ClientPresentationLimits.Id(hoverColorKey),
                SelectionColorKey = selectionColorKey == null ? null : ClientPresentationLimits.Id(selectionColorKey) };
        public ClientPresentationList WithReadOnly() =>
            new ClientPresentationList(Id, Rows, EmptyText) { CompactRows = CompactRows, IsReadOnly = true, HoverColorKey = HoverColorKey, SelectionColorKey = SelectionColorKey };
    }
    // Borrowed passive content region; owner retains assets and owns their disposal through its module lifetime.
    // Main-thread, pure Measure/Draw; no file scans, subscriptions, commands or input handling.
    // Host clips the region and restores GUI state. Use public fields/actions for interactive intents.
    public interface IClientPresentationRegion
    {
        float Measure(float width, IClientThemeReader theme);
        void Draw(IClientPresentationDrawContext context);
    }
    // Borrowed for this Repaint callback only. Retained contexts reject later use.
    // Passive drawing only: no input, actions, subscriptions, native resources or global GUI state.
    public interface IClientPresentationDrawContext
    {
        ClientPresentationBounds Bounds { get; }
        IClientThemeReader Theme { get; }
        void Progress(ClientPresentationBounds bounds, float fraction);
    }
    public sealed class ClientPresentationRegion
    {
        public ClientPresentationRegion(string id, IClientPresentationRegion content)
        { Id = ClientPresentationLimits.Id(id); Content = content ?? throw new ArgumentNullException(nameof(content)); }
        public string Id { get; }
        public IClientPresentationRegion Content { get; }
    }

    public enum ClientPresentationMediaState { Idle, Loading, Ready, Failed, Unavailable }
    // Owner-projected content only; Host neither fetches a URL nor owns/destroys the borrowed texture.
    public sealed class ClientPresentationMedia
    {
        public ClientPresentationMedia(string id, ClientPresentationMediaState state, string text, IClientPresentationImage image = null,
            float aspectRatio = 1, float maximumHeight = 360, string colorKey = null, string surfaceKey = null)
        {
            Id = ClientPresentationLimits.Id(id); Text = ClientPresentationLimits.Text(text); State = state; Image = image;
            if (!Enum.IsDefined(typeof(ClientPresentationMediaState), state) || float.IsNaN(aspectRatio) || float.IsInfinity(aspectRatio) || aspectRatio <= 0 ||
                float.IsNaN(maximumHeight) || float.IsInfinity(maximumHeight) || maximumHeight < 48 || maximumHeight > 4096) throw new ArgumentException("PresentationMediaGeometry");
            AspectRatio = aspectRatio; MaximumHeight = maximumHeight; ColorKey = colorKey == null ? null : ClientPresentationLimits.Id(colorKey);
            SurfaceKey = surfaceKey == null ? null : ClientPresentationLimits.Id(surfaceKey);
        }
        public string Id { get; } public string Text { get; } public ClientPresentationMediaState State { get; }
        public IClientPresentationImage Image { get; } public float AspectRatio { get; } public float MaximumHeight { get; } public string ColorKey { get; } public string SurfaceKey { get; }
    }
    public enum ClientPresentationMetadataPlacement { Right, AfterHeading }

    public sealed class ClientPresentationFeedRow
    {
        public ClientPresentationFeedRow(string id, string heading, string metadata, string body, string reference = null,
            string referenceTarget = null, IEnumerable<ClientPresentationMedia> media = null, bool indented = false, bool centered = false,
            string surfaceKey = null, string bodyColorKey = null, string accentKey = null, string hoverKey = null, string metadataColorKey = null, string referenceBorderKey = null)
        {
            Id = ClientPresentationLimits.Id(id); Heading = ClientPresentationLimits.Text(heading); Metadata = ClientPresentationLimits.Text(metadata);
            Body = body ?? ""; if (Body.Length > 65536) throw new ArgumentException("PresentationFeedTextLimit");
            Reference = ClientPresentationLimits.Text(reference); ReferenceTarget = referenceTarget == null ? null : ClientPresentationLimits.Id(referenceTarget);
            Media = ClientPresentationLimits.Copy(media, 32, m => m.Id); Indented = indented; Centered = centered;
            SurfaceKey = surfaceKey == null ? null : ClientPresentationLimits.Id(surfaceKey);
            BodyColorKey = bodyColorKey == null ? null : ClientPresentationLimits.Id(bodyColorKey);
            AccentKey = accentKey == null ? null : ClientPresentationLimits.Id(accentKey);
            HoverKey = hoverKey == null ? null : ClientPresentationLimits.Id(hoverKey);
            MetadataColorKey = metadataColorKey == null ? null : ClientPresentationLimits.Id(metadataColorKey);
            ReferenceBorderKey = referenceBorderKey == null ? null : ClientPresentationLimits.Id(referenceBorderKey);
        }
        // Logical GUI units; Host still constrains the reservation to the current viewport.
        public float MetadataMinimumWidth { get; private set; } = 64;
        public float MetadataGap { get; private set; } = 8;
        // Optional paint in the existing inter-row gutter; does not affect measurement or input.
        public string LeadingSeparatorKey { get; private set; }
        private ClientPresentationFeedRow Copy()
        {
            return new ClientPresentationFeedRow(Id, Heading, Metadata, Body, Reference, ReferenceTarget, Media, Indented, Centered,
                SurfaceKey, BodyColorKey, AccentKey, HoverKey, MetadataColorKey, ReferenceBorderKey)
                { MetadataPlacement=MetadataPlacement, MetadataMinimumWidth=MetadataMinimumWidth, MetadataGap=MetadataGap, LeadingSeparatorKey=LeadingSeparatorKey };
        }
        public ClientPresentationFeedRow WithLeadingSeparator(string colorKey)
        {
            var copy=Copy(); copy.LeadingSeparatorKey=colorKey==null?null:ClientPresentationLimits.Id(colorKey); return copy;
        }
        public ClientPresentationFeedRow WithMetadataLayout(float minimumWidth = 64, float gap = 8)
        {
            if (float.IsNaN(minimumWidth) || float.IsInfinity(minimumWidth) || minimumWidth < 0 || minimumWidth > 512) throw new ArgumentOutOfRangeException(nameof(minimumWidth));
            if (float.IsNaN(gap) || float.IsInfinity(gap) || gap < 0 || gap > 64) throw new ArgumentOutOfRangeException(nameof(gap));
            var copy=Copy(); copy.MetadataMinimumWidth=minimumWidth; copy.MetadataGap=gap; return copy;
        }
        public ClientPresentationMetadataPlacement MetadataPlacement { get; private set; }
        public ClientPresentationFeedRow WithMetadataPlacement(ClientPresentationMetadataPlacement placement)
        {
            if (placement != ClientPresentationMetadataPlacement.Right && placement != ClientPresentationMetadataPlacement.AfterHeading) throw new ArgumentOutOfRangeException(nameof(placement));
            if (placement == MetadataPlacement) return this;
            var copy=Copy(); copy.MetadataPlacement=placement; return copy;
        }
        public string Id { get; } public string Heading { get; } public string Metadata { get; } public string Body { get; }
        public string Reference { get; } public string ReferenceTarget { get; } public IReadOnlyList<ClientPresentationMedia> Media { get; }
        public bool Indented { get; } public bool Centered { get; } public string SurfaceKey { get; } public string BodyColorKey { get; } public string AccentKey { get; } public string HoverKey { get; } public string MetadataColorKey { get; } public string ReferenceBorderKey { get; }
    }
    public sealed class ClientPresentationFeed
    {
        public ClientPresentationFeed(string id, IEnumerable<ClientPresentationFeedRow> rows, string emptyText, bool followEnd = true)
        { Id = ClientPresentationLimits.Id(id); Rows = ClientPresentationLimits.Copy(rows, 10000, r => r.Id); EmptyText = ClientPresentationLimits.Text(emptyText); FollowEnd = followEnd; }
        public string Id { get; } public IReadOnlyList<ClientPresentationFeedRow> Rows { get; } public string EmptyText { get; } public bool FollowEnd { get; }
    }

    public sealed class ClientPresentationActionMenu
    {
        public ClientPresentationActionMenu(string id, string label, IEnumerable<string> actionIds)
        {
            Id = ClientPresentationLimits.Id(id); Label = ClientPresentationLimits.Text(label);
            var ids = new List<string>(); var unique = new HashSet<string>();
            foreach (var actionId in actionIds ?? throw new ArgumentNullException(nameof(actionIds)))
                if (ids.Count >= 32 || !unique.Add(ClientPresentationLimits.Id(actionId))) throw new ArgumentException("PresentationMenuActionsInvalid"); else ids.Add(actionId);
            if (ids.Count == 0) throw new ArgumentException("PresentationMenuEmpty"); ActionIds = ids.AsReadOnly();
        }
        public string Id { get; } public string Label { get; } public IReadOnlyList<string> ActionIds { get; }
    }
    public sealed class ClientPresentationSection
    {
        public ClientPresentationSection(string id, ClientPresentationFeed feed) : this(id, "", "")
        { Feed = feed ?? throw new ArgumentNullException(nameof(feed)); }
        public ClientPresentationFeed Feed { get; }
        public IReadOnlyList<ClientPresentationSwatch> Swatches { get; private set; } = Array.Empty<ClientPresentationSwatch>();
        public ClientPresentationSection WithSwatches(IEnumerable<ClientPresentationSwatch> swatches) =>
            new ClientPresentationSection(this) { Swatches = ClientPresentationLimits.Copy(swatches, 32, s => s.Id) };
        public IReadOnlyList<ClientPresentationBadge> Badges { get; private set; } = Array.Empty<ClientPresentationBadge>();
        public ClientPresentationSection WithBadges(IEnumerable<ClientPresentationBadge> badges) =>
            new ClientPresentationSection(this) { Badges = ClientPresentationLimits.Copy(badges, 4, b => b.Id) };
        public ClientPresentationSection(string id, string title, string summary,
            IEnumerable<ClientPresentationAction> actions = null, ClientPresentationStatus status = null,
            string surfaceKey = null, string summaryColorKey = null,
            IEnumerable<ClientPresentationTextField> fields = null, ClientPresentationList list = null, ClientPresentationRegion region = null)
        { Id = ClientPresentationLimits.Id(id); Title = ClientPresentationLimits.Text(title); Summary = ClientPresentationLimits.Text(summary, 65536);
          Actions = ClientPresentationLimits.Copy(actions, 32, a => a.Id); Status = status;
          SurfaceKey = surfaceKey == null ? null : ClientPresentationLimits.Id(surfaceKey);
          SummaryColorKey = summaryColorKey == null ? null : ClientPresentationLimits.Id(summaryColorKey);
          Fields = ClientPresentationLimits.Copy(fields, 16, f => f.Id); List = list; Region = region; }
        // Title, ordinary fields and actions share a wrapping toolbar. Rich content uses normal layout.
        public bool InlineToolbar { get; private set; }
        public ClientPresentationSection WithInlineToolbar() => new ClientPresentationSection(this) { InlineToolbar = true };
        public bool Plain { get; private set; }
        public ClientPresentationSection WithPlainLayout() => new ClientPresentationSection(this) { Plain = true };
        public IReadOnlyList<ClientPresentationActionMenu> ActionMenus { get; private set; } = Array.Empty<ClientPresentationActionMenu>();
        public ClientPresentationSection WithActionMenus(IEnumerable<ClientPresentationActionMenu> menus)
        {
            var copy = ClientPresentationLimits.Copy(menus, 4, m => m.Id); var used = new HashSet<string>();
            foreach (var menu in copy) foreach (var id in menu.ActionIds)
            {
                bool found = false; foreach (var action in Actions) found |= action.Id == id;
                if (!found || !used.Add(id)) throw new ArgumentException("PresentationMenuActionNotInSection");
            }
            return new ClientPresentationSection(this) { ActionMenus = copy };
        }
        public int FieldColumns { get; private set; } = 1;
        public float FieldMinimumWidth { get; private set; } = 180;
        public ClientPresentationSection WithFieldColumns(int columns = 2, float minimumWidth = 180)
        {
            if (columns < 1 || columns > 4 || float.IsNaN(minimumWidth) || float.IsInfinity(minimumWidth) || minimumWidth < 80 || minimumWidth > 1000) throw new ArgumentException("PresentationFieldColumnsInvalid");
            return new ClientPresentationSection(this) { FieldColumns = columns, FieldMinimumWidth = minimumWidth };
        }
        public bool WrapActions { get; private set; }
        public ClientPresentationSection WithWrappedActions() => WrapActions ? this : new ClientPresentationSection(this) { WrapActions = true };
        private ClientPresentationSection(ClientPresentationSection source)
        {
            InlineToolbar = source.InlineToolbar; Id = source.Id; Title = source.Title; Summary = source.Summary; Actions = source.Actions;
            Status = source.Status; SurfaceKey = source.SurfaceKey; SummaryColorKey = source.SummaryColorKey;
            FieldColumns = source.FieldColumns; FieldMinimumWidth = source.FieldMinimumWidth; Fields = source.Fields; List = source.List; Region = source.Region; Feed = source.Feed; WrapActions = source.WrapActions; Badges = source.Badges; Swatches = source.Swatches; Plain = source.Plain; ActionMenus = source.ActionMenus;
        }
        public string Id { get; }
        public string Title { get; }
        public string Summary { get; }
        public IReadOnlyList<ClientPresentationAction> Actions { get; }
        public ClientPresentationStatus Status { get; }
        // Optional registered keys; absent keys fall back to Host roles. No file/schema knowledge here.
        public string SurfaceKey { get; }
        public string SummaryColorKey { get; }
        public IReadOnlyList<ClientPresentationTextField> Fields { get; }
        public ClientPresentationList List { get; }
        public ClientPresentationRegion Region { get; }
    }

    // Bounded single-line input dock. Assets are borrowed; owner retains their lifetime.
    public sealed class ClientPresentationComposer
    {
        public ClientPresentationComposer(ClientPresentationTextField field, ClientPresentationAction submit,
            IClientPresentationImage image = null, IEnumerable<ClientPresentationAction> actions = null)
        { if (field?.Slider != null) throw new ArgumentException("PresentationComposerSliderUnsupported"); Actions = ClientPresentationLimits.Copy(actions, 31, a => a.Id); Field = field ?? throw new ArgumentNullException(nameof(field)); Submit = submit ?? throw new ArgumentNullException(nameof(submit)); Image = image; }
        public ClientPresentationTextField Field { get; }
        public ClientPresentationAction Submit { get; }
        public IClientPresentationImage Image { get; }
        public IReadOnlyList<ClientPresentationAction> Actions { get; }
    }

    // Two independently scrolling section groups; narrow containers use labelled pane tabs.
    public sealed class ClientPresentationSplitView
    {
        public ClientPresentationSplitView(int primarySectionCount, string primaryTitle, string secondaryTitle, float primaryFraction = 0.4f, float minimumWidth = 760)
        {
            if (primarySectionCount < 1 || primarySectionCount > 15 || float.IsNaN(primaryFraction) || primaryFraction < 0.3f || primaryFraction > 0.7f ||
                float.IsNaN(minimumWidth) || float.IsInfinity(minimumWidth) || minimumWidth < 480 || minimumWidth > 2000) throw new ArgumentException("PresentationSplitInvalid");
            PrimarySectionCount = primarySectionCount; PrimaryTitle = ClientPresentationLimits.Text(primaryTitle); SecondaryTitle = ClientPresentationLimits.Text(secondaryTitle);
            PrimaryFraction = primaryFraction; MinimumWidth = minimumWidth;
        }
        // Final sections span both panes in an independently scrolling lower area.
        public int SharedFooterSections { get; private set; }
        public float SharedFooterFraction { get; private set; } = .45f;
        public int SharedFooterHeaderSections { get; private set; }
        public ClientPresentationSplitView WithSharedFooter(int sections, float fraction = .45f, int headerSections = 0)
        {
            if (sections < 1 || sections > 13 || headerSections < 0 || headerSections >= sections || float.IsNaN(fraction) || fraction < .25f || fraction > .65f)
                throw new ArgumentException("PresentationSharedFooterInvalid");
            return new ClientPresentationSplitView(this, GlobalHeaderSections, PrimaryHeaderSections, SecondaryHeaderSections, SecondaryContextId) {
                SharedFooterSections = sections, SharedFooterFraction = fraction, SharedFooterHeaderSections = headerSections };
        }
        public int PrimaryFooterSections { get; private set; }
        public int SecondaryFooterSections { get; private set; }
        public ClientPresentationSplitView WithFixedFooters(int primarySections, int secondarySections)
        {
            if (primarySections < 0 || secondarySections < 0 || GlobalHeaderSections + PrimaryHeaderSections + primarySections >= PrimarySectionCount || secondarySections > 15) throw new ArgumentException("PresentationFixedFootersInvalid");
            return new ClientPresentationSplitView(this, GlobalHeaderSections, PrimaryHeaderSections, SecondaryHeaderSections, SecondaryContextId) { PrimaryFooterSections = primarySections, SecondaryFooterSections = secondarySections };
        }
        public bool AlignHeaders { get; private set; }
        public ClientPresentationSplitView WithAlignedHeaders() => new ClientPresentationSplitView(this, GlobalHeaderSections, PrimaryHeaderSections, SecondaryHeaderSections, SecondaryContextId) { AlignHeaders = true };
        public int GlobalHeaderSections { get; }
        public int PrimaryHeaderSections { get; }
        public int SecondaryHeaderSections { get; }
        public string SecondaryContextId { get; }
        public ClientPresentationSplitView WithFixedHeaders(int globalSections, int primarySections, int secondarySections, string secondaryContextId = null)
        {
            if (globalSections < 0 || primarySections < 0 || secondarySections < 0 || globalSections + primarySections + PrimaryFooterSections >= PrimarySectionCount || secondarySections > 15)
                throw new ArgumentException("PresentationFixedHeadersInvalid");
            return new ClientPresentationSplitView(this, globalSections, primarySections, secondarySections, secondaryContextId);
        }
        private ClientPresentationSplitView(ClientPresentationSplitView source, int global, int primary, int secondary, string context)
            : this(source.PrimarySectionCount, source.PrimaryTitle, source.SecondaryTitle, source.PrimaryFraction, source.MinimumWidth)
        { SharedFooterHeaderSections = source.SharedFooterHeaderSections; SharedFooterSections = source.SharedFooterSections; SharedFooterFraction = source.SharedFooterFraction; PrimaryFooterSections = source.PrimaryFooterSections; SecondaryFooterSections = source.SecondaryFooterSections; AlignHeaders = source.AlignHeaders; GlobalHeaderSections = global; PrimaryHeaderSections = primary; SecondaryHeaderSections = secondary; SecondaryContextId = context == null ? null : ClientPresentationLimits.Id(context); }
        public int PrimarySectionCount { get; }
        public string PrimaryTitle { get; }
        public string SecondaryTitle { get; }
        public float PrimaryFraction { get; }
        public float MinimumWidth { get; }
    }

    public sealed class ClientPresentationPage
    {
        public ClientPresentationPage(string id, string title, long revision, IEnumerable<ClientPresentationSection> sections, ClientPresentationComposer composer = null)
        { Composer = composer; Id = ClientPresentationLimits.Id(id); Title = ClientPresentationLimits.Text(title); Revision = revision;
          if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
          Sections = ClientPresentationLimits.Copy(sections, 16, s => s.Id); }
        public int FixedHeaderSectionCount { get; private set; }
        // Single-column counterpart to SplitView fixed headers; both use the same Host sections.
        public ClientPresentationPage WithFixedHeaderSections(int count)
        {
            if (count < 0 || count >= Sections.Count || SplitView != null) throw new ArgumentException("PresentationFixedHeaderSectionsInvalid");
            return new ClientPresentationPage(this, null) { FixedHeaderSectionCount = count };
        }
        public ClientPresentationSplitView SplitView { get; }
        public ClientPresentationPage WithSplitView(ClientPresentationSplitView split)
        {
            if (FixedHeaderSectionCount != 0 || split == null || split.PrimarySectionCount >= Sections.Count || split.SecondaryHeaderSections + split.SecondaryFooterSections >= Sections.Count - split.SharedFooterSections - split.PrimarySectionCount) throw new ArgumentException("PresentationSplitSectionsInvalid");
            return new ClientPresentationPage(this, split);
        }
        private ClientPresentationPage(ClientPresentationPage source, ClientPresentationSplitView split)
        { Id = source.Id; Title = source.Title; Revision = source.Revision; Sections = source.Sections; Composer = source.Composer; SplitView = split; FixedHeaderSectionCount = source.FixedHeaderSectionCount; }
        public string Id { get; }
        public string Title { get; }
        // Increment for any data, translated text, action/context or availability change.
        public long Revision { get; }
        public IReadOnlyList<ClientPresentationSection> Sections { get; }
        public ClientPresentationComposer Composer { get; }
    }

    internal static class ClientPresentationLimits
    {
        internal static string Id(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("PresentationIdInvalid");
            foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) throw new ArgumentException("PresentationIdInvalid");
            return value;
        }
        internal static string Text(string value, int maximum = 4096)
        { value = value ?? ""; if (value.Length > maximum) throw new ArgumentException("PresentationTextLimit"); return value; }
        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, int limit, Func<T, string> id) where T : class
        {
            var copy = new List<T>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            if (items != null) foreach (var item in items)
            {
                if (item == null || !ids.Add(id(item))) throw new ArgumentException("PresentationDuplicateOrNullItem");
                if (copy.Count >= limit) throw new ArgumentException("PresentationItemLimit");
                copy.Add(item);
            }
            return new ReadOnlyCollection<T>(copy);
        }
    }
}
