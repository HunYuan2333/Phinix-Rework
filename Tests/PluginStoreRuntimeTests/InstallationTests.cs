using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;

internal static partial class Program
{
    private sealed class InstallationHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> respond;
        internal InstallationHandler(Func<System.Net.Http.HttpRequestMessage, CancellationToken, Task<System.Net.Http.HttpResponseMessage>> respond) { this.respond = respond; }
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            bool package = request.RequestUri.AbsolutePath.EndsWith("/package");
            Assert(request.Headers.Accept.Single().MediaType == (package ? "application/octet-stream" : "application/json"), "Install retains metadata and payload header boundaries.");
            var response = await respond(request, token); if (response.RequestMessage == null) response.RequestMessage = request; return response;
        }
    }
    private sealed class InstallFixture
    {
        internal Dictionary<string, object> Record;
        internal byte[] Zip;
        internal PackageRecord Package;
    }
    private static InstallFixture InstallBytes(string id, string assembly, params Dictionary<string, object>[] dependencies)
    {
        var record = Package(id, "1.0.0", dependencies);
        var identity = Assembly(assembly); identity["version"] = "1.2.3.4"; record["assemblies"] = new[] { identity };
        byte[] manifest = PayloadManifest(record);
        byte[] dll = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fixture.Plugin.dll"));
        if (assembly != "Fixture.Plugin") dll = PatchAssemblyName(dll, assembly);
        byte[] zip = Zip(new List<Tuple<string, byte[]>> {
            Tuple.Create("About/About.xml", EncodingBytes("<ModMetaData><packageId>test." + id + "</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>")),
            Tuple.Create("phinix-package.json", manifest), Tuple.Create("Assemblies/" + assembly + ".dll", dll), Tuple.Create("Defs/Example.xml", EncodingBytes("<Defs/>")) });
        return new InstallFixture { Record = record, Zip = zip, Package = LockPayload(record, manifest, zip) };
    }
    private static ClientEnvironmentSnapshot InstallEnvironment(string root, IEnumerable<ClientInstalledModSnapshot> mods = null,
        IEnumerable<ClientLoadedAssemblySnapshot> assemblies = null)
    {
        var paths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData"));
        Directory.CreateDirectory(paths.LocalModsRoot);
        return new ClientEnvironmentSnapshot(paths, Path.Combine(paths.LocalModsRoot, "host"), "1.6", "0.9.7", "1.3.0", mods, assemblies, null, null);
    }
    private static async Task StageFixture(ManagedInstallation installer, InstallFixture data, CatalogSnapshot catalog,
        RepositoryEndpoint endpoint, ClientEnvironmentPaths paths)
    {
        using (var transport = new RepositoryTransport(new DownloadHandler((r, t) => Task.FromResult(PackageResponse(data.Zip)))))
        using (var download = await transport.DownloadPackage(endpoint, catalog, catalog.Packages.Single(p => p.Id == data.Package.Id), paths, CancellationToken.None))
            installer.Stage(download, CancellationToken.None);
    }
    private static InstallPlan FixturePlan(CatalogSnapshot catalog) => new InstallPlan(catalog, catalog.Packages.Select(p => new PlannedPackage(p, false, false)), null);
    private static string InstalledTarget(ClientEnvironmentSnapshot env, string id) => Path.Combine(env.Paths.LocalModsRoot, ManagedInstallation.FolderName(Source, id));
    private static async Task Installations()
    {
        var endpoint = new RepositoryEndpoint("https://repo.example.test", Source);
        InstallFixture a = InstallBytes("first", "Fixture.Plugin");
        InstallFixture b = InstallBytes("second", "Fixture.Plugi2", Dep("first", "1.0.0"));
        string root = Path.Combine(Path.GetTempPath(), "PhinixInstallTests", Guid.NewGuid().ToString("N"));
        var env = InstallEnvironment(root); byte[] bytes = Catalog(a.Record, b.Record); var catalog = CatalogReader.Read(bytes, Source);
        var logs = new List<string>();
        try
        {
            using (var installer = new ManagedInstallation(env, new RepositoryDiagnostics(logs.Add, Source)))
            {
                installer.BeginInstall(FixturePlan(catalog), bytes, endpoint, CancellationToken.None);
                await StageFixture(installer, a, catalog, endpoint, env.Paths); await StageFixture(installer, b, catalog, endpoint, env.Paths);
                Assert(!Directory.Exists(InstalledTarget(env, a.Package.Id)), "All dependencies stage outside scanned Mods before any commit.");
                installer.Seal(CancellationToken.None); installer.Commit(CancellationToken.None);
                var owned = installer.List(CancellationToken.None);
                Assert(owned.Count == 2 && owned.All(p => File.Exists(Path.Combine(p.Target, "About", "About.xml"))), "Complete dependency directories and ownership records are installed.");
                var planned = installer.PlanningInput(catalog, CancellationToken.None);
                Assert(planned.Installed.Count == 2 && planned.Installed.All(p => p.VerifiedRecord != null), "Installed byte verification supplies planner records even before RimWorld refreshes its mod list.");
                var plan = new DependencyPlanner(catalog, planned.Runtime, planned.Installed).CreatePlan("second", "1.0.0");
                Assert(plan.Packages.All(p => p.AlreadyInstalled && p.NeedsEnable), "Repeat planning recognizes the owned closure and requires explicit enable/restart.");
                Expect("PackageStillRequired", () => installer.Uninstall(ManagedInstallation.FolderName(Source, "first"), CancellationToken.None));
                installer.Uninstall(ManagedInstallation.FolderName(Source, "second"), CancellationToken.None);
                Assert(!Directory.Exists(InstalledTarget(env, "second")) && installer.List(CancellationToken.None).Count == 1, "Dependent uninstall removes only its owned package.");
            }
            string target = InstalledTarget(env, "first");
            var enabled = InstallEnvironment(root, new[] { new ClientInstalledModSnapshot("test.first", target, true) });
            using (var installer = new ManagedInstallation(enabled)) Expect("PackageInUse", () => installer.Uninstall(ManagedInstallation.FolderName(Source, "first"), CancellationToken.None));
            var loaded = InstallEnvironment(root, null, new[] { new ClientLoadedAssemblySnapshot("Fixture.Plugin", "1.2.3.4", target) });
            using (var installer = new ManagedInstallation(loaded)) Expect("PackageInUse", () => installer.Uninstall(ManagedInstallation.FolderName(Source, "first"), CancellationToken.None));
            File.WriteAllText(Path.Combine(target, "user-added.txt"), "preserve");
            using (var installer = new ManagedInstallation(env)) Expect("OwnedPackageModified", () => installer.Uninstall(ManagedInstallation.FolderName(Source, "first"), CancellationToken.None));
            Assert(File.Exists(Path.Combine(target, "user-added.txt")), "User additions block deletion and remain untouched.");
            File.Delete(Path.Combine(target, "user-added.txt"));
            Directory.CreateDirectory(Path.Combine(target, "user-empty-directory"));
            using (var installer = new ManagedInstallation(env)) Expect("OwnedPackageModified", () => installer.List(CancellationToken.None));
            Directory.Delete(Path.Combine(target, "user-empty-directory"));
            string resource = Path.Combine(target, "Defs", "Example.xml"); byte[] original = File.ReadAllBytes(resource); File.WriteAllText(resource, "changed");
            using (var installer = new ManagedInstallation(env)) Expect("OwnedPackageModified", () => installer.List(CancellationToken.None));
            File.WriteAllBytes(resource, original);
            string settings = Path.Combine(env.Paths.GetExtensionDataDirectory("first.main"), "settings.txt"); Directory.CreateDirectory(Path.GetDirectoryName(settings)); File.WriteAllText(settings, "keep");
            using (var installer = new ManagedInstallation(env)) installer.Uninstall(ManagedInstallation.FolderName(Source, "first"), CancellationToken.None);
            Assert(File.Exists(settings) && !Directory.Exists(target), "Uninstall preserves plugin settings and save data.");
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name == "Fixture.Plugin"), "Installation never loads or executes candidate code.");
            Assert(logs.Any(x => x.Contains("install.journal_prepared")) && logs.Any(x => x.Contains("install.package_committed")) && logs.All(x => !x.Contains(root)), "Installation audit has stages and excludes local paths.");
            await InterruptedInstalls(endpoint, a, b);
            await InstallationController(endpoint, a);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task InterruptedInstalls(RepositoryEndpoint endpoint, InstallFixture a, InstallFixture b)
    {
        foreach (string failure in new[] { "write", "staged", "journal", "moved", "recorded", "quarantined", "deleted" })
        {
            string root = Path.Combine(Path.GetTempPath(), "PhinixInstallTests", Guid.NewGuid().ToString("N"));
            var env = InstallEnvironment(root); byte[] bytes = Catalog(a.Record, b.Record); var catalog = CatalogReader.Read(bytes, Source);
            try
            {
                bool injected = false;
                using (var installer = new ManagedInstallation(env))
                {
                    installer.FaultPoint = point => { if (point == failure && !injected) { injected = true; throw new IOException("simulated process interruption"); } };
                    try
                    {
                        installer.BeginInstall(FixturePlan(catalog), bytes, endpoint, CancellationToken.None);
                        await StageFixture(installer, a, catalog, endpoint, env.Paths); await StageFixture(installer, b, catalog, endpoint, env.Paths);
                        installer.Seal(CancellationToken.None); installer.Commit(CancellationToken.None);
                        if (failure == "quarantined" || failure == "deleted") installer.Uninstall(ManagedInstallation.FolderName(Source, "second"), CancellationToken.None);
                    }
                    catch (IOException) { Assert(injected, "Expected injected transaction interruption at " + failure); }
                }
                using (var installer = new ManagedInstallation(env))
                {
                    installer.Recover(CancellationToken.None);
                    int expected = failure == "recorded" ? 2 : (failure == "quarantined" || failure == "deleted") ? 1 : 0;
                    Assert(installer.List(CancellationToken.None).Count == expected, "Recovery reconciles " + failure + " without claiming a partial install.");
                    installer.Recover(CancellationToken.None);
                    Assert(installer.List(CancellationToken.None).Count == expected, "Recovery is idempotent at " + failure);
                }
            }
            finally { Directory.Delete(root, true); }
        }
        {
            string root = Path.Combine(Path.GetTempPath(), "PhinixInstallTests", Guid.NewGuid().ToString("N")); var env = InstallEnvironment(root);
            byte[] bytes = Catalog(a.Record, b.Record); var catalog = CatalogReader.Read(bytes, Source);
            try
            {
                using (var installer = new ManagedInstallation(env))
                {
                    installer.BeginInstall(FixturePlan(catalog), bytes, endpoint, CancellationToken.None);
                    await StageFixture(installer, a, catalog, endpoint, env.Paths); await StageFixture(installer, b, catalog, endpoint, env.Paths); installer.Seal(CancellationToken.None);
                    installer.FaultPoint = p => { if (p == "moved") throw new IOException("interrupted move"); };
                    try { installer.Commit(CancellationToken.None); } catch (IOException) { }
                }
                using (var installer = new ManagedInstallation(env))
                {
                    installer.FaultPoint = p => { if (p == "recoverydeleted") throw new IOException("interrupted recovery"); };
                    try { installer.Recover(CancellationToken.None); throw new Exception("Expected recovery interruption."); }
                    catch (IOException) { assertions++; }
                }
                using (var installer = new ManagedInstallation(env))
                {
                    installer.Recover(CancellationToken.None);
                    Assert(installer.List(CancellationToken.None).Count == 0, "A second process interruption during rollback resumes only verified remaining owned bytes.");
                }
            }
            finally { Directory.Delete(root, true); }
        }
        // A modified target after interrupted commit must remain intact.
        {
            string root = Path.Combine(Path.GetTempPath(), "PhinixInstallTests", Guid.NewGuid().ToString("N")); var env = InstallEnvironment(root);
            byte[] bytes = Catalog(a.Record, b.Record); var catalog = CatalogReader.Read(bytes, Source);
            try
            {
                using (var installer = new ManagedInstallation(env))
                {
                    installer.BeginInstall(FixturePlan(catalog), bytes, endpoint, CancellationToken.None);
                    await StageFixture(installer, a, catalog, endpoint, env.Paths); await StageFixture(installer, b, catalog, endpoint, env.Paths); installer.Seal(CancellationToken.None);
                    installer.FaultPoint = p => { if (p == "moved") throw new IOException("interrupted"); };
                    try { installer.Commit(CancellationToken.None); } catch (IOException) { }
                }
                string changed = Path.Combine(InstalledTarget(env, "first"), "Defs", "Example.xml"); File.WriteAllText(changed, "edited");
                using (var installer = new ManagedInstallation(env)) Expect("OwnedPackageModified", () => installer.Recover(CancellationToken.None));
                Assert(File.ReadAllText(changed) == "edited", "Uncertain crash recovery preserves edited files and its journal.");
            }
            finally { Directory.Delete(root, true); }
        }
        // Malformed journal and symlinked Roots block writes.
        {
            string root = Path.Combine(Path.GetTempPath(), "PhinixInstallTests", Guid.NewGuid().ToString("N")); var env = InstallEnvironment(root);
            try
            {
                using (var installer = new ManagedInstallation(env)) { }
                string journalDir = Path.Combine(env.Paths.GetExtensionDataDirectory("phinix.plugin-store"), "installation-v1", "journals");
                File.WriteAllText(Path.Combine(journalDir, new string('a', 32) + ".json"), "{");
                using (var installer = new ManagedInstallation(env)) Expect("InvalidInstallationRecord", () => installer.Recover(CancellationToken.None));
                Assert(Directory.GetDirectories(env.Paths.LocalModsRoot).Length == 0, "Corrupt journal cannot initiate package deletion.");
                File.Delete(Path.Combine(journalDir, new string('a', 32) + ".json"));
                using (var installer = new ManagedInstallation(env))
                { Expect("InstallationBusy", () => { using (var duplicate = new ManagedInstallation(env)) { } }); }
                if (Path.DirectorySeparatorChar != '\\')
                {
                    string outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
                    Directory.CreateSymbolicLink(Path.Combine(env.Paths.LocalModsRoot, ManagedInstallation.FolderName(Source, "first")), outside);
                    var catalog = CatalogReader.Read(Catalog(a.Record), Source);
                    using (var installer = new ManagedInstallation(env)) Expect("UnsafeInstallationPath", () => installer.BeginInstall(FixturePlan(catalog), Catalog(a.Record), endpoint, CancellationToken.None));
                    Assert(Directory.GetFiles(outside).Length == 0, "Symlink target stays untouched.");
                }
            }
            finally { Directory.Delete(root, true); }
        }
    }

    private static async Task InstallationController(RepositoryEndpoint endpoint, InstallFixture fixture)
    {
        string root = Path.Combine(Path.GetTempPath(), "PhinixInstallTests", Guid.NewGuid().ToString("N")); var env = InstallEnvironment(root);
        var data = RepositoryFixture(fixture.Package.Id, packageChange: p => { p.Clear(); foreach (var pair in fixture.Record) p.Add(pair.Key, pair.Value); });
        try
        {
            using (var browser = new StoreBrowserController())
            {
                await browser.RefreshRepository(endpoint, env.Paths.GetExtensionDataDirectory("phinix.plugin-store"), FixtureRepositoryTransport(endpoint, data));
                await browser.CreatePlan(fixture.Package.Id, "1.0.0", StoreEnvironmentAdapter.FromSnapshot(env), browser.Snapshot.Catalog);
                int stableReads = 0;
                using (var transport = new RepositoryTransport(new InstallationHandler((r, t) =>
                { if (r.RequestUri == endpoint.Stable) stableReads++; if (r.RequestUri.AbsolutePath.EndsWith("/package")) return Task.FromResult(PackageResponse(fixture.Zip)); return FixtureReply(endpoint, data, r); })))
                    await browser.InstallPackages(browser.Snapshot.Plan, env, transport);
                Assert(browser.Snapshot.State == StoreBrowserState.Installed && browser.Snapshot.Managed.Count == 1 && stableReads == 2, "UI install performs fresh checks both before downloading and before commit. " + browser.Snapshot.State + " " + browser.Snapshot.ErrorCode + " " + browser.Snapshot.Diagnostic);
                await browser.UninstallPackage(browser.Snapshot.Managed[0].Receipt.Folder, env);
                Assert(browser.Snapshot.State == StoreBrowserState.Uninstalled && browser.Snapshot.Managed.Count == 0, "UI uninstall has an explicit terminal outcome.");
                await browser.ReadCachedRepository(endpoint, env.Paths.GetExtensionDataDirectory("phinix.plugin-store"));
                await browser.CreatePlan(fixture.Package.Id, "1.0.0", StoreEnvironmentAdapter.FromSnapshot(env), browser.Snapshot.Catalog);
                Expect("OnlineSnapshotRequired", () => browser.InstallPackages(browser.Snapshot.Plan, env));
            }
            using (var browser = new StoreBrowserController())
            {
                await browser.RefreshRepository(endpoint, env.Paths.GetExtensionDataDirectory("phinix.plugin-store"), FixtureRepositoryTransport(endpoint, data));
                await browser.CreatePlan(fixture.Package.Id, "1.0.0", StoreEnvironmentAdapter.FromSnapshot(env), browser.Snapshot.Catalog);
                var withdrawn = RepositoryFixture(fixture.Package.Id, revision: new string('6', 40), packageChange: p => { p.Clear(); foreach (var pair in fixture.Record) p.Add(pair.Key, pair.Value); p["state"] = "withdrawn"; });
                int reads = 0;
                using (var transport = new RepositoryTransport(new InstallationHandler((r, t) =>
                {
                    if (r.RequestUri == endpoint.Stable) reads++;
                    if (r.RequestUri.AbsolutePath.EndsWith("/package")) return Task.FromResult(PackageResponse(fixture.Zip));
                    return FixtureReply(endpoint, reads < 2 ? data : withdrawn, r);
                }))) await browser.InstallPackages(browser.Snapshot.Plan, env, transport);
                Assert(browser.Snapshot.State == StoreBrowserState.Failed && browser.Snapshot.ErrorCode == "SnapshotChanged" && !Directory.Exists(InstalledTarget(env, fixture.Package.Id)), "Withdrawal between download and commit prevents visible installation.");
            }
            using (var browser = new StoreBrowserController())
            {
                await browser.RefreshRepository(endpoint, env.Paths.GetExtensionDataDirectory("phinix.plugin-store"), FixtureRepositoryTransport(endpoint, data));
                await browser.CreatePlan(fixture.Package.Id, "1.0.0", StoreEnvironmentAdapter.FromSnapshot(env), browser.Snapshot.Catalog);
                int reads = 0; var reached = new TaskCompletionSource<bool>();
                using (var transport = new RepositoryTransport(new InstallationHandler(async (r, t) =>
                {
                    if (r.RequestUri == endpoint.Stable && ++reads == 2) { reached.SetResult(true); await Task.Delay(Timeout.Infinite, t); }
                    if (r.RequestUri.AbsolutePath.EndsWith("/package")) return PackageResponse(fixture.Zip);
                    return await FixtureReply(endpoint, data, r);
                })))
                {
                    Task installing = browser.InstallPackages(browser.Snapshot.Plan, env, transport);
                    if (await Task.WhenAny(reached.Task, Task.Delay(5000)) != reached.Task) throw new Exception("Final freshness request not reached.");
                    browser.Cancel(); await installing;
                }
                Assert(browser.Snapshot.State == StoreBrowserState.Cancelled && !Directory.Exists(InstalledTarget(env, fixture.Package.Id)), "Cancel before final freshness completion leaves no visible target.");
                Assert(TemporaryDownloads(env.Paths).Length == 0, "Installation cancellation releases the held package download.");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
