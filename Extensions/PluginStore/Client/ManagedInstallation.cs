using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using PhinixClient.Framework;

namespace Phinix.PluginStore
{
    [DataContract]
    internal sealed class OwnedFile
    {
        [DataMember] public string Path;
        [DataMember] public long Length;
        [DataMember] public string Sha256;
    }

    [DataContract]
    internal sealed class InstallationReceipt
    {
        [DataMember] public int Schema = 1;
        [DataMember] public string Transaction;
        [DataMember] public string Source;
        [DataMember] public string EndpointKey;
        [DataMember] public string Folder;
        [DataMember] public byte[] Catalog;
        [DataMember] public List<OwnedFile> Files;
        internal PackageRecord Package => CatalogReader.Read(Catalog, Source).Packages.Single();
    }

    [DataContract]
    internal sealed class InstallationJournal
    {
        [DataMember] public int Schema = 1;
        [DataMember] public string Transaction;
        [DataMember] public string Kind;
        [DataMember] public List<InstallationReceipt> Receipts;
    }

    internal sealed class ManagedPackage
    {
        internal ManagedPackage(InstallationReceipt receipt, string target) { Receipt = receipt; Target = target; }
        internal InstallationReceipt Receipt { get; }
        internal PackageRecord Package => Receipt.Package;
        internal string Target { get; }
    }

    // Filesystem-only service. It neither changes ModsConfig nor loads candidate DLLs.
    // A journal precedes every visible directory move; there is no cross-directory
    // atomicity claim. Uncertain or edited ownership always stops automatic deletion.
    internal sealed class ManagedInstallation : IDisposable
    {
        private const string Stamp = ".phinix-store-owner";
        private const int MaxRecord = 3 * 1024 * 1024;
        private const int MaxJournal = 16 * 1024 * 1024;
        private readonly ClientEnvironmentSnapshot environment;
        private readonly RepositoryDiagnostics audit;
        private readonly string mods, records, journals, work;
        private FileStream lease;
        private string transaction;
        private InstallationJournal pending;
        private bool journalWritten;
        private readonly bool readOnly;
        private readonly HashSet<string> scratchFiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> scratchDirectories = new HashSet<string>(StringComparer.Ordinal);
        internal Action<string> FaultPoint { get; set; }
        internal IEnumerable<ManagedPackage> PreparedPackages => pending.Receipts.Select(r => new ManagedPackage(r, Target(r))).ToArray();

        internal ManagedInstallation(ClientEnvironmentSnapshot environment, RepositoryDiagnostics audit = null, bool readOnly = false)
        {
            if (environment == null || !environment.IsComplete) throw Error("IncompleteEnvironment", "Capture complete game paths and loaded mod identities first.");
            this.environment = environment; this.audit = audit; this.readOnly = readOnly;
            mods = environment.Paths.LocalModsRoot;
            if (!Directory.Exists(mods)) throw Error("ModsRootUnavailable", "The local Mods directory must already exist.");
            string data = Path.Combine(environment.Paths.GetExtensionDataDirectory("phinix.plugin-store"), "installation-v1");
            records = Path.Combine(data, "installed"); journals = Path.Combine(data, "journals");
            // Sibling of Mods: not scanned by RimWorld and normally on the same volume.
            string parent = Directory.GetParent(mods)?.FullName;
            if (parent == null) throw Error("UnsafeInstallationRoot", "A filesystem root cannot be used as Mods.");
            work = Path.Combine(parent, ".phinix-store-" + CatalogReader.Hash(Encoding.UTF8.GetBytes(mods)).Substring(0, 16));
            if (ClientPathOwnership.Contains(mods, data) || ClientPathOwnership.Contains(mods, work)) throw Error("UnsafeInstallationRoot", "Staging and journals must be outside Mods.");
            foreach (string dir in new[] { mods, records, journals, work }) { CheckLinks(dir); if (!readOnly) Directory.CreateDirectory(dir); CheckLinks(dir); }
            if (readOnly) return; // Planning needs verified facts, not writable Mods.
            try { lease = new FileStream(Path.Combine(work, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new StoreValidationException("InstallationBusy", "Another store operation owns this Mods directory.", ex); }
            try
            {
                ProbeWritable(mods); ProbeWritable(records); ProbeWritable(journals); ProbeWritable(work);
            }
            catch { Dispose(); throw; }
        }

        internal static string FolderName(string source, string id)
        { return "phinix-store-" + CatalogReader.Hash(Encoding.UTF8.GetBytes(source + "\n" + id)).Substring(0, 32); }
        private string Target(InstallationReceipt r) => Path.Combine(mods, r.Folder);
        private string Staged(InstallationReceipt r) => Path.Combine(work, r.Transaction, r.Folder);
        private string Record(InstallationReceipt r) => Path.Combine(records, r.Folder + ".json");
        private string Journal(string id) => Path.Combine(journals, id + ".json");

        internal IReadOnlyList<ManagedPackage> List(CancellationToken token)
        {
            RequireNoJournal();
            var result = new List<ManagedPackage>();
            string[] files = Directory.Exists(records) ? Directory.GetFiles(records, "*.json") : new string[0];
            if (files.Length > 1024) throw Error("InstallationLimit", "Too many installation records.");
            foreach (string path in files.OrderBy(p => p, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var receipt = Read<InstallationReceipt>(path, MaxRecord);
                Validate(receipt);
                if (path != Record(receipt)) throw Error("InvalidOwnershipRecord", "Installation record name disagrees with ownership.");
                VerifyDirectory(Target(receipt), receipt, token);
                result.Add(new ManagedPackage(receipt, Target(receipt)));
            }
            return result;
        }

        internal StoreEnvironmentInput PlanningInput(CatalogSnapshot catalog, CancellationToken token)
        {
            StoreEnvironmentInput raw = StoreEnvironmentAdapter.FromSnapshot(environment);
            var local = raw.Installed.ToList();
            foreach (ManagedPackage item in List(token))
            {
                PackageRecord recorded = item.Package;
                PackageRecord current = catalog.Packages.FirstOrDefault(p => p.Id == recorded.Id && p.Version.CompareTo(recorded.Version) == 0);
                if (catalog.SourceId != item.Receipt.Source || current == null) continue;
                if (!CatalogReader.SameManifest(recorded, current) || !SameArtifact(recorded.Artifact, current.Artifact))
                    throw Error("PackageIdentityChanged", "Installed content differs from the current locked package: " + recorded.Id);
                var matches = environment.InstalledMods.Where(m => m.PackageId == recorded.RimWorldPackageId).ToArray();
                if (matches.Any(m => !SamePath(m.RootDirectory, item.Target))) throw Error("LocalIdentityConflict", "A manual or Workshop copy conflicts with " + recorded.Id);
                local.RemoveAll(p => p.RimWorldPackageId == recorded.RimWorldPackageId);
                local.Add(new InstalledPackage(item.Receipt.Source, current, recorded.RimWorldPackageId,
                    recorded.Modules.Select(m => m.Id), recorded.Assemblies.Select(a => a.Name), matches.Any(m => m.Enabled)));
            }
            return new StoreEnvironmentInput(environment, raw.Runtime, local);
        }

        internal void BeginInstall(InstallPlan plan, byte[] catalogBytes, RepositoryEndpoint endpoint, CancellationToken token)
        {
            RequireWritable();
            RequireNoJournal();
            if (pending != null) throw new InvalidOperationException("An installation is already prepared.");
            transaction = Guid.NewGuid().ToString("N");
            if (audit != null) audit.TransactionId = transaction;
            pending = new InstallationJournal { Transaction = transaction, Kind = "install", Receipts = new List<InstallationReceipt>() };
            var catalog = CatalogReader.Read(catalogBytes, endpoint.SourceId);
            if (plan.SourceId != catalog.SourceId || plan.SnapshotId != catalog.SnapshotId || plan.CatalogSha256 != catalog.Sha256)
                throw Error("SnapshotChanged", "Refresh and review the plan again.");
            foreach (PlannedPackage item in plan.Packages.Where(p => !p.AlreadyInstalled))
            {
                token.ThrowIfCancellationRequested();
                if (item.Package.Artifact.PayloadKind != "rimworld-mod-zip") throw Error("UnsupportedPayloadInstall", "This installer accepts validated ZIP packages only.");
                var receipt = new InstallationReceipt { Transaction = transaction, Source = endpoint.SourceId, EndpointKey = endpoint.CacheKey,
                    Folder = FolderName(endpoint.SourceId, item.Package.Id), Catalog = CatalogReader.OwnershipCatalog(catalogBytes, item.Package, endpoint.SourceId), Files = new List<OwnedFile>() };
                EnsureNew(receipt);
                pending.Receipts.Add(receipt);
            }
            if (pending.Receipts.Count == 0 || pending.Receipts.Count > 64) throw Error("NoPackagesToInstall", "The plan must contain 1 to 64 new packages.");
            string scratch = Path.Combine(work, transaction);
            if (Directory.Exists(scratch) || File.Exists(scratch)) throw Error("InstallationConflict", "A scratch operation directory already exists.");
            Directory.CreateDirectory(scratch); scratchDirectories.Add(scratch);
            audit?.Event("install.preparation_started");
        }

        internal void Stage(ValidatedPackageDownload download, CancellationToken token)
        {
            if (pending == null || pending.Kind != "install" || journalWritten) throw new InvalidOperationException("No open preparation.");
            var receipt = pending.Receipts.Single(r => r.Package.Id == download.Report.Package.Id);
            PackageRecord expected = receipt.Package;
            if (download.Report.Sha256 != expected.Artifact.Sha256 || !CatalogReader.SameManifest(expected, download.Report.Package))
                throw Error("PayloadDigestMismatch", "Only the locked, held download can be staged.");
            string directory = Staged(receipt); CheckLinks(directory); Directory.CreateDirectory(directory); scratchDirectories.Add(directory);
            download.Read(stream =>
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    foreach (ValidatedPayloadFile report in download.Report.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        string relative = PayloadValidator.ValidatePath(report.Path, false);
                        var entry = zip.GetEntry(relative);
                        if (entry == null || entry.Length != report.Length) throw Error("PayloadChanged", "Validated archive entries changed.");
                        string path = ResolveFile(directory, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)); CheckLinks(path);
                        for (string parent = Path.GetDirectoryName(path); ClientPathOwnership.Contains(directory, parent); parent = Path.GetDirectoryName(parent))
                        { scratchDirectories.Add(parent); if (SamePath(parent, directory)) break; }
                        using (var input = entry.Open())
                        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        using (var hash = SHA256.Create())
                        {
                            scratchFiles.Add(path);
                            FaultPoint?.Invoke("write");
                            byte[] buffer = new byte[64 * 1024]; long length = 0; int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                token.ThrowIfCancellationRequested(); length += read;
                                if (length > report.Length) throw Error("PayloadChanged", "Extracted content exceeds the checked length.");
                                output.Write(buffer, 0, read); hash.TransformBlock(buffer, 0, read, buffer, 0);
                            }
                            hash.TransformFinalBlock(new byte[0], 0, 0);
                            if (length != report.Length || Hex(hash.Hash) != report.Sha256) throw Error("PayloadChanged", "Extracted content differs from the checked bytes.");
                            output.Flush(true);
                        }
                        receipt.Files.Add(new OwnedFile { Path = relative, Length = report.Length, Sha256 = report.Sha256 });
                    }
                }
            });
            Validate(receipt);
            WriteStamp(directory, receipt); scratchFiles.Add(Path.Combine(directory, Stamp));
            VerifyDirectory(directory, receipt, token);
            audit?.Event("install.package_staged", package: expected);
            FaultPoint?.Invoke("staged");
        }

        internal void Seal(CancellationToken token)
        {
            foreach (var receipt in pending.Receipts) { EnsureNew(receipt); VerifyDirectory(Staged(receipt), receipt, token); }
            token.ThrowIfCancellationRequested();
            Write(Journal(transaction), pending, MaxJournal);
            journalWritten = true;
            audit?.Event("install.journal_prepared"); FaultPoint?.Invoke("journal");
        }

        // Called only after the last online freshness check and generation check.
        // Cancellation is accepted before the first move; after it, journal recovery
        // owns the outcome. No per-package copy fallback across filesystems.
        internal void Commit(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!journalWritten || pending.Kind != "install") throw new InvalidOperationException("Prepare the journal before commit.");
            foreach (var receipt in pending.Receipts) EnsureNew(receipt);
            audit?.Event("install.commit_started");
            foreach (var receipt in pending.Receipts)
            {
                CheckLinks(Target(receipt)); CheckLinks(Staged(receipt));
                Directory.Move(Staged(receipt), Target(receipt));
                audit?.Event("install.package_committed", package: receipt.Package);
                FaultPoint?.Invoke("moved");
            }
            foreach (var receipt in pending.Receipts) { Write(Record(receipt), receipt, MaxRecord); FaultPoint?.Invoke("recorded"); }
            Directory.Delete(Path.Combine(work, transaction));
            File.Delete(Journal(transaction)); journalWritten = false;
            audit?.Event("install.committed"); pending = null;
        }

        internal void Uninstall(string folder, CancellationToken token)
        {
            RequireWritable();
            var all = List(token);
            ManagedPackage selected = all.SingleOrDefault(p => p.Receipt.Folder == folder);
            if (selected == null) throw Error("NotManaged", "Only a verified store-owned package can be uninstalled.");
            PackageRecord package = selected.Package;
            CheckUnused(selected.Receipt);
            foreach (var other in all.Where(p => p.Receipt.Folder != folder))
                if (other.Package.Dependencies.Any(d => !d.Optional && d.Id == package.Id) ||
                    other.Package.Modules.Any(m => m.DependsOn.Any(id => package.Modules.Any(own => own.Id == id))) ||
                    other.Package.ExternalMods.Any(m => m.PackageId == package.RimWorldPackageId))
                    throw Error("PackageStillRequired", "Uninstall its dependent package first: " + other.Package.Id);
            // Also honor declared RimWorld dependencies of unmanaged mods.
            CheckExternalDependents(package.RimWorldPackageId, selected.Target);
            token.ThrowIfCancellationRequested();
            transaction = Guid.NewGuid().ToString("N");
            if (audit != null) audit.TransactionId = transaction;
            pending = new InstallationJournal { Transaction = transaction, Kind = "uninstall", Receipts = new List<InstallationReceipt> { selected.Receipt } };
            // Quarantine uses the new operation ID, receipt ownership remains original.
            string quarantine = Path.Combine(work, transaction, folder);
            CheckLinks(quarantine); Directory.CreateDirectory(Path.GetDirectoryName(quarantine));
            Write(Journal(transaction), pending, MaxJournal); journalWritten = true;
            audit?.Event("uninstall.journal_prepared", package: package); FaultPoint?.Invoke("journal");
            VerifyDirectory(selected.Target, selected.Receipt, token); CheckUnused(selected.Receipt);
            token.ThrowIfCancellationRequested();
            Directory.Move(selected.Target, quarantine);
            audit?.Event("uninstall.quarantined", package: package); FaultPoint?.Invoke("quarantined");
            VerifyDirectory(quarantine, selected.Receipt, CancellationToken.None);
            // Ownership is checked again before deletion. No saves/settings are removed.
            DeleteOwned(quarantine, selected.Receipt, true, () => FaultPoint?.Invoke("deleted"));
            File.Delete(Record(selected.Receipt));
            Directory.Delete(Path.GetDirectoryName(quarantine));
            File.Delete(Journal(transaction)); journalWritten = false; pending = null;
            audit?.Event("uninstall.committed", package: package);
        }

        internal void ValidateUninstall(string folder, CancellationToken token)
        {
            var all = List(token);
            var selected = all.SingleOrDefault(p => p.Receipt.Folder == folder);
            if (selected == null) throw Error("NotManaged", "Only verified store-owned packages can be uninstalled.");
            CheckUnused(selected.Receipt);
            foreach (var other in all.Where(p => p.Receipt.Folder != folder))
                if (other.Package.Dependencies.Any(d => !d.Optional && d.Id == selected.Package.Id) ||
                    other.Package.Modules.Any(m => m.DependsOn.Any(id => selected.Package.Modules.Any(own => own.Id == id))) ||
                    other.Package.ExternalMods.Any(m => m.PackageId == selected.Package.RimWorldPackageId))
                    throw Error("PackageStillRequired", "Uninstall its dependent package first: " + other.Package.Id);
            CheckExternalDependents(selected.Package.RimWorldPackageId, selected.Target);
        }

        internal void Recover(CancellationToken token)
        {
            RequireWritable();
            string[] files = Directory.GetFiles(journals, "*.json");
            if (files.Length > 64) throw Error("RecoveryRequired", "Too many unfinished installation transactions; inspect manually.");
            foreach (string path in files.OrderBy(p => p, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var journal = Read<InstallationJournal>(path, MaxJournal);
                if (journal.Schema != 1 || !IsTransaction(journal.Transaction) || path != Journal(journal.Transaction) ||
                    (journal.Kind != "install" && journal.Kind != "install-rollback" && journal.Kind != "uninstall") || journal.Receipts == null || journal.Receipts.Count < 1 || journal.Receipts.Count > 64 ||
                    journal.Receipts.Select(r => r.Folder).Distinct(StringComparer.Ordinal).Count() != journal.Receipts.Count)
                    throw Error("InvalidInstallationJournal", "An unfinished journal is invalid; files were preserved.");
                foreach (var r in journal.Receipts) { Validate(r); if (journal.Kind.StartsWith("install", StringComparison.Ordinal) && r.Transaction != journal.Transaction) throw Error("InvalidInstallationJournal", "Journal ownership is inconsistent."); }
                if (audit != null) audit.TransactionId = journal.Transaction;
                audit?.Event("recovery.started", stage: journal.Kind.StartsWith("install", StringComparison.Ordinal) ? "Install" : "Uninstall");
                if (journal.Kind.StartsWith("install", StringComparison.Ordinal))
                {
                    bool rollback = journal.Kind == "install-rollback";
                    bool complete = !rollback && journal.Receipts.All(r => Directory.Exists(Target(r)));
                    foreach (var r in journal.Receipts)
                    {
                        CheckLinks(Target(r)); CheckLinks(Staged(r));
                        if (File.Exists(Target(r)) || File.Exists(Staged(r))) throw Error("OwnershipChanged", "A transaction directory was replaced by a file; preserve it.");
                        if (Directory.Exists(Target(r))) VerifyDirectory(Target(r), r, token, rollback);
                        if (Directory.Exists(Staged(r))) VerifyDirectory(Staged(r), r, token, rollback);
                    }
                    if (complete)
                    {
                        foreach (var r in journal.Receipts)
                            if (File.Exists(Record(r)))
                            {
                                var owned = Read<InstallationReceipt>(Record(r), MaxRecord); Validate(owned);
                                if (owned.Transaction != r.Transaction || CatalogReader.Hash(owned.Catalog) != CatalogReader.Hash(r.Catalog))
                                    throw Error("RecoveryRequired", "Installed record ownership changed; preserve it.");
                            }
                        foreach (var r in journal.Receipts) Write(Record(r), r, MaxRecord);
                        audit?.Event("recovery.install_completed");
                    }
                    else
                    {
                        // Verify all targets before removing any, and never touch loaded code.
                        foreach (var r in journal.Receipts) if (Directory.Exists(Target(r))) CheckUnused(r);
                        if (!rollback)
                        {
                            journal.Kind = "install-rollback";
                            Write(path, journal, MaxJournal); // Intent precedes resumable file deletion.
                        }
                        foreach (var r in journal.Receipts)
                        {
                            if (Directory.Exists(Target(r))) DeleteOwned(Target(r), r, true, () => FaultPoint?.Invoke("recoverydeleted"));
                            if (File.Exists(Record(r)))
                            {
                                var owned = Read<InstallationReceipt>(Record(r), MaxRecord); Validate(owned);
                                if (owned.Transaction != r.Transaction) throw Error("RecoveryRequired", "Installation ownership changed; preserve the record.");
                                File.Delete(Record(r));
                            }
                        }
                        audit?.Event("recovery.install_rolled_back");
                    }
                    foreach (var r in journal.Receipts) if (Directory.Exists(Staged(r))) DeleteOwned(Staged(r), r, journal.Kind == "install-rollback", () => FaultPoint?.Invoke("recoverydeleted"));
                }
                else
                {
                    if (journal.Receipts.Count != 1) throw Error("InvalidInstallationJournal", "Uninstallation removes one package at a time.");
                    var r = journal.Receipts[0]; string quarantine = Path.Combine(work, journal.Transaction, r.Folder);
                    CheckLinks(Target(r)); CheckLinks(quarantine);
                    if (File.Exists(Target(r)) || File.Exists(quarantine)) throw Error("OwnershipChanged", "An uninstall directory was replaced by a file; preserve it.");
                    if (Directory.Exists(Target(r)))
                    {
                        VerifyDirectory(Target(r), r, token);
                        if (Directory.Exists(quarantine)) throw Error("RecoveryRequired", "Both target and quarantine exist; files were preserved.");
                        // Crash before move: abort the uninstall, keep the installed record.
                        audit?.Event("recovery.uninstall_aborted");
                    }
                    else
                    {
                        if (Directory.Exists(quarantine)) { CheckUnused(r); DeleteOwned(quarantine, r, true); }
                        if (File.Exists(Record(r)))
                        {
                            var owned = Read<InstallationReceipt>(Record(r), MaxRecord); Validate(owned);
                            if (owned.Transaction != r.Transaction) throw Error("RecoveryRequired", "Uninstall ownership changed; preserve the record.");
                            File.Delete(Record(r));
                        }
                        audit?.Event("recovery.uninstall_completed");
                    }
                }
                string operationRoot = Path.Combine(work, journal.Transaction);
                CheckLinks(operationRoot);
                if (Directory.Exists(operationRoot)) Directory.Delete(operationRoot); // Only empty, never recursive unknown data.
                File.Delete(path);
            }
        }

        private void RequireNoJournal()
        { if (Directory.Exists(journals) && Directory.GetFiles(journals, "*.json").Length != 0) throw Error("RecoveryRequired", "Recover unfinished store operations before changing packages."); }
        private void RequireWritable()
        { if (readOnly) throw new InvalidOperationException("Read-only package inspection cannot change files."); }
        private static bool SameArtifact(GitHubArtifact a, GitHubArtifact b)
        {
            return a.Repository == b.Repository && a.RepositoryId == b.RepositoryId && a.OwnerId == b.OwnerId &&
                a.SourceCommit == b.SourceCommit && a.Tag == b.Tag && a.ReleaseId == b.ReleaseId && a.AssetId == b.AssetId &&
                a.AssetName == b.AssetName && a.PayloadKind == b.PayloadKind && a.Sha256 == b.Sha256 &&
                a.ManifestSha256 == b.ManifestSha256 && a.SizeBytes == b.SizeBytes;
        }
        private void EnsureNew(InstallationReceipt receipt)
        {
            string target = Target(receipt); CheckLinks(target);
            if (Directory.Exists(target) || File.Exists(target) || File.Exists(Record(receipt))) throw Error("InstallationConflict", "A target or ownership record already exists; first version never overwrites packages.");
            var package = receipt.Package;
            if (environment.InstalledMods.Any(m => m.PackageId == package.RimWorldPackageId) ||
                environment.LoadedAssemblies.Any(a => package.Assemblies.Any(p => p.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase))))
                throw Error("LocalIdentityConflict", "An installed or loaded copy conflicts with " + package.Id);
            foreach (string dir in Directory.GetDirectories(mods))
            {
                CheckLinks(dir); string about = Path.Combine(dir, "About", "About.xml");
                if (File.Exists(about) && ReadAbout(about).Element("packageId")?.Value.Trim().Equals(package.RimWorldPackageId, StringComparison.OrdinalIgnoreCase) == true)
                    throw Error("LocalIdentityConflict", "A local mod already owns package ID " + package.RimWorldPackageId);
            }
        }
        private void CheckUnused(InstallationReceipt receipt)
        {
            PackageRecord p = receipt.Package; string target = Target(receipt);
            if (environment.InstalledMods.Any(m => m.PackageId == p.RimWorldPackageId && m.Enabled) ||
                environment.LoadedAssemblies.Any(a => (a.SourceModRoot != null && SamePath(a.SourceModRoot, target)) || p.Assemblies.Any(assembly => string.Equals(a.Name, assembly.Name, StringComparison.OrdinalIgnoreCase))) ||
                environment.Modules.Any(m => (m.SourceModRoot != null && SamePath(m.SourceModRoot, target)) && m.Active))
                throw Error("PackageInUse", "Disable this package in RimWorld's Mods list, restart the game, then uninstall. Disabling its extension alone does not unload its DLL.");
        }
        private void CheckExternalDependents(string id, string excluded)
        {
            foreach (string root in environment.InstalledMods.Select(m => m.RootDirectory).Concat(Directory.GetDirectories(mods)).Distinct(StringComparer.Ordinal))
            {
                if (SamePath(root, excluded)) continue;
                string about = Path.Combine(root, "About", "About.xml");
                if (!File.Exists(about)) continue;
                XElement xml = ReadAbout(about);
                if (xml.Elements().Where(e => e.Name.LocalName == "modDependencies" || e.Name.LocalName == "modDependenciesByVersion")
                    .SelectMany(e => e.Descendants("packageId")).Any(e => string.Equals(e.Value.Trim(), id, StringComparison.OrdinalIgnoreCase)))
                    throw Error("PackageStillRequired", "Another local or Workshop mod declares this dependency.");
            }
        }
        private static XElement ReadAbout(string path)
        {
            CheckLinks(path);
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > 1024 * 1024) throw Error("LocalMetadataInvalid", "Local About.xml is too large; package identity is uncertain.");
                using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }))
                    return XElement.Load(reader);
            }
        }
        private static void Validate(InstallationReceipt r)
        {
            if (r == null || r.Schema != 1 || !IsTransaction(r.Transaction) || r.Catalog == null || r.Catalog.Length > CatalogReader.MaxCatalogBytes ||
                !Digest(r.EndpointKey) || r.Files == null || r.Files.Count < 1 || r.Files.Count > PayloadValidator.MaxEntries)
                throw Error("InvalidOwnershipRecord", "Invalid installation ownership record; files were preserved.");
            CatalogSnapshot catalog = CatalogReader.Read(r.Catalog, r.Source);
            if (catalog.Packages.Count != 1 || catalog.Packages[0].IsWorkshop || catalog.Packages[0].Artifact.PayloadKind != "rimworld-mod-zip" ||
                r.Folder != FolderName(r.Source, catalog.Packages[0].Id)) throw Error("InvalidOwnershipRecord", "Ownership identities disagree.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            foreach (var file in r.Files)
            {
                if (file == null || file.Path == Stamp || file.Length < 0 || file.Length > PayloadValidator.MaxEntryBytes || !Digest(file.Sha256) ||
                    !paths.Add(PayloadValidator.ValidatePath(file.Path, false))) throw Error("InvalidOwnershipRecord", "Invalid owned file list.");
                expanded = checked(expanded + file.Length);
            }
            if (expanded > PayloadValidator.MaxExpandedBytes || !paths.Contains("phinix-package.json") || !paths.Contains("About/About.xml"))
                throw Error("InvalidOwnershipRecord", "Incomplete owned file list.");
        }
        private static void VerifyDirectory(string directory, InstallationReceipt receipt, CancellationToken token, bool allowMissing = false)
        {
            Validate(receipt); CheckLinks(directory);
            if (!Directory.Exists(directory)) throw Error("OwnedPackageMissing", "Store-owned package directory is missing.");
            string stamp = Path.Combine(directory, Stamp); CheckLinks(stamp);
            var actual = EnumerateFiles(directory, token, receipt.Files.Select(f => f.Path));
            bool hasStamp = File.Exists(stamp);
            if ((!hasStamp && (!allowMissing || actual.Count != 0)) || (hasStamp && (new FileInfo(stamp).Length > 128 ||
                File.ReadAllText(stamp, Encoding.UTF8) != receipt.Transaction + "\n" + CatalogReader.Hash(receipt.Catalog))))
                throw Error("OwnershipChanged", "The package ownership marker is missing or changed; files were preserved.");
            var expected = new HashSet<string>(receipt.Files.Select(f => f.Path), StringComparer.Ordinal); expected.Add(Stamp);
            if (allowMissing ? !actual.IsSubsetOf(expected) : !actual.SetEquals(expected)) throw Error("OwnedPackageModified", "The installed file set changed; automatic deletion is blocked.");
            foreach (var item in receipt.Files)
            {
                token.ThrowIfCancellationRequested(); string path = ResolveFile(directory, item.Path);
                if (allowMissing && !File.Exists(path)) continue;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var hash = SHA256.Create())
                {
                    if (stream.Length != item.Length) throw Error("OwnedPackageModified", "An installed file changed length; files were preserved.");
                    byte[] buffer = new byte[64 * 1024]; int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, read, buffer, 0); }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    if (Hex(hash.Hash) != item.Sha256) throw Error("OwnedPackageModified", "An installed file changed; files were preserved.");
                }
            }
            string manifestPath = ResolveFile(directory, "phinix-package.json");
            if (!allowMissing || File.Exists(manifestPath)) CatalogReader.VerifyManifest(receipt.Package, File.ReadAllBytes(manifestPath));
        }
        private static HashSet<string> EnumerateFiles(string root, CancellationToken token, IEnumerable<string> files)
        {
            var directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in files)
                for (int slash = file.IndexOf('/'); slash >= 0; slash = file.IndexOf('/', slash + 1)) directories.Add(file.Substring(0, slash));
            var result = new HashSet<string>(StringComparer.Ordinal); var todo = new Stack<string>(); todo.Push(root); int dirs = 0;
            while (todo.Count != 0)
            {
                token.ThrowIfCancellationRequested(); string dir = todo.Pop(); CheckLinks(dir);
                if (++dirs > PayloadValidator.MaxEntries * 16) throw Error("OwnedPackageModified", "Too many package directories.");
                foreach (string child in Directory.GetDirectories(dir))
                {
                    CheckLinks(child);
                    if (!directories.Contains(child.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/')))
                        throw Error("OwnedPackageModified", "An unrecorded directory was added; preserve it.");
                    todo.Push(child);
                }
                foreach (string file in Directory.GetFiles(dir))
                {
                    CheckLinks(file); if (result.Count >= PayloadValidator.MaxEntries + 1) throw Error("OwnedPackageModified", "Too many package files.");
                    result.Add(file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
            return result;
        }
        private static void DeleteOwned(string path, InstallationReceipt r, bool partial = false, Action deleted = null)
        {
            VerifyDirectory(path, r, CancellationToken.None, partial);
            // The marker is last. A crash while deleting known files leaves enough
            // evidence to verify the remaining subset and finish quarantine cleanup.
            foreach (var file in r.Files)
            {
                string owned = ResolveFile(path, file.Path);
                if (File.Exists(owned)) { File.Delete(owned); deleted?.Invoke(); }
            }
            VerifyDirectory(path, r, CancellationToken.None, true);
            string stamp = Path.Combine(path, Stamp); CheckLinks(stamp);
            if (File.Exists(stamp)) File.Delete(stamp);
            Directory.Delete(path, true); // Verified empty, recorded directories only.
        }
        private static void WriteStamp(string path, InstallationReceipt r)
        {
            using (var file = new FileStream(Path.Combine(path, Stamp), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { byte[] bytes = Encoding.UTF8.GetBytes(r.Transaction + "\n" + CatalogReader.Hash(r.Catalog)); file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        }
        private static string ResolveFile(string directory, string relative)
        {
            PayloadValidator.ValidatePath(relative, false);
            string path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!ClientPathOwnership.Contains(directory, path)) throw Error("UnsafeInstallationPath", "File leaves its owned directory.");
            CheckLinks(path); return path;
        }
        internal static void CheckLinks(string path)
        {
            for (var current = new FileInfo(Path.GetFullPath(path)); current != null; current = current.Directory == null ? null : new FileInfo(current.Directory.FullName))
            {
                if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw Error("UnsafeInstallationPath", "Installation paths must not traverse symbolic links or junctions.");
            }
        }
        private static void ProbeWritable(string dir)
        {
            string path = Path.Combine(dir, ".phinix-write-" + Guid.NewGuid().ToString("N"));
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) file.Flush(true);
        }
        private static T Read<T>(string path, int limit)
        {
            CheckLinks(path);
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length > limit || file.Length < 1) throw Error("InvalidInstallationRecord", "Installation metadata exceeds its bound.");
                    byte[] bytes = new byte[(int)file.Length]; int offset = 0, count;
                    while (offset < bytes.Length && (count = file.Read(bytes, offset, bytes.Length - offset)) != 0) offset += count;
                    if (offset != bytes.Length) throw Error("InvalidInstallationRecord", "Installation metadata was truncated.");
                    CatalogReader.ValidateDocumentBoundary(bytes);
                    using (var frozen = new MemoryStream(bytes, false)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(frozen);
                }
            }
            catch (StoreValidationException ex) when (ex.Code == "InvalidJson") { throw new StoreValidationException("InvalidInstallationRecord", "Installation metadata is corrupt; files were preserved.", ex); }
            catch (SerializationException ex) { throw new StoreValidationException("InvalidInstallationRecord", "Installation metadata is corrupt; files were preserved.", ex); }
        }
        private static void Write<T>(string path, T value, int limit)
        {
            CheckLinks(path); string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var memory = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(memory, value);
                    if (memory.Length > limit) throw Error("InstallationRecordLimit", "Installation metadata exceeds its bound.");
                    using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { memory.Position = 0; memory.CopyTo(file); file.Flush(true); }
                }
                CheckLinks(path);
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        private static bool SamePath(string a, string b) => string.Equals(a, b, ClientPathOwnership.Comparison);
        private static bool IsTransaction(string value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "\\A[a-f0-9]{32}\\z");
        private static bool Digest(string value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "\\A[a-f0-9]{64}\\z");
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static StoreValidationException Error(string code, string message) => new StoreValidationException(code, message);
        public void Dispose()
        {
            try
            {
                // Before an intent journal, no visible target is owned. Only this
                // operation's scratch data is removed; recovery handles logged moves.
                if (!journalWritten && transaction != null && pending != null)
                {
                    string root = Path.Combine(work, transaction); CheckLinks(root);
                    if (Directory.Exists(root))
                    {
                        var todo = new Stack<string>(); todo.Push(root);
                        while (todo.Count != 0)
                        {
                            string directory = todo.Pop(); CheckLinks(directory);
                            if (!scratchDirectories.Contains(directory)) throw Error("StageOwnershipChanged", "Unknown scratch directory; cleanup stopped.");
                            foreach (string child in Directory.GetDirectories(directory)) todo.Push(child);
                            foreach (string file in Directory.GetFiles(directory))
                            {
                                CheckLinks(file);
                                if (!scratchFiles.Contains(file)) throw Error("StageOwnershipChanged", "Unknown scratch file; cleanup stopped.");
                            }
                        }
                        foreach (string file in scratchFiles) { CheckLinks(file); if (File.Exists(file)) File.Delete(file); }
                        Directory.Delete(root, true); // Checked tree contains only this preparation's paths.
                    }
                    audit?.Event("install.preparation_cleaned");
                }
            }
            finally { lease?.Dispose(); lease = null; }
        }
    }
}
