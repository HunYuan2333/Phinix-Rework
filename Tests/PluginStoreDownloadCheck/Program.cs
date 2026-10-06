using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;

// An explicit live check. Never run automatically by the regression harness or CI.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length>=5 && args[0]=="--local-identities") return LocalIdentityCheck.Check(args);
        if (args.Length == 4 && (args[3] == "--managed" || args[3] == "--managed-github" || args[3] == "--managed-cf" ||
            args[3] == "--official-github" || args[3] == "--official-cf" || args[3]=="--official-catalog-github" || args[3]=="--official-catalog-cf"))
        {
            try { ManagedLiveCheck.Check(args).GetAwaiter().GetResult(); return 0; }
            catch (StoreValidationException error)
            { Console.Error.WriteLine("Rejected: " + error.Code + "; requestId=" + (error.RequestId ?? "unavailable")); return 1; }
            catch (OperationCanceledException) { Console.Error.WriteLine("Check cancelled or timed out."); return 1; }
            catch (Exception error) { Console.Error.WriteLine("Check failed: " + error.GetType().Name); return 1; }
        }
        if (args.Length != 3 && (args.Length != 4 || args[3] != "--install-check"))
        { Console.Error.WriteLine("Usage: HTTPS-origin source-id new-absolute-state-directory [--managed|--managed-github|--managed-cf|--official-github|--official-cf|--install-check]"); return 2; }
        try { Check(args).GetAwaiter().GetResult(); return 0; }
        catch (StoreValidationException error)
        { Console.Error.WriteLine("Rejected: " + error.Code + "; requestId=" + (error.RequestId ?? "unavailable")); return 1; }
        catch (OperationCanceledException) { Console.Error.WriteLine("Check cancelled or timed out."); return 1; }
        catch (Exception error) { Console.Error.WriteLine("Check failed: " + error.GetType().Name); return 1; }
    }

    private static async Task Check(string[] args)
    {
        var endpoint = new RepositoryEndpoint(args[0], args[1]);
        string root = ClientEnvironmentPaths.NormalizeAbsolute(args[2]);
        if (Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("Use a new test state directory.");
        var paths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData"));
        var diagnostics = new RepositoryDiagnostics(Console.WriteLine, endpoint.SourceId);
        var cache = new RepositoryCache(paths.GetExtensionDataDirectory("phinix.plugin-store"), endpoint);
        // This live check explicitly disables system proxies without changing game defaults.
        using (var connection = new RepositoryTransport(new HttpClientHandler {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false, UseProxy = false
        }, diagnostics))
        using (var deadline = new CancellationTokenSource(180000))
        {
            RepositoryCacheEntry first;
            using (var metadataDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                metadataDeadline.CancelAfter(30000);
                first = await RepositoryBrowser.Refresh(endpoint, cache, connection, metadataDeadline.Token).ConfigureAwait(false);
            }
            bool install = args.Length == 4;
            string id = install ? "phinix.poc.playtest" : "phinix.poc.marker";
            if (first.Catalog.SourceId != "phinix.poc" || !first.Catalog.Packages.Any(p => p.Id == id))
                throw new InvalidOperationException("This check requires the controlled PoC package source.");
            cache.Save(first, deadline.Token);
            using (var metadataDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
            {
                metadataDeadline.CancelAfter(30000);
                var second = await RepositoryBrowser.Refresh(endpoint, cache, connection, metadataDeadline.Token).ConfigureAwait(false);
                if (second.Catalog.Sha256 != first.Catalog.Sha256) throw new InvalidOperationException("Snapshot changed during check.");
            }
            var package = first.Catalog.Packages.Single(p => p.Id == id);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool rejected = false;
                try { using (var ignored = await connection.DownloadPackage(endpoint, first.Catalog, package, paths, cancelled.Token).ConfigureAwait(false)) { } }
                catch (OperationCanceledException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Pre-cancelled download was admitted.");
            }
            ManagedInstallation installer = null;
            if (install)
            {
                // Only this newly allocated test directory is writable. Never use
                // the real game Mods directory for the command-line smoke check.
                Directory.CreateDirectory(paths.LocalModsRoot);
                string host = Path.Combine(paths.LocalModsRoot, "fake-host"); Directory.CreateDirectory(Path.Combine(host, "About"));
                File.WriteAllText(Path.Combine(host, "About", "About.xml"), "<ModMetaData><packageId>hunyuan2333.phinixrework</packageId></ModMetaData>");
                var env = new ClientEnvironmentSnapshot(paths, host, "1.6", "0.9.7", "1.3.0",
                    new[] { new ClientInstalledModSnapshot("hunyuan2333.phinixrework", host, true) }, null, null, null);
                installer = new ManagedInstallation(env, diagnostics);
                var input = installer.PlanningInput(first.Catalog, deadline.Token);
                var plan = new DependencyPlanner(first.Catalog, input.Runtime, input.Installed).CreatePlan(id, package.Version.ToString(), deadline.Token);
                installer.BeginInstall(plan, first.CatalogBytes, endpoint, deadline.Token);
            }
            try
            {
            using (var download = await connection.DownloadPackage(endpoint, first.Catalog, package, paths, deadline.Token).ConfigureAwait(false))
            {
                Console.WriteLine("Validated live package: files=" + download.Report.Files.Count + "; sha256=" + package.Artifact.Sha256 +
                    "; requestId=" + (download.RequestId ?? "unavailable"));
                if (install) installer.Stage(download, deadline.Token);
                download.Read(stream => {
                    if (stream.CanWrite || stream.Length != package.Artifact.SizeBytes)
                        throw new InvalidOperationException("Held download file is invalid.");
                });
            }
            if (install)
            {
                var final = await RepositoryBrowser.Refresh(endpoint, cache, connection, deadline.Token).ConfigureAwait(false);
                if (final.Catalog.Sha256 != first.Catalog.Sha256) throw new InvalidOperationException("Snapshot changed before commit.");
                installer.Seal(deadline.Token); installer.Commit(deadline.Token);
                var owned = installer.List(deadline.Token);
                if (owned.Count != 1 || owned[0].Package.Id != id) throw new InvalidOperationException("Owned installation is incomplete.");
                installer.Uninstall(owned[0].Receipt.Folder, deadline.Token);
                installer.Recover(deadline.Token);
                if (installer.List(deadline.Token).Count != 0) throw new InvalidOperationException("Owned uninstall is incomplete.");
                Console.WriteLine("Live held-byte staging, ownership verification, commit and uninstall passed in isolated test Mods.");
            }
            }
            finally { installer?.Dispose(); }
            string downloads = Path.Combine(paths.GetExtensionDataDirectory("phinix.plugin-store"), "package-downloads");
            if (Directory.Exists(downloads) && Directory.GetFiles(downloads, "*.partial", SearchOption.AllDirectories).Length != 0)
                throw new InvalidOperationException("Temporary download survived disposal.");
            if (!install && Directory.Exists(paths.LocalModsRoot)) throw new InvalidOperationException("Live preview wrote to Mods.");
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => package.Assemblies.Any(candidate => candidate.Name == a.GetName().Name)))
                throw new InvalidOperationException("Downloaded assembly was loaded.");
        }
        Console.WriteLine("Live client metadata, cache revalidation, pre-cancellation, ZIP validation and temporary cleanup passed. Optional installation check uses isolated test Mods only.");
    }
}
