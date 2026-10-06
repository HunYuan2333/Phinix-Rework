using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Utils.Framework.ManagedExtensions;
using PhinixClient.Framework;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void ManagedExtensionContracts()
    {
        string root = Path.Combine(Path.GetTempPath(), "phinix-managed-contracts-" + Guid.NewGuid().ToString("N"));
        byte[] dll = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fixture.Plugin.dll"));
        try
        {
            var paths = new ManagedExtensionPaths(Path.Combine(root, "SaveData"));
            var gamePaths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), paths.SaveDataRoot);
            Assert(gamePaths.ManagedExtensions.RootDirectory == paths.RootDirectory && !Directory.Exists(root), "Managed path allocation is independent of Mods and creates no directory.");
            string hostRoot=Path.Combine(root,"Host");
            string managedRoot=paths.GetPackageDirectory("test.source","test.plugin");
            var managedEnvironment=new ClientEnvironmentSnapshot(gamePaths,hostRoot,"1.6","0.9.7",ClientAbstractionsCompatibility.Version,
                new[]{new ClientInstalledModSnapshot("test.host",hostRoot,true)},
                new[]{new ClientLoadedAssemblySnapshot("Managed.Plugin","1.0.0.0",null,"test.source","test.plugin",managedRoot)},
                new[]{new ClientModuleSnapshot("test.plugin",null,true,"test.source","test.plugin",managedRoot)},new string[0]);
            var managedInput=StoreEnvironmentAdapter.FromSnapshot(managedEnvironment);
            Assert(managedInput.Installed.Count==1 && managedInput.Installed[0].RimWorldPackageId=="test.host" && managedEnvironment.Modules[0].SourceModRoot==null,
                "Managed ownership does not fabricate a RimWorld mod entry or mod root.");
            Assert(managedInput.Runtime.ProvidedModuleIds.Contains("test.plugin") && managedEnvironment.LoadedAssemblies[0].ManagedPackageRoot==managedRoot,
                "Active managed modules participate in existing dependency facts with explicit ownership.");
            ExpectArgument(() => new ManagedExtensionPaths("relative"));
            ExpectArgument(() => new ClientEnvironmentPaths(Path.Combine(paths.RootDirectory, "packages"), paths.SaveDataRoot));
            ManagedFailure("InvalidId", () => ManagedExtensionPaths.PackageKey("../source", "test.plugin"));
            Assert(ManagedExtensionPaths.PackageKey("test.source", "test.plugin") != ManagedExtensionPaths.PackageKey("test.other", "test.plugin"), "Source identity participates in stable package paths.");
            string oldCwd = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Assert(new ManagedExtensionPaths(paths.SaveDataRoot).RootDirectory == paths.RootDirectory, "Managed paths do not follow working-directory changes.");
            }
            finally { Directory.SetCurrentDirectory(oldCwd); }
            var empty = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None);
            Assert(empty.Packages.Count == 0 && empty.Diagnostics.Count == 0 && !Directory.Exists(root), "Empty inventory reads are side-effect free.");
            var documented = ManagedExtensionManifestReader.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ManagedProtocol", "manifest.example.json")));
            Assert(documented.PackageId == "example.extension" && documented.ExternalMods.Single().PackageId == "hunyuan2333.phinixrework", "Documented static manifest is parsed by the production contract.");

            var document = ManagedManifest(dll);
            byte[] bytes = Serialize(document);
            var manifest = ManagedExtensionManifestReader.Read(bytes);
            Assert(manifest.PackageId == "test.managed" && manifest.Assemblies.Count == 1 && manifest.Modules.Count == 1 &&
                manifest.Assemblies[0].FullName == "Fixture.Plugin, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null", "Managed manifest preserves package, module and full assembly identity separately.");
            ExpectReadOnly(() => ((IList<ManagedExtensionModule>)manifest.Modules).Clear());
            ManagedFailure("UnsupportedSchema", () => ManagedExtensionManifestReader.Read(Serialize(With(document, "schemaVersion", 2))));
            ManagedFailure("UnsupportedManagement", () => ManagedExtensionManifestReader.Read(Serialize(With(document, "management", "rimworld-mod"))));
            ManagedFailure("UnknownField", () => ManagedExtensionManifestReader.Read(Serialize(With(document, "rimWorldPackageId", "test.fake"))));
            ManagedFailure("MissingField", () => { var d = With(document, "name", "test"); d.Remove("modules"); ManagedExtensionManifestReader.Read(Serialize(d)); });
            foreach (string invalid in new[] { "", "1.0", "1.0.0.0", "v1.0.0", "01.0.0", "1.0.0-beta", " 1.0.0", "١.0.0", "2147483648.0.0" })
                ManagedFailure("InvalidVersion", () => ManagedExtensionVersion.Parse(invalid));
            var range = ManagedExtensionVersionRange.Parse(">=1.0.0 <2.0.0");
            Assert(range.Contains(ManagedExtensionVersion.Parse("1.0.0")) && !range.Contains(ManagedExtensionVersion.Parse("2.0.0")), "Generic compatibility ranges use explicit stable version bounds.");
            foreach (string invalid in new[] { "*", "^1.0.0", ">=1.0.0", ">=2.0.0 <1.0.0", ">=1.0.0 <=2.0.0", ">=1.0.0  <2.0.0" })
                ManagedFailure("InvalidRange", () => ManagedExtensionVersionRange.Parse(invalid));
            string json = Encoding.UTF8.GetString(bytes);
            ManagedFailure("DuplicateField", () => ManagedExtensionManifestReader.Read(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"))));
            // The BCL reader rejects escaped property aliases before constructing the object.
            ManagedFailure("InvalidJson", () => ManagedExtensionManifestReader.Read(Encoding.UTF8.GetBytes(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schema\\u0056ersion\":1"))));
            foreach (string invalid in new[] { json + "{}", json + "garbage", json.Substring(0, json.Length - 1) + ",}", json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1/*comment*/") })
                ManagedFailure("InvalidJson", () => ManagedExtensionManifestReader.Read(Encoding.UTF8.GetBytes(invalid)));
            ManagedFailure("InvalidJson", () => ManagedExtensionManifestReader.Read(new byte[] { 123, 34, 0xff, 34, 58, 49, 125 }));
            ManagedFailure("DocumentLimit", () => ManagedExtensionManifestReader.Read(new byte[ManagedExtensionManifestReader.MaxManifestBytes + 1]));
            ManagedFailure("UnknownField", () => ManagedExtensionManifestReader.Read(Serialize(With(document, "__type", "Arbitrary"))));

            foreach (string path in new[] { "../x.dll", "Assemblies/../x.dll", "Assemblies\\x.dll", "Assemblies/CON.dll", "Assemblies/x.dll.", "Assemblies/x.dll:stream", "/Assemblies/x.dll" })
                ManagedFailure("InvalidPath", () => ReadManagedAssembly(document, "path", path));
            ManagedFailure("ProtectedAssembly", () => ReadManagedAssembly(document, "name", "Utils"));
            ManagedFailure("ProtectedAssembly", () => ReadManagedAssembly(document, "path", "Assemblies/Utils.dll"));
            ManagedFailure("InvalidAssembly", () => ReadManagedAssembly(document, "version", "1.0.0"));
            ManagedFailure("InvalidAssembly", () => ReadManagedAssembly(document, "length", 0));
            ManagedFailure("InvalidAssembly", () => ReadManagedAssembly(document, "culture", "en-US"));
            ManagedFailure("InvalidDigest", () => ReadManagedAssembly(document, "publicKeyToken", "0123"));
            ManagedFailure("InvalidAssemblyPath", () => ReadManagedAssembly(document, "path", "Resources/Fixture.Plugin.dll"));
            ManagedFailure("InvalidModuleEntry", () =>
            {
                var d = ManagedManifest(dll); ((Dictionary<string, object>)((object[])d["modules"])[0])["assemblyName"] = "Missing.Assembly";
                ManagedExtensionManifestReader.Read(Serialize(d));
            });
            ManagedFailure("InvalidDependency", () =>
            {
                var d = ManagedManifest(dll); d["dependencies"] = new[] { new { packageId = "test.managed", versionRange = "1.0.0", optional = false } };
                ManagedExtensionManifestReader.Read(Serialize(d));
            });
            ManagedFailure("DuplicateAssembly", () =>
            {
                var d = ManagedManifest(dll); d["assemblies"] = new[] { ((object[])d["assemblies"])[0], ((object[])d["assemblies"])[0] };
                ManagedExtensionManifestReader.Read(Serialize(d));
            });
            ManagedFailure("PathAlias", () =>
            {
                var d = ManagedManifest(dll); d["resources"] = new[] {
                    new { path = "Resources/Text/a.txt", length = 1, sha256 = Hash },
                    new { path = "Resources/text/b.txt", length = 1, sha256 = Hash } };
                ManagedExtensionManifestReader.Read(Serialize(d));
            });

            bool loadedBefore = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin");
            Assert(!loadedBefore, "Static validation fixture must remain unloaded before inventory inspection.");
            WriteManagedFixture(paths, "test.source", document, dll, "enabled");
            var inventory = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None);
            var row = inventory.Packages.Single();
            Assert(row.ContentState == ManagedExtensionContentState.ContentVerified && row.DesiredState == ManagedExtensionDesiredState.Enabled && row.DiagnosticCode == null,
                "Complete bound receipt and desired state produce a verified-content row.");
            Assert(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin") == loadedBefore, "Inventory hashing never loads candidate DLLs.");
            Assert(inventory.Diagnostics.Count == 0, "Healthy inventory has no root diagnostic.");
            Assert(row.CatalogSnapshotId == Revision && row.CatalogSha256 == Hash && row.ArtifactSha256 == Hash,
                "Inventory retains fixed publication/artifact provenance without storing an endpoint or credentials.");
            ExpectReadOnly(() => ((IList<ManagedExtensionPackageSnapshot>)inventory.Packages).Clear());
            string packageRoot = paths.GetPackageDirectory("test.source", "test.managed");
            string dllPath = Path.Combine(packageRoot, "Assemblies", "Fixture.Plugin.dll");
            foreach (string state in new[] { "disabled", "pending-removal" })
            {
                WriteManagedState(paths, "test.source", bytes, state);
                row = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single();
                Assert(row.ContentState == ManagedExtensionContentState.ContentVerified && row.DiagnosticCode == null &&
                    row.DesiredState == (state == "disabled" ? ManagedExtensionDesiredState.Disabled : ManagedExtensionDesiredState.PendingRemoval), "Unloaded disabled/removing packages remain visible.");
            }
            File.Delete(paths.GetDesiredStatePath("test.source", "test.managed"));
            row = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single();
            Assert(row.ContentState == ManagedExtensionContentState.ContentVerified && row.DesiredState == ManagedExtensionDesiredState.Unknown && row.DiagnosticCode == "DesiredStateMissing", "Missing intent never defaults to enabled.");
            WriteManagedState(paths, "test.source", bytes, "enabled", new string('f', 64));
            AssertManagedRow(paths, "DesiredStateIdentityMismatch", true);
            WriteManagedState(paths, "test.source", bytes, "unknown");
            AssertManagedRow(paths, "InvalidDesiredState", true);
            WriteManagedState(paths, "test.source", bytes, "disabled");
            byte[] modified = (byte[])dll.Clone(); modified[modified.Length - 1] ^= 1; File.WriteAllBytes(dllPath, modified);
            AssertManagedRow(paths, "FileDigestMismatch");
            Assert(File.ReadAllBytes(dllPath).SequenceEqual(modified), "Failed inspection preserves edited content.");
            File.WriteAllBytes(dllPath, dll);
            File.WriteAllText(Path.Combine(packageRoot, "extra.txt"), "keep");
            AssertManagedRow(paths, "UnexpectedFile");
            File.Delete(Path.Combine(packageRoot, "extra.txt"));
            Directory.CreateDirectory(Path.Combine(packageRoot, "extra"));
            AssertManagedRow(paths, "UnexpectedDirectory");
            Directory.Delete(Path.Combine(packageRoot, "extra"));
            File.Delete(dllPath);
            AssertManagedRow(paths, "PackageFileMissing");
            File.WriteAllBytes(dllPath, dll);
            string receiptPath = paths.GetInstalledRecordPath("test.source", "test.managed");
            byte[] receipt = File.ReadAllBytes(receiptPath);
            File.WriteAllText(receiptPath, "{} trailing");
            AssertManagedRow(paths, "InvalidJson");
            File.WriteAllBytes(receiptPath, receipt);
            File.WriteAllText(receiptPath, Encoding.UTF8.GetString(receipt).Replace("\"catalogSnapshotId\":\"" + Revision + "\"", "\"catalogSnapshotId\":\"main\""));
            AssertManagedRow(paths, "InvalidDigest");
            File.WriteAllBytes(receiptPath, receipt);
            File.WriteAllText(receiptPath, Encoding.UTF8.GetString(receipt).Replace("\"sourceId\":\"test.source\"", "\"sourceId\":\"test.other\""));
            AssertManagedRow(paths, "ReceiptIdentityMismatch");
            File.WriteAllBytes(receiptPath, receipt);
            WriteManagedFixture(paths, "test.other", document, dll, "disabled");
            File.WriteAllText(receiptPath, "{}");
            inventory = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None);
            Assert(inventory.Packages.Count == 2 && inventory.Packages.Count(p => p.ContentState == ManagedExtensionContentState.ContentVerified) == 1,
                "A damaged package record does not hide unrelated packages.");
            File.WriteAllBytes(receiptPath, receipt);
            var orphanRoot = paths.GetPackageDirectory("test.orphan", "test.managed"); Directory.CreateDirectory(orphanRoot);
            Assert(ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Count == 2 && Directory.Exists(orphanRoot), "Unowned directories are neither adopted nor deleted.");

            if (Path.DirectorySeparatorChar == '/')
            {
                File.Delete(dllPath); File.CreateSymbolicLink(dllPath, Path.Combine(paths.GetPackageDirectory("test.other", "test.managed"), "Assemblies", "Fixture.Plugin.dll"));
                row = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single(p => p.SourceId == "test.source");
                Assert(row.DiagnosticCode == "UnsafeFilesystemLink" && row.ContentState == ManagedExtensionContentState.Invalid, "Candidate symlinks fail without following their content.");
                File.Delete(dllPath); File.WriteAllBytes(dllPath, dll);
                string linkRoot = Path.Combine(root, "LinkSave"); Directory.CreateSymbolicLink(linkRoot, paths.SaveDataRoot);
                var linked = ManagedExtensionInventoryReader.Read(new ManagedExtensionPaths(linkRoot), CancellationToken.None);
                Assert(linked.Packages.Count == 0 && linked.Diagnostics.Contains("UnsafeFilesystemLink"), "Ancestor links invalidate the inventory root.");
                Directory.Delete(linkRoot);
            }
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { ManagedExtensionInventoryReader.Read(paths, cancelled.Token); throw new Exception("Expected cancellation."); }
                catch (OperationCanceledException) { assertions++; }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static Dictionary<string, object> ManagedManifest(byte[] dll)
    {
        return new Dictionary<string, object>
        {
            { "schemaVersion", 1 }, { "management", "phinix-dll" }, { "packageId", "test.managed" }, { "name", "Managed test" }, { "version", "1.0.0" }, { "targetFramework", "net472" },
            { "compatibility", new { rimWorldVersions = new[] { "1.6" }, phinixRange = ">=0.9.7 <1.0.0", abstractionsRange = ">=1.3.0 <2.0.0" } },
            { "dependencies", new object[0] }, { "resources", new object[0] }, { "externalMods", new object[0] },
            { "modules", new object[] { new Dictionary<string, object> { { "id", "test.managed.module" }, { "assemblyName", "Fixture.Plugin" }, { "entryType", "Fixture.Plugin.Module" }, { "dependsOn", new string[0] } } } },
            { "assemblies", new object[] { new Dictionary<string, object> {
                { "name", "Fixture.Plugin" }, { "version", "1.2.3.4" }, { "culture", "neutral" }, { "publicKeyToken", "null" },
                { "path", "Assemblies/Fixture.Plugin.dll" }, { "length", dll.Length }, { "sha256", ManagedExtensionPaths.Hash(dll) } } } }
        };
    }
    private static Dictionary<string, object> With(Dictionary<string, object> original, string key, object value)
    { var copy = new Dictionary<string, object>(original); copy[key] = value; return copy; }
    private static void ReadManagedAssembly(Dictionary<string, object> original, string key, object value)
    {
        var copy = With(original, "assemblies", new[] { With((Dictionary<string, object>)((object[])original["assemblies"])[0], key, value) });
        ManagedExtensionManifestReader.Read(Serialize(copy));
    }
    private static void ManagedFailure(string code, Action action)
    {
        try { action(); } catch (ManagedExtensionValidationException ex) { Assert(ex.Code == code, "Expected managed error " + code + ", got " + ex.Code); return; }
        throw new Exception("Expected managed error " + code);
    }
    private static void AssertManagedRow(ManagedExtensionPaths paths, string code, bool contentVerified = false)
    {
        var row = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single(p => p.RecordKey == ManagedExtensionPaths.PackageKey("test.source", "test.managed"));
        Assert(row.DiagnosticCode == code && row.ContentState == (contentVerified ? ManagedExtensionContentState.ContentVerified : ManagedExtensionContentState.Invalid), "Expected inventory failure " + code + ", got " + row.DiagnosticCode);
        Assert(row.DesiredState == ManagedExtensionDesiredState.Unknown, "Failed verification never enables a package.");
    }
    private static void WriteManagedFixture(ManagedExtensionPaths paths, string source, Dictionary<string, object> document, byte[] dll, string state)
    {
        string packageRoot = paths.GetPackageDirectory(source, "test.managed");
        Directory.CreateDirectory(Path.Combine(packageRoot, "Assemblies"));
        Directory.CreateDirectory(paths.InstalledRecordsDirectory);
        byte[] manifest = Serialize(document); string hash = ManagedExtensionPaths.Hash(manifest);
        File.WriteAllBytes(Path.Combine(packageRoot, "manifest.json"), manifest);
        File.WriteAllBytes(Path.Combine(packageRoot, "Assemblies", "Fixture.Plugin.dll"), dll);
        File.WriteAllBytes(paths.GetInstalledRecordPath(source, "test.managed"), Serialize(new
        {
            schemaVersion = 1, sourceId = source, repositoryIdentitySha256 = Hash, packageId = "test.managed", version = "1.0.0", manifestSha256 = hash,
            catalogSnapshotId = Revision, catalogSha256 = Hash, artifactSha256 = Hash,
            installationTransactionId = new string('a', 32), files = new[] {
                new { path = "manifest.json", length = manifest.Length, sha256 = hash },
                new { path = "Assemblies/Fixture.Plugin.dll", length = dll.Length, sha256 = ManagedExtensionPaths.Hash(dll) } }
        }));
        WriteManagedState(paths, source, manifest, state);
    }
    private static void WriteManagedState(ManagedExtensionPaths paths, string source, byte[] manifest, string state, string hash = null)
    {
        Directory.CreateDirectory(paths.DesiredStateDirectory);
        File.WriteAllBytes(paths.GetDesiredStatePath(source, "test.managed"), Serialize(new
        { schemaVersion = 1, sourceId = source, packageId = "test.managed", manifestSha256 = hash ?? ManagedExtensionPaths.Hash(manifest), operationId = new string('b', 32), desiredState = state }));
    }
}
