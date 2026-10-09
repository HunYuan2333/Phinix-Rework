using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Utils.Framework.ManagedExtensions;

internal static class OfflinePreflight
{
    internal static void Validate(string[] args)
    {
        if(args.Length<2 || args.Length%2!=0) throw new ArgumentException("Use --validate ZIP [--host-assembly DLL ...].");
        var host=new List<ManagedAssemblyIdentity>();
        for(int i=2;i<args.Length;i+=2)
        {
            if(args[i]!="--host-assembly") throw new ArgumentException("Unknown preflight option: "+args[i]);
            host.Add(ManagedAssemblyIdentity.FromAssemblyName(AssemblyName.GetAssemblyName(args[i+1])));
        }
        using(var input=File.OpenRead(args[1]))
        {
            var zip=ManagedExtensionZip.Read(input,CancellationToken.None);
            if(host.Count!=0)
            {
                var owned=zip.Inspected.Assemblies.Select(a=>a.Metadata.Identity).ToArray();
                var unique=host.GroupBy(a=>a.FullName,StringComparer.Ordinal).Select(g=>g.First()).ToArray();
                if(owned.Any(a=>unique.Any(h=>string.Equals(a.Name,h.Name,StringComparison.OrdinalIgnoreCase))))
                    throw new ArgumentException("Package assembly conflicts with a supplied host assembly.");
                foreach(var reference in zip.Inspected.Assemblies.SelectMany(a=>a.Metadata.References))
                {
                    var local=owned.Where(a=>string.Equals(a.Name,reference.Name,StringComparison.OrdinalIgnoreCase)).ToArray();
                    if(local.Length!=0?local.Count(a=>a.FullName==reference.FullName)!=1:ManagedAssemblyIdentity.SelectHostReference(reference,unique)==null)
                        throw new ArgumentException("Unavailable CLR reference: "+reference.FullName);
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new {packageId=zip.Manifest.PackageId,version=zip.Manifest.Version.ToString(),sha256=zip.Sha256,
                manifestSha256=ManagedExtensionPaths.Hash(zip.CopyManifestBytes()),staticValidation="passed",hostReferencesChecked=host.Count!=0,
                modules=zip.Manifest.Modules.Select(m=>m.Id),dependencies=zip.Manifest.Dependencies.Select(d=>new {packageId=d.PackageId,versionRange=d.VersionRange.Text,optional=d.Optional}),
                compatibility=new {rimWorldVersions=zip.Manifest.Compatibility.RimWorldVersions,phinixRange=zip.Manifest.Compatibility.PhinixRange.Text,abstractionsRange=zip.Manifest.Compatibility.AbstractionsRange.Text}}));
        }
    }
}
