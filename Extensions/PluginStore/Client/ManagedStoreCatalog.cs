using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml.Linq;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    // Deliberately separate from the v1 local-Mod record and installation planner.
    internal sealed class ManagedStoreRecord
    {
        internal ManagedStoreRecord(string id, string name, string author, string license, string summary,
            IEnumerable<string> tags, string state, ManagedExtensionManifest manifest, string declarationHash,
            GitHubArtifact artifact, string modId, string workshopId, IEnumerable<string> gameVersions, ExtensionDisplayLocalization localization=null)
        {
            Id=id; Name=name; Author=author; License=license; Summary=summary; Tags=StoreCollections.Freeze(tags); State=state;
            Manifest=manifest; DeclarationHash=declarationHash; Artifact=artifact; RimWorldPackageId=modId; WorkshopId=workshopId;
            RimWorldVersions=StoreCollections.Freeze(gameVersions); Localization=localization;
        }
        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
        public string License { get; }
        public string Summary { get; }
        public ExtensionDisplayLocalization Localization { get; }
        public string DisplayName(string locale) => Localization?.Resolve("name",locale)??Name;
        public string DisplaySummary(string locale) => Localization?.Resolve("summary",locale)??Summary;
        public string DisplayChangelog(string locale) => Localization?.Resolve("changelog",locale);
        public ReadOnlyCollection<string> Tags { get; }
        public string State { get; }
        public ManagedExtensionManifest Manifest { get; }
        internal string DeclarationHash { get; }
        public GitHubArtifact Artifact { get; }
        public string RimWorldPackageId { get; }
        public string WorkshopId { get; }
        public ReadOnlyCollection<string> RimWorldVersions { get; }
        public bool IsWorkshop => WorkshopId!=null;
        public string WorkshopUrl => IsWorkshop?"https://steamcommunity.com/sharedfiles/filedetails/?id="+WorkshopId:null;
    }

    internal sealed class ManagedStoreCatalogSnapshot
    {
        internal ManagedStoreCatalogSnapshot(string source, string snapshot, string hash, IEnumerable<ManagedStoreRecord> packages)
        { SourceId=source; SnapshotId=snapshot; Sha256=hash; Packages=StoreCollections.Freeze(packages); }
        public string SourceId { get; }
        public string SnapshotId { get; }
        public string Sha256 { get; }
        public ReadOnlyCollection<ManagedStoreRecord> Packages { get; }
    }

    internal static class ManagedStoreCatalogReader
    {
        internal const int SchemaVersion=3;
        internal const int MaxSummaryCharacters=1024;
        internal static ManagedStoreCatalogSnapshot Read(byte[] bytes, string expectedSource)
        {
            if(bytes==null || bytes.Length==0 || bytes.Length>CatalogReader.MaxCatalogBytes) throw Error("DocumentLimit");
            bytes=(byte[])bytes.Clone();
            var fields=CatalogReader.Object(CatalogReader.ReadJson(bytes),"catalog","schemaVersion","sourceId","snapshotId","packages");
            if(CatalogReader.Integer(Get(fields,"schemaVersion"),"schemaVersion",1,int.MaxValue)!=SchemaVersion) throw Error("UnsupportedSchema");
            string source=CatalogReader.Identifier(Get(fields,"sourceId"),"sourceId");
            if(source!=expectedSource) throw Error("SourceMismatch");
            string snapshot=CatalogReader.Hex(Get(fields,"snapshotId"),"snapshotId",40);
            var packages=CatalogReader.Array(Get(fields,"packages"),"packages",1024).Select(ReadPackage).ToList();
            ValidateIdentities(packages);
            return new ManagedStoreCatalogSnapshot(source,snapshot,CatalogReader.Hash(bytes),packages);
        }

        private static ManagedStoreRecord ReadPackage(XElement node)
        {
            var f=CatalogReader.Object(node,"package","id","name","author","license","summary","tags","state","channel","management",
                "manifest","artifact","rimWorldPackageId","workshopId","rimWorldVersions","localization");
            string id=CatalogReader.Identifier(Get(f,"id"),"id");
            string author=CatalogReader.Text(Get(f,"author"),"author",160), license=CatalogReader.Text(Get(f,"license"),"license",128);
            var tags=CatalogReader.Array(Get(f,"tags"),"tags",8).Select(t=>CatalogReader.Identifier(t,"tag")).ToList();
            if(tags.Any(t=>t.Length>32) || tags.Distinct(StringComparer.Ordinal).Count()!=tags.Count) throw Error("InvalidTags");
            string state=CatalogReader.Choice(Get(f,"state"),"state","active","withdrawn","unmaintained");
            string channel=CatalogReader.Choice(Get(f,"channel"),"channel","github-release","steam-workshop");
            string management=CatalogReader.Text(Get(f,"management"),"management",32);
            if(channel=="steam-workshop")
            {
                if(management!="rimworld-mod") throw Error("UnsupportedManagement");
                CatalogReader.Forbid(f,"manifest","artifact","localization");
                string name=CatalogReader.Text(Get(f,"name"),"name",160), summary=CatalogReader.Text(Get(f,"summary"),"summary",MaxSummaryCharacters);
                var versions=CatalogReader.Array(Get(f,"rimWorldVersions"),"rimWorldVersions",16).Select(v=>CatalogReader.Text(v,"rimWorldVersion",32)).ToList();
                if(versions.Count==0 || versions.Distinct().Count()!=versions.Count || versions.Any(v=>!System.Text.RegularExpressions.Regex.IsMatch(v,@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z"))) throw Error("InvalidCompatibility");
                return new ManagedStoreRecord(id,name,author,license,summary,tags,state,null,null,null,
                    CatalogReader.Identifier(Get(f,"rimWorldPackageId"),"rimWorldPackageId"),CatalogReader.PositiveId(Get(f,"workshopId"),"workshopId"),versions);
            }
            if(management!=ManagedExtensionManifest.Management) throw Error("UnsupportedManagement");
            CatalogReader.Forbid(f,"rimWorldPackageId","workshopId","rimWorldVersions","name","summary");
            ExtensionDisplayLocalization localization;
            try { localization=ExtensionDisplayLocalization.Read(Get(f,"localization")); }
            catch(ManagedExtensionValidationException ex) { throw new StoreValidationException(ex.Code,"Invalid catalog localization.",ex); }
            var declared=Get(f,"manifest");
            byte[] manifestBytes=JsonBytes(declared,false);
            ManagedExtensionManifest manifest;
            try { manifest=ManagedExtensionManifestReader.Read(manifestBytes); }
            catch(ManagedExtensionValidationException ex) { throw new StoreValidationException(ex.Code,"Invalid managed catalog manifest.",ex); }
            if(manifest.PackageId!=id) throw Error("ManifestMismatch");
            PackageVersion version;
            if(!PackageVersion.TryParse(manifest.Version.ToString(),out version)) throw Error("InvalidVersion");
            var artifact=CatalogReader.ReadArtifact(Get(f,"artifact"),version,true);
            return new ManagedStoreRecord(id,manifest.Name,author,license,localization.Resolve("summary","en"),tags,state,manifest,CatalogReader.Hash(JsonBytes(declared,true)),artifact,null,null,manifest.Compatibility.RimWorldVersions,localization);
        }

        internal static ManagedExtensionManifest VerifyManifest(ManagedStoreRecord expected, byte[] bytes)
        {
            if(expected==null || expected.IsWorkshop || expected.Artifact==null) throw new ArgumentException("A locked managed package is required.",nameof(expected));
            if(bytes==null || bytes.Length>ManagedExtensionManifestReader.MaxManifestBytes) throw Error("DocumentLimit");
            bytes=(byte[])bytes.Clone();
            if(CatalogReader.Hash(bytes)!=expected.Artifact.ManifestSha256) throw Error("ManifestDigestMismatch");
            ManagedExtensionManifest actual;
            try { actual=ManagedExtensionManifestReader.Read(bytes); }
            catch(ManagedExtensionValidationException ex) { throw new StoreValidationException(ex.Code,"Invalid managed ZIP manifest.",ex); }
            // Object property order is insignificant; array order is part of this strict declaration contract.
            if(CatalogReader.Hash(JsonBytes(CatalogReader.ReadJson(bytes),true))!=expected.DeclarationHash) throw Error("ManifestMismatch");
            return actual;
        }

        private static byte[] JsonBytes(XElement node, bool sort)
        {
            var root=Copy(node,sort); root.Name="root";
            using(var output=new MemoryStream())
            {
                using(var writer=JsonReaderWriterFactory.CreateJsonWriter(output,Encoding.UTF8,false)) root.WriteTo(writer);
                return output.ToArray();
            }
        }
        private static XElement Copy(XElement node, bool sort)
        {
            var copy=new XElement(node.Name,node.Attributes());
            if(!node.HasElements) copy.Value=node.Value;
            else foreach(var child in sort && (string)node.Attribute("type")=="object"?node.Elements().OrderBy(e=>e.Name.LocalName,StringComparer.Ordinal):node.Elements()) copy.Add(Copy(child,sort));
            return copy;
        }
        private static void ValidateIdentities(List<ManagedStoreRecord> packages)
        {
            var versions=new HashSet<string>(StringComparer.Ordinal);
            var first=new Dictionary<string,ManagedStoreRecord>(StringComparer.Ordinal);
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            var identities=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in packages)
            {
                if(!versions.Add(p.Id+"@"+(p.IsWorkshop?"workshop":p.Manifest.Version.ToString()))) throw Error("DuplicateVersion");
                int count; counts.TryGetValue(p.Id,out count); if(count>=32) throw Error("CandidateLimit"); counts[p.Id]=count+1;
                ManagedStoreRecord old;
                if(first.TryGetValue(p.Id,out old))
                {
                    if(p.IsWorkshop!=old.IsWorkshop || p.RimWorldPackageId!=old.RimWorldPackageId || p.WorkshopId!=old.WorkshopId ||
                        !p.IsWorkshop && (p.Artifact.RepositoryId!=old.Artifact.RepositoryId || p.Artifact.OwnerId!=old.Artifact.OwnerId)) throw Error("IdentityChanged");
                }
                else first.Add(p.Id,p);
                if(p.IsWorkshop) Claim(identities,"mod:"+p.RimWorldPackageId,p.Id);
                else
                {
                    foreach(var m in p.Manifest.Modules) Claim(identities,"module:"+m.Id,p.Id);
                    foreach(var a in p.Manifest.Assemblies) Claim(identities,"assembly:"+a.Name,p.Id);
                }
            }
        }
        private static void Claim(Dictionary<string,string> identities, string key, string owner)
        { string old; if(identities.TryGetValue(key,out old) && old!=owner) throw Error("IdentityConflict"); identities[key]=owner; }
        private static XElement Get(Dictionary<string,XElement> fields,string name) { return CatalogReader.Required(fields,name); }
        private static StoreValidationException Error(string code) { return new StoreValidationException(code,"Managed catalog validation rejected: "+code); }
    }
}
