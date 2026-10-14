using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed partial class ManagedPluginStoreView
    {
        private IPhinixExtensionModule presentationOwner;
        private IClientPresentationService renderer;
        private IClientExtensionPointService points;
        private IClientActionPoint<PluginStorePresentationActionContext> actionPoint;
        private IClientPresentationSession presentation;
        private readonly string pageId = "store." + Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, ManagedStoreEntry> presentationRows = new Dictionary<string, ManagedStoreEntry>();
        private readonly Dictionary<string, ClientPresentationConfirmation> confirmations = new Dictionary<string, ClientPresentationConfirmation>();
        private long presentationRevision, publishedPoints = -1, publishedControls = -1;
        private ManagedStoreSnapshot publishedSnapshot;
        private string publishedQuery, publishedSelection, publishedError;
        private int publishedFilter = -1;
        private object publishedLanguage;
        private bool publishedDevMode;
        private ManagedStorePlan pendingPlan;
        private ManagedStoreSnapshot pendingLocalConfirmation;
        private bool presentationDirty = true;
        private ClientActionExecutionResult publishedResult;
        internal void ConfigurePresentation(IPhinixExtensionModule owner, IClientPresentationService renderer, IClientExtensionPointService points, IClientActionPoint<PluginStorePresentationActionContext> point)
        { presentationOwner = owner; this.renderer = renderer; this.points = points; actionPoint = point; }
        internal void OpenPresentation()
        { if (stopped || presentation != null && !presentation.IsClosed) return; presentation = renderer.Open(presentationOwner, pageId); publishedSnapshot = null; presentationDirty = true; UpdatePresentation(); }
        internal void ClosePresentation()
        { var old = presentation; presentation = null; old?.Dispose(); publishedSnapshot = null; }
        private static string EntryIdentity(ManagedStoreEntry entry) => entry == null ? null : entry.Key + "\n" + entry.Version;
        private static string ListIdentity(ManagedStoreEntry entry)
        { using (var hash = SHA256.Create()) return "package." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(EntryIdentity(entry)))).Replace("-", "").ToLowerInvariant(); }
        private static string Bounded(string text) => text == null ? "" : text.Length <= 4096 ? text : text.Substring(0, 4095) + "…";
        internal void UpdatePresentation()
        {
            if (stopped || presentation == null || presentation.IsClosed) return;
            var inputs = new List<ClientPresentationInput>();
            while (presentation.TryDequeueInput(out var input)) inputs.Add(input);
            foreach (var input in inputs)
            {
                if (input.Id == "search") { search = input.Value; presentationDirty = true; }
                else if ((input.Id == "packages" || input.Id == "versions") && presentationRows.TryGetValue(input.Value, out var entry))
                { pendingPlan = null; Select(entry, controller.Snapshot); presentationDirty = true; }
            }
            ManagedStoreSnapshot snapshot;
            using (new ClientGuiScope(true)) { snapshot = AdvanceOwner(); Rebuild(snapshot); }
            if (pendingPlan != null && (snapshot.Plan != pendingPlan || selected?.CatalogRecord != pendingPlan.Root)) pendingPlan = null;
            if (pendingLocalConfirmation != null && (snapshot != pendingLocalConfirmation || !localImportActive || !Prefs.DevMode)) pendingLocalConfirmation = null;
            string selection = EntryIdentity(selected); long controlRevision = controlState?.Revision ?? -1;
            long pointRevision = points.Revision; var nextLanguage = LanguageDatabase.activeLanguage; bool dev = Prefs.DevMode;
            string feedback = error ?? linkNotice;
            var nextResult = presentation.LastActionResult;
            if (!presentationDirty && ReferenceEquals(nextResult, publishedResult) && publishedSnapshot == snapshot && publishedQuery == search && publishedSelection == selection && publishedError == feedback &&
                publishedFilter == filter && publishedControls == controlRevision && publishedPoints == pointRevision && ReferenceEquals(publishedLanguage, nextLanguage) && publishedDevMode == dev) return;
            long revision = ++presentationRevision; var picked = selected; var framePlan = pendingPlan;
            Func<bool> current = () => !stopped && presentation != null && !presentation.IsClosed && !presentationDirty && presentationRevision == revision && pendingPlan == framePlan && ReferenceEquals(presentation.LastActionResult, nextResult) &&
                controller.Snapshot == snapshot && EntryIdentity(selected) == selection && search == publishedQuery && filter == publishedFilter &&
                (controls?.Capture().Revision ?? -1) == controlRevision && points.Revision == pointRevision && ReferenceEquals(LanguageDatabase.activeLanguage, nextLanguage) && Prefs.DevMode == dev;
            confirmations.Clear();
            var definitions = BuildPresentationActions(snapshot, picked);
            var actions = actionPoint.Collect(new PluginStorePresentationActionContext(pageId, snapshot.Revision, selection, snapshot.State.ToString(), picked?.Id, picked?.Version, snapshot.Busy || controlState?.Busy == true, InventoryKnown, definitions), current);
            try
            {
                var toolbar = new List<ClientPresentationAction>(); var utilities = new List<ClientPresentationAction>(); var operations = new List<ClientPresentationAction>();
                foreach (var action in actions.Actions)
                {
                    confirmations.TryGetValue(action.Id, out var confirmation);
                    var binding = new ClientPresentationAction(action.OwnerId + "." + action.Id, action, kind: action.OwnerId != presentationOwner.ExtensionId ? ClientButtonKind.Normal :
                        action.Id == "package-uninstall" ? ClientButtonKind.Danger :
                        action.Id == "package-install" || action.Id == "package-update" || action.Id == "package-confirm-plan" || action.Id == "local-confirm" ? ClientButtonKind.Primary : ClientButtonKind.Normal,
                        confirmation: action.OwnerId == presentationOwner.ExtensionId ? confirmation : null);
                    if (action.OwnerId == presentationOwner.ExtensionId)
                        binding = binding.WithSelection(action.Id == "filter." + filter || action.Id == "access-github" && accessMethod == RepositoryAccessMethod.GitHub || action.Id == "access-cloudflare" && accessMethod == RepositoryAccessMethod.Cloudflare);
                    if (action.OwnerId == presentationOwner.ExtensionId && action.Id.StartsWith("package-", StringComparison.Ordinal)) operations.Add(binding);
                    else if (action.OwnerId == presentationOwner.ExtensionId && (action.Id.StartsWith("filter.", StringComparison.Ordinal))) toolbar.Add(binding);
                    else utilities.Add(binding);
                }
                var routeIds = utilities.Where(b => b.Action.OwnerId == presentationOwner.ExtensionId && (b.Action.Id == "access-github" || b.Action.Id == "access-cloudflare")).Select(b => b.Id).ToArray();
                var moreIds = utilities.Where(b => b.Action.OwnerId != presentationOwner.ExtensionId || b.Action.Id == "management" || b.Action.Id == "themes" || b.Action.Id == "local-import").Select(b => b.Id).ToArray();
                var menus = new List<ClientPresentationActionMenu>();
                if (routeIds.Length > 0) menus.Add(new ClientPresentationActionMenu("connection", T("connection") + ": " + T(accessMethod == RepositoryAccessMethod.GitHub ? "accessGithub" : "accessCloudflare"), routeIds));
                if (moreIds.Length > 0) menus.Add(new ClientPresentationActionMenu("more", T("moreTools"), moreIds));
                var sections = new List<ClientPresentationSection>();
                string state = error == null ? linkNotice ?? status : localFailure != null ? T(localFailure.MessageKey) : error;
                if (snapshot.Progress != null) state += "\n" + progressCaption + "\n" + progressBytes;
                string localReview = pendingLocalConfirmation == null ? "" : LocalImportSummary(pendingLocalConfirmation);
                sections.Add(new ClientPresentationSection("store-tools", pendingLocalConfirmation == null ? "" : T("localConfirmation"), localReview, utilities,
                    new ClientPresentationStatus(Bounded(state), error != null || snapshot.State == ManagedStoreState.Failed ? ClientPresentationTone.Error : !InventoryKnown || snapshot.Repository?.Offline == true || snapshot.Repository?.Stale == true ? ClientPresentationTone.Warning : snapshot.Busy ? ClientPresentationTone.Pending : ClientPresentationTone.Neutral)).WithWrappedActions().WithActionMenus(menus));
                sections.Add(new ClientPresentationSection("browse-tools", "", "", toolbar,
                    fields: new[] { new ClientPresentationTextField("search", T("search"), search, T("searchShort"), 128, snapshot.Busy || controlState?.Busy == true ? T("state_" + snapshot.State) : null) }).WithWrappedActions());
                presentationRows.Clear(); var list = new List<ClientPresentationRow>();
                foreach (var group in rows)
                {
                    var row = group.Preferred; string id = ListIdentity(row); presentationRows[id] = row;
                    string route = T(row.IsLocalDevelopment ? "localBadge" : row.IsWorkshop ? "workshop" : "managedBadge");
                    var local = row.Installed; string facts = row.Version + " · " + Clean(row.Author);
                    if (local != null) facts += "\n" + T("installedVersion") + " " + Clean(local.Package.Version) + " · " + T("desired_" + local.Package.DesiredState);
                    list.Add(new ClientPresentationRow(id, Bounded(Clean(row.DisplayName(locale))), Bounded(facts), snapshot.Busy || controlState?.Busy == true ? T("state_" + snapshot.State) : null).WithBadges(BuildBadges(row, snapshot)));
                }
                string selectedRow = picked == null ? null : ListIdentity(picked);
                if (!list.Any(row => row.Id == selectedRow)) selectedRow = null;
                var browseSection = new ClientPresentationSection("browse", resultCaption, "", list: new ClientPresentationList("packages", list, T(snapshot.Catalog == null ? "refreshFirst" : "empty"), selectedRow));
                sections.Add(browseSection);
                if (accessMethod == RepositoryAccessMethod.GitHub) sections.Add(new ClientPresentationSection("access", "", T("githubAccessGuidance")));
                int primarySections = sections.Count;
                if (picked == null) { sections.Add(new ClientPresentationSection("empty-detail", T("detailPane"), T("choose"))); sections.Add(new ClientPresentationSection("choose", "", "")); }
                if (picked != null)
                {
                    sections.Add(new ClientPresentationSection("selected", Bounded(Clean(picked.DisplayName(locale))), T("version") + " " + Clean(picked.Version) + " · " + Clean(picked.Author) + (picked.License == null ? "" : " · " + Clean(picked.License)), operations).WithWrappedActions().WithBadges(BuildBadges(picked, snapshot)));
                    if (!string.IsNullOrWhiteSpace(detail)) sections.Add(new ClientPresentationSection("package-facts", "", detail));
                    if (picked.CatalogRecord != null && !picked.IsWorkshop)
                    {
                        var versions = ManagedStoreListing.BuildEntries(snapshot.Catalog, SharedInventory(snapshot), snapshot.Repository?.Endpoint, "", locale).SelectMany(group => group.Versions).Where(entry => entry.Id == picked.Id).ToArray();
                        var versionRows = new List<ClientPresentationRow>();
                        foreach (var version in versions) { string id = ListIdentity(version); presentationRows[id] = version; versionRows.Add(new ClientPresentationRow(id, version.Version, T(version.State == "active" ? "managedBadge" : "withdrawn"), snapshot.Busy ? T("state_" + snapshot.State) : null)); }
                        sections.Add(new ClientPresentationSection("versions", T("version"), "", list: new ClientPresentationList("versions", versionRows, "", versionRows.Any(row => row.Id == ListIdentity(picked)) ? ListIdentity(picked) : null)));
                    }
                    for (int i = 0; i < this.sections.Count; i++) sections.Add(new ClientPresentationSection("detail." + i, this.sections[i].Title, this.sections[i].Body));
                }
                if (pendingPlan != null) sections.Add(new ClientPresentationSection("plan", T("dependencyConfirmation"), PlanSummary(pendingPlan)));
                publishedQuery = search; publishedFilter = filter;
                presentationDirty = false;
                presentation.Publish(new ClientPresentationPage(pageId, "", revision, sections.Select(s => s.WithPlainLayout())).WithSplitView(new ClientPresentationSplitView(primarySections, T("browsePane"), T("detailPane"), .5f).WithFixedHeaders(1, 1, 1, picked == null ? "none" : ListIdentity(picked)).WithAlignedHeaders()), actions, current);
            }
            catch { presentationDirty = true; actions.Dispose(); throw; }
            publishedSnapshot = snapshot; publishedSelection = selection; publishedError = feedback; publishedControls = controlRevision;
            publishedPoints = pointRevision; publishedLanguage = nextLanguage; publishedDevMode = dev; publishedResult = nextResult;
        }
        private static string LocalImportSummary(ManagedStoreSnapshot prepared) => Clean(prepared.LocalPackage.Manifest.PackageId) + "\n" + prepared.LocalPackage.Manifest.Version + "\nSHA-256: " + prepared.LocalPackage.Sha256;
        private IEnumerable<ClientPresentationBadge> BuildBadges(ManagedStoreEntry entry, ManagedStoreSnapshot snapshot)
        {
            if (maintainers.IsOfficial(entry.CatalogRecord, snapshot.Catalog, snapshot.Repository?.Endpoint))
                yield return new ClientPresentationBadge("official", T("officialBadge"), ClientTextureImage.FromTexture(badgeIcons?.Get(StoreBadgeKind.Official))).WithColor("ui.badge.maintained");
            StoreBadgeKind kind = entry.IsLocalDevelopment ? StoreBadgeKind.Local : entry.IsWorkshop ? StoreBadgeKind.Workshop : StoreBadgeKind.Managed;
            yield return new ClientPresentationBadge(kind.ToString().ToLowerInvariant(), T(entry.IsLocalDevelopment ? "localBadge" : entry.IsWorkshop ? "workshop" : "managedBadge"), ClientTextureImage.FromTexture(badgeIcons?.Get(kind))).WithColor(entry.IsLocalDevelopment ? "ui.badge.development" : entry.IsWorkshop ? "ui.badge.external" : "ui.badge.managed");
        }
        private string PlanSummary(ManagedStorePlan plan) => T(plan.ReplacesPackages ? "updateConfirmation" : "dependencyConfirmation") + "\n\n" + string.Join("\n", plan.Items.Select(item => Clean(item.Package.DisplayName(locale)) + " " + item.Package.Manifest.Version + " · " + T(item.Installed == null ? "newPackage" : item.RequiresDownload ? "updatedPackage" : "existingPackage"))) + "\n\n" + T("downloadSize") + " " + (plan.DownloadBytes / 1024d / 1024d).ToString("F2") + " MiB";
        private IEnumerable<ClientActionDefinition> BuildPresentationActions(ManagedStoreSnapshot snapshot, ManagedStoreEntry picked)
        {
            var result = new List<ClientActionDefinition>();
            bool operationBusy = snapshot.Busy || controlState?.Busy == true;
            // Preserve the old modal review's exclusion of competing install/management operations.
            bool reviewingLocal = pendingLocalConfirmation == snapshot && snapshot.LocalPackage != null;
            bool busy = operationBusy || reviewingLocal;
            Action<string, string, Action, bool> add = (id, caption, execute, enabled) => result.Add(new ClientActionDefinition(id, caption, execute, enabled ? null : T(busy ? snapshot.Busy ? "state_" + snapshot.State : "state_Managing" : "blockedFriendly")));
            add("refresh", T("refresh"), () => ToolbarAction(0), !busy);
            add("access-github", T("accessGithub"), () => SwitchAccess(RepositoryAccessMethod.GitHub), !busy && accessMethod != RepositoryAccessMethod.GitHub);
            add("access-cloudflare", T("accessCloudflare"), () => SwitchAccess(RepositoryAccessMethod.Cloudflare), !busy && accessMethod != RepositoryAccessMethod.Cloudflare);
            add("management", T("management"), () => ToolbarAction(2), !busy);
            add("themes", T("localThemes"), () => ToolbarAction(3), true);
            if (Prefs.DevMode) add("local-import", T("localImport"), () => ToolbarAction(4), !busy);
            for (int i = 0; i < 3; i++) { int captured = i; add("filter." + i, T("filter" + i), () => { filter = captured; cached = null; }, !busy && filter != i); }
            if (snapshot.Busy) add("cancel", T("cancel"), () => { installIntent = null; pendingPlan = null; controller.Cancel(); }, true);
            if (error != null || snapshot.State == ManagedStoreState.Failed) add("diagnostics", T("copySummary"), () => GUIUtility.systemCopyBuffer = ErrorDetails(snapshot), true);
            if (reviewingLocal)
            {
                var prepared = snapshot;
                add("local-confirm", T("install"), () => Act(() => {
                    if (stopped || !localImportActive || !Prefs.DevMode) return;
                    controller.InstallLocal(prepared, environment.Capture(), Prefs.DevMode);
                }), !operationBusy && Prefs.DevMode);
                confirmations["local-confirm"] = new ClientPresentationConfirmation(T("localConfirmation"), LocalImportSummary(prepared), T("install"), T("cancel"));
                add("local-discard", T("cancel"), () => { localImportActive = false; controller.DiscardLocal(prepared); pendingLocalConfirmation = null; }, !operationBusy);
            }
            if (picked == null) return result;
            var local = selectedLocal; var state = local == null || controlState == null ? null : new ClientExtensionPackageState(local, controlState);
            bool owned = local != null && InventoryKnown;
            bool undo = local?.Package.DesiredState == ManagedExtensionDesiredState.PendingRemoval;
            bool enable = local != null && (local.Package.DesiredState != ManagedExtensionDesiredState.Enabled || state?.CanRestoreSingleModule == true);
            bool newer = local != null && !picked.IsWorkshop && !picked.IsInstalledOnly && picked.Manifest.Version.CompareTo(ManagedExtensionVersion.Parse(local.Package.Version)) > 0;
            bool fetch = InventoryKnown && !picked.IsInstalledOnly && snapshot.Repository != null && !snapshot.Repository.Offline && !snapshot.Repository.Stale && picked.State == "active";
            if (picked.IsWorkshop) add("package-workshop", T("openWorkshop"), OpenWorkshop, !busy && picked.State == "active");
            else if (local == null) add("package-install", T("install"), () => BeginInstall(snapshot, false), !busy && fetch);
            else if (!owned)
                add("package-management", T("management"), () => Act(management.OpenExtensionManagerWindow), !busy);
            else if (undo) add("package-cancel-removal", T("cancelRemoval"), () => CancelRemoval(local), !busy && local.DisableBlockCode == null);
            else if (newer) add("package-update", T("update"), () => BeginInstall(snapshot, true), !busy && fetch && local.Package.DesiredState == ManagedExtensionDesiredState.Enabled && local.Package.ContentState == ManagedExtensionContentState.ContentVerified && local.Package.DiagnosticCode == null);
            else if (state != null && state.ModuleCount > 0 && state.EnabledModuleCount == 0 && !state.CanRestoreSingleModule)
                add("package-management", T("management"), () => Act(management.OpenExtensionManagerWindow), !busy);
            else add("package-toggle", T(enable ? "enable" : "disable"), () => SetEnabled(local, enable, controlState?.Revision), !busy && (state?.CanRestoreSingleModule == true || (enable ? local.EnableBlockCode : local.DisableBlockCode) == null));
            if (owned && !undo && (newer || state != null && state.ModuleCount > 0 && state.EnabledModuleCount == 0 && !state.CanRestoreSingleModule))
                add("package-toggle-intent", T(enable ? "enable" : "disable"), () => SetEnabled(local, enable, controlState?.Revision), !busy && (state?.CanRestoreSingleModule == true || (enable ? local.EnableBlockCode : local.DisableBlockCode) == null));
            if (owned && !undo)
            {
                add("package-uninstall", T("uninstall"), () => Act(() => controller.Change(local.Package, ManagedExtensionDesiredState.PendingRemoval, environment.Capture())), !busy && local.RemovalBlockCode == null);
                confirmations["package-uninstall"] = new ClientPresentationConfirmation(T("uninstall"), T("confirmUninstall"), T("uninstall"), T("cancel"));
            }
            if (pendingPlan != null)
            {
                var plan = pendingPlan;
                add("package-confirm-plan", T("install"), () => Act(() => controller.Download(plan, environment.Capture(), true)), !busy && snapshot.Plan == plan && picked.CatalogRecord == plan.Root);
                confirmations["package-confirm-plan"] = new ClientPresentationConfirmation(T("install"), PlanSummary(plan), T("install"), T("cancel"));
            }
            return result;
        }
    }
}
