using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;

internal static partial class Program
{
    private const string Source = "test.local";
    private const string Revision = "1111111111111111111111111111111111111111";
    private const string Hash = "2222222222222222222222222222222222222222222222222222222222222222";
    private static int assertions;

    private static int Main()
    {
        try
        {
            Versions();
            ReaderBoundaries();
            Manifests();
            ChainAndDiamond();
            Backtracking();
            Failures();
            LocalPackages();
            ModulesAndExternalMods();
            LimitsAndCancellation();
            FixedFixtures();
            EnvironmentSnapshots();
            BrowserTaskStates();
            Payloads();
            CacheTemporaryPathShape();
            Repositories();
            StoreFailureRegression();
            StoreBrowserFailureRegression();
            RepositoryAudit().GetAwaiter().GetResult();
            PackageDownloads().GetAwaiter().GetResult();
            Installations().GetAwaiter().GetResult();
            ManagedExtensionContracts();
            RepositoryAdapters().GetAwaiter().GetResult();
            Console.WriteLine("All plugin store runtime tests passed (" + assertions + " assertions).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Versions()
    {
        Assert(V("1.10.0").CompareTo(V("1.2.0")) > 0, "Version components must compare numerically.");
        PackageVersion unused;
        foreach (string invalid in new[] { "", "1.0", "1.0.0.0", "v1.0.0", "01.0.0", "1.0.0-beta", "1.0.0+build", " 1.0.0", "2147483648.0.0", "١.0.0", "+1.0.0" })
            Assert(!PackageVersion.TryParse(invalid, out unused), "Must reject unsupported version " + invalid);
        PackageVersionRange range;
        Assert(PackageVersionRange.TryParse(">=1.0.0 <2.0.0", out range) && range.Contains(V("1.0.0")) && !range.Contains(V("2.0.0")), "Range lower inclusive, upper exclusive.");
        Assert(PackageVersionRange.TryParse("1.2.0", out range) && !range.Contains(V("1.2.1")), "Exact version is exact.");
        foreach (string invalid in new[] { "*", "^1.0.0", ">=1.0.0", ">=2.0.0 <1.0.0", ">=1.0.0 <=2.0.0", ">=1.0.0  <2.0.0" })
            Assert(!PackageVersionRange.TryParse(invalid, out range), "Unsupported ranges must fail " + invalid);
    }

    private static void ReaderBoundaries()
    {
        Dictionary<string, object> a = Package("a");
        byte[] bytes = Catalog(a);
        CatalogSnapshot snapshot = CatalogReader.Read(bytes, Source);
        Assert(snapshot.Packages.Count == 1 && snapshot.Sha256 == Digest(bytes), "Snapshot is bound to actual catalog bytes.");
        Expect("SourceMismatch", () => CatalogReader.Read(bytes, "different.source"));
        Expect("UnsupportedSchema", () => ReadRoot(new Dictionary<string, object> { { "schemaVersion", 2 }, { "sourceId", Source }, { "snapshotId", Revision }, { "packages", new[] { a } } }));
        Expect("DocumentLimit", () => CatalogReader.Read(new byte[CatalogReader.MaxCatalogBytes + 1], Source));
        Expect("DocumentLimit", () => CatalogReader.Read(new byte[0], Source));
        Expect("InvalidJson", () => CatalogReader.Read(Encoding.UTF8.GetBytes("{"), Source));
        ExpectFailure(() => CatalogReader.Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes) + " {}"), Source));
        string json = Encoding.UTF8.GetString(bytes);
        ExpectFailure(() => CatalogReader.Read(Encoding.UTF8.GetBytes(json.Substring(0, json.Length - 1) + ",}"), Source));
        ExpectFailure(() => CatalogReader.Read(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1/*comment*/")), Source));
        Expect("DuplicateField", () => CatalogReader.Read(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")), Source));
        ExpectFailure(() => CatalogReader.Read(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schema\\u0056ersion\":1")), Source));
        Expect("InvalidJson", () => CatalogReader.Read(Encoding.UTF8.GetBytes("{\"__type\":\"Arbitrary\"," + json.Substring(1)), Source));
        a["downloadUrl"] = "https://localhost/payload.dll";
        Expect("UnknownField", () => Read(a));
        a.Remove("downloadUrl");
        Artifact(a)["repository"] = "https://github.com/author/repo";
        Expect("InvalidRepository", () => Read(a));
        Artifact(a)["repository"] = "test-owner/a";
        Artifact(a)["sha256"] = "PLACEHOLDER";
        Expect("InvalidDigest", () => Read(a));
        Artifact(a)["sha256"] = Hash;
        Artifact(a)["sourceCommit"] = "main";
        Expect("InvalidDigest", () => Read(a));
        Artifact(a)["sourceCommit"] = Revision;
        Artifact(a)["sizeBytes"] = 0;
        Expect("InvalidNumber", () => Read(a));
        Artifact(a)["sizeBytes"] = 1.5;
        Expect("InvalidNumber", () => Read(a));
        Artifact(a)["sizeBytes"] = CatalogReader.MaxPackageBytes + 1;
        Expect("InvalidNumber", () => Read(a));
        Artifact(a)["sizeBytes"] = 100;
        Artifact(a)["assetName"] = "../escape.zip";
        Expect("InvalidFileName", () => Read(a));
        Artifact(a)["assetName"] = "CON.zip";
        Expect("InvalidFileName", () => Read(a));
        Artifact(a)["assetName"] = "a.zip";
        Dictionary<string, object> dll = Package("dll");
        Artifact(dll)["payloadKind"] = "dll-with-manifest"; Artifact(dll)["assetName"] = "DLL.dll";
        Assert(Read(dll).Packages[0].Artifact.PayloadKind == "dll-with-manifest", "Declared single DLL metadata is accepted without executing code.");
        Artifact(dll)["assetName"] = "other.dll";
        Expect("InvalidDllPayload", () => Read(dll));
        Artifact(a)["ownerId"] = "01";
        Expect("InvalidSourceId", () => Read(a));
        Artifact(a)["ownerId"] = "1";
        foreach (string name in new[] { "Utils", "UnityEngine.CoreModule", "unityengine", "system", "Assembly-CSharp", "com.rlabrecque.steamworks.net" })
        {
            a["assemblies"] = new[] { Assembly(name) };
            Expect("ProtectedAssembly", () => Read(a));
        }
        a["assemblies"] = new[] { Assembly("A") };
        var business = Package("business"); business["assemblies"] = new[] { Assembly("TradeExtension") };
        Assert(Read(business).Packages.Single().Assemblies.Single().Name == "TradeExtension", "Static legacy metadata does not reserve official business identities; actual host facts decide occupancy.");
        Expect("DuplicateVersion", () => Read(a, a));
        Dictionary<string, object> b = Package("b");
        b["rimWorldPackageId"] = a["rimWorldPackageId"];
        Expect("IdentityConflict", () => Read(a, b));
        b = Package("b"); b["assemblies"] = a["assemblies"];
        Expect("IdentityConflict", () => Read(a, b));
        b = Package("a", "2.0.0"); Artifact(b)["repositoryId"] = "99";
        Expect("IdentityChanged", () => Read(a, b));
        Dictionary<string, object> workshop = Workshop("steam", "18446744073709551615");
        PackageRecord listing = Read(workshop).Packages[0];
        Assert(listing.Version == null && listing.Artifact == null && listing.WorkshopUrl.EndsWith("18446744073709551615", StringComparison.Ordinal), "Workshop IDs retain UInt64 precision and do not invent hashes/versions.");
        workshop["workshopId"] = "18446744073709551616";
        Expect("InvalidSourceId", () => Read(workshop));
        workshop["workshopId"] = "0";
        Expect("InvalidSourceId", () => Read(workshop));
        workshop["workshopId"] = "1"; workshop["version"] = "1.0.0";
        Expect("UnexpectedField", () => Read(workshop));
    }

    private static void Manifests()
    {
        Dictionary<string, object> record = Package("a");
        Dictionary<string, object> manifestRecord = new Dictionary<string, object>(record);
        manifestRecord.Remove("artifact");
        manifestRecord.Remove("state");
        byte[] manifest = Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "package", manifestRecord } });
        Artifact(record)["manifestSha256"] = Digest(manifest);
        PackageRecord expected = Read(record).Packages[0];
        Assert(CatalogReader.VerifyManifest(expected, manifest).Artifact == null, "Manifest excludes release/artifact hashes to avoid circular self-hashing.");
        Expect("ManifestDigestMismatch", () => CatalogReader.VerifyManifest(expected, Encoding.UTF8.GetBytes("{}")));
        manifestRecord["version"] = "2.0.0";
        byte[] changed = Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "package", manifestRecord } });
        Artifact(record)["manifestSha256"] = Digest(changed);
        Expect("ManifestMismatch", () => CatalogReader.VerifyManifest(Read(record).Packages[0], changed));
        manifestRecord["artifact"] = Artifact(record);
        Expect("UnexpectedField", () => CatalogReader.ReadManifest(Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "package", manifestRecord } })));
    }

    private static void ChainAndDiamond()
    {
        Dictionary<string, object> a = Package("a", "1.0.0", Dep("b", "1.0.0"));
        Dictionary<string, object> b = Package("b", "1.0.0", Dep("c", "1.0.0"));
        Dictionary<string, object> c = Package("c");
        InstallPlan plan = Plan(Read(a, b, c), "a");
        Assert(Order(plan) == "c,b,a" && plan.DownloadBytes == 300, "Dependencies precede dependents and totals include the full closure.");
        a["dependencies"] = new[] { Dep("b", "1.0.0"), Dep("d", "1.0.0") };
        Dictionary<string, object> d = Package("d", "1.0.0", Dep("c", "1.0.0"));
        plan = Plan(Read(d, c, a, b), "a");
        Assert(Order(plan) == "c,b,d,a" && plan.Packages.Count == 4, "Diamond installs its shared dependency once in stable order.");
        Assert(Order(Plan(Read(b, a, c, d), "a")) == Order(plan), "Catalog enumeration order cannot alter the plan.");
        string frozenHash = plan.CatalogSha256;
        a["name"] = "Edited later"; Artifact(c)["sha256"] = new string('3', 64);
        CatalogSnapshot refreshed = Read(a, b, c, d);
        Assert(plan.CatalogSha256 == frozenHash && plan.CatalogSha256 != refreshed.Sha256 && plan.Packages[0].Package.Artifact.Sha256 == Hash,
            "Refreshing/mutating the input cannot alter a locked plan.");
        ExpectReadOnly(() => ((IList<PlannedPackage>)plan.Packages).Clear());
    }

    private static void Backtracking()
    {
        Dictionary<string, object> a = Package("a", "1.0.0", Dep("b", ">=1.0.0 <3.0.0"), Dep("c", "1.0.0"));
        Dictionary<string, object> b2 = Package("b", "2.0.0", Dep("d", "2.0.0"));
        Dictionary<string, object> b1 = Package("b", "1.0.0", Dep("d", "1.0.0"));
        Dictionary<string, object> c = Package("c", "1.0.0", Dep("d", "1.0.0"));
        InstallPlan plan = Plan(Read(a, b2, b1, c, Package("d"), Package("d", "2.0.0")), "a");
        Assert(plan.Packages.Single(p => p.Package.Id == "b").Package.Version.ToString() == "1.0.0", "Backtrack rather than falsely reject a solvable diamond.");
        b2["dependencies"] = new[] { Dep("a", "1.0.0") };
        plan = Plan(Read(a, b2, b1, c, Package("d")), "a");
        Assert(plan.Packages.Single(p => p.Package.Id == "b").Package.Version.ToString() == "1.0.0", "A cyclic newer candidate does not hide a valid older candidate.");
    }

    private static void Failures()
    {
        Dictionary<string, object> a = Package("a", "1.0.0", Dep("b", "1.0.0"));
        Expect("MissingPackage", () => Plan(Read(a), "a"));
        Dictionary<string, object> b = Package("b", "1.0.0", Dep("a", "1.0.0"));
        Expect("DependencyCycle", () => Plan(Read(a, b), "a"));
        b["dependencies"] = new object[0]; b["state"] = "withdrawn";
        Expect("WithdrawnPackage", () => Plan(Read(a, b), "a"));
        b["state"] = "active";
        a["dependencies"] = new[] { Dep("b", "1.0.0"), Dep("c", "1.0.0") };
        Dictionary<string, object> c = Package("c", "1.0.0", Dep("b", "2.0.0"));
        StoreValidationException failure = Expect("VersionConflict", () => Plan(Read(a, b, Package("b", "2.0.0"), c), "a"));
        Assert(failure.Message.Contains("a@1.0.0") && failure.Message.Contains("c@1.0.0"), "Conflict identifies both parent constraints.");
        a["dependencies"] = new[] { Dep("optional.missing", "1.0.0", true) };
        Assert(Plan(Read(a), "a").Packages.Count == 1, "Optional dependencies are not installed implicitly.");
        a["dependencies"] = new[] { Dep("steam", "1.0.0") };
        Expect("WorkshopDependency", () => Plan(Read(a, Workshop("steam", "123")), "a"));
        Expect("WorkshopLinkOnly", () => Plan(Read(Workshop("steam", "123")), "steam"));
        a["dependencies"] = new object[0];
        Expect("UnknownRuntime", () => Plan(Read(a), "a", facts: new StoreRuntimeFacts("1.6", null, V("1.1.0"), null, null)));
        Expect("IncompatibleRuntime", () => Plan(Read(a), "a", facts: new StoreRuntimeFacts("1.5", V("0.9.7"), V("1.1.0"), null, null)));
        Expect("MissingVersion", () => new DependencyPlanner(Read(a), Facts(), null).CreatePlan("a", "2.0.0"));
    }

    private static void LocalPackages()
    {
        Dictionary<string, object> a = Package("a", "1.0.0", Dep("b", ">=1.0.0 <3.0.0"));
        CatalogSnapshot snapshot = Read(a, Package("b"), Package("b", "2.0.0"));
        PackageRecord localB = snapshot.Packages.Single(p => p.Id == "b" && p.Version.ToString() == "1.0.0");
        InstalledPackage local = Installed(localB, Source, false);
        InstallPlan plan = Plan(snapshot, "a", new[] { local });
        PlannedPackage b = plan.Packages.Single(p => p.Package.Id == "b");
        Assert(b.AlreadyInstalled && b.NeedsEnable && b.Package.Version.ToString() == "1.0.0" && plan.DownloadBytes == 100,
            "Keep a verifiable installed dependency rather than upgrading to highest catalog version.");
        Assert(Plan(Read(a, Package("b", "2.0.0")), "a", new[] { local }).DownloadBytes == 100, "Verified installed version can satisfy dependency even after disappearing from newer catalog.");
        Expect("LocalSourceConflict", () => Plan(snapshot, "a", new[] { Installed(localB, "other.source", true) }));
        Expect("UnknownLocalVersion", () => Plan(snapshot, "a", new[] { new InstalledPackage(null, null, "test.b", new string[0], new string[0], false) }));
        a["dependencies"] = new[] { Dep("b", "2.0.0") };
        Expect("InstalledVersionConflict", () => Plan(Read(a, Package("b"), Package("b", "2.0.0")), "a", new[] { local }));
        a["dependencies"] = new[] { Dep("b", "1.0.0") };
        Dictionary<string, object> modified = Package("b"); Artifact(modified)["sha256"] = new string('4', 64);
        Expect("LocalContentConflict", () => Plan(Read(a, modified), "a", new[] { local }));
        modified = Package("b"); Artifact(modified)["assetId"] = "99";
        Expect("LocalContentConflict", () => Plan(Read(a, modified), "a", new[] { local }));
        modified = Package("b"); modified["state"] = "withdrawn";
        Expect("WithdrawnPackage", () => Plan(Read(a, modified), "a", new[] { local }));
        Expect("IdentityConflict", () => Plan(snapshot, "a", new[] { local, local }));
        Expect("IdentityConflict", () => Plan(Read(Package("a")), "a", new[] { new InstalledPackage(null, null, "manual.other", new string[0], new[] { "A" }, true) }));
        Expect("InvalidLocalSnapshot", () => Plan(snapshot, "a", new[] { new InstalledPackage(Source, localB, "wrong.mod", new string[0], new string[0], true) }));
    }

    private static void ModulesAndExternalMods()
    {
        Dictionary<string, object> a = Package("a");
        a["modules"] = new[] { Module("a.main", "builtin.inventory") };
        Assert(Plan(Read(a), "a").Packages.Count == 1, "Existing host modules need no package download.");
        a["modules"] = new[] { Module("a.main", "b.main") };
        Expect("MissingModule", () => Plan(Read(a, Package("b")), "a"));
        a["dependencies"] = new[] { Dep("b", "1.0.0") };
        Assert(Plan(Read(a, Package("b")), "a").Packages.Count == 2, "Explicit package closure provides module dependency.");
        Dictionary<string, object> b = Package("b"); b["modules"] = new[] { Module("b.main", "a.main") };
        Expect("ModuleCycle", () => Plan(Read(a, b), "a"));
        a = Package("a"); a["modules"] = new[] { Module("builtin.inventory") };
        Expect("IdentityConflict", () => Plan(Read(a), "a"));
        a = Package("a"); a["integrationKind"] = "rimworld-mod"; a["modules"] = new object[0];
        a["compatibility"] = new Dictionary<string, object> { { "targetFramework", "net472" }, { "rimWorldVersions", new[] { "1.6" } } };
        a["externalMods"] = new[] { External("external.patch", "123") };
        InstallPlan plan = Plan(Read(a), "a", facts: new StoreRuntimeFacts("1.6", null, null, null, null));
        Assert(plan.Packages.Count == 1 && plan.ExternalMods.Count == 1 && plan.ExternalMods[0].WorkshopUrl.EndsWith("123", StringComparison.Ordinal),
            "Plain patch packages need no Phinix module; Workshop prerequisites are link advice, not downloads.");
    }

    private static void LimitsAndCancellation()
    {
        List<Dictionary<string, object>> many = new List<Dictionary<string, object>>();
        for (int i = 0; i < 65; i++) many.Add(Package("p" + i, "1.0.0"));
        many[0]["dependencies"] = many.Skip(1).Select(p => Dep((string)p["id"], "1.0.0")).ToArray();
        Expect("PlanLimit", () => Plan(Read(many.ToArray()), "p0"));
        many.Clear();
        for (int i = 0; i < 35; i++) many.Add(Package("p" + i, "1.0.0", i == 34 ? new Dictionary<string, object>[0] : new[] { Dep("p" + (i + 1), "1.0.0") }));
        Expect("PlanLimit", () => Plan(Read(many.ToArray()), "p0"));
        many.Clear();
        Dictionary<string, object> moduleChain = Package("a");
        moduleChain["modules"] = Enumerable.Range(0, 35).Select(i => i == 0 ? Module("m0") : Module("m" + i, "m" + (i - 1))).ToArray();
        Expect("PlanLimit", () => Plan(Read(moduleChain), "a"));
        for (int i = 0; i < 33; i++) many.Add(Package("a", "1.0." + i));
        Expect("CandidateLimit", () => Read(many.ToArray()));
        CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { new DependencyPlanner(Read(Package("a")), Facts(), null).CreatePlan("a", "1.0.0", cancellation.Token); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { assertions++; }
        finally { cancellation.Dispose(); }
    }

    private static void FixedFixtures()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chain.catalog.json");
        CatalogSnapshot snapshot = CatalogReader.Read(File.ReadAllBytes(path), Source);
        Assert(Order(Plan(snapshot, "a")) == "c,b,a", "Client/CI share the checked-in chain fixture.");
        Expect("UnsupportedSchema", () => CatalogReader.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unsupported-schema.catalog.json")), Source));
    }

    private static void BrowserTaskStates()
    {
        byte[] good = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "chain.catalog.json"));
        using (var browser = new StoreBrowserController())
        {
            browser.ReadIndex(t => good, Source).GetAwaiter().GetResult();
            CatalogSnapshot accepted = browser.Snapshot.Catalog;
            Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady, "Background completion is authoritative without any UI callback.");
            browser.ReadIndex(t => Encoding.UTF8.GetBytes("{}"), Source).GetAwaiter().GetResult();
            Assert(browser.Snapshot.State == StoreBrowserState.Failed && ReferenceEquals(browser.Snapshot.Catalog, accepted), "Invalid refresh preserves the previous valid index.");
            string root = Path.Combine(Path.GetTempPath(), "PhinixBrowser", Guid.NewGuid().ToString("N"));
            string host = Path.Combine(root, "Mods", "host");
            var input = StoreEnvironmentAdapter.FromSnapshot(new ClientEnvironmentSnapshot(
                new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData")), host, "1.6", "0.9.7", "1.2.0",
                new[] { new ClientInstalledModSnapshot("test.host", host, true) }, null, null, null));
            browser.CreatePlan("a", "1.0.0", input, accepted).GetAwaiter().GetResult();
            Assert(browser.Snapshot.State == StoreBrowserState.PlanReady && Order(browser.Snapshot.Plan) == "c,b,a", "Browser creates the full frozen plan off-thread.");
            InstallPlan frozen = browser.Snapshot.Plan;
            browser.ReadIndex(t => good, Source).GetAwaiter().GetResult();
            Assert(frozen.CatalogSha256 == accepted.Sha256 && browser.Snapshot.Plan == null, "Refresh cannot mutate old plans and invalidates the current confirmation.");
            Expect("SnapshotChanged", () => browser.CreatePlan("a", "1.0.0", input, accepted));
            browser.ReadLocalIndex("relative.json", Source).GetAwaiter().GetResult();
            Assert(browser.Snapshot.ErrorCode == "InvalidIndexPath", "Local preview rejects relative index paths.");
            browser.ReadLocalIndex(Path.Combine(root, "missing.json"), Source).GetAwaiter().GetResult();
            Assert(browser.Snapshot.State == StoreBrowserState.Failed && browser.Snapshot.Catalog != null, "Missing local file does not lose accepted metadata.");
        }
        using (var browser = new StoreBrowserController())
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            Task old = browser.ReadIndex(t => { entered.Set(); release.Wait(5000); return good; }, Source);
            Assert(entered.Wait(5000), "Slow test reader started.");
            ExpectInvalidOperation(() => browser.ReadIndex(t => good, Source));
            browser.Cancel();
            Assert(browser.Snapshot.State == StoreBrowserState.Cancelled, "Cancel records its terminal state immediately.");
            browser.ReadIndex(t => good, Source).GetAwaiter().GetResult();
            CatalogSnapshot newer = browser.Snapshot.Catalog;
            release.Set(); old.GetAwaiter().GetResult();
            Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady && ReferenceEquals(browser.Snapshot.Catalog, newer), "Late cancelled completion cannot overwrite a newer index.");
            browser.Cancel();
            Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady, "Cancel after completion cannot erase success.");
        }
        using (var browser = new StoreBrowserController(50))
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            Task old = browser.ReadIndex(t => { entered.Set(); release.Wait(5000); return good; }, Source);
            Assert(entered.Wait(5000), "Timeout test reader started.");
            Assert(SpinWait.SpinUntil(() => browser.Snapshot.State == StoreBrowserState.Failed, 5000), "Watchdog stores terminal timeout without a dispatcher callback.");
            Assert(browser.Snapshot.ErrorCode == "TaskTimeout", "Timeout differs from proven validation failure.");
            browser.Dispose(); release.Set(); old.GetAwaiter().GetResult();
            Assert(browser.Snapshot.State == StoreBrowserState.Shutdown && browser.Snapshot.Catalog == null, "Late work after shutdown cannot revive state.");
        }
        using (var browser = new StoreBrowserController())
        using (var entered1 = new ManualResetEventSlim())
        using (var entered2 = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            Task first = browser.ReadIndex(t => { entered1.Set(); release.Wait(5000); return good; }, Source);
            Assert(entered1.Wait(5000), "First bounded reader started."); browser.Cancel();
            Task second = browser.ReadIndex(t => { entered2.Set(); release.Wait(5000); return good; }, Source);
            Assert(entered2.Wait(5000), "Second bounded reader started."); browser.Cancel();
            Expect("PendingWorkers", () => browser.ReadIndex(t => good, Source));
            release.Set(); Task.WhenAll(first, second).GetAwaiter().GetResult();
            browser.ReadIndex(t => good, Source).GetAwaiter().GetResult();
            Assert(browser.Snapshot.State == StoreBrowserState.CatalogReady, "Finishing stale readers releases capacity for later requests.");
        }
        string temp = Path.Combine(Path.GetTempPath(), "PhinixOversizeIndex", Guid.NewGuid().ToString("N"), "index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(temp));
        try
        {
            using (var file = File.Create(temp)) file.SetLength(CatalogReader.MaxCatalogBytes + 1);
            using (var browser = new StoreBrowserController())
            {
                browser.ReadLocalIndex(temp, Source).GetAwaiter().GetResult();
                Assert(browser.Snapshot.ErrorCode == "DocumentLimit", "Read rejects oversized files before allocating their entire contents.");
            }
        }
        finally { File.Delete(temp); Directory.Delete(Path.GetDirectoryName(temp)); }
    }

    private static void ExpectInvalidOperation(Action action)
    { try { action(); } catch (InvalidOperationException) { assertions++; return; } throw new Exception("Expected busy operation rejection."); }

    private static void EnvironmentSnapshots()
    {
        string root = Path.Combine(Path.GetTempPath(), "PhinixEnvironmentTests", Guid.NewGuid().ToString("N"));
        string host = Path.Combine(root, "Mods", "host");
        string other = Path.Combine(root, "Mods", "other");
        var paths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData"));
        string storage = paths.GetExtensionDataDirectory("phinix.plugin-store");
        Assert(Path.IsPathRooted(storage) && !ClientPathOwnership.Contains(paths.LocalModsRoot, storage), "New storage is absolute and outside Mods.");
        Assert(!Directory.Exists(root), "Allocating environment paths creates no files/directories.");
        foreach (string id in new[] { "../escape", "a/b", "a\\b", "a:b", "a..b", "CON", "con.data", "a\n", "" })
            ExpectArgument(() => paths.GetExtensionDataDirectory(id));
        ExpectArgument(() => new ClientEnvironmentPaths("relative/Mods", paths.SaveDataRoot));
        ExpectArgument(() => new ClientEnvironmentPaths(paths.LocalModsRoot, Path.Combine(paths.LocalModsRoot, "SaveData")));
        Assert(ClientPathOwnership.Contains(host, Path.Combine(host, "Assemblies", "A.dll")), "Real child has lexical ownership.");
        Assert(!ClientPathOwnership.Contains(host, Path.Combine(host + "-other", "A.dll")), "A shared path prefix does not claim another mod.");
        Assert(!ClientPathOwnership.Contains(host, Path.Combine(host, "..", "other", "A.dll")), "Canonical parent traversal is not a child.");
        var modList = new List<ClientInstalledModSnapshot>
        {
            new ClientInstalledModSnapshot("test.host", host + Path.DirectorySeparatorChar, true),
            new ClientInstalledModSnapshot("test.b", other, false)
        };
        Assert(ClientPathOwnership.ResolveModRoot(Path.Combine(host, "A.dll"), modList) == host, "Ownership normalizes trailing separators.");
        Assert(ClientPathOwnership.ResolveModRoot(null, modList) == null, "No assembly location gives no claimed owner.");
        Assert(ClientPathOwnership.ResolveModRoot(Path.Combine(host, "A.dll"), new[] { modList[0], modList[0] }) == null, "Duplicate roots make ownership ambiguous.");
        var nested = new ClientInstalledModSnapshot("test.nested", Path.Combine(host, "nested"), true);
        Assert(ClientPathOwnership.ResolveModRoot(Path.Combine(nested.RootDirectory, "A.dll"), new[] { modList[0], nested }) == null, "Nested mod roots are not silently resolved by enumeration order.");
        var modules = new List<ClientModuleSnapshot>
        {
            new ClientModuleSnapshot("host.inventory", host, true),
            new ClientModuleSnapshot("host.disabled", host, false),
            new ClientModuleSnapshot("b.main", other, false)
        };
        var asms = new List<ClientLoadedAssemblySnapshot>
        {
            new ClientLoadedAssemblySnapshot("Utils", "0.9.7.0", host),
            new ClientLoadedAssemblySnapshot("System", "4.0.0.0", null),
            new ClientLoadedAssemblySnapshot("B", "99.0.0.0", other)
        };
        var environment = new ClientEnvironmentSnapshot(paths, host, "1.6", "0.9.7", ClientAbstractionsCompatibility.Version,
            modList, asms, modules, new string[0]);
        StoreEnvironmentInput input = StoreEnvironmentAdapter.FromSnapshot(environment);
        Assert(input.Runtime.ProvidedModuleIds.SequenceEqual(new[] { "host.inventory" }), "Only actual active host modules satisfy host dependencies.");
        Assert(input.Runtime.ProvidedAssemblyNames.SequenceEqual(new[] { "Utils", "System" }), "Unowned loaded assemblies reserve identities, other mods keep their owners.");
        Assert(input.Installed.Count == 2 && input.Installed.All(p => p.VerifiedRecord == null && p.SourceId == null), "Game metadata never guesses store version/source from CLR versions.");
        Assert(!input.Installed[1].Enabled && input.Installed[1].AssemblyNames.Single() == "B", "Disabled/local mods remain ownership conflicts.");
        Assert(input.DataDirectory == storage && input.Runtime.AbstractionsVersion.ToString() == ClientAbstractionsCompatibility.Version, "Adapter retains declared compatibility and stable new storage.");
        modList.Clear(); asms.Clear(); modules.Clear();
        Assert(environment.InstalledMods.Count == 2 && environment.LoadedAssemblies.Count == 3 && environment.Modules.Count == 3, "Environment collections are copies.");
        ExpectReadOnly(() => ((IList<ClientInstalledModSnapshot>)environment.InstalledMods).Clear());
        ExpectReadOnly(() => ((IList<InstalledPackage>)input.Installed).Clear());
        var a = Package("a", "1.0.0", Dep("b", "1.0.0"));
        Expect("UnknownLocalVersion", () => Plan(Read(a, Package("b")), "a", input.Installed, input.Runtime));
        a = Package("a"); a["rimWorldPackageId"] = "test.host";
        Expect("UnknownLocalVersion", () => Plan(Read(a), "a", input.Installed, input.Runtime));
        a = Package("a"); a["modules"] = new[] { Module("a.main", "host.disabled") };
        Expect("MissingModule", () => Plan(Read(a), "a", input.Installed, input.Runtime));
        a["modules"] = new[] { Module("a.main", "host.inventory") };
        Assert(Plan(Read(a), "a", input.Installed, input.Runtime).Packages.Count == 1, "An actual host module does not force a package download.");
        Expect("IncompleteEnvironment", () => StoreEnvironmentAdapter.FromSnapshot(null));
        Expect("IncompleteEnvironment", () => StoreEnvironmentAdapter.FromSnapshot(new ClientEnvironmentSnapshot(paths, null, "1.6", "0.9.7", "1.2.0", null, null, null, null)));
        Expect("IncompleteEnvironment", () => StoreEnvironmentAdapter.FromSnapshot(new ClientEnvironmentSnapshot(null, host, "1.6", "0.9.7", "1.2.0", null, null, null, null)));
        Expect("IncompleteEnvironment", () => StoreEnvironmentAdapter.FromSnapshot(new ClientEnvironmentSnapshot(paths, host, "1.6", "0.9.7", "1.2.0", null, null, null, new[] { "ExtensionDiscoveryNotReady" })));
        string oldCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            var second = new ClientEnvironmentPaths(paths.LocalModsRoot, paths.SaveDataRoot);
            Assert(second.GetExtensionDataDirectory("phinix.plugin-store") == storage, "Changing the working directory does not change new records.");
        }
        finally { Directory.SetCurrentDirectory(oldCwd); }
    }

    private static void ExpectArgument(Action action)
    { try { action(); } catch (ArgumentException) { assertions++; return; } throw new Exception("Expected path argument rejection."); }

    private static Dictionary<string, object> Package(string id, string version = "1.0.0", params Dictionary<string, object>[] dependencies)
    {
        return new Dictionary<string, object>
        {
            { "id", id }, { "name", id }, { "author", "Test Author" }, { "license", "MIT" }, { "channel", "github-release" },
            { "rimWorldPackageId", "test." + id }, { "integrationKind", "phinix-extension" }, { "state", "active" }, { "version", version },
            { "compatibility", new Dictionary<string, object> { { "targetFramework", "net472" }, { "rimWorldVersions", new[] { "1.6" } },
                { "phinixRange", ">=0.9.7 <0.10.0" }, { "abstractionsRange", ">=1.1.0 <2.0.0" } } },
            { "dependencies", dependencies }, { "modules", new[] { Module(id + ".main") } },
            { "assemblies", new[] { Assembly(id.ToUpperInvariant()) } }, { "externalMods", new object[0] },
            { "artifact", new Dictionary<string, object>
                {
                    { "repository", "test-owner/" + id }, { "repositoryId", "1" }, { "ownerId", "1" }, { "sourceCommit", Revision },
                    { "tag", "v" + version }, { "releaseId", "1" }, { "assetId", "1" }, { "assetName", id + ".zip" },
                    { "payloadKind", "rimworld-mod-zip" }, { "sha256", Hash }, { "manifestSha256", Hash }, { "sizeBytes", 100 }
                }
            }
        };
    }

    private static Dictionary<string, object> Workshop(string id, string workshopId)
    {
        Dictionary<string, object> record = Package(id);
        record["channel"] = "steam-workshop"; record["integrationKind"] = "rimworld-mod";
        record["compatibility"] = new Dictionary<string, object> { { "rimWorldVersions", new[] { "1.6" } } };
        record["modules"] = new object[0]; record["assemblies"] = new object[0]; record["workshopId"] = workshopId;
        record.Remove("version"); record.Remove("artifact"); return record;
    }

    private static Dictionary<string, object> Dep(string id, string range, bool optional = false)
    { return new Dictionary<string, object> { { "packageId", id }, { "versionRange", range }, { "optional", optional } }; }
    private static Dictionary<string, object> Module(string id, params string[] dependsOn)
    { return new Dictionary<string, object> { { "id", id }, { "dependsOn", dependsOn } }; }
    private static Dictionary<string, object> Assembly(string name)
    { return new Dictionary<string, object> { { "name", name }, { "version", "1.0.0.0" }, { "fileName", name + ".dll" } }; }
    private static Dictionary<string, object> External(string id, string workshopId)
    { return new Dictionary<string, object> { { "packageId", id }, { "workshopId", workshopId } }; }
    private static Dictionary<string, object> Artifact(Dictionary<string, object> record) { return (Dictionary<string, object>)record["artifact"]; }
    private static byte[] Serialize(object value) { return JsonSerializer.SerializeToUtf8Bytes(value); }
    private static byte[] Catalog(params Dictionary<string, object>[] packages)
    { return Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "sourceId", Source }, { "snapshotId", Revision }, { "packages", packages } }); }
    private static CatalogSnapshot Read(params Dictionary<string, object>[] packages) { return CatalogReader.Read(Catalog(packages), Source); }
    private static CatalogSnapshot ReadRoot(Dictionary<string, object> root) { return CatalogReader.Read(Serialize(root), Source); }
    private static PackageVersion V(string value) { PackageVersion version; if (!PackageVersion.TryParse(value, out version)) throw new Exception("Invalid test version."); return version; }
    private static StoreRuntimeFacts Facts() { return new StoreRuntimeFacts("1.6", V("0.9.7"), V("1.1.0"), new[] { "builtin.inventory", "builtin.trade" }, new[] { "Utils", "TradeExtension", "InventoryExtension" }); }
    private static InstallPlan Plan(CatalogSnapshot snapshot, string root, IEnumerable<InstalledPackage> installed = null, StoreRuntimeFacts facts = null)
    { return new DependencyPlanner(snapshot, facts ?? Facts(), installed).CreatePlan(root, "1.0.0"); }
    private static InstalledPackage Installed(PackageRecord record, string source, bool enabled)
    { return new InstalledPackage(source, record, record.RimWorldPackageId, record.Modules.Select(m => m.Id), record.Assemblies.Select(a => a.Name), enabled); }
    private static string Order(InstallPlan plan) { return string.Join(",", plan.Packages.Select(p => p.Package.Id)); }
    private static string Digest(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static StoreValidationException Expect(string code, Action action)
    {
        try { action(); } catch (StoreValidationException ex) { Assert(ex.Code == code, "Expected " + code + ", got " + ex.Code + ": " + ex.Message); return ex; }
        throw new Exception("Expected " + code + ".");
    }
    private static void ExpectFailure(Action action)
    { try { action(); } catch (StoreValidationException) { assertions++; return; } throw new Exception("Expected validation failure."); }
    private static void ExpectReadOnly(Action action)
    { try { action(); } catch (NotSupportedException) { assertions++; return; } throw new Exception("Expected immutable collection."); }
}
