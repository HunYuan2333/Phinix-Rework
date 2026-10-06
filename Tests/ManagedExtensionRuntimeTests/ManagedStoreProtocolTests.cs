using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Phinix.PluginStore;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private const string ManagedSource="test.source";
    private static string ManagedArtifact(byte[] zip,byte[] manifest)
    {
        return "{\"repository\":\"test-owner/release\",\"repositoryId\":\"11\",\"ownerId\":\"12\",\"sourceCommit\":"+Q(new string('a',40))+
            ",\"tag\":\"v1.0.0\",\"releaseId\":\"13\",\"assetId\":\"14\",\"assetName\":\"package.zip\",\"payloadKind\":\"managed-dll-zip\",\"sha256\":"+Q(CatalogReader.Hash(zip))+
            ",\"manifestSha256\":"+Q(CatalogReader.Hash(manifest))+",\"sizeBytes\":"+zip.Length+"}";
    }
    private static string ManagedListing(string manifest,byte[] zip,byte[] rawManifest=null)
    {
        return "{\"id\":\"test.package\",\"author\":\"Test author\",\"license\":\"MIT\",\"localization\":{\"translations\":{\"en-US\":{\"name\":\"Metadata test\",\"summary\":\"A small managed extension.\"}}},\"tags\":[\"utility\"],"+
            "\"state\":\"active\",\"channel\":\"github-release\",\"management\":\"phinix-dll\",\"manifest\":"+manifest+",\"artifact\":"+ManagedArtifact(zip,rawManifest??Utf8(manifest))+"}";
    }
    private static string ManagedCatalog(params string[] packages)
    { return "{\"schemaVersion\":3,\"sourceId\":"+Q(ManagedSource)+",\"snapshotId\":"+Q(new string('b',40))+",\"packages\":["+string.Join(",",packages)+"]}"; }
    private static byte[] ManagedZip(IEnumerable<KeyValuePair<string,byte[]>> files,Action<ZipArchiveEntry> decorate=null,CompressionLevel compression=CompressionLevel.NoCompression)
    {
        using(var output=new MemoryStream())
        {
            using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
                foreach(var pair in files)
                {
                    var entry=zip.CreateEntry(pair.Key,compression); entry.ExternalAttributes=0; decorate?.Invoke(entry);
                    using(var stream=entry.Open()) stream.Write(pair.Value,0,pair.Value.Length);
                }
            return output.ToArray();
        }
    }
    private static void StoreReject(string code,Action action)
    { try { action(); } catch(StoreValidationException ex) { Assert(ex.Code==code,"Expected store "+code+", got "+ex.Code); return; } throw new Exception("Expected store rejection "+code); }
    private static string RepositoryEnvelope(string extra,string catalogHash,int catalogSize,int schema=3)
    { return "{\"schemaVersion\":1,\"sourceId\":"+Q(ManagedSource)+",\"snapshotId\":"+Q(new string('b',40))+",\"catalogSchemaVersion\":"+schema+",\"catalogSha256\":"+Q(catalogHash)+",\"catalogSizeBytes\":"+catalogSize+","+extra+"}"; }

    private static void ManagedStoreProtocolRegression(Dictionary<string,byte[]> code)
    {
        string manifest=Manifest(code); var archive=new Dictionary<string,byte[]>(code) { {"manifest.json",Utf8(manifest)} };
        byte[] zip=ManagedZip(archive); string listing=ManagedListing(manifest,zip); string catalog=ManagedCatalog(listing);
        var snapshot=ManagedStoreCatalogReader.Read(Utf8(catalog),ManagedSource); var selected=snapshot.Packages.Single();
        Assert(snapshot.Sha256==CatalogReader.Hash(Utf8(catalog)) && selected.Manifest.PackageId=="test.package" && selected.RimWorldPackageId==null,"Managed catalog preserves source/hash/manifest without a fabricated Mod ID");
        Assert(selected.Summary=="A small managed extension." && selected.Tags.SequenceEqual(new[]{"utility"}),"Bounded display metadata is part of the catalog");
        string workshop="{\"id\":\"test.workshop\",\"name\":\"Workshop item\",\"author\":\"Test\",\"license\":\"MIT\",\"summary\":\"Subscribe on Steam.\",\"tags\":[],\"state\":\"active\",\"channel\":\"steam-workshop\",\"management\":\"rimworld-mod\",\"rimWorldPackageId\":\"test.fullmod\",\"workshopId\":\"123\",\"rimWorldVersions\":[\"1.6\"]}";
        var mixed=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing,workshop)),ManagedSource);
        Assert(mixed.Packages[1].IsWorkshop && mixed.Packages[1].Manifest==null && mixed.Packages[1].Artifact==null && mixed.Packages[1].WorkshopUrl.EndsWith("id=123"),"Workshop remains an index/link-only route");
        StoreReject("UnsupportedSchema",()=>CatalogReader.Read(Utf8(catalog),ManagedSource));
        StoreReject("UnsupportedSchema",()=>ManagedStoreCatalogReader.Read(Utf8(catalog.Replace("\"schemaVersion\":3","\"schemaVersion\":1")),ManagedSource));
        StoreReject("UnsupportedSchema",()=>ManagedStoreCatalogReader.Read(Utf8(catalog.Replace("\"schemaVersion\":3","\"schemaVersion\":2")),ManagedSource));
        StoreReject("SourceMismatch",()=>ManagedStoreCatalogReader.Read(Utf8(catalog),"different.source"));
        StoreReject("UnknownField",()=>ManagedStoreCatalogReader.Read(Utf8(catalog.Replace("\"summary\":","\"imageUrl\":\"https://example.com/img.png\",\"summary\":")),ManagedSource));
        StoreReject("DuplicateField",()=>ManagedStoreCatalogReader.Read(Utf8(catalog.Replace("\"schemaVersion\":3","\"schemaVersion\":3,\"schemaVersion\":3")),ManagedSource));
        StoreReject("InvalidJson",()=>ManagedStoreCatalogReader.Read(Utf8(catalog+" {}"),ManagedSource));
        StoreReject("DocumentLimit",()=>ManagedStoreCatalogReader.Read(new byte[CatalogReader.MaxCatalogBytes+1],ManagedSource));
        foreach(var pair in new[]{
            Tuple.Create("\"phinix-dll\"","\"rimworld-mod\"","UnsupportedManagement"),
            Tuple.Create("\"managed-dll-zip\"","\"rimworld-mod-zip\"","UnsupportedValue"),
            Tuple.Create("\"managed-dll-zip\"","\"dll-with-manifest\"","UnsupportedValue"),
            Tuple.Create("\"utility\"","\"utility\",\"utility\"","InvalidTags"),
            Tuple.Create("A small managed extension.",new string('x',1025),"InvalidLocalizationText"),
            Tuple.Create("\"state\":\"active\"","\"state\":\"enabled\"","UnsupportedValue"),
            Tuple.Create("\"id\":\"test.package\"","\"id\":\"test.forged\"","ManifestMismatch"),
            Tuple.Create("\"manifest\":","\"rimWorldPackageId\":\"fake.mod\",\"manifest\":","UnexpectedField")})
            StoreReject(pair.Item3,()=>ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing.Replace(pair.Item1,pair.Item2))),ManagedSource));
        StoreReject("UnexpectedField",()=>ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(workshop.Replace("\"tags\":[]","\"tags\":[],\"manifest\":"+manifest))),ManagedSource));
        StoreReject("DuplicateVersion",()=>ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing,listing)),ManagedSource));
        string conflict=listing.Replace("test.package","test.other");
        StoreReject("IdentityConflict",()=>ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing,conflict)),ManagedSource));
        string v2=listing.Replace("\"version\":\"1.0.0\"","\"version\":\"1.1.0\"").Replace("v1.0.0","v1.1.0");
        Assert(ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing,v2)),ManagedSource).Packages.Count==2,"Multiple managed package versions share their declared identities");
        StoreReject("IdentityChanged",()=>ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(listing,v2.Replace("\"ownerId\":\"12\"","\"ownerId\":\"99\""))),ManagedSource));
        var endpoint=new RepositoryEndpoint("https://plugins.example.com",ManagedSource);
        Assert(endpoint.Package(snapshot,selected).AbsoluteUri=="https://plugins.example.com/v1/sources/"+ManagedSource+"/snapshots/"+new string('b',40)+"/packages/test.package/1.0.0/"+selected.Artifact.Sha256+"/package","Managed packages use the fixed hashed resource route");
        StoreReject("DownloadSnapshotMismatch",()=>endpoint.Package(snapshot,ManagedStoreCatalogReader.Read(Utf8(catalog),ManagedSource).Packages.Single()));
        StoreReject("DownloadSnapshotMismatch",()=>endpoint.Package(mixed,mixed.Packages[1]));
        var withdrawn=ManagedStoreCatalogReader.Read(Utf8(catalog.Replace("\"state\":\"active\"","\"state\":\"withdrawn\"")),ManagedSource);
        StoreReject("DownloadSnapshotMismatch",()=>endpoint.Package(withdrawn,withdrawn.Packages.Single()));
        var report=ManagedStorePayloadValidator.Validate(selected,new MemoryStream(zip,false),CancellationToken.None);
        Assert(report.Files.Count==3 && report.Inspected.Assemblies.Count==2 && report.Sha256==selected.Artifact.Sha256,"Exact managed ZIP bytes and actual net472 DLL declarations are verified");
        byte[] manifestCopy=report.CopyManifestBytes(); manifestCopy[0]=0;
        Assert(report.CopyManifestBytes()[0]=='{',"Returned manifest bytes cannot mutate validation evidence");
        Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Fixture.Managed.Plugin"),"ZIP metadata inspection executes no plugin code");
        byte[] resource=Utf8("package owned text");
        string withResource=manifest.Replace("\"resources\":[]","\"resources\":["+FileEntry("Resources/help.txt",resource)+"]");
        var resourceFiles=new Dictionary<string,byte[]>(archive) { {"Resources/help.txt",resource} }; resourceFiles["manifest.json"]=Utf8(withResource);
        byte[] resourceZip=ManagedZip(resourceFiles);
        var resourceRecord=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(withResource,resourceZip))),ManagedSource).Packages.Single();
        Assert(ManagedStorePayloadValidator.Validate(resourceRecord,new MemoryStream(resourceZip),CancellationToken.None).Files.Count==4,"Explicit package-owned resources are hashed with the manifest and code");
        resourceFiles["Resources/help.txt"]=Utf8("package owned texx"); CheckManagedZipFailure(withResource,resourceFiles,"ManagedFileDigestMismatch");
        resourceFiles["Resources/help.txt"]=Utf8("short"); CheckManagedZipFailure(withResource,resourceFiles,"ManagedFileLengthMismatch");
        byte[] bomb=new byte[2*1024*1024];
        string bombManifest=manifest.Replace("\"resources\":[]","\"resources\":["+FileEntry("Resources/bomb.txt",bomb)+"]");
        var bombFiles=new Dictionary<string,byte[]>(archive) { {"Resources/bomb.txt",bomb} }; bombFiles["manifest.json"]=Utf8(bombManifest);
        byte[] bombZip=ManagedZip(bombFiles,compression:CompressionLevel.Optimal);
        var bombRecord=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(bombManifest,bombZip))),ManagedSource).Packages.Single();
        StoreReject("PayloadLimit",()=>ManagedStorePayloadValidator.Validate(bombRecord,new MemoryStream(bombZip),CancellationToken.None));
        StoreReject("PayloadDigestMismatch",()=>ManagedStorePayloadValidator.Validate(selected,new MemoryStream(zip.Reverse().ToArray()),CancellationToken.None));
        StoreReject("PayloadSizeMismatch",()=>ManagedStorePayloadValidator.Validate(selected,new MemoryStream(zip.Take(zip.Length-1).ToArray()),CancellationToken.None));
        StoreReject("PayloadLimit",()=>ManagedStorePayloadValidator.Validate(selected,new MemoryStream(zip.Concat(new byte[]{0}).ToArray()),CancellationToken.None));
        bool canceled=false; try { ManagedStorePayloadValidator.Validate(selected,new MemoryStream(zip),new CancellationToken(true)); } catch(OperationCanceledException) { canceled=true; }
        Assert(canceled,"Canceled managed payload reads stop before inspection");
        foreach(string forbidden in new[]{"About/About.xml","Defs/item.xml","Patches/patch.xml","Languages/English/Keyed/test.xml","README.md","Assemblies/hidden.dll","Resources/undeclared.txt","manifest.json/child.txt"})
        {
            var changed=new Dictionary<string,byte[]>(archive) { {forbidden,Utf8("unexpected")} };
            CheckManagedZipFailure(manifest,changed,forbidden.StartsWith("manifest.json/")?"ArchivePathConflict":"UnsupportedManagedLayout");
        }
        foreach(string unsafePath in new[]{"../outside.txt","/absolute.txt","Assemblies\\bad.dll","Resources/CON.txt","Resources/a:b.txt"})
        { var changed=new Dictionary<string,byte[]>(archive) { {unsafePath,Utf8("bad")} }; CheckManagedZipFailure(manifest,changed,"UnsafeArchivePath"); }
        var absent=new Dictionary<string,byte[]>(archive); absent.Remove("manifest.json"); CheckManagedZipFailure(manifest,absent,"ManagedFilesMismatch");
        absent=new Dictionary<string,byte[]>(archive); absent.Remove(code.Keys.First()); CheckManagedZipFailure(manifest,absent,"ManagedFilesMismatch");
        var duplicate=archive.Concat(new[]{archive.First()}); CheckManagedZipFailure(manifest,duplicate,"ArchivePathConflict");
        var alias=new Dictionary<string,byte[]>(archive) { {"assemblies/alias.dll",new byte[]{0}} }; CheckManagedZipFailure(manifest,alias,"ArchivePathConflict");
        CheckManagedZipFailure(manifest,archive,"UnsafeArchiveEntry",e=>e.ExternalAttributes=0xa000<<16);
        var extraDirectory=archive.Concat(new[]{new KeyValuePair<string,byte[]>("Resources/",new byte[0])}); CheckManagedZipFailure(manifest,extraDirectory,"UnsupportedManagedLayout");
        var goodDirectory=archive.Concat(new[]{new KeyValuePair<string,byte[]>("Assemblies/",new byte[0])});
        byte[] withDirectory=ManagedZip(goodDirectory); var directoryRecord=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,withDirectory))),ManagedSource).Packages.Single();
        Assert(ManagedStorePayloadValidator.Validate(directoryRecord,new MemoryStream(withDirectory),CancellationToken.None).Files.Count==3,"Declared ancestor directory entries are allowed without granting extra content");
        var changedCode=new Dictionary<string,byte[]>(archive); changedCode[code.Keys.First()]=(byte[])code.Values.First().Clone(); changedCode[code.Keys.First()][100]^=1;
        CheckManagedZipFailure(manifest,changedCode,"ManagedFileDigestMismatch");
        string forged=manifest.Replace("test.managed","test.forged"); var forgedZip=new Dictionary<string,byte[]>(archive); forgedZip["manifest.json"]=Utf8(forged);
        CheckManagedZipFailure(forged,forgedZip,"AssemblyModuleDeclarationMismatch");
        byte[] invalid=new byte[256]; string badPe=manifest.Replace(FileEntry(code.Keys.First(),code.Values.First()).Substring(1),FileEntry(code.Keys.First(),invalid).Substring(1));
        var invalidCode=new Dictionary<string,byte[]>(archive); invalidCode[code.Keys.First()]=invalid; invalidCode["manifest.json"]=Utf8(badPe);
        CheckManagedZipFailure(badPe,invalidCode,"AssemblyMetadataInvalid");
        // Raw digest and parsed declaration both bind; a matching raw hash alone does not permit a forged manifest.
        byte[] forgedBytes=Utf8(forged); byte[] forgedArchive=ManagedZip(forgedZip);
        var mismatch=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,forgedArchive,forgedBytes))),ManagedSource).Packages.Single();
        StoreReject("ManifestMismatch",()=>ManagedStorePayloadValidator.Validate(mismatch,new MemoryStream(forgedArchive),CancellationToken.None));
        byte[] reordered=Utf8(manifest.Replace("\"schemaVersion\":1,\"management\":\"phinix-dll\"","\"management\":\"phinix-dll\",\"schemaVersion\":1"));
        var reorderedFiles=new Dictionary<string,byte[]>(archive); reorderedFiles["manifest.json"]=reordered; byte[] reorderedZip=ManagedZip(reorderedFiles);
        var reorderedRecord=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,reorderedZip,reordered))),ManagedSource).Packages.Single();
        Assert(ManagedStorePayloadValidator.Validate(reorderedRecord,new MemoryStream(reorderedZip),CancellationToken.None).Files.Count==3,"Manifest property order is insignificant after strict parsing");
        byte[] catalogBytes=Utf8(catalog);
        byte[] published=Utf8(RepositoryEnvelope("\"repository\":\"test-owner/index\",\"repositoryId\":\"21\",\"ownerId\":\"22\",\"releaseId\":\"23\",\"assetId\":\"24\",\"assetName\":\"catalog.json\"",CatalogReader.Hash(catalogBytes),catalogBytes.Length));
        byte[] stableBytes=Utf8(RepositoryEnvelope("\"publishedSha256\":"+Q(CatalogReader.Hash(published))+",\"publishedSizeBytes\":"+published.Length,CatalogReader.Hash(catalogBytes),catalogBytes.Length));
        var stable=RepositoryMetadata.ReadManagedStable(stableBytes,ManagedSource);
        Assert(stable.CatalogSchemaVersion==3 && RepositoryMetadata.VerifyManaged(stable,published,catalogBytes).Sha256==snapshot.Sha256,"Versioned catalog is bound through the unchanged stable/published envelope");
        StoreReject("InvalidNumber",()=>RepositoryMetadata.ReadStable(stableBytes,ManagedSource));
        StoreReject("UnsupportedSchema",()=>RepositoryMetadata.Verify(stable,published,catalogBytes));
        StoreReject("InvalidNumber",()=>RepositoryMetadata.ReadManagedStable(Utf8(Encoding.UTF8.GetString(stableBytes).Replace("\"catalogSchemaVersion\":3","\"catalogSchemaVersion\":1")),ManagedSource));
        byte[] wrongPublished=Utf8(Encoding.UTF8.GetString(published).Replace("\"catalogSchemaVersion\":3","\"catalogSchemaVersion\":1"));
        var wrongStable=new RepositoryStable(ManagedSource,new string('b',40),snapshot.Sha256,catalogBytes.Length,CatalogReader.Hash(wrongPublished),wrongPublished.Length,3);
        StoreReject("InvalidNumber",()=>RepositoryMetadata.VerifyManaged(wrongStable,wrongPublished,catalogBytes));
        StoreReject("CatalogDigestMismatch",()=>RepositoryMetadata.VerifyManaged(stable,published,Utf8(catalog.Replace("utility","utilitx"))));
        StoreReject("PublishedDigestMismatch",()=>RepositoryMetadata.VerifyManaged(stable,Utf8(Encoding.UTF8.GetString(published).Replace("\"ownerId\":\"22\"","\"ownerId\":\"29\"")),catalogBytes));
        string examples=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ManagedStoreProtocol");
        var sample=RepositoryMetadata.ReadManagedStable(File.ReadAllBytes(Path.Combine(examples,"stable.example.json")),"example.source");
        var exampleCatalog=RepositoryMetadata.VerifyManaged(sample,File.ReadAllBytes(Path.Combine(examples,"published.example.json")),File.ReadAllBytes(Path.Combine(examples,"catalog.example.json")));
        Assert(exampleCatalog.Packages.Count==2,"Documented example catalog/stable/published hashes agree with the production parser");
        Assert(ManagedStoreCatalogReader.VerifyManifest(exampleCatalog.Packages[0],File.ReadAllBytes(Path.Combine(examples,"manifest.example.json"))).PackageId=="example.extension","Documented raw manifest hash and parsed declaration agree");
    }
    private static void CheckManagedZipFailure(string manifest,IEnumerable<KeyValuePair<string,byte[]>> files,string expectedCode,Action<ZipArchiveEntry> decorate=null)
    {
        byte[] zip=ManagedZip(files,decorate);
        var expected=ManagedStoreCatalogReader.Read(Utf8(ManagedCatalog(ManagedListing(manifest,zip))),ManagedSource).Packages.Single();
        StoreReject(expectedCode,()=>ManagedStorePayloadValidator.Validate(expected,new MemoryStream(zip),CancellationToken.None));
    }
}
