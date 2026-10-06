using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Utils.Framework.ManagedExtensions;

namespace Phinix.PluginStore
{
    // Static validation evidence only. Installation must recheck the frozen input and host facts.
    internal sealed partial class ManagedStorePayloadReport
    {
        private readonly byte[] manifestBytes;
        private readonly Dictionary<string,byte[]> content;
        internal ManagedStorePayloadReport(ManagedStoreRecord package, string hash, byte[] manifest,
            ManagedExtensionInspectedPayload inspected, IEnumerable<ValidatedPayloadFile> files, Dictionary<string,byte[]> content)
        { Package=package; Sha256=hash; manifestBytes=(byte[])manifest.Clone(); Inspected=inspected; Files=StoreCollections.Freeze(files); this.content=content; }
        public ManagedStoreRecord Package { get; }
        public string Sha256 { get; }
        public ManagedExtensionInspectedPayload Inspected { get; }
        public ReadOnlyCollection<ValidatedPayloadFile> Files { get; }
        internal byte[] CopyManifestBytes() { return (byte[])manifestBytes.Clone(); }
    }

    internal static class ManagedStorePayloadValidator
    {
        internal static ManagedStorePayloadReport Validate(ManagedStoreRecord expected, Stream asset, CancellationToken token)
        {
            if(expected==null || expected.IsWorkshop || expected.Manifest==null || expected.Artifact?.PayloadKind!="managed-dll-zip")
                throw new ArgumentException("A locked managed-dll-zip record is required.",nameof(expected));
            if(asset==null || !asset.CanRead) throw new ArgumentException("A readable stream is required.",nameof(asset));
            token.ThrowIfCancellationRequested();
            if(expected.Artifact.SizeBytes<1 || expected.Artifact.SizeBytes>CatalogReader.MaxPackageBytes) throw Error("PayloadLimit");
            byte[] bytes=PayloadValidator.ReadBounded(asset,expected.Artifact.SizeBytes,token);
            if(bytes.LongLength!=expected.Artifact.SizeBytes) throw Error("PayloadSizeMismatch");
            token.ThrowIfCancellationRequested();
            string hash=CatalogReader.Hash(bytes);
            if(hash!=expected.Artifact.Sha256) throw Error("PayloadDigestMismatch");
            try
            {
                using(var input=new MemoryStream(bytes,false))
                using(var zip=new ZipArchive(input,ZipArchiveMode.Read,false))
                {
                    if(zip.Entries.Count>PayloadValidator.MaxEntries) throw Error("PayloadLimit");
                    var declarations=expected.Manifest.Assemblies.Select(a=>a.File).Concat(expected.Manifest.Resources).ToDictionary(f=>f.Path,StringComparer.Ordinal);
                    var allowed=new HashSet<string>(declarations.Keys,StringComparer.Ordinal) { ManagedExtensionManifest.FileName };
                    var allowedDirectories=new HashSet<string>(StringComparer.Ordinal);
                    foreach(string path in allowed)
                        for(int slash=path.IndexOf('/');slash>=0;slash=path.IndexOf('/',slash+1)) allowedDirectories.Add(path.Substring(0,slash));
                    var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    var explicitPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
                    long expanded=0;
                    foreach(var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        bool directory=entry.FullName.EndsWith("/",StringComparison.Ordinal);
                        string path=PayloadValidator.ValidatePath(entry.FullName,directory);
                        int kind=(entry.ExternalAttributes>>16)&0xf000;
                        if((entry.ExternalAttributes&0x448)!=0 || (!directory && (entry.ExternalAttributes&0x10)!=0) ||
                            kind!=0 && kind!=(directory?0x4000:0x8000)) throw Error("UnsafeArchiveEntry");
                        if(!explicitPaths.Add(path)) throw Error("ArchivePathConflict");
                        PayloadValidator.RegisterPath(paths,path,directory);
                        if(directory?!allowedDirectories.Contains(path):!allowed.Contains(path)) throw Error("UnsupportedManagedLayout");
                        if(entry.Length<0 || entry.Length>ManagedExtensionManifestReader.MaxFileBytes || entry.CompressedLength<0 ||
                            entry.CompressedLength>bytes.LongLength || directory && entry.Length!=0) throw Error("PayloadLimit");
                        expanded=checked(expanded+entry.Length);
                        if(expanded>ManagedExtensionManifestReader.MaxExpandedBytes || entry.Length>Math.Max(1024*1024L,entry.CompressedLength*PayloadValidator.MaxCompressionRatio)) throw Error("PayloadLimit");
                        if(!directory) entries.Add(path,entry);
                    }
                    if(!new HashSet<string>(entries.Keys,StringComparer.Ordinal).SetEquals(allowed)) throw Error("ManagedFilesMismatch");
                    byte[] manifest=PayloadValidator.ReadEntry(entries[ManagedExtensionManifest.FileName],ManagedExtensionManifestReader.MaxManifestBytes,token);
                    var actual=ManagedStoreCatalogReader.VerifyManifest(expected,manifest);
                    var files=new List<ValidatedPayloadFile> {new ValidatedPayloadFile(ManagedExtensionManifest.FileName,manifest.Length,CatalogReader.Hash(manifest))};
                    var assemblies=new Dictionary<string,byte[]>(StringComparer.Ordinal);
                    var allContent=new Dictionary<string,byte[]>(StringComparer.Ordinal);
                    var assemblyPaths=new HashSet<string>(actual.Assemblies.Select(a=>a.File.Path),StringComparer.Ordinal);
                    foreach(var declaration in declarations.Values.OrderBy(d=>d.Path,StringComparer.Ordinal))
                    {
                        token.ThrowIfCancellationRequested();
                        var entry=entries[declaration.Path];
                        if(entry.Length!=declaration.Length) throw Error("ManagedFileLengthMismatch");
                        byte[] content=PayloadValidator.ReadEntry(entry,(int)ManagedExtensionManifestReader.MaxFileBytes,token);
                        string digest=CatalogReader.Hash(content);
                        if(digest!=declaration.Sha256) throw Error("ManagedFileDigestMismatch");
                        files.Add(new ValidatedPayloadFile(declaration.Path,content.Length,digest));
                        allContent.Add(declaration.Path,content);
                        if(assemblyPaths.Contains(declaration.Path)) assemblies.Add(declaration.Path,content);
                    }
                    var languages=ExtensionLocalizationCatalog.Load(actual.Localization,file=>allContent[file.Path],token);
                    if(actual.Localization!=null) expected.Localization.VerifyProjection(languages);
                    var inspected=ManagedExtensionPayloadInspector.Inspect(actual,assemblies,token);
                    token.ThrowIfCancellationRequested();
                    return new ManagedStorePayloadReport(expected,hash,manifest,inspected,files.OrderBy(f=>f.Path,StringComparer.Ordinal),allContent);
                }
            }
            catch(ManagedExtensionValidationException ex) { throw new StoreValidationException(ex.Code,"Managed static payload inspection failed.",ex); }
            catch(InvalidDataException ex) { throw new StoreValidationException("InvalidArchive","Malformed managed ZIP.",ex); }
            catch(OverflowException ex) { throw new StoreValidationException("PayloadLimit","Managed ZIP size overflow.",ex); }
        }
        private static StoreValidationException Error(string code) { return new StoreValidationException(code,"Managed ZIP validation rejected: "+code); }
    }
}
