using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Phinix.PluginStore;
using PhinixClient.Framework;
using Utils.Framework.Themes;

// Explicit live verification only; writes to a new isolated directory, never game saves.
internal static class ThemeLiveCheck
{
    internal static async Task Check(string[] args)
    {
        var method=args[0]=="--themes-github"?RepositoryAccessMethod.GitHub:RepositoryAccessMethod.Cloudflare;
        var endpoint=new RepositoryEndpoint(RepositoryProfile.Themes,method);
        string root=ClientEnvironmentPaths.NormalizeAbsolute(args[1]);
        if(Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("Choose a fresh isolated directory.");
        Directory.CreateDirectory(root);
        var audit=new RepositoryDiagnostics(Console.WriteLine,endpoint.SourceId);
        Func<HttpClientHandler> handler=()=>new HttpClientHandler {AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.None,UseCookies=false,UseProxy=false};
        using(var github=new GitHubRepositoryAccess(handler(),audit))
        using(var binary=new RepositoryTransport(handler(),audit))
        using(var accelerated=new CloudflareRepositoryAccess(new RepositoryTransport(handler(),audit)))
        using(var deadline=new CancellationTokenSource(180000))
        using(var store=new ManagedThemePackageStore(Path.Combine(root,"isolated-theme-state"),_=>{}))
        {
            IManagedRepositoryAccess access=method==RepositoryAccessMethod.GitHub?(IManagedRepositoryAccess)github:accelerated;
            var raw=await access.ReadMetadata(endpoint,null,deadline.Token,true).ConfigureAwait(false);
            var stable=RepositoryMetadata.ReadStableCore(raw.Stable.Body,endpoint.SourceId,4);
            endpoint.Profile.VerifyPublished(raw.Published);
            var catalog=RepositoryMetadata.VerifyTheme(stable,raw.Published,raw.Catalog);
            if(catalog.Packages.Count<2) throw new InvalidOperationException("Expected both published themes.");
            int installed=0;
            foreach(var record in catalog.Packages)
            {
                if(record.State!="active")continue;
                var bytes=method==RepositoryAccessMethod.GitHub
                    ?await github.DownloadTheme(endpoint,catalog,record,deadline.Token).ConfigureAwait(false)
                    :await binary.DownloadTheme(endpoint,catalog,record,deadline.Token).ConfigureAwait(false);
                var package=record.Verify(bytes,deadline.Token).WithOrigin(new ThemePackageOrigin(catalog.SourceId,endpoint.IdentityKey,catalog.SnapshotId,catalog.Sha256));
                File.WriteAllBytes(Path.Combine(root,record.Id+"-"+record.Manifest.Version+".zip"),bytes);
                var fresh=await access.ReadMetadata(endpoint,null,deadline.Token,true).ConfigureAwait(false);
                var current=RepositoryMetadata.VerifyTheme(RepositoryMetadata.ReadStableCore(fresh.Stable.Body,endpoint.SourceId,4),fresh.Published,fresh.Catalog);
                if(current.Sha256!=catalog.Sha256) throw new InvalidOperationException("Theme pointer moved during download.");
                store.Install(package,null,"0.10.3",_=>{},deadline.Token);installed++;
                Console.WriteLine("VERIFIED "+record.Id+" "+record.Manifest.Version+" sha256="+package.Sha256+" origin="+package.Origin.SourceId);
            }
            var saved=store.Refresh(deadline.Token);
            if(saved.Diagnostics.Count!=0 || saved.Packages.Count!=installed) throw new InvalidOperationException("Isolated theme inventory did not reload.");
            Console.WriteLine("PASS source="+catalog.SourceId+" snapshot="+catalog.SnapshotId+" catalog="+catalog.Sha256+" packages="+installed+" transport="+method+" isolated-store-only; game acceptance pending");
        }
    }
}
