using System;
using System.IO;
using System.Linq;
using System.Threading;
using Phinix.PluginStore;
internal static class Program
{
    private static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--managed") return Managed(args.Skip(1).ToArray());
        if (args.Length != 3 && args.Length != 5) { Console.Error.WriteLine("Usage: source-id catalog.json package.zip [stable.json published.json]"); return 2; }
        try
        {
            byte[] bytes = File.ReadAllBytes(args[1]);
            var catalog = CatalogReader.Read(bytes, args[0]);
            if (args.Length == 5) RepositoryMetadata.Verify(RepositoryMetadata.ReadStable(File.ReadAllBytes(args[3]), args[0]), File.ReadAllBytes(args[4]), bytes);
            var package = catalog.Packages.Count == 1 ? catalog.Packages[0] :
                catalog.Packages.Single(p => !p.IsWorkshop && p.Artifact.AssetName == Path.GetFileName(args[2]));
            using (var asset = File.OpenRead(args[2]))
            {
                var report = PayloadValidator.Validate(package, asset, null, CancellationToken.None);
                Console.WriteLine("Validated controlled payload " + package.Id + ": " + report.Files.Count + " files; catalog SHA-256=" + catalog.Sha256);
            }
            return 0;
        }
        catch (StoreValidationException ex) { Console.Error.WriteLine("Rejected: " + ex.Code); return 1; }
        catch (Exception ex) { Console.Error.WriteLine("Check failed: " + ex.GetType().Name); return 1; }
    }
    private static int Managed(string[] args)
    {
        if(args.Length!=3 && args.Length!=5) { Console.Error.WriteLine("Usage: --managed source-id catalog.json package.zip [stable.json published.json]"); return 2; }
        try
        {
            byte[] bytes=File.ReadAllBytes(args[1]); var catalog=ManagedStoreCatalogReader.Read(bytes,args[0]);
            if(args.Length==5) RepositoryMetadata.VerifyManaged(RepositoryMetadata.ReadManagedStable(File.ReadAllBytes(args[3]),args[0]),File.ReadAllBytes(args[4]),bytes);
            var package=catalog.Packages.Single(p=>!p.IsWorkshop && p.Artifact.AssetName==Path.GetFileName(args[2]));
            using(var stream=File.OpenRead(args[2]))
            { var report=ManagedStorePayloadValidator.Validate(package,stream,CancellationToken.None); Console.WriteLine("Managed payload verified: "+package.Id+" "+package.Manifest.Version+"; files="+report.Files.Count+"; sha256="+report.Sha256); }
            return 0;
        }
        catch(StoreValidationException ex) { Console.Error.WriteLine("Rejected: "+ex.Code); return 1; }
        catch(Exception ex) { Console.Error.WriteLine("Check failed: "+ex.GetType().Name); return 1; }
    }
}
