using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Utils.Framework.ManagedExtensions;

internal static partial class Program
{
    private static int assertions;
    private static void Assert(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    private static void Failure(string code, Action action)
    { try { action(); } catch (ManagedExtensionValidationException e) { Assert(e.Code == code, "Expected " + code + ", got " + e.Code); return; } throw new Exception("Expected " + code); }
    private static string Q(string value) { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value); }
    private static string FileEntry(string path, byte[] bytes)
    { return "{\"path\":" + Q(path) + ",\"length\":" + bytes.Length + ",\"sha256\":" + Q(ManagedExtensionPaths.Hash(bytes)) + "}"; }
    private static string Manifest(Dictionary<string, byte[]> files, string package = "test.package", string dependencies = "[]", string external = "[]")
    {
        string assemblies = string.Join(",", files.Select(pair => {
            var identity = ManagedExtensionMetadataReader.Read(pair.Value).Identity;
            return "{\"name\":" + Q(identity.Name) + ",\"version\":" + Q(identity.Version) + ",\"culture\":" + Q(identity.Culture) +
                ",\"publicKeyToken\":" + Q(identity.PublicKeyToken) + "," + FileEntry(pair.Key, pair.Value).Substring(1); }));
        return "{\"schemaVersion\":1,\"management\":\"phinix-dll\",\"packageId\":" + Q(package) + ",\"name\":\"Metadata test\",\"version\":\"1.0.0\",\"targetFramework\":\"net472\"," +
            "\"compatibility\":{\"rimWorldVersions\":[\"1.6\"],\"phinixRange\":\">=0.9.7 <1.0.0\",\"abstractionsRange\":\">=1.3.0 <2.0.0\"},\"dependencies\":" + dependencies + ",\"externalMods\":" + external + ",\"resources\":[],\"assemblies\":[" + assemblies + "],\"modules\":[" +
            "{\"id\":\"test.managed\",\"assemblyName\":\"Fixture.Managed.Plugin\",\"entryType\":\"Fixture.Managed.Module\",\"dependsOn\":[\"builtin.host\"]}," +
            "{\"id\":\"test.nested\",\"assemblyName\":\"Fixture.Managed.Plugin\",\"entryType\":\"Fixture.Managed.Outer+Nested\",\"dependsOn\":[]}]}";
    }
    private static void Install(ManagedExtensionPaths paths, string source, string json, Dictionary<string, byte[]> files, string state)
    {
        var manifestBytes = Utf8(json); var manifest = ManagedExtensionManifestReader.Read(manifestBytes); string id = manifest.PackageId;
        string root = paths.GetPackageDirectory(source, id); Directory.CreateDirectory(Path.Combine(root, "Assemblies"));
        File.WriteAllBytes(Path.Combine(root, "manifest.json"), manifestBytes);
        foreach (var file in files) { string path=Path.Combine(root,file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,file.Value); }
        Directory.CreateDirectory(paths.InstalledRecordsDirectory); Directory.CreateDirectory(paths.DesiredStateDirectory);
        var receiptFiles = files.Select(p => FileEntry(p.Key, p.Value)).Concat(new[] { FileEntry("manifest.json", manifestBytes) });
        string hash = ManagedExtensionPaths.Hash(manifestBytes);
        File.WriteAllText(paths.GetInstalledRecordPath(source, id), "{\"schemaVersion\":1,\"sourceId\":" + Q(source) + ",\"repositoryIdentitySha256\":" + Q(new string('a',64)) +
            ",\"packageId\":" + Q(id) + ",\"version\":\"1.0.0\",\"manifestSha256\":" + Q(hash) + ",\"catalogSnapshotId\":" + Q(new string('b',40)) +
            ",\"catalogSha256\":" + Q(new string('c',64)) + ",\"artifactSha256\":" + Q(new string('d',64)) + ",\"installationTransactionId\":" + Q(new string('e',32)) + ",\"files\":[" + string.Join(",",receiptFiles) + "]}");
        File.WriteAllText(paths.GetDesiredStatePath(source,id), "{\"schemaVersion\":1,\"sourceId\":" + Q(source) + ",\"packageId\":" + Q(id) + ",\"manifestSha256\":" + Q(hash) + ",\"operationId\":" + Q(new string('f',32)) + ",\"desiredState\":" + Q(state) + "}");
    }
    private static int Main(string[] args)
    {
        if(args.Length!=0 && (args[0]=="positive" || args[0]=="lease" || args[0]=="managed-intent" || args[0]=="install-startup" || args[0]=="upgrade-live" || args[0]=="upgrade-restart" || args[0]=="host-upgrade")) return StartupChild(args);
        string root = Path.Combine(Path.GetTempPath(), "phinix-managed-metadata-" + Guid.NewGuid().ToString("N"));
        string sentinel = Path.Combine(root, "executed"); Environment.SetEnvironmentVariable("PHINIX_MANAGED_METADATA_SENTINEL", sentinel);
        try
        {
            string fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures");
            var files = new Dictionary<string, byte[]> {
                { "Assemblies/Fixture.Managed.Plugin.dll", File.ReadAllBytes(Path.Combine(fixtureRoot,"Fixture.Managed.Plugin.dll")) },
                { "Assemblies/Fixture.Managed.Helper.dll", File.ReadAllBytes(Path.Combine(fixtureRoot,"Fixture.Managed.Helper.dll")) } };
            HostReferenceRegression();
            AssemblyOccupancyRegression(files);
            LocalizationRegression(files);
            ManagedStoreProtocolRegression(files);
            ManagedStoreExperienceRegression(files);
            StoreMaintainerRegression(files);
            StoreReleaseNoticeRegression();
            byte[] plugin = files.Values.First();
            var metadata = ManagedExtensionMetadataReader.Read(plugin);
            Assert(metadata.Identity.FullName == "Fixture.Managed.Plugin, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null", "Exact CLR identity");
            Assert(ManagedExtensionMetadataReader.ReadIdentity(plugin).FullName==metadata.Identity.FullName,"Identity-only inspection preserves exact CLR identity");
            var localOnly=(byte[])plugin.Clone(); int scopeAt=Find(localOnly,Utf8("mscorlib\0"));
            Assert(scopeAt>=0,"Local library fixture has a framework scope");
            Buffer.BlockCopy(Utf8("localclr"),0,localOnly,scopeAt,8);
            Assert(ManagedExtensionMetadataReader.ReadIdentity(localOnly).FullName==metadata.Identity.FullName,"Local identity inspection ignores unrelated target-framework attribute scopes");
            Failure("AssemblyMetadataInvalid",()=>ManagedExtensionMetadataReader.Read(localOnly));
            Assert(metadata.TargetFramework == ".NETFramework,Version=v4.7.2", "Real target framework");
            Assert(metadata.References.Any(r => r.Name == "Utils") && metadata.References.Any(r => r.Name == "Fixture.Managed.Helper"), "Actual CLR references");
            Assert(metadata.Modules.Count == 2 && metadata.ModuleTypes.Count == 2, "Concrete direct/inherited modules");
            Assert(metadata.Modules.Single(m => m.Id == "test.managed").DependsOn.SequenceEqual(new[] { "builtin.host" }), "Serialized attribute arguments");
            Assert(metadata.Modules.Any(m => m.EntryType == "Fixture.Managed.Outer+Nested"), "Nested entry name");
            var json = Manifest(files); var manifest = ManagedExtensionManifestReader.Read(Utf8(json));
            var payload = ManagedExtensionPayloadInspector.Inspect(manifest, files, CancellationToken.None);
            Assert(payload.Assemblies.Count == 2, "Multi DLL inspection");
            byte[] copy = payload.Assemblies[0].CopyBytes(); copy[0] = 0;
            Assert(payload.Assemblies[0].CopyBytes()[0] == plugin[0], "Frozen bytes never expose writable storage");
            Failure("AssemblyModuleDeclarationMismatch", () => ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(json.Replace("Fixture.Managed.Module", "Fixture.Managed.Missing"))), files, CancellationToken.None));
            Failure("AssemblyModuleDeclarationMismatch", () => ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(json.Replace("builtin.host", "builtin.fake"))), files, CancellationToken.None));
            Failure("AssemblyModuleDeclarationMismatch", () => ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(json.Replace("test.managed", "test.forged"))), files, CancellationToken.None));
            string nested = ",{\"id\":\"test.nested\",\"assemblyName\":\"Fixture.Managed.Plugin\",\"entryType\":\"Fixture.Managed.Outer+Nested\",\"dependsOn\":[]}";
            Failure("AssemblyModulesMismatch", () => ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(json.Replace(nested,""))), files, CancellationToken.None));
            Failure("AssemblyIdentityMismatch", () => ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(json.Replace("1.2.3.4", "1.2.3.5"))), files, CancellationToken.None));
            var altered = files.ToDictionary(p => p.Key, p => (byte[])p.Value.Clone()); altered.Values.First()[100] ^= 1;
            Failure("AssemblyDigestMismatch", () => ManagedExtensionPayloadInspector.Inspect(manifest, altered, CancellationToken.None));
            var missing = new Dictionary<string,byte[]>(files); missing.Remove(files.Keys.First());
            Failure("AssemblyFilesMismatch", () => ManagedExtensionPayloadInspector.Inspect(manifest, missing, CancellationToken.None));
            try { ManagedExtensionPayloadInspector.Inspect(manifest, files, new CancellationToken(true)); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { assertions++; }
            foreach (int length in new[] { 0, 1, 63, 128, 512, plugin.Length / 2 })
            {
                Failure(length == 0 ? "AssemblySizeInvalid" : "AssemblyMetadataInvalid", () => ManagedExtensionMetadataReader.Read(plugin.Take(length).ToArray()));
                Failure(length == 0 ? "AssemblySizeInvalid" : "AssemblyMetadataInvalid", () => ManagedExtensionMetadataReader.ReadIdentity(plugin.Take(length).ToArray()));
            }
            // Corrupt header/RVA and lengths must produce bounded metadata errors, never runtime loading.
            foreach (int offset in new[] { 0, 0x3c, BitConverter.ToInt32(plugin,0x3c), BitConverter.ToInt32(plugin,0x3c)+6 })
            { var bad = (byte[])plugin.Clone(); for (int i = 0; i < 4; i++) bad[offset+i] = 255; Failure("AssemblyMetadataInvalid", () => ManagedExtensionMetadataReader.Read(bad)); }
            // Retargeting metadata without retargeting the outer manifest is rejected.
            var retargeted = files.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());
            int frameworkAt = Find(retargeted.Values.First(), Utf8(".NETFramework,Version=v4.7.2"));
            Assert(frameworkAt >= 0,"Target framework attribute fixture exists");
            Buffer.BlockCopy(Utf8(".NETFramework,Version=v4.8.0"),0,retargeted.Values.First(),frameworkAt,Utf8(".NETFramework,Version=v4.8.0").Length);
            Failure("AssemblyTargetFrameworkMismatch",()=>ManagedExtensionPayloadInspector.Inspect(ManagedExtensionManifestReader.Read(Utf8(Manifest(retargeted))),retargeted,CancellationToken.None));
            var random = new Random(314159);
            for(int i=0;i<128;i++)
            {
                var mutated=(byte[])plugin.Clone(); int at=random.Next(mutated.Length); mutated[at]^=(byte)random.Next(1,256);
                try { ManagedExtensionMetadataReader.Read(mutated); } catch(ManagedExtensionValidationException) { }
                assertions++; // Any other exception, hang or execution is a regression.
            }
            var paths = new ManagedExtensionPaths(root); Install(paths, "test.source",json,files,"enabled");
            var hostRefs = payload.Assemblies.SelectMany(a => a.Metadata.References).Where(r => !manifest.Assemblies.Any(a => a.Name == r.Name)).GroupBy(r => r.FullName).Select(g => g.First()).ToList();
            Func<IEnumerable<ManagedAssemblyIdentity>,string,IEnumerable<string>,IEnumerable<string>,ManagedExtensionHostFacts> host = (refs, game, modules, mods) => new ManagedExtensionHostFacts(game,"0.9.7","1.4.0",refs,modules,mods);
            var providerFiles = new Dictionary<string,byte[]> { {"Assemblies/Fixture.Managed.Provider.dll",File.ReadAllBytes(Path.Combine(fixtureRoot,"Fixture.Managed.Provider.dll"))} };
            string providerJson = Manifest(providerFiles,"test.provider");
            int moduleStart = providerJson.IndexOf("\"modules\":[",StringComparison.Ordinal);
            providerJson=providerJson.Substring(0,moduleStart)+"\"modules\":[{\"id\":\"test.provider\",\"assemblyName\":\"Fixture.Managed.Provider\",\"entryType\":\"Fixture.Managed.Provider\",\"dependsOn\":[]}]}";
            Func<ManagedExtensionHostFacts,ReadOnlyResult> plan = facts => {
                var inventory = ManagedExtensionInventoryReader.Read(paths,CancellationToken.None);
                var inspected = new Dictionary<string,ManagedExtensionInspectedPayload>();
                foreach (var row in inventory.Packages.Where(r=>r.ContentState==ManagedExtensionContentState.ContentVerified))
                    inspected.Add(row.RecordKey,ManagedExtensionPayloadInspector.Inspect(row.Manifest,row.PackageId=="test.provider"?providerFiles:files,CancellationToken.None));
                string startupId=new string('9',32);
                var results=ManagedExtensionCandidatePlanner.Plan(inventory,inspected,facts,startupId,CancellationToken.None);
                Assert(results.All(r=>r.Audit.StartupId==startupId && r.Audit.Stage=="preflight" && r.Audit.ManifestSha256==r.Package.ManifestSha256 &&
                    r.Audit.InstallationTransactionId==r.Package.InstallationTransactionId && r.Audit.Code==(r.DiagnosticCode??"CandidatePreflightPassed")),"Each outcome retains correlation and provenance for host logging");
                Assert(results.All(r=>r.DiagnosticCode==null || r.Payload==null),"Rejected rows expose no executable payload");
                return new ReadOnlyResult(results.Select(r=>r.DiagnosticCode).ToArray()); };
            var goodHost = host(hostRefs,"1.6",new[]{"builtin.host"},new string[0]);
            Assert(plan(goodHost).Codes.Single()==null,"Validated package enters candidate list");
            var boundInventory=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None);
            var noInputs=new Dictionary<string,ManagedExtensionInspectedPayload>();
            string auditId=new string('9',32);
            Assert(ManagedExtensionCandidatePlanner.Plan(boundInventory,noInputs,goodHost,auditId,CancellationToken.None).Single().DiagnosticCode=="CandidateNotInspected","Uninspected owned content cannot become candidate");
            noInputs[boundInventory.Packages.Single().RecordKey]=payload;
            Assert(ManagedExtensionCandidatePlanner.Plan(boundInventory,noInputs,goodHost,auditId,CancellationToken.None).Single().DiagnosticCode=="CandidateManifestMismatch","Equivalent but unrelated manifest instance cannot replace observed inventory");
            Failure("InvalidStartupId",()=>ManagedExtensionCandidatePlanner.Plan(boundInventory,noInputs,goodHost,"../path",CancellationToken.None));
            try { ManagedExtensionCandidatePlanner.Plan(boundInventory,noInputs,goodHost,auditId,new CancellationToken(true)); throw new Exception("Plan cancellation ignored"); } catch(OperationCanceledException) { assertions++; }
            Assert(plan(host(hostRefs,"1.5",new[]{"builtin.host"},new string[0])).Codes.Single()=="CandidateHostIncompatible","Game compatibility gate");
            Assert(plan(host(hostRefs.Where(r=>r.Name!="Utils"),"1.6",new[]{"builtin.host"},new string[0])).Codes.Single()=="CandidateAssemblyReferenceUnavailable","Missing CLR ref gate");
            var wrongVersionRefs=hostRefs.Select(r=>r.Name=="Utils"?ManagedAssemblyIdentity.FromAssemblyName(new AssemblyName(r.FullName.Replace("0.9.7.0","0.9.6.0"))):r);
            Assert(plan(host(wrongVersionRefs,"1.6",new[]{"builtin.host"},new string[0])).Codes.Single()=="CandidateAssemblyReferenceUnavailable","Host downgrade rejected");
            var newerHostRefs=hostRefs.Select(r=>r.Name=="Utils"?ManagedAssemblyIdentity.FromAssemblyName(new AssemblyName(r.FullName.Replace("0.9.7.0","0.9.8.0"))):r).ToList();
            Assert(plan(host(newerHostRefs,"1.6",new[]{"builtin.host"},new string[0])).Codes.Single()==null,"Candidate accepts same-major newer host library");
            var majorHostRefs=hostRefs.Select(r=>r.Name=="Utils"?ManagedAssemblyIdentity.FromAssemblyName(new AssemblyName(r.FullName.Replace("0.9.7.0","1.0.0.0"))):r);
            Assert(plan(host(majorHostRefs,"1.6",new[]{"builtin.host"},new string[0])).Codes.Single()=="CandidateAssemblyReferenceUnavailable","Candidate rejects cross-major host library");
            var ambiguousHostRefs=newerHostRefs.Concat(newerHostRefs.Where(r=>r.Name=="Utils").Select(r=>ManagedAssemblyIdentity.FromAssemblyName(new AssemblyName(r.FullName.Replace("0.9.8.0","0.9.9.0")))));
            Assert(plan(host(ambiguousHostRefs,"1.6",new[]{"builtin.host"},new string[0])).Codes.Single()=="CandidateAssemblyReferenceUnavailable","Candidate rejects ambiguous host upgrades");
            byte[] originalHelper=files["Assemblies/Fixture.Managed.Helper.dll"];
            files["Assemblies/Fixture.Managed.Helper.dll"]=File.ReadAllBytes(Path.Combine(fixtureRoot,"HostUpgrade","Fixture.Managed.Helper.dll"));
            Install(paths,"test.source",Manifest(files),files,"enabled");
            Assert(plan(goodHost).Codes.Single()=="CandidateAssemblyReferenceUnavailable","Owned helper version mismatch is not relaxed as a host upgrade");
            files["Assemblies/Fixture.Managed.Helper.dll"]=originalHelper;
            Install(paths,"test.source",json,files,"enabled");
            Assert(plan(host(hostRefs,"1.6",new string[0],new string[0])).Codes.Single()=="CandidateModuleDependencyUnavailable","Module dependency gate");
            Assert(plan(host(hostRefs.Concat(new[]{metadata.Identity}),"1.6",new[]{"builtin.host"},new string[0])).Codes.Single()=="CandidateAssemblyConflict","Host CLR name conflict even matching bytes");
            Assert(plan(host(hostRefs,"1.6",new[]{"builtin.host","test.managed"},new string[0])).Codes.Single()=="CandidateModuleConflict","Host module identity conflict");
            foreach (string state in new[]{"disabled","pending-removal"})
            { Install(paths,"test.source",json,files,state); Assert(plan(goodHost).Codes.Single()=="CandidateNotEnabled","Non enabled state excluded"); }
            Install(paths,"test.source",json,files,"enabled");
            string dep = "[{\"packageId\":\"test.missing\",\"versionRange\":\"1.0.0\",\"optional\":false}]";
            Install(paths,"test.source",Manifest(files,dependencies:dep),files,"enabled");
            Assert(plan(goodHost).Codes.Single()=="CandidatePackageDependencyUnavailable","Required package gate");
            Install(paths,"test.source",Manifest(files,dependencies:dep.Replace("false","true")),files,"enabled");
            Assert(plan(goodHost).Codes.Single()==null,"Absent optional package");
            Install(paths,"test.source",Manifest(files,external:"[{\"packageId\":\"test.external\"}]"),files,"enabled");
            Assert(plan(goodHost).Codes.Single()=="CandidateExternalModMissing","Actual active external mod required");
            Assert(plan(host(hostRefs,"1.6",new[]{"builtin.host"},new[]{"test.external"})).Codes.Single()==null,"Active external mod accepted");
            string providerDependency="[{\"packageId\":\"test.provider\",\"versionRange\":\"1.0.0\",\"optional\":false}]";
            Install(paths,"test.source",Manifest(files,dependencies:providerDependency),files,"enabled");
            Install(paths,"test.source",providerJson,providerFiles,"enabled");
            Assert(plan(goodHost).Codes.All(c=>c==null),"Explicit package dependency accepted");
            Install(paths,"test.source",providerJson,providerFiles,"disabled");
            Assert(plan(goodHost).Codes.Contains("CandidatePackageDependencyUnavailable"),"Disabled provider blocks consumer");
            Install(paths,"test.source",Manifest(files,dependencies:providerDependency.Replace("false","true")),files,"enabled");
            Assert(plan(goodHost).Codes.Contains("CandidatePackageDependencyUnavailable"),"Installed disabled optional provider is not absent");
            Install(paths,"test.source",providerJson.Replace("\"rimWorldVersions\":[\"1.6\"]","\"rimWorldVersions\":[\"1.5\"]"),providerFiles,"enabled");
            Assert(plan(goodHost).Codes.Contains("CandidatePackageDependencyUnavailable") && plan(goodHost).Codes.Contains("CandidateHostIncompatible"),"Incompatible provider blocks consumer");
            Install(paths,"test.source",providerJson,providerFiles,"enabled");
            var conflictHost=host(hostRefs,"1.6",new[]{"builtin.host","test.provider"},new string[0]);
            Assert(plan(conflictHost).Codes.Contains("CandidateDependencyRejected") && plan(conflictHost).Codes.Contains("CandidateModuleConflict"),"Later provider rejection propagates to consumer");
            string reverseDependency="[{\"packageId\":\"test.package\",\"versionRange\":\"1.0.0\",\"optional\":false}]";
            Install(paths,"test.source",providerJson.Replace("\"dependencies\":[]","\"dependencies\":"+reverseDependency),providerFiles,"enabled");
            Assert(plan(goodHost).Codes.All(c=>c=="CandidatePackageDependencyCycle"),"Every member of package cycle rejected");
            File.Delete(paths.GetInstalledRecordPath("test.source","test.provider"));
            Install(paths,"test.source",json,files,"enabled"); Install(paths,"test.other",json,files,"enabled");
            Assert(plan(goodHost).Codes.All(c=>c=="CandidatePackageConflict"),"No enumeration winner across sources");
            File.Delete(paths.GetInstalledRecordPath("test.other","test.package"));
            File.Delete(paths.GetDesiredStatePath("test.source","test.package"));
            Assert(plan(goodHost).Codes.Single()=="DesiredStateMissing","Missing desired state never enables an inspected package");
            Install(paths,"test.source",json,files,"enabled");
            for(int i=0;i<ManagedExtensionInventoryReader.MaxPackages;i++) File.WriteAllText(Path.Combine(paths.InstalledRecordsDirectory,"unknown-"+i),"{}");
            var uncertain=ManagedExtensionInventoryReader.Read(paths,CancellationToken.None);
            Assert(uncertain.Diagnostics.Contains("InventoryLimit"),"Enumeration flood becomes explicit root uncertainty");
            Assert(ManagedExtensionCandidatePlanner.Plan(uncertain,new Dictionary<string,ManagedExtensionInspectedPayload>(),goodHost,auditId,CancellationToken.None).All(r=>r.DiagnosticCode=="CandidateInventoryUncertain" && r.Payload==null),"Root uncertainty excludes the entire managed candidate domain");
            Assert(!File.Exists(sentinel) && !AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name.StartsWith("Fixture.Managed",StringComparison.Ordinal)),"All checks leave plugin and helper unloaded; no static initializer runs");
            StartupRegression(files);
            ManagementRegression(files);
            InstallationRuntimeRegression(files);
            ReplacementRuntimeRegression(files);
            ManagedStoreFlowRegression(files);
            ManagementControllerRegression(files);
            Console.WriteLine("Managed extension metadata/preflight/startup passed: " + assertions + " assertions."); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { Environment.SetEnvironmentVariable("PHINIX_MANAGED_METADATA_SENTINEL",null); if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static int Find(byte[] bytes, byte[] needle)
    {
        for(int at=0;at<=bytes.Length-needle.Length;at++) { bool found=true; for(int i=0;i<needle.Length;i++) if(bytes[at+i]!=needle[i]) { found=false; break; } if(found) return at; }
        return -1;
    }
    private sealed class ReadOnlyResult { internal ReadOnlyResult(string[] codes) { Codes=codes; } internal string[] Codes; }
}
