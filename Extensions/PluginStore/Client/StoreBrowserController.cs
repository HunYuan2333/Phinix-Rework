using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Phinix.PluginStore
{
    internal enum StoreBrowserState { Idle, ReadingIndex, CatalogReady, Planning, PlanReady, CheckingPackages, PayloadsVerified, Installing, Committing, Installed, Managing, Uninstalled, Failed, Cancelled, Shutdown }

    internal sealed class StoreBrowserSnapshot
    {
        public StoreBrowserSnapshot(long revision, StoreBrowserState state, CatalogSnapshot catalog, InstallPlan plan, string errorCode, string error, string diagnostic = null, RepositoryBrowseInfo repository = null,
            System.Collections.Generic.IEnumerable<PayloadValidationReport> payloads = null, System.Collections.Generic.IEnumerable<ManagedPackage> managed = null)
        { Managed = StoreCollections.Freeze(managed); Payloads = StoreCollections.Freeze(payloads); Repository = repository; Revision = revision; State = state; Catalog = catalog; Plan = plan; ErrorCode = errorCode; Error = error; Diagnostic = diagnostic; }
        public System.Collections.ObjectModel.ReadOnlyCollection<PayloadValidationReport> Payloads { get; }
        public System.Collections.ObjectModel.ReadOnlyCollection<ManagedPackage> Managed { get; }
        public RepositoryBrowseInfo Repository { get; }
        public long Revision { get; }
        public StoreBrowserState State { get; }
        public CatalogSnapshot Catalog { get; }
        public InstallPlan Plan { get; }
        public string ErrorCode { get; }
        public string Error { get; }
        public string Diagnostic { get; }
        public bool Busy => State == StoreBrowserState.ReadingIndex || State == StoreBrowserState.Planning || State == StoreBrowserState.CheckingPackages || State == StoreBrowserState.Installing || State == StoreBrowserState.Committing || State == StoreBrowserState.Managing;
    }

    // The terminal state is stored before UI observes it. No dispatcher callback
    // or open window is needed to make a task finish.
    internal sealed class StoreBrowserController : IDisposable
    {
        private readonly object gate = new object();
        private StoreBrowserSnapshot snapshot = new StoreBrowserSnapshot(0, StoreBrowserState.Idle, null, null, null, null);
        private CancellationTokenSource running;
        private long generation;
        private bool disposed;
        private int workerCount;
        private readonly int timeoutMilliseconds;
        private readonly Action<string> repositoryLog;

        internal StoreBrowserController(int timeoutMilliseconds = 10000, Action<string> repositoryLog = null)
        {
            if (timeoutMilliseconds < 1 || timeoutMilliseconds > 10000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            this.timeoutMilliseconds = timeoutMilliseconds; this.repositoryLog = repositoryLog;
        }

        public StoreBrowserSnapshot Snapshot { get { lock (gate) return snapshot; } }

        public Task ReadLocalIndex(string filePath, string expectedSourceId)
        {
            return ReadIndex(token => ReadBounded(filePath, token), expectedSourceId);
        }

        internal Task ReadIndex(Func<CancellationToken, byte[]> read, string expectedSourceId)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            return Run(StoreBrowserState.ReadingIndex, token =>
            {
                byte[] bytes = read(token);
                token.ThrowIfCancellationRequested();
                CatalogSnapshot catalog = CatalogReader.Read(bytes, expectedSourceId);
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new StoreBrowserSnapshot(0, StoreBrowserState.CatalogReady, catalog, null, null, null));
            }, loading: true);
        }

        public Task RefreshRepository(RepositoryEndpoint endpoint, string extensionDataDirectory)
        { return RefreshRepository(endpoint, extensionDataDirectory, null); }

        internal Task RefreshRepository(RepositoryEndpoint endpoint, string extensionDataDirectory, RepositoryTransport transport)
        {
            var cache = new RepositoryCache(extensionDataDirectory, endpoint);
            RepositoryCacheEntry entry = null;
            RepositoryCacheWrite staged = null;
            var audit = new RepositoryDiagnostics(repositoryLog, endpoint.SourceId);
            return Run(StoreBrowserState.ReadingIndex, async token =>
            {
                audit.Event("repository.refresh_started");
                using (var connection = transport ?? new RepositoryTransport(audit))
                {
                    try { entry = await RepositoryBrowser.Refresh(endpoint, cache, connection, token).ConfigureAwait(false); }
                    catch (StoreValidationException ex) { ex.RequestId = ex.RequestId ?? connection.LastRequestId; throw; }
                }
                audit.Event("repository.chain_verified", bytes: entry.CatalogBytes.Length);
                staged = cache.Stage(entry, token); audit.Event("repository.cache_staged");
                return new StoreBrowserSnapshot(0, StoreBrowserState.CatalogReady, entry.Catalog, null, null, null,
                    repository: new RepositoryBrowseInfo(endpoint, entry.CheckedUtc, false, false));
            }, token => { staged.Commit(token); audit.Event("repository.cache_committed"); }, endpoint, true,
                () => { try { staged?.Dispose(); audit.Event("repository.cleanup_complete"); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    { audit.Event("repository.cleanup_failed", reason: "CacheCleanupFailed"); } }, audit);
        }

        public Task ReadCachedRepository(RepositoryEndpoint endpoint, string extensionDataDirectory)
        {
            var cache = new RepositoryCache(extensionDataDirectory, endpoint);
            return Run(StoreBrowserState.ReadingIndex, token =>
            {
                var entry = cache.TryRead(token);
                if (entry == null) throw new StoreValidationException("CacheUnavailable", "No valid browsing cache exists for this endpoint/source. Refresh online first.");
                bool stale = DateTime.UtcNow - entry.CheckedUtc > TimeSpan.FromMinutes(30) || entry.CheckedUtc > DateTime.UtcNow;
                return Task.FromResult(new StoreBrowserSnapshot(0, StoreBrowserState.CatalogReady, entry.Catalog, null, null, null,
                    repository: new RepositoryBrowseInfo(endpoint, entry.CheckedUtc, true, stale)));
            }, endpoint: endpoint, loading: true);
        }

        public Task CreatePlan(string packageId, string version, StoreEnvironmentInput input, CatalogSnapshot expectedCatalog)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            CatalogSnapshot catalog;
            lock (gate)
            {
                catalog = snapshot.Catalog;
                if (catalog == null) throw new InvalidOperationException("Load a valid index before planning.");
                if (!ReferenceEquals(catalog, expectedCatalog)) throw new StoreValidationException("SnapshotChanged", "Index changed; review the selected package again.");
                RepositoryBrowseInfo repository = snapshot.Repository;
                return Run(StoreBrowserState.Planning, token =>
                {
                    if (Directory.Exists(input.Environment.Paths.LocalModsRoot)) using (var installer = new ManagedInstallation(input.Environment, readOnly: true))
                    {
                        input = installer.PlanningInput(catalog, token);
                    }
                    InstallPlan plan = new DependencyPlanner(catalog, input.Runtime, input.Installed).CreatePlan(packageId, version, token);
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(new StoreBrowserSnapshot(0, StoreBrowserState.PlanReady, catalog, plan, null, null, repository: repository));
                });
            }
        }

        public Task CheckPlanDownloads(InstallPlan expectedPlan, PhinixClient.Framework.ClientEnvironmentPaths paths)
        { return CheckPlanDownloads(expectedPlan, paths, null); }

        internal Task CheckPlanDownloads(InstallPlan expectedPlan, PhinixClient.Framework.ClientEnvironmentPaths paths,
            RepositoryTransport transport, PackageDownloadBudget budget = null)
        {
            lock (gate)
            {
                if (expectedPlan == null || !ReferenceEquals(snapshot.Plan, expectedPlan) || snapshot.Catalog == null)
                    throw new StoreValidationException("SnapshotChanged", "Review the current dependency plan before downloading.");
                RepositoryBrowseInfo repository = snapshot.Repository;
                if (repository == null || repository.Offline || repository.Stale || DateTime.UtcNow - repository.CheckedUtc > TimeSpan.FromMinutes(30) || repository.CheckedUtc > DateTime.UtcNow)
                    throw new StoreValidationException("OnlineSnapshotRequired", "Refresh the online index and create a new plan first.");
                CatalogSnapshot catalog = snapshot.Catalog;
                var packages = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(expectedPlan.Packages, p => !p.AlreadyInstalled));
                if (packages.Length == 0) throw new StoreValidationException("NoPackagesToDownload", "This plan has no new package downloads.");
                foreach (var package in packages)
                    if (package.Package.Artifact.PayloadKind != "rimworld-mod-zip")
                        throw new StoreValidationException("UnsupportedPayloadDownload", "This preview downloads and checks ZIP packages only.");
                var audit = new RepositoryDiagnostics(repositoryLog, repository.Endpoint.SourceId);
                return Run(StoreBrowserState.CheckingPackages, async token =>
                {
                    var reports = new System.Collections.Generic.List<PayloadValidationReport>();
                    audit.Event("package.plan_check_started");
                    using (var connection = transport ?? new RepositoryTransport(audit))
                        foreach (var package in packages)
                            using (var download = await connection.DownloadPackage(repository.Endpoint, catalog, package.Package, paths, token, budget).ConfigureAwait(false))
                                reports.Add(download.Report);
                    token.ThrowIfCancellationRequested(); audit.Event("package.plan_checked");
                    return new StoreBrowserSnapshot(0, StoreBrowserState.PayloadsVerified, catalog, expectedPlan, null, null,
                        repository: repository, payloads: reports);
                }, audit: audit, operationTimeout: 180000);
            }
        }

        public Task ReadManagedPackages(PhinixClient.Framework.ClientEnvironmentSnapshot environment)
        {
            var audit = new RepositoryDiagnostics(repositoryLog);
            return Run(StoreBrowserState.Managing, token =>
            {
                using (var installer = new ManagedInstallation(environment, audit))
                {
                    installer.Recover(token);
                    return Task.FromResult(new StoreBrowserSnapshot(0, StoreBrowserState.CatalogReady, Snapshot.Catalog, null, null, null,
                        repository: Snapshot.Repository, managed: installer.List(token)));
                }
            }, audit: audit, operationTimeout: 180000);
        }

        public Task InstallPackages(InstallPlan expectedPlan, PhinixClient.Framework.ClientEnvironmentSnapshot environment,
            RepositoryTransport transport = null)
        {
            StoreBrowserSnapshot reviewed;
            lock (gate)
            {
                reviewed = snapshot;
                if (expectedPlan == null || !ReferenceEquals(reviewed.Plan, expectedPlan))
                    throw new StoreValidationException("SnapshotChanged", "Review the current plan before installation.");
                if (reviewed.Repository == null || reviewed.Repository.Offline || reviewed.Repository.Stale)
                    throw new StoreValidationException("OnlineSnapshotRequired", "Refresh online and review the plan before installation.");
            }
            var endpoint = reviewed.Repository.Endpoint;
            var audit = new RepositoryDiagnostics(repositoryLog, endpoint.SourceId);
            ManagedInstallation installer = null;
            return Run(StoreBrowserState.Installing, async token =>
            {
                installer = new ManagedInstallation(environment, audit);
                var existing = installer.List(token);
                var cache = new RepositoryCache(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"), endpoint);
                using (var connection = transport ?? new RepositoryTransport(audit))
                {
                    var entry = await RepositoryBrowser.Refresh(endpoint, cache, connection, token).ConfigureAwait(false);
                    CheckFreshPlan(expectedPlan, entry.Catalog);
                    StoreEnvironmentInput facts = installer.PlanningInput(entry.Catalog, token);
                    var root = expectedPlan.Packages[expectedPlan.Packages.Count - 1].Package;
                    InstallPlan current = new DependencyPlanner(entry.Catalog, facts.Runtime, facts.Installed).CreatePlan(root.Id, root.Version.ToString(), token);
                    if (!System.Linq.Enumerable.SequenceEqual(System.Linq.Enumerable.Select(current.Packages, p => p.Package.Id + "@" + p.Package.Version + ":" + p.AlreadyInstalled),
                        System.Linq.Enumerable.Select(expectedPlan.Packages, p => p.Package.Id + "@" + p.Package.Version + ":" + p.AlreadyInstalled)))
                        throw new StoreValidationException("LocalStateChanged", "Local packages changed; create a new plan.");
                    installer.BeginInstall(expectedPlan, entry.CatalogBytes, endpoint, token);
                    var reports = new System.Collections.Generic.List<PayloadValidationReport>();
                    foreach (var package in current.Packages)
                        if (!package.AlreadyInstalled)
                            using (var download = await connection.DownloadPackage(endpoint, entry.Catalog, package.Package, environment.Paths, token).ConfigureAwait(false))
                            { installer.Stage(download, token); reports.Add(download.Report); }
                    // Check stable again immediately before logging commit intent. A
                    // changed snapshot is deliberately conservative: re-review it.
                    var final = await RepositoryBrowser.Refresh(endpoint, cache, connection, token).ConfigureAwait(false);
                    CheckFreshPlan(expectedPlan, final.Catalog);
                    installer.Seal(token);
                    audit.Event("install.freshness_verified");
                    return new StoreBrowserSnapshot(0, StoreBrowserState.Installed, entry.Catalog, null, null, null,
                        repository: new RepositoryBrowseInfo(endpoint, final.CheckedUtc, false, false), payloads: reports,
                        managed: System.Linq.Enumerable.Concat(existing, installer.PreparedPackages));
                }
            }, token => installer.Commit(token), cleanup: () => installer?.Dispose(), audit: audit,
                operationTimeout: 180000, criticalCommit: true);
        }

        public Task UninstallPackage(string folder, PhinixClient.Framework.ClientEnvironmentSnapshot environment)
        {
            var audit = new RepositoryDiagnostics(repositoryLog);
            ManagedInstallation installer = null;
            StoreBrowserSnapshot before = Snapshot;
            return Run(StoreBrowserState.Managing, token =>
            {
                installer = new ManagedInstallation(environment, audit);
                var all = installer.List(token);
                installer.ValidateUninstall(folder, token);
                return Task.FromResult(new StoreBrowserSnapshot(0, StoreBrowserState.Uninstalled, before.Catalog, null, null, null,
                    repository: before.Repository, managed: System.Linq.Enumerable.Where(all, p => p.Receipt.Folder != folder)));
            }, token => installer.Uninstall(folder, token), cleanup: () => installer?.Dispose(), audit: audit,
                operationTimeout: 180000, criticalCommit: true);
        }

        private static void CheckFreshPlan(InstallPlan plan, CatalogSnapshot current)
        {
            if (plan.SourceId != current.SourceId || plan.SnapshotId != current.SnapshotId || plan.CatalogSha256 != current.Sha256)
                throw new StoreValidationException("SnapshotChanged", "The online index changed; refresh and review a new plan.");
            foreach (var item in plan.Packages)
                if (!System.Linq.Enumerable.Any(current.Packages, p => p.Id == item.Package.Id && p.Version.CompareTo(item.Package.Version) == 0 &&
                    p.State == "active" && p.Artifact.Sha256 == item.Package.Artifact.Sha256 && CatalogReader.SameManifest(p, item.Package)))
                    throw new StoreValidationException("PackageWithdrawn", "A planned package is no longer active or unchanged.");
        }

        private Task Run(StoreBrowserState state, Func<CancellationToken, Task<StoreBrowserSnapshot>> work,
            Action<CancellationToken> commit = null, RepositoryEndpoint endpoint = null, bool loading = false, Action cleanup = null, RepositoryDiagnostics audit = null, int? operationTimeout = null, bool criticalCommit = false)
        {
            CancellationTokenSource source;
            long ticket;
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(StoreBrowserController));
                if (snapshot.Busy) throw new InvalidOperationException("A store operation is already running.");
                if (workerCount >= 2) throw new StoreValidationException("PendingWorkers", "Cancelled operations are still finishing; wait before starting another operation.");
                workerCount++;
                source = new CancellationTokenSource(); running = source; ticket = ++generation;
                bool changedScope = loading && snapshot.Repository?.Endpoint.CacheKey != endpoint?.CacheKey;
                Publish(state, changedScope ? null : snapshot.Catalog, null, null, null,
                    repository: changedScope ? null : snapshot.Repository);
            }
            // A watchdog records timeout even if local filesystem IO is stalled.
            // Late work cannot change the terminal state or a newer operation.
            return RunWorker(ticket, source, work, commit, cleanup, audit, operationTimeout ?? timeoutMilliseconds, criticalCommit, state);
        }

        private async Task RunWorker(long ticket, CancellationTokenSource source, Func<CancellationToken, Task<StoreBrowserSnapshot>> work, Action<CancellationToken> commit, Action cleanup, RepositoryDiagnostics audit, int timeout, bool criticalCommit, StoreBrowserState operation)
        {
            Task<StoreBrowserSnapshot> worker = Task.Run(() => work(source.Token));
            Task deadline = Task.Delay(timeout, source.Token);
            try
            {
                Task winner = await Task.WhenAny(worker, deadline).ConfigureAwait(false);
                if (winner != worker)
                {
                    lock (gate)
                    {
                        if (!disposed && generation == ticket)
                        {
                            ++generation; running = null;
                            Publish(StoreBrowserState.Failed, snapshot.Catalog, null, "TaskTimeout", "Store operation exceeded " + timeout + " ms.");
                            source.Cancel(); audit?.Event("repository.task_timeout", reason: "TaskTimeout");
                        }
                    }
                    // Observe eventual exceptions and release its cancellation source.
                    await worker.ConfigureAwait(false);
                    return;
                }
                StoreBrowserSnapshot result = await worker.ConfigureAwait(false);
                bool committing = false;
                lock (gate)
                    if (!disposed && generation == ticket)
                    {
                        source.Token.ThrowIfCancellationRequested();
                        if (criticalCommit)
                        {
                            committing = true;
                            Publish(StoreBrowserState.Committing, result.Catalog, result.Plan, null, null, repository: result.Repository);
                        }
                        else
                        {
                            commit?.Invoke(source.Token); running = null;
                            Publish(result.State, result.Catalog, result.Plan, null, null, repository: result.Repository, payloads: result.Payloads, managed: (operation == StoreBrowserState.Managing || operation == StoreBrowserState.Installing) ? result.Managed : snapshot.Managed);
                        }
                    }
                    else audit?.Event("repository.task_discarded", reason: "GenerationChanged");
                if (committing)
                {
                    // Keep disk IO off the GUI lock. Cancel no longer changes the
                    // outcome once directory commit begins; shutdown still allows
                    // the durable transaction to finish without touching game APIs.
                    commit?.Invoke(CancellationToken.None);
                    lock (gate) if (!disposed && generation == ticket)
                    {
                        running = null;
                        Publish(result.State, result.Catalog, result.Plan, null, null, repository: result.Repository, payloads: result.Payloads, managed: (operation == StoreBrowserState.Managing || operation == StoreBrowserState.Installing) ? result.Managed : snapshot.Managed);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                audit?.Event("repository.task_cancelled");
                lock (gate)
                    if (!disposed && generation == ticket)
                    { running = null; Publish(StoreBrowserState.Cancelled, snapshot.Catalog, null, null, null); }
            }
            catch (Exception ex)
            {
                var validation = ex as StoreValidationException;
                if (validation == null && (operation == StoreBrowserState.Installing || operation == StoreBrowserState.Managing) &&
                    (ex is IOException || ex is UnauthorizedAccessException))
                    validation = new StoreValidationException("InstallationStorageFailed", "Filesystem operation failed. Use refresh/recovery before retrying; ownership records and uncertain files were preserved.", ex);
                audit?.Event("repository.task_failed", reason: validation?.Code ?? "StoreOperationFailed", requestId: validation?.RequestId);
                lock (gate)
                    if (!disposed && generation == ticket)
                    { running = null; Publish(StoreBrowserState.Failed, snapshot.Catalog, null, validation?.Code ?? "StoreOperationFailed", (validation?.Message ?? ex.Message) + (validation?.RequestId == null ? "" : "\nRequest ID: " + validation.RequestId),
                        validation?.RequestId == null ? ex.ToString() : validation.Code + " requestId=" + validation.RequestId); }
            }
            finally
            {
                try { cleanup?.Invoke(); }
                catch (Exception ex)
                {
                    var validation = ex as StoreValidationException;
                    audit?.Event("install.cleanup_failed", reason: validation?.Code ?? "InstallationCleanupFailed");
                    lock (gate) if (!disposed && generation == ticket)
                        Publish(snapshot.State, snapshot.Catalog, snapshot.Plan, "InstallationCleanupRequired",
                            "Temporary cleanup failed; unknown files were preserved. Inspect the installation audit transaction before retrying.", repository: snapshot.Repository);
                }
                finally
                {
                    source.Cancel(); source.Dispose();
                    lock (gate) workerCount--;
                }
            }
        }

        public void Cancel()
        {
            lock (gate)
            {
                if (disposed || !snapshot.Busy || snapshot.State == StoreBrowserState.Committing) return;
                ++generation;
                running?.Cancel(); running = null;
                Publish(StoreBrowserState.Cancelled, snapshot.Catalog, null, null, null);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true; ++generation;
                running?.Cancel(); running = null;
                Publish(StoreBrowserState.Shutdown, null, null, null, null);
            }
        }

        private void Publish(StoreBrowserState state, CatalogSnapshot catalog, InstallPlan plan, string errorCode, string error, string diagnostic = null, RepositoryBrowseInfo repository = null,
            System.Collections.Generic.IEnumerable<PayloadValidationReport> payloads = null, System.Collections.Generic.IEnumerable<ManagedPackage> managed = null)
        {
            if (state == StoreBrowserState.Failed || state == StoreBrowserState.Cancelled)
                repository = snapshot.State == StoreBrowserState.ReadingIndex ? snapshot.Repository?.AsStale() : snapshot.Repository;
            snapshot = new StoreBrowserSnapshot(snapshot.Revision + 1, state, catalog, plan, errorCode, error, diagnostic, repository, payloads, managed ?? snapshot.Managed);
        }

        private static byte[] ReadBounded(string filePath, CancellationToken token)
        {
            if (string.IsNullOrEmpty(filePath) || filePath.Length > 2048 || !Path.IsPathRooted(filePath))
                throw new StoreValidationException("InvalidIndexPath", "Select an absolute local JSON path.");
            if (filePath.StartsWith("\\\\", StringComparison.Ordinal) || filePath.StartsWith("//", StringComparison.Ordinal))
                throw new StoreValidationException("InvalidIndexPath", "Network paths are not supported by the local preview.");
            try { filePath = PhinixClient.Framework.ClientEnvironmentPaths.NormalizeAbsolute(filePath); }
            catch (ArgumentException ex) { throw new StoreValidationException("InvalidIndexPath", "Select a fully qualified local JSON path.", ex); }
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var result = new MemoryStream())
            {
                if (stream.Length > CatalogReader.MaxCatalogBytes) throw new StoreValidationException("DocumentLimit", "Local index exceeds 2 MiB.");
                byte[] buffer = new byte[8192];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (result.Length + read > CatalogReader.MaxCatalogBytes) throw new StoreValidationException("DocumentLimit", "Local index exceeds 2 MiB.");
                    result.Write(buffer, 0, read);
                }
                return result.ToArray();
            }
        }
    }
}
