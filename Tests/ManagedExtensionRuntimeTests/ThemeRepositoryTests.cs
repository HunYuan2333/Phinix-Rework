using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Phinix.PluginStore;
using Utils.Framework.Themes;

internal static partial class Program
{
    private static void ThemeRepositoryRegression()
    {
        var package=ThemePackageBuilder.Build("test.theme","Theme test","Test author","1.0.0",">=0.10.1 <1.0.0",new[]{
            new ThemePackageInput("dark","Dark",Utf8("<PhinixTheme version='2'><color key='ui.text.primary' r='1' g='1' b='1' a='1'/></PhinixTheme>"))});
        byte[] manifest=package.CopyFile("manifest.json");
        string record="{\"id\":\"test.theme\",\"state\":\"active\",\"management\":\"phinix-theme\",\"channel\":\"github-release\",\"summary\":\"Theme fixture\",\"license\":\"MIT\",\"manifest\":"+Encoding.UTF8.GetString(manifest)+",\"artifact\":"+ManagedArtifact(package.CopyArchive(),manifest).Replace("managed-dll-zip","theme-zip")+"}";
        Func<string,string> envelope=rows=>"{\"schemaVersion\":4,\"sourceId\":\"phinix.themes\",\"snapshotId\":"+Q(new string('b',40))+",\"packages\":["+rows+"]}";
        var catalog=ThemeStoreCatalog.Read(Utf8(envelope(record)),"phinix.themes");var selected=catalog.Packages.Single();
        Assert(selected.Verify(package.CopyArchive(),CancellationToken.None).Sha256==package.Sha256,"Theme catalog verifies actual ZIP, XML and locked manifest digests");
        var browseRows=ManagedStoreListing.BuildEntries(null,null,null,"","en",null,catalog);
        var clicked=browseRows.Single().Preferred;
        var rebuilt=ManagedStoreListing.BuildEntries(null,null,null,"","en",null,catalog);
        Assert(ManagedStoreListing.RestoreEntry(rebuilt,clicked)?.RemoteTheme==selected,"Clicking an online-only theme survives the immediate list rebuild and retains the detail/install candidate");
        var refreshed=ThemeStoreCatalog.Read(Utf8(envelope(record)),"phinix.themes");
        Assert(ManagedStoreListing.RestoreEntry(ManagedStoreListing.BuildEntries(null,null,null,"","en",null,refreshed),clicked)?.RemoteTheme==refreshed.Packages.Single(),"A refreshed theme selection binds the current record rather than an obsolete install candidate");
        Assert(ManagedStoreListing.RestoreEntry(ManagedStoreListing.BuildEntries(null,null,null,"no-match","en",null,catalog),clicked)==null,"Filtering out a remote theme clears its detail selection");
        bool rejected=false;try {ThemeStoreCatalog.Read(Utf8(envelope(record)),"phinix.official");}catch {rejected=true;}
        Assert(rejected,"A theme feed cannot reinterpret the official plugin source");
        foreach(var invalid in new[]{envelope(record+","+record),envelope(record.Replace("\"payloadKind\":\"theme-zip\"","\"payloadKind\":\"managed-dll-zip\"")),envelope(record.Replace("\"management\":\"phinix-theme\"","\"management\":\"phinix-dll\""))})
        {rejected=false;try {ThemeStoreCatalog.Read(Utf8(invalid),"phinix.themes");}catch {rejected=true;}Assert(rejected,"Duplicate or reinterpreted theme records are rejected");}
        var corrupt=package.CopyArchive();corrupt[corrupt.Length/2]^=1;rejected=false;
        try {selected.Verify(corrupt,CancellationToken.None);}catch {rejected=true;}
        Assert(rejected,"A damaged theme cannot be installed");
        var origin=new ThemePackageOrigin(catalog.SourceId,RepositoryProfile.Themes.IdentityKey,catalog.SnapshotId,catalog.Sha256);
        Assert(package.Origin==null && package.WithOrigin(origin).Origin==origin,"Adding catalog provenance never mutates a local input package");
        string directory=Path.Combine(Path.GetTempPath(),"theme-origin-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            using(var store=new ManagedThemePackageStore(directory,_=>{}))
            {
                store.Install(package.WithOrigin(origin),null,"0.10.3",_=>{},CancellationToken.None);
                var inventory=store.Refresh(CancellationToken.None);
                Assert(inventory.Diagnostics.Count==0,"A catalog theme receipt is accepted by the real recovery reader");
                var saved=inventory.Packages.Single();
                Assert(saved.SourceId==catalog.SourceId && saved.Origin.RepositoryIdentitySha256==origin.RepositoryIdentitySha256 && saved.Origin.CatalogSha256==catalog.Sha256,"Catalog source identity and snapshot digest survive a storage reload");
                var rows=ManagedStoreListing.BuildEntries(null,null,null,"","en",inventory.Packages,catalog);
                Assert(rows.Length==1 && rows[0].Preferred.IsTheme && rows[0].Preferred.IsInstalled && !rows[0].Preferred.IsInstalledOnly,"Remote and installed themes merge into one store row");
                Assert(ManagedStoreListing.RestoreEntry(rows,rows.Single().Preferred)?.InstalledTheme==saved,"An installed remote theme keeps its details and uninstall/select operations after rebuild");
                Assert(ManagedStoreListing.Filter(rows,2,2,new System.Collections.Generic.HashSet<string>{"test.theme"}).Length==0,"A same-ID DLL update cannot leak into theme update filtering");
                store.Remove(saved.Manifest.PackageId,saved.ArtifactSha256,"",CancellationToken.None);
                Assert(store.Refresh(CancellationToken.None).Packages.Count==0,"Catalog provenance does not redirect physical removal away from the original storage namespace");
            }
        }
        finally {if(Directory.Exists(directory)) Directory.Delete(directory,true);}
        int reads=0;var freshReads=new System.Collections.Generic.List<bool>();
        using(var operation=new ThemeStoreController((m,t,fresh)=>{reads++;freshReads.Add(fresh);return System.Threading.Tasks.Task.FromResult(catalog);},(m,c,p,t)=>System.Threading.Tasks.Task.FromResult(package.CopyArchive())))
        {
            operation.Refresh(RepositoryAccessMethod.GitHub);WaitTheme(operation);
            Assert(operation.Catalog==catalog && operation.Error==null,"Independent theme metadata refresh becomes visible without a plugin catalog");
            operation.Download(RepositoryAccessMethod.GitHub,catalog,selected,null,"0.10.3");WaitTheme(operation);
            string expected;var ready=operation.TakeReady(out expected);
            Assert(ready!=null && ready.Origin.SourceId==catalog.SourceId && expected==null && reads==2,"Download rechecks the live pointer before yielding a main-thread install candidate");
            Assert(operation.TakeReady(out expected)==null,"A completed download can be claimed only once");
            Assert(freshReads.SequenceEqual(new[]{false,true}),"Ordinary theme browsing permits metadata caching while final install authorization forces a live recheck");
        }
        var moved=ThemeStoreCatalog.Read(Utf8(envelope(record).Replace(new string('b',40),new string('c',40))),"phinix.themes");
        reads=0;
        using(var operation=new ThemeStoreController((m,t,fresh)=>System.Threading.Tasks.Task.FromResult(++reads==1?catalog:moved),(m,c,p,t)=>System.Threading.Tasks.Task.FromResult(package.CopyArchive())))
        {
            operation.Refresh(RepositoryAccessMethod.Cloudflare);WaitTheme(operation);
            operation.Download(RepositoryAccessMethod.Cloudflare,catalog,selected,null,"0.10.3");WaitTheme(operation);
            string ignored;Assert(operation.Error=="SnapshotChanged" && operation.TakeReady(out ignored)==null,"A changed or withdrawn publication cannot authorize installation after transfer");
        }
        var late=new System.Threading.Tasks.TaskCompletionSource<ThemeStoreCatalog>();
        var closing=new ThemeStoreController((m,t,fresh)=>late.Task);
        closing.Refresh(RepositoryAccessMethod.GitHub);
        Assert(closing.Busy && !closing.Installing,"Theme metadata refresh does not block independent plugin operations");
        closing.Dispose();late.SetResult(catalog);
        WaitTheme(closing);Assert(closing.Catalog==null,"Late metadata cannot revive a disposed view owner");
        Assert(RepositoryProfile.Themes.IdentityKey!=RepositoryProfile.Official.IdentityKey,"Theme feed ownership is distinct from official plugin ownership");
        Assert(new RepositoryEndpoint(RepositoryProfile.Themes,RepositoryAccessMethod.GitHub).IdentityKey==new RepositoryEndpoint(RepositoryProfile.Themes,RepositoryAccessMethod.Cloudflare).IdentityKey,"Direct and accelerated theme downloads share one source identity");
    }
    private static void WaitTheme(ThemeStoreController operation)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(operation.Busy && DateTime.UtcNow<deadline) Thread.Sleep(1);
        Assert(!operation.Busy,"Theme operation finishes within the fixture deadline");
    }
}
