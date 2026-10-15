using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Utils.Framework.Themes;

namespace Phinix.PluginStore
{
    internal sealed class ThemeStoreRecord
    {
        internal string Id, State, Summary, License, DeclarationHash;
        internal ThemePackageManifest Manifest;
        internal GitHubArtifact Artifact;
        internal ThemePackage Verify(byte[] archive,CancellationToken token)
        {
            using(var stream=new System.IO.MemoryStream(archive,false))
            {
                var package=ThemePackage.Read(stream,token);
                if(package.Length!=Artifact.SizeBytes || package.Sha256!=Artifact.Sha256 || package.ManifestSha256!=Artifact.ManifestSha256 ||
                    DeclarationHash!=CatalogReader.Hash(ManagedStoreCatalogReader.JsonBytes(CatalogReader.ReadJson(package.CopyFile("manifest.json")),true)))
                    throw Error("ThemePayloadMismatch");
                return package;
            }
        }
        private static StoreValidationException Error(string code)=>new StoreValidationException(code,"Theme payload differs from its locked catalog.");
    }
    internal sealed class ThemeStoreCatalog
    {
        internal const int SchemaVersion=4;
        internal string SourceId, SnapshotId, Sha256;
        internal System.Collections.ObjectModel.ReadOnlyCollection<ThemeStoreRecord> Packages;
        internal static ThemeStoreCatalog Read(byte[] bytes,string expectedSource)
        {
            var f=CatalogReader.Object(CatalogReader.ReadJson(bytes),"themeCatalog","schemaVersion","sourceId","snapshotId","packages");
            CatalogReader.Integer(CatalogReader.Required(f,"schemaVersion"),"schemaVersion",SchemaVersion,SchemaVersion);
            string source=CatalogReader.Identifier(CatalogReader.Required(f,"sourceId"),"sourceId");
            if(source!=expectedSource) throw new StoreValidationException("SourceMismatch","Theme source differs from configuration.");
            var identities=new HashSet<string>(StringComparer.Ordinal);
            var records=new List<ThemeStoreRecord>();
            foreach(var node in CatalogReader.Array(CatalogReader.Required(f,"packages"),"packages",256))
            {
                var p=CatalogReader.Object(node,"themePackage","id","state","management","channel","manifest","artifact","summary","license");
                if(CatalogReader.Text(CatalogReader.Required(p,"management"),"management",32)!=ThemePackageManifest.Management ||
                    CatalogReader.Text(CatalogReader.Required(p,"channel"),"channel",32)!="github-release") throw new StoreValidationException("UnsupportedManagement","Themes must be data-only GitHub packages.");
                var declared=CatalogReader.Required(p,"manifest");
                var manifest=ThemePackageManifest.Read(ManagedStoreCatalogReader.JsonBytes(declared,false));
                PackageVersion version;if(!PackageVersion.TryParse(manifest.Version.ToString(),out version)) throw new StoreValidationException("InvalidVersion","Invalid theme version.");
                var record=new ThemeStoreRecord {Id=CatalogReader.Identifier(CatalogReader.Required(p,"id"),"id"),
                    State=CatalogReader.Choice(CatalogReader.Required(p,"state"),"state","active","withdrawn","unmaintained"),Manifest=manifest,
                    Summary=CatalogReader.Text(CatalogReader.Required(p,"summary"),"summary",1024),License=CatalogReader.Text(CatalogReader.Required(p,"license"),"license",128),
                    DeclarationHash=CatalogReader.Hash(ManagedStoreCatalogReader.JsonBytes(declared,true)),
                    Artifact=CatalogReader.ReadArtifact(CatalogReader.Required(p,"artifact"),version,false,true)};
                if(record.Id!=manifest.PackageId || !identities.Add(record.Id+"@"+manifest.Version) || record.Artifact.SizeBytes>ThemePackage.MaximumPackageBytes)
                    throw new StoreValidationException("ThemeCatalogIdentityMismatch","Theme identity is inconsistent or duplicated.");
                records.Add(record);
            }
            return new ThemeStoreCatalog {SourceId=source,SnapshotId=CatalogReader.Hex(CatalogReader.Required(f,"snapshotId"),"snapshotId",40),
                Sha256=CatalogReader.Hash(bytes),Packages=records.AsReadOnly()};
        }
    }
}
