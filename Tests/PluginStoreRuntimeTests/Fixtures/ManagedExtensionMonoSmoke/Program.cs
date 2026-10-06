using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Utils.Framework.ManagedExtensions;

// Run against the production net472 Utils on Mono. Candidate DLLs are only hashed.
internal static class ManagedSmoke
{
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int symlink(string target, string linkPath);
    public static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "phinix-managed-mono-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected the regression fixture DLL path.");
            var paths = new ManagedExtensionPaths(root);
            if (ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Count != 0 || Directory.Exists(root))
                throw new Exception("Read created storage.");
            byte[] dll = File.ReadAllBytes(args[0]);
            string hash = ManagedExtensionPaths.Hash(dll);
            string content = "{\"schemaVersion\":1,\"management\":\"phinix-dll\",\"packageId\":\"test.managed\",\"name\":\"Mono static test\",\"version\":\"1.0.0\",\"targetFramework\":\"net472\"," +
                "\"compatibility\":{\"rimWorldVersions\":[\"1.6\"],\"phinixRange\":\">=0.9.7 <1.0.0\",\"abstractionsRange\":\">=1.3.0 <2.0.0\"},\"dependencies\":[],\"resources\":[],\"externalMods\":[]," +
                "\"modules\":[{\"id\":\"test.managed.module\",\"assemblyName\":\"Fixture.Plugin\",\"entryType\":\"Fixture.Plugin.Module\",\"dependsOn\":[]}]," +
                "\"assemblies\":[{\"name\":\"Fixture.Plugin\",\"version\":\"1.2.3.4\",\"culture\":\"neutral\",\"publicKeyToken\":\"null\",\"path\":\"Assemblies/Fixture.Plugin.dll\",\"length\":" + dll.Length.ToString(CultureInfo.InvariantCulture) + ",\"sha256\":\"" + hash + "\"}]}";
            byte[] manifest = Encoding.UTF8.GetBytes(content);
            string manifestHash = ManagedExtensionPaths.Hash(manifest);
            var parsed = ManagedExtensionManifestReader.Read(manifest);
            if (parsed.Modules.Count != 1 || parsed.Assemblies[0].PublicKeyToken != "null") throw new Exception("Manifest failed.");
            Reject(content + "{}", "InvalidJson");
            Reject(content.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"), "UnsupportedSchema");
            Reject(content.Replace("\"management\":\"phinix-dll\"", "\"management\":\"rimworld-mod\""), "UnsupportedManagement");
            string packageRoot = paths.GetPackageDirectory("test.source", "test.managed");
            Directory.CreateDirectory(Path.Combine(packageRoot, "Assemblies"));
            Directory.CreateDirectory(paths.InstalledRecordsDirectory);
            Directory.CreateDirectory(paths.DesiredStateDirectory);
            File.WriteAllBytes(Path.Combine(packageRoot, "manifest.json"), manifest);
            string dllPath = Path.Combine(packageRoot, "Assemblies", "Fixture.Plugin.dll");
            File.WriteAllBytes(dllPath, dll);
            string receipt = "{\"schemaVersion\":1,\"sourceId\":\"test.source\",\"repositoryIdentitySha256\":\"" + hash + "\",\"packageId\":\"test.managed\",\"version\":\"1.0.0\",\"manifestSha256\":\"" + manifestHash +
                "\",\"catalogSnapshotId\":\"" + new string('a', 40) + "\",\"catalogSha256\":\"" + hash + "\",\"artifactSha256\":\"" + hash + "\",\"installationTransactionId\":\"" + new string('a', 32) + "\",\"files\":[" +
                "{\"path\":\"manifest.json\",\"length\":" + manifest.Length.ToString(CultureInfo.InvariantCulture) + ",\"sha256\":\"" + manifestHash + "\"}," +
                "{\"path\":\"Assemblies/Fixture.Plugin.dll\",\"length\":" + dll.Length.ToString(CultureInfo.InvariantCulture) + ",\"sha256\":\"" + hash + "\"}]}";
            File.WriteAllText(paths.GetInstalledRecordPath("test.source", "test.managed"), receipt);
            foreach (string desired in new[] { "enabled", "disabled", "pending-removal" })
            {
                string state = "{\"schemaVersion\":1,\"sourceId\":\"test.source\",\"packageId\":\"test.managed\",\"manifestSha256\":\"" + manifestHash + "\",\"operationId\":\"" + new string('b', 32) + "\",\"desiredState\":\"" + desired + "\"}";
                File.WriteAllText(paths.GetDesiredStatePath("test.source", "test.managed"), state);
                var row = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single();
                if (row.ContentState != ManagedExtensionContentState.ContentVerified || row.DiagnosticCode != null || row.DesiredState == ManagedExtensionDesiredState.Unknown)
                    throw new Exception("State failed: " + row.DiagnosticCode);
            }
            File.Delete(paths.GetDesiredStatePath("test.source", "test.managed"));
            var missingState = ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single();
            if (missingState.DesiredState != ManagedExtensionDesiredState.Unknown || missingState.DiagnosticCode != "DesiredStateMissing") throw new Exception("Missing intent enabled package.");
            dll[dll.Length - 1] ^= 1; File.WriteAllBytes(dllPath, dll);
            if (ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single().DiagnosticCode != "FileDigestMismatch") throw new Exception("Changed DLL accepted.");
            if (Path.DirectorySeparatorChar == '/')
            {
                foreach (string target in new[] { Path.GetFullPath(args[0]), Path.Combine(root, "missing-target.dll") })
                {
                    File.Delete(dllPath);
                    if (symlink(target, dllPath) != 0) throw new Exception("Could not create isolated symlink fixture.");
                    if (ManagedExtensionInventoryReader.Read(paths, CancellationToken.None).Packages.Single().DiagnosticCode != "UnsafeFilesystemLink")
                        throw new Exception("Mono link check failed.");
                    File.Delete(dllPath);
                }
            }
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin")) throw new Exception("Candidate DLL executed.");
            Console.WriteLine("Mono production managed manifest/inventory/state/tamper/link/no-execution smoke passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void Reject(string text, string code)
    {
        try { ManagedExtensionManifestReader.Read(Encoding.UTF8.GetBytes(text)); }
        catch (ManagedExtensionValidationException ex) { if (ex.Code == code) return; throw; }
        throw new Exception("Expected rejection: " + code);
    }
}
