using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal static class ManagedSampleCheck
{
    internal static int Run(string archive,string game)
    {
        var files=new Dictionary<string,byte[]>();
        using(var zip=ZipFileStream(archive)) foreach(var entry in zip.Entries)
            using(var memory=new MemoryStream()) {using(var input=entry.Open()) input.CopyTo(memory); files.Add(entry.FullName,memory.ToArray());}
        byte[] manifestBytes=files["manifest.json"]; files.Remove("manifest.json");
        var manifest=ManagedExtensionManifestReader.Read(manifestBytes);
        var assemblies=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic).Select(a=>ManagedAssemblyIdentity.FromAssemblyName(a.GetName()))
            .Concat(Directory.EnumerateFiles(game,"*.dll").Select(p=>ManagedAssemblyIdentity.FromAssemblyName(AssemblyName.GetAssemblyName(p))))
            .GroupBy(a=>a.FullName).Select(g=>g.First()).ToList();
        var facts=new ManagedExtensionHostFacts("1.6","0.9.7","1.9.0",assemblies,new string[0],new string[0]);
        var request=new ManagedExtensionInstallRequest(new[]{new ManagedExtensionInstallPackage("sample.test",new string('a',64),new string('b',40),new string('c',64),ManagedExtensionPaths.Hash(File.ReadAllBytes(archive)),manifestBytes,files)});
        string temp=Path.Combine(Path.GetTempPath(),"phinix-real-sample-"+Guid.NewGuid().ToString("N")); var paths=new ManagedExtensionPaths(temp);
        try
        {
            using(var runtime=new ManagedExtensionRuntime(paths))
            {
                runtime.Start(facts,new string[0],new string[0],CancellationToken.None);
                var install=runtime.Install(request,new string[0],CancellationToken.None);
                if(!install.Succeeded) throw new Exception("Actual sample install failed: "+install.Code);
                var inventory=runtime.Refresh(new string[0],CancellationToken.None);
                if(inventory.Packages.Count!=1 || AppDomain.CurrentDomain.GetAssemblies().Any(a=>manifest.Assemblies.Any(m=>m.Name==a.GetName().Name))) throw new Exception("Install must persist exactly one package without executing it");
                var remove=runtime.ChangeDesiredState(inventory.Packages.Single().Package,ManagedExtensionDesiredState.PendingRemoval,new string[0],CancellationToken.None);
                if(!remove.Succeeded) throw new Exception("Sample removal intent failed: "+remove.Code);
            }
            using(var runtime=new ManagedExtensionRuntime(paths))
            {
                runtime.Start(facts,manifest.Modules.Select(m=>m.Id),new string[0],CancellationToken.None);
                if(runtime.Refresh(new string[0],CancellationToken.None).Packages.Count!=0) throw new Exception("Restart must finish owned removal");
                if(!runtime.Install(request,new string[0],CancellationToken.None).Succeeded) throw new Exception("Actual sample reinstall failed");
            }
            Console.WriteLine(manifest.PackageId+": actual managed install/removal/recovery/reinstall passed, candidate never loaded.");
            return 0;
        }
        finally {if(Directory.Exists(temp)) Directory.Delete(temp,true);}
    }
    private static ZipArchive ZipFileStream(string path) {return new ZipArchive(File.OpenRead(path),ZipArchiveMode.Read);}
}
