using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Phinix.PluginStore
{
    // Presentation configuration is shipped by the client, never asserted by an author name/tag.
    internal sealed class StoreMaintainerRegistry
    {
        private string sourceId, repositoryId, ownerId, branch;
        private readonly HashSet<string> managed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> workshop = new HashSet<string>(StringComparer.Ordinal);
        internal static StoreMaintainerRegistry Empty => new StoreMaintainerRegistry();

        internal static StoreMaintainerRegistry Load()
        {
            using(var stream=typeof(StoreMaintainerRegistry).Assembly.GetManifestResourceStream("Phinix.PluginStore.Assets.maintainers.json"))
            {
                if(stream==null || stream.Length>16384) throw Error();
                using(var output=new MemoryStream()) { stream.CopyTo(output); return Read(output.ToArray()); }
            }
        }
        internal static StoreMaintainerRegistry Read(byte[] bytes)
        {
            if(bytes==null || bytes.Length==0 || bytes.Length>16384) throw Error();
            var f=CatalogReader.Object(CatalogReader.ReadJson(bytes),"maintainers","schemaVersion","sourceId","indexRepositoryId","indexOwnerId","publicationBranch","managedOrigins","workshopOrigins");
            if(CatalogReader.Integer(Get(f,"schemaVersion"),"schemaVersion",1,1)!=1) throw Error();
            var result=new StoreMaintainerRegistry {
                sourceId=CatalogReader.Identifier(Get(f,"sourceId"),"sourceId"),
                repositoryId=CatalogReader.PositiveId(Get(f,"indexRepositoryId"),"indexRepositoryId"),
                ownerId=CatalogReader.PositiveId(Get(f,"indexOwnerId"),"indexOwnerId"),
                branch=CatalogReader.Text(Get(f,"publicationBranch"),"publicationBranch",128)
            };
            foreach(var node in CatalogReader.Array(Get(f,"managedOrigins"),"managedOrigins",32))
            {
                var m=CatalogReader.Object(node,"managedOrigin","repositoryId","ownerId");
                string key=CatalogReader.PositiveId(Get(m,"repositoryId"),"repositoryId")+":"+CatalogReader.PositiveId(Get(m,"ownerId"),"ownerId");
                if(!result.managed.Add(key)) throw Error();
            }
            foreach(var node in CatalogReader.Array(Get(f,"workshopOrigins"),"workshopOrigins",32))
            {
                var m=CatalogReader.Object(node,"workshopOrigin","workshopId","rimWorldPackageId");
                string key=CatalogReader.PositiveId(Get(m,"workshopId"),"workshopId")+":"+CatalogReader.Identifier(Get(m,"rimWorldPackageId"),"rimWorldPackageId");
                if(!result.workshop.Add(key)) throw Error();
            }
            return result;
        }
        internal bool IsOfficial(ManagedStoreRecord package,ManagedStoreCatalogSnapshot catalog,RepositoryEndpoint endpoint)
        {
            var profile=endpoint?.Profile;
            if(package==null || catalog==null || profile==null || !catalog.Packages.Contains(package) || catalog.SourceId!=sourceId || profile.SourceId!=sourceId ||
                profile.RepositoryId!=repositoryId || profile.OwnerId!=ownerId || profile.PublicationBranch!=branch) return false;
            return package.IsWorkshop?workshop.Contains(package.WorkshopId+":"+package.RimWorldPackageId):
                package.Artifact!=null && managed.Contains(package.Artifact.RepositoryId+":"+package.Artifact.OwnerId);
        }
        private static XElement Get(Dictionary<string,XElement> fields,string key)
        { XElement value; if(!fields.TryGetValue(key,out value)) throw Error(); return value; }
        private static StoreValidationException Error() { return new StoreValidationException("InvalidMaintainerConfiguration","Check bundled maintainer identities."); }
    }
}
