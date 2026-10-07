using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void StoreMaintainerRegression(Dictionary<string,byte[]> files)
    {
        string config=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"StorePresentation","maintainers.json"));
        var maintainers=StoreMaintainerRegistry.Read(Utf8(config));
        string listing=ManagedListing(Manifest(files),new byte[]{1,2,3})
            .Replace("\"repositoryId\":\"11\"","\"repositoryId\":\"1406005374\"")
            .Replace("\"ownerId\":\"12\"","\"ownerId\":\"64630568\"");
        string workshop="{\"id\":\"test.workshop\",\"name\":\"Workshop item\",\"author\":\"Test\",\"license\":\"See Workshop page\",\"summary\":\"Subscribe on Steam.\",\"tags\":[],\"state\":\"active\",\"channel\":\"steam-workshop\",\"management\":\"rimworld-mod\",\"rimWorldPackageId\":\"hunyuan2333.phinixrework\",\"workshopId\":\"3735269431\",\"rimWorldVersions\":[\"1.6\"]}";
        Func<string[],ManagedStoreCatalogSnapshot> catalog=items=>ManagedStoreCatalogReader.Read(
            Utf8(ManagedCatalog(items).Replace("\"sourceId\":\"test.source\"","\"sourceId\":\"phinix.official\"")),"phinix.official");
        var trusted=catalog(new[]{listing,workshop});
        var github=new RepositoryEndpoint(RepositoryProfile.Official,RepositoryAccessMethod.GitHub);
        var cf=new RepositoryEndpoint(RepositoryProfile.Official,RepositoryAccessMethod.Cloudflare);
        foreach(var record in trusted.Packages)
        {
            Assert(maintainers.IsOfficial(record,trusted,github),"Fixed managed repository and Workshop identities receive maintainer badges.");
            Assert(maintainers.IsOfficial(record,trusted,cf),"Access adapter switching preserves maintainer identity.");
            Assert(!StoreMaintainerRegistry.Empty.IsOfficial(record,trusted,github),"Missing presentation config safely hides official badges.");
            Assert(!maintainers.IsOfficial(record,trusted,new RepositoryEndpoint("https://example.com","phinix.official")),"An arbitrary endpoint cannot assert official provenance.");
        }
        foreach(string mutant in new[]{
            listing.Replace("1406005374","1406005375"),listing.Replace("64630568","64630569"),
            workshop.Replace("3735269431","3735269432"),workshop.Replace("hunyuan2333.phinixrework","test.forged")})
        {
            var spoof=catalog(new[]{mutant.Replace("Test author","HunYuan2333").Replace("Workshop item","Phinix Rework")});
            Assert(!maintainers.IsOfficial(spoof.Packages.Single(),spoof,github),"Names and authors cannot spoof a mismatching origin identity.");
        }
        Assert(!maintainers.IsOfficial(catalog(new[]{listing}).Packages.Single(),trusted,github),"A record outside the bound catalog cannot gain a badge.");
        var otherSource=new ManagedStoreCatalogSnapshot("other.source",trusted.SnapshotId,trusted.Sha256,trusted.Packages);
        Assert(!maintainers.IsOfficial(trusted.Packages[0],otherSource,github),"A different source cannot reuse maintainer identities.");
        foreach(var profile in new[]{
            new RepositoryProfile("phinix.official","test-owner/index","1","64630568","main","https://example.com"),
            new RepositoryProfile("phinix.official","test-owner/index","1402564805","1","main","https://example.com"),
            new RepositoryProfile("phinix.official","test-owner/index","1402564805","64630568","preview","https://example.com")})
            Assert(!maintainers.IsOfficial(trusted.Packages[0],trusted,new RepositoryEndpoint(profile,RepositoryAccessMethod.GitHub)),"Index repository, owner and publication branch are all bound.");
        foreach(string malformed in new[]{"{}",config.Replace("\"schemaVersion\": 1","\"schemaVersion\": 2"),
            config.Replace("\"managedOrigins\": [","\"managedOrigins\": [{\"repositoryId\":\"1406005374\",\"ownerId\":\"64630568\"},"),
            config.Replace("\"schemaVersion\": 1","\"schemaVersion\": 1,\"unexpected\":true")})
        {
            bool rejected=false;
            try { StoreMaintainerRegistry.Read(Utf8(malformed)); } catch(StoreValidationException) { rejected=true; }
            Assert(rejected,"Malformed presentation config is rejected before rendering badges.");
        }
    }
}
