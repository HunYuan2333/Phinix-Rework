using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    internal sealed partial class ManagedStorePayloadReport
    {
        private readonly ManagedExtensionZip zip;
        internal ManagedStorePayloadReport(ManagedStoreRecord package,ManagedExtensionZip zip)
        { Package=package; this.zip=zip; Files=StoreCollections.Freeze(zip.Files.Select(f=>new ValidatedPayloadFile(f.Path,f.Length,f.Sha256))); }
        public ManagedStoreRecord Package { get; }
        public string Sha256 => zip.Sha256;
        public ManagedExtensionInspectedPayload Inspected => zip.Inspected;
        public ReadOnlyCollection<ValidatedPayloadFile> Files { get; }
        internal byte[] CopyManifestBytes() => zip.CopyManifestBytes();
    }
    internal static class ManagedStorePayloadValidator
    {
        internal static ManagedStorePayloadReport Validate(ManagedStoreRecord expected,Stream asset,CancellationToken token)
        {
            if(expected==null || expected.IsWorkshop || expected.Manifest==null || expected.Artifact?.PayloadKind!="managed-dll-zip")
                throw new ArgumentException("A locked managed-dll-zip record is required.",nameof(expected));
            if(expected.Artifact.SizeBytes<1 || expected.Artifact.SizeBytes>CatalogReader.MaxPackageBytes)
                throw new StoreValidationException("PayloadLimit","Managed ZIP exceeds size limit.");
            try
            {
                var zip=ManagedExtensionZip.Read(asset,token,expected.Artifact.SizeBytes,expected.Artifact.Sha256,
                    manifest=>ManagedStoreCatalogReader.VerifyManifest(expected,manifest));
                var actual=ManagedStoreCatalogReader.VerifyManifest(expected,zip.CopyManifestBytes());
                if(actual.Localization!=null) expected.Localization.VerifyProjection(zip.Localization);
                return new ManagedStorePayloadReport(expected,zip);
            }
            catch(ManagedExtensionValidationException ex) { throw new StoreValidationException(ex.Code,"Managed static payload inspection failed.",ex); }
        }
    }
}
