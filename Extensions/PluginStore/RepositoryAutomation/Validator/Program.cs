using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Phinix.PluginStore;
using Utils.Framework.ManagedExtensions;

// Only trusted validator code runs. Candidate assemblies remain byte inputs.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3 && args.Length != 4 && args.Length != 7) return 2;
        try
        {
            var catalog = ManagedStoreCatalogReader.Read(File.ReadAllBytes(args[2]), args[1]);
            if ((args.Length == 3 || args.Length == 4) && args[0] == "publication")
            { PublicationClosure.Validate(catalog,args.Length==4?PublicationHostProfile.Read(args[3]):null); Console.WriteLine("Publication dependency closure verified."); return 0; }
            if (args.Length == 3 && args[0] == "catalog")
            { Console.WriteLine("Catalog structure verified."); return 0; }
            if (args.Length != 7 || args[0] != "payload") return 2;
            var package = catalog.Packages.Single(p => !p.IsWorkshop && p.Id == args[4] && p.Manifest.Version.ToString() == args[5]);
            if (package.State != "active") throw new InvalidOperationException("Inactive candidate.");
            using (var stream = File.OpenRead(args[3]))
            {
                var report = ManagedStorePayloadValidator.Validate(package, stream, CancellationToken.None);
                var result = new {
                    schemaVersion = 1, packageId = package.Id, version = package.Manifest.Version.ToString(),
                    sha256 = report.Sha256, files = report.Files.Count,
                    assemblies = report.Inspected.Assemblies.Select(a => new {
                        name = a.Metadata.Identity.FullName,
                        references = a.Metadata.References.Select(r => r.FullName).ToArray()
                    }).ToArray()
                };
                File.WriteAllText(args[6], JsonSerializer.Serialize(result));
            }
            Console.WriteLine("ZIP and CLR metadata verified without executing plugin code.");
            return 0;
        }
        catch (StoreValidationException error) { Console.Error.WriteLine("Rejected: " + error.Code); return 1; }
        catch (ManagedExtensionValidationException error) { Console.Error.WriteLine("Rejected: " + error.Code); return 1; }
        catch (Exception error) { Console.Error.WriteLine("Rejected: " + error.GetType().Name); return 1; }
    }
}
