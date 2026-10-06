using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;

// Explicit live verification, using the production v3 client path and a new isolated directory.
// Never installs or executes the downloaded assembly.
internal static class ManagedLiveCheck
{
    internal static async Task Check(string[] args)
    {
        bool catalogOnly=args[3]=="--official-catalog-github" || args[3]=="--official-catalog-cf";
        bool official = args[3]=="--official-github" || args[3]=="--official-cf" || catalogOnly;
        var profile = official ? new RepositoryProfile("phinix.official", "HunYuan2333/Phinix-Plugin-Index",
            "1402564805", "64630568", "main", "https://plugins.hunyuan2333.com") : new RepositoryProfile("phinix.managed","HunYuan2333/Phinix-PluginStore-PoC","1403380030","64630568","codex/managed-publication","https://plugins-staging.hunyuan2333.com");
        var endpoint = official || args[3]=="--managed-github" || args[3]=="--managed-cf"
            ? new RepositoryEndpoint(profile,args[3]=="--managed-github" || args[3]=="--official-github" || args[3]=="--official-catalog-github"?RepositoryAccessMethod.GitHub:RepositoryAccessMethod.Cloudflare)
            : new RepositoryEndpoint(args[0],args[1]);
        if(endpoint.SourceId!=args[1] || endpoint.Origin!=args[0]) throw new InvalidOperationException("Use the configured test profile origin/source.");
        string root = ClientEnvironmentPaths.NormalizeAbsolute(args[2]);
        if (Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("Use a new test state directory.");
        var paths = new ClientEnvironmentPaths(Path.Combine(root, "Mods"), Path.Combine(root, "SaveData"));
        var cache = new ManagedRepositoryCache(paths.GetExtensionDataDirectory("phinix.plugin-store"), endpoint);
        var diagnostics = new RepositoryDiagnostics(Console.WriteLine, endpoint.SourceId);
        using (IManagedRepositoryAccess transport = endpoint.AccessMethod==RepositoryAccessMethod.GitHub
            ? (IManagedRepositoryAccess)new GitHubRepositoryAccess(new HttpClientHandler {AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None,UseCookies=false,UseProxy=false},diagnostics)
            : new CloudflareRepositoryAccess(new RepositoryTransport(new HttpClientHandler {AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None,UseCookies=false,UseProxy=false},diagnostics)))
        using (var deadline = new CancellationTokenSource(180000))
        {
            var before = await ManagedRepositoryBrowser.Refresh(endpoint, cache, transport, deadline.Token, true).ConfigureAwait(false);
            if (before.Catalog.SourceId != profile.SourceId) throw new InvalidOperationException("Use the configured controlled source.");
            if(catalogOnly)
            {
                cache.Save(before,deadline.Token);
                var persisted=cache.Read(deadline.Token);
                var fresh=await ManagedRepositoryBrowser.Refresh(endpoint,cache,transport,deadline.Token,true).ConfigureAwait(false);
                if(persisted.Catalog.Sha256!=before.Catalog.Sha256 || fresh.Catalog.Sha256!=before.Catalog.Sha256 || fresh.Catalog.SnapshotId!=before.Catalog.SnapshotId ||
                    fresh.Catalog.Packages.Any(p=>p.Id=="phinix.poc.playtest")) throw new InvalidOperationException("Official cleaned catalog continuity failed.");
                Console.WriteLine("Official catalog/cache/revalidation passed; access="+endpoint.AccessMethod+"; packages="+fresh.Catalog.Packages.Count+"; snapshot="+fresh.Catalog.SnapshotId+"; sha256="+fresh.Catalog.Sha256+"; identity="+endpoint.IdentityKey);
                return;
            }
            string sampleId=official?"phinix.example.basic":"phinix.poc.playtest";
            var package = before.Catalog.Packages.Where(p => p.Id == sampleId && p.State == "active").OrderByDescending(p=>p.Manifest.Version).First();
            if(package.Localization==null || package.DisplayName("zh-CN")==package.DisplayName("en-US") || package.DisplayChangelog("zh-CN")==null || package.DisplaySummary("fr-FR")!=package.DisplaySummary("en-US")) throw new InvalidOperationException("Localized store display/fallback failed.");
            cache.Save(before, deadline.Token);
            var cached = cache.Read(deadline.Token);
            if (cached.Catalog.Sha256 != before.Catalog.Sha256) throw new InvalidOperationException("Cache round trip changed the catalog.");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool rejected=false;
                try { await transport.DownloadManagedPackage(endpoint, before.Catalog, package, paths, cancelled.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { rejected=true; }
                if (!rejected) throw new InvalidOperationException("Pre-cancelled managed download was admitted.");
            }
            var report = await transport.DownloadManagedPackage(endpoint, before.Catalog, package, paths, deadline.Token).ConfigureAwait(false);
            var input = report.InstallationInput(endpoint, before.Catalog);
            var after = await ManagedRepositoryBrowser.Refresh(endpoint, cache, transport, deadline.Token, true).ConfigureAwait(false);
            if (after.Catalog.Sha256 != before.Catalog.Sha256 || after.Catalog.SnapshotId != before.Catalog.SnapshotId)
                throw new InvalidOperationException("Snapshot changed during transfer.");
            string downloads = Path.Combine(paths.GetExtensionDataDirectory("phinix.plugin-store"), "package-downloads");
            if (Directory.Exists(downloads) && Directory.GetFiles(downloads,"*.partial",SearchOption.AllDirectories).Length != 0)
                throw new InvalidOperationException("Managed temporary download survived validation.");
            if (Directory.Exists(paths.LocalModsRoot) || Directory.Exists(Path.Combine(paths.SaveDataRoot,"Phinix","ManagedExtensions")))
                throw new InvalidOperationException("Download-only verification wrote installation files.");
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => package.Manifest.Assemblies.Any(candidate => candidate.Name == a.GetName().Name)))
                throw new InvalidOperationException("Downloaded code was loaded.");
            Console.WriteLine("Live "+endpoint.AccessMethod+" managed metadata/cache, pre-cancellation, fresh pre/post-transfer chain, ZIP/PE validation and temporary cleanup passed; files=" + report.Files.Count + "; sha256=" + report.Sha256 + "; snapshot=" + before.Catalog.SnapshotId);
        }
    }
}
