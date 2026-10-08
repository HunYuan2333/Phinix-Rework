using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Utils.Framework.ManagedExtensions;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length!=0 && args[0]=="--export-host-profile") { HostProfileExport.Export(args); return 0; }
            var options=new Dictionary<string,string>(); var files=new Dictionary<string,byte[]>(); var declarations=new List<object>(); var modules=new List<object>();
            var host=new List<ManagedAssemblyIdentity>(); var metadataInputs=new List<ManagedAssemblyMetadata>();
            var resources=new List<object>(); var languagePaths=new List<string>();
            if(args.Length%2!=0) throw new ArgumentException("Expected option/value pairs.");
            for(int i=0;i<args.Length;i+=2)
            {
                if(args[i]=="--host-assembly") { host.Add(ManagedAssemblyIdentity.FromAssemblyName(AssemblyName.GetAssemblyName(args[i+1]))); continue; }
                if(args[i]=="--language-file")
                {
                    using(var input=File.OpenRead(args[i+1]))
                    {
                        if(input.Length<1 || input.Length>ExtensionLocalizationDeclaration.MaxFileBytes) throw new ArgumentException("Language file size limit.");
                        byte[] bytes=new byte[(int)input.Length]; input.ReadExactly(bytes);
                        var language=ExtensionLanguageFile.Read(bytes); string path="Resources/Localization/"+language.Locale+".json";
                        files.Add(path,bytes); languagePaths.Add(path);
                        resources.Add(new {path,length=bytes.Length,sha256=ManagedExtensionPaths.Hash(bytes)});
                    }
                    continue;
                }
                if(args[i]!="--assembly") { options.Add(args[i],args[i+1]); continue; }
                using(var input=File.OpenRead(args[i+1]))
                {
                    if(input.Length<1 || input.Length>ManagedExtensionManifestReader.MaxFileBytes) throw new ArgumentException("Assembly size limit.");
                    byte[] bytes=new byte[(int)input.Length]; input.ReadExactly(bytes); var metadata=ManagedExtensionMetadataReader.Read(bytes); var a=metadata.Identity;
                    metadataInputs.Add(metadata);
                    string path="Assemblies/"+a.Name+".dll"; files.Add(path,bytes);
                    declarations.Add(new {name=a.Name,version=a.Version,culture=a.Culture,publicKeyToken=a.PublicKeyToken,path,length=bytes.Length,sha256=ManagedExtensionPaths.Hash(bytes)});
                    foreach(var module in metadata.Modules) modules.Add(new {id=module.Id,assemblyName=a.Name,entryType=module.EntryType,dependsOn=module.DependsOn});
                }
            }
            string[] required={"--package-id","--name","--version","--output"};
            string[] allowed=required.Concat(new[]{"--default-locale","--bundle-output","--display-output","--abstractions-range"}).ToArray();
            if(required.Any(k=>!options.ContainsKey(k)) || options.Keys.Any(k=>!allowed.Contains(k))) throw new ArgumentException("Use --assembly/--host-assembly/--language-file (repeatable), --package-id, --name, --version, --output, optional --default-locale, --bundle-output, --display-output and --abstractions-range.");
            if(languagePaths.Count==0 && options.ContainsKey("--default-locale")) throw new ArgumentException("Default locale needs language files.");
            if(host.Count!=0)
            {
                var owned=metadataInputs.Select(a=>a.Identity).ToList();
                var hostIdentities=host.GroupBy(a=>a.FullName,StringComparer.Ordinal).Select(g=>g.First()).ToList();
                foreach(var reference in metadataInputs.SelectMany(a=>a.References))
                {
                    var ownReferences=owned.Where(a=>string.Equals(a.Name,reference.Name,StringComparison.OrdinalIgnoreCase)).ToList();
                    bool satisfied=ownReferences.Count!=0?ownReferences.Count(a=>a.FullName==reference.FullName)==1:
                        ManagedAssemblyIdentity.SelectHostReference(reference,hostIdentities)!=null;
                    if(!satisfied)
                        throw new ArgumentException("Unavailable CLR reference (exact package identity or same-major host upgrade required): "+reference.FullName+"; supplied identities: "+string.Join("; ",host.Where(a=>a.Name==reference.Name).Select(a=>a.FullName)));
                }
            }
            object localization=languagePaths.Count==0?null:new {defaultLocale=options.ContainsKey("--default-locale")?ExtensionLocale.Normalize(options["--default-locale"]):null,files=languagePaths};
            var jsonOptions=new JsonSerializerOptions {DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
            byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,management="phinix-dll",packageId=options["--package-id"],name=options["--name"],version=options["--version"],targetFramework="net472",
                compatibility=new {rimWorldVersions=new[]{"1.6"},phinixRange=">=0.9.7 <1.0.0",abstractionsRange=options.ContainsKey("--abstractions-range")?options["--abstractions-range"]:">=1.7.0 <2.0.0"},dependencies=new object[0],externalMods=new object[0],resources,localization,assemblies=declarations,modules},jsonOptions);
            var parsed=ManagedExtensionManifestReader.Read(manifest);
            if(parsed.Assemblies.SelectMany(ManagedExtensionManifestReader.AssemblyNames).Any(name=>host.Any(a=>string.Equals(a.Name,name,StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("Package assembly conflicts with a supplied host assembly.");
            ManagedExtensionPayloadInspector.Inspect(parsed,parsed.Assemblies.ToDictionary(a=>a.File.Path,a=>files[a.File.Path]),CancellationToken.None);
            var languages=ExtensionLocalizationCatalog.Load(parsed.Localization,file=>files[file.Path],CancellationToken.None);
            byte[] display=null;
            if(options.ContainsKey("--display-output"))
            {
                if(parsed.Localization==null) throw new ArgumentException("Display extraction needs at least one language file.");
                display=JsonSerializer.SerializeToUtf8Bytes(new {defaultLocale=languages.DefaultLocale,translations=languages.Languages.OrderBy(l=>l.Locale,StringComparer.Ordinal).ToDictionary(l=>l.Locale,l=>l.Display)},jsonOptions);
                if(File.Exists(options["--display-output"])) throw new IOException("Display output already exists.");
            }
            string bundle=options.ContainsKey("--bundle-output")?Path.GetFullPath(options["--bundle-output"]):null;
            if(bundle!=null && (Directory.Exists(bundle) || File.Exists(bundle))) throw new IOException("Bundle output already exists.");
            if(files.Values.Sum(b=>(long)b.Length)+manifest.Length>ManagedExtensionManifestReader.MaxExpandedBytes) throw new ArgumentException("Expanded package size limit.");
            string destination=Path.GetFullPath(options["--output"]); if(File.Exists(destination)) throw new IOException("Output already exists; use a new version/path."); Directory.CreateDirectory(Path.GetDirectoryName(destination));
            files.Add("manifest.json",manifest);
            using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)) foreach(var pair in files.OrderBy(p=>p.Key,StringComparer.Ordinal))
                {
                    var entry=zip.CreateEntry(pair.Key,CompressionLevel.Optimal); entry.LastWriteTime=new DateTimeOffset(2026,10,5,0,0,0,TimeSpan.Zero); entry.ExternalAttributes=0;
                    using(var stream=entry.Open()) stream.Write(pair.Value,0,pair.Value.Length);
                }
                output.Flush(true);
            }
            byte[] package=File.ReadAllBytes(destination);
            if(display!=null) using(var output=new FileStream(options["--display-output"],FileMode.CreateNew,FileAccess.Write,FileShare.None)) output.Write(display,0,display.Length);
            if(bundle!=null)
            {
                Directory.CreateDirectory(bundle);
                Func<string,string> bundledPath=path=>"Resources/"+parsed.PackageId+"/Localization/"+Path.GetFileName(path);
                var bundledResources=parsed.Localization?.Files.Select(file=>new {path=bundledPath(file.Path),length=file.Length,sha256=file.Sha256}).ToArray();
                var bundledLocalization=parsed.Localization==null?null:new {defaultLocale=parsed.Localization.DefaultLocale,files=languagePaths.Select(bundledPath).ToArray()};
                foreach(var assembly in parsed.Assemblies)
                {
                    string dll=Path.Combine(bundle,assembly.Name+".dll"); File.WriteAllBytes(dll,files[assembly.File.Path]);
                    if(localization!=null) File.WriteAllBytes(dll+".localization.json",JsonSerializer.SerializeToUtf8Bytes(new {schemaVersion=1,assemblyName=assembly.Name,resources=bundledResources,localization=bundledLocalization},jsonOptions));
                }
                foreach(string path in languagePaths)
                { string target=Path.Combine(bundle,bundledPath(path)); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllBytes(target,files[path]); }
            }
            Console.WriteLine(JsonSerializer.Serialize(new {packageId=parsed.PackageId,version=parsed.Version.ToString(),sizeBytes=package.Length,sha256=ManagedExtensionPaths.Hash(package),manifestSha256=ManagedExtensionPaths.Hash(manifest)})); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message); return 1; }
    }
}
