using System;
using System.Linq;
using System.Text;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils;
using Verse;

namespace Phinix.PluginStore
{
    internal sealed class PluginStoreView
    {
        private readonly StoreBrowserController controller;
        private readonly IClientEnvironmentService environment;
        private readonly IClientSettingsContext settings;
        private readonly IClientExtensionManagementWindowService management;
        private bool inventoryStarted;
        private readonly Action<string, LogLevel> log;
        private StoreBrowserSnapshot snapshot;
        private CatalogSnapshot displayedCatalog;
        private PackageRecord[] packages = new PackageRecord[0];
        private string[] rowLabels = new string[0], rowTips = new string[0];
        private PackageRecord selected;
        private string endpointText, remoteSourceId;
        private string filePath, sourceId, search = "", cachedSearch, localError;
        private Vector2 scroll, listScroll;
        private object language;
        private float width = -1, detailHeight, statusHeight, introHeight, selectionHeight;
        private string title, intro, pathLabel, sourceLabel, searchLabel, loadLabel, cancelLabel, planLabel, managementLabel;
        private string statusText, detailText, selectionText, sourceSettingsLabel;
        private string endpointLabel, remoteSourceLabel, refreshLabel, cacheLabel, localToolsLabel, checkDownloadsLabel;
        private bool showLocalTools, lastTimedStale;
        private bool showSourceSettings = true;
        private long revision = -1;
        private long loggedRevision = -1;
        private bool dirty = true;

        public PluginStoreView(StoreBrowserController controller, IClientEnvironmentService environment,
            IClientSettingsContext settings, IClientExtensionManagementWindowService management, Action<string, LogLevel> log)
        {
            this.controller = controller; this.environment = environment; this.settings = settings; this.management = management; this.log = log;
            endpointText = settings.Get("plugin-store.repositoryEndpoint", "");
            remoteSourceId = settings.Get("plugin-store.repositorySourceId", "phinix.official");
            filePath = settings.Get("plugin-store.localCatalogPath", "");
            sourceId = settings.Get("plugin-store.localSourceId", "test.local");
            showLocalTools = filePath.Length != 0;
        }

        public void Draw(Rect inRect)
        {
            if (inRect.width <= 0f || inRect.height <= 0f) return;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Color oldColor = GUI.color;
            bool oldWrap = Text.WordWrap;
            bool oldEnabled = GUI.enabled;
            try
            {
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.WordWrap = true;
                snapshot = controller.Snapshot;
                if (!inventoryStarted && !snapshot.Busy)
                {
                    inventoryStarted = true;
                    ReadManaged(); snapshot = controller.Snapshot;
                }
                Rebuild(Mathf.Max(1f, inRect.width - 16f));
                StoreBrowserLayout layout = StoreBrowserLayout.Calculate(inRect, selectionHeight, statusHeight);
                if (layout.Management.height > 0f && Widgets.ButtonText(layout.Management, managementLabel))
                {
                    try { localError = null; dirty = true; management.OpenExtensionManagerWindow(); }
                    catch (Exception ex) { Fail(ex); }
                }
                BoundedLabel(layout.Selection, selectionText, selectionHeight);
                try
                {
                    GUI.enabled = oldEnabled && !snapshot.Busy && selected != null && !selected.IsWorkshop &&
                        selected.State == "active" && snapshot.State != StoreBrowserState.Shutdown;
                    if (layout.Plan.height > 0f && Widgets.ButtonText(layout.Plan, planLabel)) Plan();
                }
                finally { GUI.enabled = oldEnabled; }
                BoundedLabel(layout.Feedback, statusText, statusHeight);
                if (layout.Body.height <= 0f) return;

                float listHeight = StoreBrowserLayout.PackageListHeight(packages.Length);
                float total = 110f + introHeight + listHeight + detailHeight;
                if (displayedCatalog != null) total += 34f;
                if (showSourceSettings || displayedCatalog == null) total += 226f + (showLocalTools ? 158f : 0f);
                if (snapshot.Busy) total += 34f;
                if (snapshot.Plan != null) total += 68f;
                total += 62f + snapshot.Managed.Count * 62f;
                scroll.y = Mathf.Clamp(scroll.y, 0f, Mathf.Max(0f, total - layout.Body.height));
                Widgets.BeginScrollView(layout.Body, ref scroll, new Rect(0, 0, width, Mathf.Max(layout.Body.height, total)));
                try
                {
                    float y = 0;
                    Label(ref y, title, 32f); Label(ref y, intro, introHeight);
                    if (displayedCatalog != null && Widgets.ButtonText(Row(ref y, 30), sourceSettingsLabel))
                    { showSourceSettings = !showSourceSettings; dirty = true; }
                    if (showSourceSettings || displayedCatalog == null)
                    {
                        Label(ref y, endpointLabel, 24f);
                        endpointText = Widgets.TextField(Row(ref y, 30), endpointText, 2048);
                        Label(ref y, remoteSourceLabel, 24f);
                        remoteSourceId = Widgets.TextField(Row(ref y, 30), remoteSourceId, 128);
                        try
                        {
                            GUI.enabled = oldEnabled && !snapshot.Busy && snapshot.State != StoreBrowserState.Shutdown;
                            if (Widgets.ButtonText(Row(ref y, 30), refreshLabel)) LoadRepository(false);
                            if (Widgets.ButtonText(Row(ref y, 30), cacheLabel)) LoadRepository(true);
                        }
                        finally { GUI.enabled = oldEnabled; }
                        if (Widgets.ButtonText(Row(ref y, 30), localToolsLabel)) { showLocalTools = !showLocalTools; dirty = true; }
                        if (showLocalTools)
                        {
                            Label(ref y, pathLabel, 24f);
                            filePath = Widgets.TextField(Row(ref y, 30), filePath, 2048);
                            Label(ref y, sourceLabel, 24f);
                            sourceId = Widgets.TextField(Row(ref y, 30), sourceId, 128);
                            try
                            {
                                GUI.enabled = oldEnabled && !snapshot.Busy && snapshot.State != StoreBrowserState.Shutdown;
                                if (Widgets.ButtonText(Row(ref y, 30), loadLabel)) Load();
                            }
                            finally { GUI.enabled = oldEnabled; }
                        }
                    }
                    if (snapshot.Busy && snapshot.State != StoreBrowserState.Committing && Widgets.ButtonText(Row(ref y, 30), cancelLabel)) controller.Cancel();
                    Label(ref y, searchLabel, 24f);
                    string updated = Widgets.TextField(Row(ref y, 30), search, 128);
                    if (updated != search) { search = updated; dirty = true; }
                    DrawPackages(Row(ref y, listHeight));
                    Label(ref y, detailText, detailHeight);
                    if (snapshot.Plan != null)
                    {
                        try
                        {
                            GUI.enabled = oldEnabled && !snapshot.Busy && snapshot.Repository != null && !snapshot.Repository.Offline && !snapshot.Repository.Stale &&
                                snapshot.Plan.Packages.Any(p => !p.AlreadyInstalled) && snapshot.State != StoreBrowserState.Shutdown;
                            if (Widgets.ButtonText(Row(ref y, 30), checkDownloadsLabel)) CheckDownloads();
                            GUI.enabled = GUI.enabled && snapshot.State != StoreBrowserState.Installed;
                            GUI.enabled = false;
                            Widgets.ButtonText(Row(ref y, 30), T("install"));
                        }
                        finally { GUI.enabled = oldEnabled; }
                    }
                    try
                    {
                        GUI.enabled = oldEnabled && !snapshot.Busy && snapshot.State != StoreBrowserState.Shutdown;
                        if (Widgets.ButtonText(Row(ref y, 30), T("managedRefresh"))) ReadManaged();
                        Label(ref y, T("managedPackages"), 24f);
                        foreach (ManagedPackage item in snapshot.Managed)
                        {
                            Label(ref y, Clean(item.Package.Name) + " · " + item.Package.Version, 24f);
                            if (Widgets.ButtonText(Row(ref y, 30), T("uninstall"))) ConfirmUninstall(item);
                        }
                    }
                    finally { GUI.enabled = oldEnabled; }
                }
                finally { Widgets.EndScrollView(); }
            }
            finally
            {
                Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor;
                Text.WordWrap = oldWrap; GUI.enabled = oldEnabled;
            }
        }

        private static void BoundedLabel(Rect rect, string text, float measuredHeight)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            GUI.BeginGroup(rect);
            try { Widgets.Label(new Rect(0f, 0f, rect.width, measuredHeight), text); }
            finally { GUI.EndGroup(); }
            TooltipHandler.TipRegion(rect, text);
        }

        private Rect Row(ref float y, float height) { var rect = new Rect(0, y, width, Mathf.Max(0, height)); y += height + 4; return rect; }
        private void Label(ref float y, string value, float height) { Widgets.Label(Row(ref y, height), value); }

        private void Load()
        {
            localError = null; dirty = true;
            try
            {
                settings.Set("plugin-store.localCatalogPath", filePath);
                settings.Set("plugin-store.localSourceId", sourceId);
                _ = controller.ReadLocalIndex(filePath, sourceId);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void LoadRepository(bool offline)
        {
            localError = null; dirty = true;
            try
            {
                var endpoint = new RepositoryEndpoint(endpointText, remoteSourceId);
                // Capture game paths only in this UI action, before background IO starts.
                ClientEnvironmentSnapshot captured = environment.Capture();
                if (captured.Paths == null) throw new StoreValidationException("EnvironmentUnavailable", "Game save-data paths are unavailable.");
                string directory = captured.Paths.GetExtensionDataDirectory("phinix.plugin-store");
                settings.Set("plugin-store.repositoryEndpoint", endpoint.Origin);
                settings.Set("plugin-store.repositorySourceId", endpoint.SourceId);
                if (offline) _ = controller.ReadCachedRepository(endpoint, directory);
                else _ = controller.RefreshRepository(endpoint, directory);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Plan()
        {
            localError = null; dirty = true;
            try
            {
                StoreEnvironmentInput input = StoreEnvironmentAdapter.FromSnapshot(environment.Capture());
                _ = controller.CreatePlan(selected.Id, selected.Version.ToString(), input, displayedCatalog);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void CheckDownloads()
        {
            localError = null; dirty = true;
            try
            {
                ClientEnvironmentSnapshot captured = environment.Capture();
                if (captured.Paths == null) throw new StoreValidationException("EnvironmentUnavailable", "Game save-data paths are unavailable.");
                _ = controller.CheckPlanDownloads(snapshot.Plan, captured.Paths);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void ReadManaged()
        {
            localError = null; dirty = true;
            try { _ = controller.ReadManagedPackages(environment.Capture()); }
            catch (Exception ex) { Fail(ex); }
        }

        private void ConfirmInstall()
        {
            InstallPlan reviewed = snapshot.Plan;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T("installConfirm") + "\n" +
                string.Join("\n", reviewed.Packages.Where(p => !p.AlreadyInstalled).Select(p => p.Package.Name + " " + p.Package.Version)), () =>
            {
                localError = null; dirty = true;
                try { _ = controller.InstallPackages(reviewed, environment.Capture()); }
                catch (Exception ex) { Fail(ex); }
            }));
        }

        private void ConfirmUninstall(ManagedPackage item)
        {
            string folder = item.Receipt.Folder;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(T("uninstallConfirm") + "\n" + Clean(item.Package.Name) + " " + item.Package.Version, () =>
            {
                localError = null; dirty = true;
                try { _ = controller.UninstallPackage(folder, environment.Capture()); }
                catch (Exception ex) { Fail(ex); }
            }));
        }

        private void Fail(Exception ex)
        {
            var failure=new RepositoryDiagnostics(line=>log?.Invoke("Plugin store audit: "+line,LogLevel.WARNING)) {Operation="UserAction"}
                .Failure("repository.action_failed","StoreView",ex);
            localError=Clean(("Phinix_store2_"+failure.MessageKey).Translate()+"\n"+failure.Diagnostic);
            dirty = true;
        }

        private void Rebuild(float availableWidth)
        {
            RepositoryBrowseInfo repository = snapshot.Repository;
            bool timedStale = repository != null && (DateTime.UtcNow - repository.CheckedUtc > TimeSpan.FromMinutes(30) || repository.CheckedUtc > DateTime.UtcNow);
            if (timedStale != lastTimedStale) { dirty = true; lastTimedStale = timedStale; }
            bool catalogChanged = !ReferenceEquals(displayedCatalog, snapshot.Catalog);
            bool changedLanguage = !ReferenceEquals(language, LanguageDatabase.activeLanguage);
            if (!dirty && !catalogChanged && !changedLanguage && Mathf.Approximately(width, availableWidth) && revision == snapshot.Revision && cachedSearch == search) return;
            width = availableWidth; language = LanguageDatabase.activeLanguage; revision = snapshot.Revision; cachedSearch = search; dirty = false;
            if (snapshot.Diagnostic != null && loggedRevision != snapshot.Revision)
            { loggedRevision = snapshot.Revision; new RepositoryDiagnostics(line=>log?.Invoke("Plugin store audit: "+line,LogLevel.WARNING)) {Operation="StoreView"}.Event("repository.failure_displayed","StoreView",snapshot.ErrorCode); }
            endpointLabel = T("endpoint"); remoteSourceLabel = T("remoteSource"); refreshLabel = T("refresh"); cacheLabel = T("cache"); localToolsLabel = T("localTools");
            title = T("title"); intro = T("preview"); pathLabel = T("path"); sourceLabel = T("source"); searchLabel = T("search");
            loadLabel = T("load"); cancelLabel = T("cancel"); planLabel = T("plan"); managementLabel = T("management"); sourceSettingsLabel = T("sourceSettings");
            checkDownloadsLabel = T("checkDownloads");
            if (catalogChanged) { displayedCatalog = snapshot.Catalog; selected = null; listScroll = Vector2.zero; if (displayedCatalog != null) showSourceSettings = false; }
            packages = displayedCatalog == null ? new PackageRecord[0] : displayedCatalog.Packages
                .Where(p => search.Length == 0 || p.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.Id, StringComparer.Ordinal).ThenByDescending(p => p.Version).ToArray();
            rowLabels = new string[packages.Length]; rowTips = new string[packages.Length];
            for (int i = 0; i < packages.Length; i++)
            {
                PackageRecord p = packages[i];
                rowTips[i] = Clean(p.Name) + " · " + p.Id + " · " + (p.IsWorkshop ? T("workshop") : p.Version.ToString()) + " · " + p.State;
                rowLabels[i] = Fit(rowTips[i], Mathf.Max(1, width - 20));
            }
            statusText = T("state_" + snapshot.State);
            if (repository != null)
            {
                statusText += "\n" + T(repository.Offline ? "offline" : "online") + " · " + repository.Endpoint.Origin;
                statusText += "\n" + T("checked") + ": " + repository.CheckedUtc.ToLocalTime().ToString("g");
                if (repository.Stale || timedStale) statusText += "\n" + T("stale");
            }

            if (snapshot.Error != null)
            {
                var failure=StoreFailureInfo.FromCode(snapshot.ErrorCode);
                statusText += "\n"+("Phinix_store2_"+failure.MessageKey).Translate()+"\n"+Clean(snapshot.Error);
            }
            if (localError != null) statusText += "\n" + T("operationFailed") + "\n" + localError;
            selectionText = selected == null ? T("select") : T("selected") + ": " +
                Clean(selected.Name) + " " + (selected.IsWorkshop ? T("workshop") : selected.Version.ToString()) +
                "\n" + (selected.IsWorkshop ? T("workshopPending") : selected.State == "active" ? T("selectionAction") : T("inactive"));
            selectionHeight = Mathf.Max(24, Text.CalcHeight(selectionText, width));
            detailText = Details();
            introHeight = Mathf.Max(24, Text.CalcHeight(intro, width));
            statusHeight = Mathf.Max(24, Text.CalcHeight(statusText, width));
            detailHeight = Mathf.Max(48, Text.CalcHeight(detailText, width));
        }

        private void DrawPackages(Rect rect)
        {
            float rowWidth = Mathf.Max(0, rect.width - 16);
            float height = packages.Length * 32f;
            listScroll.y = Mathf.Clamp(listScroll.y, 0, Mathf.Max(0, height - rect.height));
            Widgets.BeginScrollView(rect, ref listScroll, new Rect(0, 0, rowWidth, Mathf.Max(rect.height, height)));
            try
            {
                VirtualListRange range = VirtualListLayout.GetFixedRange(packages.Length, 32, listScroll.y, rect.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    Rect row = new Rect(0, i * 32, rowWidth, 30);
                    if (ReferenceEquals(selected, packages[i])) Widgets.DrawHighlightSelected(row);
                    Widgets.Label(row, rowLabels[i]); TooltipHandler.TipRegion(row, rowTips[i]);
                    if (Widgets.ButtonInvisible(row)) { selected = packages[i]; dirty = true; }
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private string Details()
        {
            var text = new StringBuilder();
            if (displayedCatalog != null)
                text.AppendLine(displayedCatalog.SourceId + " @ " + displayedCatalog.SnapshotId);
            if (selected != null)
            {
                text.AppendLine(Clean(selected.Name));
                text.AppendLine(selected.Id + " · " + selected.RimWorldPackageId);
                text.AppendLine(Clean(selected.Author) + " · " + Clean(selected.License));
                if (selected.IsWorkshop) text.AppendLine(T("workshopPending") + "\n" + selected.WorkshopUrl);
                else
                {
                    text.AppendLine(selected.Version + " · " + selected.Artifact.Repository);
                    text.AppendLine(T("dependencies") + ": " + string.Join(", ", selected.Dependencies.Select(d => d.Id + " " + d.Range.Text)));
                    text.AppendLine("SHA-256: " + selected.Artifact.Sha256);
                }
            }
            if (snapshot.Plan != null)
            {
                InstallPlan plan = snapshot.Plan;
                text.AppendLine(T("planPreview"));
                text.AppendLine(plan.SourceId + " @ " + plan.SnapshotId);
                text.AppendLine("SHA-256: " + plan.CatalogSha256);
                text.AppendLine(T("bytes") + ": " + plan.DownloadBytes);
                foreach (PlannedPackage package in plan.Packages)
                    text.AppendLine(package.Package.Id + " " + package.Package.Version + " · " + (package.AlreadyInstalled ? T("existing") : T("new")) + (package.NeedsEnable ? " · " + T("enable") : ""));
                foreach (ExternalModRequirement mod in plan.ExternalMods) text.AppendLine(T("external") + ": " + mod.PackageId + " " + mod.WorkshopUrl);
                text.AppendLine(T("noInstall"));
            }
            foreach (PayloadValidationReport payload in snapshot.Payloads)
                text.AppendLine(T("payloadVerified") + ": " + payload.Package.Id + " " + payload.Package.Version + " · SHA-256: " + payload.Sha256);
            return text.Length == 0 ? T("select") : text.ToString();
        }

        private static string T(string suffix) { return ("Phinix_store_" + suffix).Translate(); }
        private static string Clean(string text) { return TextHelper.StripRichText(text ?? ""); }
        private static string Fit(string text, float width)
        {
            if (Text.CalcSize(text).x <= width) return text;
            int low = 0, high = text.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Text.CalcSize(text.Substring(0, middle) + "…").x <= width) low = middle; else high = middle - 1;
            }
            if (Text.CalcSize("…").x > width) return "";
            if (low > 0 && char.IsHighSurrogate(text[low - 1])) low--;
            return text.Substring(0, low) + "…";
        }
    }
}
