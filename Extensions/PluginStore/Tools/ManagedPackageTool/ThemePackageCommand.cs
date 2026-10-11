using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Utils.Framework.Themes;

internal static class ThemePackageCommand
{
    internal static void Run(string[] args)
    {
        if(args[0]=="--validate-theme")
        {
            if(args.Length!=2) throw new ArgumentException("Use --validate-theme ZIP.");
            using(var input=File.OpenRead(args[1])) Report(ThemePackage.Read(input,CancellationToken.None));
            return;
        }
        if(args.Length!=3) throw new ArgumentException("Use --pack-theme CONFIG OUTPUT.zip.");
        string configPath=Path.GetFullPath(args[1]);
        byte[] configBytes=ReadLimited(configPath,65536);
        var f=ThemePackageJson.Object(ThemePackageJson.Read(configBytes,65536),"packageId","name","author","version","phinixRange","themes");
        string id=ThemePackageJson.Id(ThemePackageJson.Get(f,"packageId"),128);
        string name=ThemePackageJson.Text(ThemePackageJson.Get(f,"name"),160), author=ThemePackageJson.Text(ThemePackageJson.Get(f,"author"),160);
        string version=ThemePackageJson.Text(ThemePackageJson.Get(f,"version"),32),range=ThemePackageJson.Text(ThemePackageJson.Get(f,"phinixRange"),128);
        var inputs=new List<ThemePackageInput>();
        foreach(var item in ThemePackageJson.Array(ThemePackageJson.Get(f,"themes"),16))
        {
            var t=ThemePackageJson.Object(item,"id","name","file");
            string variantId=ThemePackageJson.Id(ThemePackageJson.Get(t,"id"),64);
            string variantName=ThemePackageJson.Text(ThemePackageJson.Get(t,"name"),160);
            string file=ThemePackageJson.Text(ThemePackageJson.Get(t,"file"),4096);
            byte[] content=ReadLimited(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath),file)),1048576);
            inputs.Add(new ThemePackageInput(variantId,variantName,content));
        }
        var verified=ThemePackageBuilder.Build(id,name,author,version,range,inputs);
        ThemePackageBuilder.WriteNew(verified,Path.GetFullPath(args[2]));
        Report(verified);
    }
    private static byte[] ReadLimited(string path,int maximum)
    {
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            if(input.Length<1 || input.Length>maximum) throw new ArgumentException("Theme input size limit.");
            var result=new byte[(int)input.Length]; int position=0,count;
            while(position<result.Length && (count=input.Read(result,position,result.Length-position))>0) position+=count;
            if(position!=result.Length || input.ReadByte()!=-1) throw new IOException("Theme input changed during read.");
            return result;
        }
    }
    private static void Report(ThemePackage package)
    {
        Console.WriteLine(JsonSerializer.Serialize(new {management=ThemePackageManifest.Management,packageId=package.Manifest.PackageId,
            version=package.Manifest.Version.ToString(),sizeBytes=package.Length,sha256=package.Sha256,manifestSha256=package.ManifestSha256,
            themes=package.Manifest.Themes.Select(t=>new {id=t.Id,identity=package.Manifest.ThemeIdentity(t),name=t.Name,schemaVersion=t.SchemaVersion}),
            validation="static-data-only",installed=false}));
    }
}
