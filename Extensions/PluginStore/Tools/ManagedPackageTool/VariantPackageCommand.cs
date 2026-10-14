using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Utils.Framework.ManagedExtensions;

internal static class VariantPackageCommand
{
    internal static void Run(string[] args)
    {
        if(args.Length<5 || args.Length%2!=1)throw new ArgumentException("Use --pack-variants --variant ZIP [--variant ZIP] --output ZIP.");
        string output=null;
        var variants=new List<JsonElement>();
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        for(int i=1;i<args.Length;i+=2)
        {
            if(args[i]=="--output")
            { if(output!=null)throw new ArgumentException("Duplicate output.");output=Path.GetFullPath(args[i+1]);continue; }
            if(args[i]!="--variant" || variants.Count==2)throw new ArgumentException("Unknown option or too many variants.");
            using(var input=File.OpenRead(args[i+1]))
            {
                var zip=ManagedExtensionZip.Read(input,CancellationToken.None);
                if(zip.Manifest.Variants.Count!=0 || zip.Manifest.Compatibility.RimWorldVersions.Count!=1)throw new ArgumentException("Each input must be a single-game payload.");
                string game=zip.Manifest.Compatibility.RimWorldVersions[0];
                using(var document=JsonDocument.Parse(zip.CopyManifestBytes()))variants.Add(document.RootElement.Clone());
                foreach(var file in zip.Files.Where(f=>f.Path!="manifest.json"))files.Add(game+"/"+file.Path,zip.CopyFile(file.Path));
            }
        }
        if(output==null || variants.Count==0)throw new ArgumentException("Output and at least one variant are required.");
        if(File.Exists(output))throw new IOException("Choose a new immutable output.");
        byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=ManagedExtensionManifest.VariantSchemaVersion,variants});
        var parsed=ManagedExtensionManifestReader.Read(manifest);
        files.Add("manifest.json",manifest);
        byte[] bytes;
        using(var buffer=new MemoryStream())
        {
            using(var archive=new ZipArchive(buffer,ZipArchiveMode.Create,true))
                foreach(var file in files.OrderBy(p=>p.Key,StringComparer.Ordinal))
                {
                    var entry=archive.CreateEntry(file.Key,CompressionLevel.Optimal);
                    entry.LastWriteTime=new DateTimeOffset(2026,10,14,0,0,0,TimeSpan.Zero);entry.ExternalAttributes=0;
                    using(var stream=entry.Open())stream.Write(file.Value,0,file.Value.Length);
                }
            buffer.Position=0;ManagedExtensionZip.Read(buffer,CancellationToken.None);bytes=buffer.ToArray();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes,0,bytes.Length);
        Console.WriteLine(JsonSerializer.Serialize(new {packageId=parsed.PackageId,version=parsed.Version.ToString(),games=parsed.Compatibility.RimWorldVersions,
            variants=parsed.Variants.Count,sha256=ManagedExtensionPaths.Hash(bytes),sizeBytes=bytes.Length}));
    }
}
