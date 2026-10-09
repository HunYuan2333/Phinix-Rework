using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal sealed class PackageConfiguration
{
    internal object Compatibility,Dependencies,ExternalMods;
    internal static PackageConfiguration Read(Dictionary<string,string> options)
    {
        var result=new PackageConfiguration {
            Compatibility=new {rimWorldVersions=new[]{"1.6"},phinixRange=">=0.9.7 <1.0.0",
                abstractionsRange=options.ContainsKey("--abstractions-range")?options["--abstractions-range"]:">=1.7.0 <2.0.0"},
            Dependencies=new object[0],ExternalMods=new object[0]};
        string path;
        if(!options.TryGetValue("--config",out path)) return result;
        if(options.ContainsKey("--abstractions-range")) throw new ArgumentException("Use config.compatibility.abstractionsRange with --config.");
        using(var file=File.OpenRead(path))
        {
            if(file.Length<1 || file.Length>65536) throw new ArgumentException("Package config size limit.");
            using(var document=JsonDocument.Parse(file,new JsonDocumentOptions {MaxDepth=16}))
            {
                var root=document.RootElement;
                if(root.ValueKind!=JsonValueKind.Object) throw new ArgumentException("Package config must be an object.");
                var names=new HashSet<string>(StringComparer.Ordinal);
                foreach(var field in root.EnumerateObject())
                    if(!new[]{"compatibility","dependencies","externalMods"}.Contains(field.Name) || !names.Add(field.Name))
                        throw new ArgumentException("Unknown or duplicate package config field: "+field.Name);
                if(!names.Contains("compatibility")) throw new ArgumentException("Package config requires compatibility.");
                result.Compatibility=root.GetProperty("compatibility").Clone();
                if(names.Contains("dependencies")) result.Dependencies=root.GetProperty("dependencies").Clone();
                if(names.Contains("externalMods")) result.ExternalMods=root.GetProperty("externalMods").Clone();
            }
        }
        // The generated manifest is checked by the same strict reader used at installation.
        return result;
    }
}
