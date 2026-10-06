using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Utils.Framework.ManagedExtensions;

// Maintainer-owned data, generated from declared modules in a built main package.
// No plugin names, capabilities or publisher-specific exceptions live here.
internal sealed class PublicationHostProfile
{
    internal ManagedExtensionVersionRange Range;
    internal Dictionary<string,string[]> Modules;
    internal HashSet<string> Assemblies;

    internal static List<PublicationHostProfile> Read(string path)
    {
        if(new FileInfo(path).Length>262144) throw new InvalidOperationException("HostProfileLimit");
        using(var document=JsonDocument.Parse(File.ReadAllBytes(path)))
        {
            var root=Fields(document.RootElement,"schemaVersion","profiles");
            if(root["schemaVersion"].GetInt32()!=1) throw new InvalidOperationException("HostProfileSchema");
            var result=new List<PublicationHostProfile>();
            foreach(var node in Array(root["profiles"],16))
            {
                var fields=Fields(node,"phinixRange","modules","assemblies");
                var profile=new PublicationHostProfile { Range=ManagedExtensionVersionRange.Parse(Text(fields["phinixRange"],128)),
                    Modules=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase), Assemblies=new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
                foreach(var module in Array(fields["modules"],256))
                {
                    var value=Fields(module,"id","dependsOn"); string id=Id(value["id"]);
                    var dependencies=Array(value["dependsOn"],256).Select(Id).ToArray();
                    if(dependencies.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=dependencies.Length || profile.Modules.ContainsKey(id)) throw new InvalidOperationException("HostProfileDuplicate");
                    profile.Modules.Add(id,dependencies);
                }
                foreach(var assembly in Array(fields["assemblies"],256))
                {
                    var value=Fields(assembly,"name","sha256"); string name=Text(value["name"],256),hash=Text(value["sha256"],64);
                    if(!Regex.IsMatch(hash,"\\A[0-9a-f]{64}\\z") || !profile.Assemblies.Add(name)) throw new InvalidOperationException("HostProfileAssembly");
                }
                if(!PublicationClosure.ValidModuleGraph(profile.Modules)) throw new InvalidOperationException("HostProfileDependencyGraphInvalid");
                result.Add(profile);
            }
            return result;
        }
    }
    internal bool Covers(ManagedExtensionVersionRange range)
    {
        if(range.Exact!=null) return Range.Contains(range.Exact);
        return Range.Exact==null && range.Lower.CompareTo(Range.Lower)>=0 && range.Upper.CompareTo(Range.Upper)<=0;
    }
    private static Dictionary<string,JsonElement> Fields(JsonElement node,params string[] names)
    {
        if(node.ValueKind!=JsonValueKind.Object) throw new InvalidOperationException("HostProfileObject");
        var fields=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var field in node.EnumerateObject())
            if(!names.Contains(field.Name) || fields.ContainsKey(field.Name)) throw new InvalidOperationException("HostProfileField");
            else fields.Add(field.Name,field.Value);
        if(fields.Count!=names.Length) throw new InvalidOperationException("HostProfileField");
        return fields;
    }
    private static IEnumerable<JsonElement> Array(JsonElement node,int limit)
    {
        if(node.ValueKind!=JsonValueKind.Array || node.GetArrayLength()>limit) throw new InvalidOperationException("HostProfileLimit");
        return node.EnumerateArray().ToArray();
    }
    private static string Text(JsonElement node,int limit)
    {
        if(node.ValueKind!=JsonValueKind.String) throw new InvalidOperationException("HostProfileText");
        string value=node.GetString(); if(string.IsNullOrEmpty(value) || value.Length>limit) throw new InvalidOperationException("HostProfileText"); return value;
    }
    private static string Id(JsonElement node)
    {
        string value=Text(node,128); if(!Regex.IsMatch(value,"\\A[a-z0-9][a-z0-9._-]*\\z")) throw new InvalidOperationException("HostProfileId"); return value;
    }
}
