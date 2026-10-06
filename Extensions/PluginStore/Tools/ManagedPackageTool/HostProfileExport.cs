using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Utils.Framework.ManagedExtensions;

internal static class HostProfileExport
{
    internal static void Export(string[] args)
    {
        if(args.Length!=6 || args[0]!="--export-host-profile" || args[2]!="--phinix-range" || args[4]!="--output")
            throw new ArgumentException("Use --export-host-profile EXTENSIONS_DIRECTORY --phinix-range RANGE --output JSON_PATH.");
        var range=ManagedExtensionVersionRange.Parse(args[3]);
        if(File.Exists(args[5])) throw new ArgumentException("Choose a new immutable profile output.");
        var modules=new Dictionary<string,ManagedModuleMetadata>(StringComparer.OrdinalIgnoreCase);
        var assemblies=new List<object>(); long total=0;
        var files=Directory.GetFiles(args[1],"*.dll").OrderBy(path=>path,StringComparer.Ordinal).ToList();
        if(files.Count==0 || files.Count>256) throw new ArgumentException("Host assembly count limit.");
        foreach(string path in files)
        {
            long length=new FileInfo(path).Length; total+=length;
            if(length<1 || length>ManagedExtensionManifestReader.MaxFileBytes || total>ManagedExtensionManifestReader.MaxExpandedBytes)
                throw new ArgumentException("Host profile memory limit.");
            byte[] bytes=File.ReadAllBytes(path); var metadata=ManagedExtensionMetadataReader.Read(bytes);
            assemblies.Add(new {name=metadata.Identity.Name,sha256=ManagedExtensionPaths.Hash(bytes)});
            foreach(var module in metadata.Modules)
            {
                if(modules.ContainsKey(module.Id)) throw new ArgumentException("Duplicate host module.");
                modules.Add(module.Id,module);
            }
        }
        if(modules.Count>256 || modules.Values.Any(m=>m.DependsOn.Any(id=>!modules.ContainsKey(id)))) throw new ArgumentException("Host module closure unavailable.");
        var profile=new {schemaVersion=1,profiles=new[]{new {phinixRange=range.Text,assemblies,
            modules=modules.Values.OrderBy(m=>m.Id,StringComparer.Ordinal).Select(m=>new {id=m.Id,dependsOn=m.DependsOn}).ToArray()}}};
        File.WriteAllBytes(args[5],JsonSerializer.SerializeToUtf8Bytes(profile,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("Host profile discovered without loading plugin code: "+modules.Count+" modules.");
    }
}
