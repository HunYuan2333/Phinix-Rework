using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void Payloads()
    {
        byte[] dll = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fixture.Plugin.dll"));
        var record = PayloadRecord();
        byte[] manifest = PayloadManifest(record);
        var files = PayloadFiles(manifest, dll);
        byte[] zip = Zip(files);
        PackageRecord expected = LockPayload(record, manifest, zip);
        string sentinel = Path.Combine(Path.GetTempPath(), "phinix-payload-sentinel-" + Guid.NewGuid().ToString("N"));
        string previous = Environment.GetEnvironmentVariable("PHINIX_PAYLOAD_TEST_SENTINEL");
        Environment.SetEnvironmentVariable("PHINIX_PAYLOAD_TEST_SENTINEL", sentinel);
        try
        {
            Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin"), "Fixture is not loaded before validation.");
            PayloadValidationReport report = ValidatePayload(expected, zip);
            Assert(report.Package == expected && report.Sha256 == Digest(zip) && report.Files.Count == 4, "Valid ZIP report retains locked identity and exact asset hash.");
            Assert(report.Files.Select(f => f.Path).SequenceEqual(report.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal)), "Report file order is deterministic.");
            Assert(report.Files.Single(f => f.Path == "Assemblies/Fixture.Plugin.dll").Sha256 == Digest(dll), "Actual file bytes are hashed.");
            using (var streaming = new ChunkedPayloadStream(zip))
                Assert(PayloadValidator.Validate(expected, streaming, null, CancellationToken.None).Sha256 == report.Sha256, "Non-seekable chunked transfer yields the same frozen validation input.");
            ExpectReadOnly(() => ((IList<ValidatedPayloadFile>)report.Files).Clear());
            Assert(!File.Exists(sentinel) && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin"), "Metadata validation never loads the candidate or runs its module initializer.");
            Expect("PayloadSizeMismatch", () => ValidatePayload(expected, zip.Take(zip.Length - 1).ToArray()));
            Expect("PayloadLimit", () => ValidatePayload(expected, zip.Concat(new byte[] { 0 }).ToArray()));
            byte[] changed = (byte[])zip.Clone(); changed[0] ^= 1;
            Expect("PayloadDigestMismatch", () => ValidatePayload(expected, changed));
            Expect("UnexpectedManifest", () => PayloadValidator.Validate(expected, new MemoryStream(zip), manifest, CancellationToken.None));
            Expect("InvalidArchive", () => ValidatePayload(LockPayload(record, manifest, dll), dll));
            var removed = PayloadFiles(manifest, dll); removed.RemoveAll(f => f.Item1 == "phinix-package.json");
            RejectZip("MissingManifest", record, manifest, removed);
            removed = PayloadFiles(manifest, dll); removed.RemoveAll(f => f.Item1 == "About/About.xml");
            RejectZip("MissingAbout", record, manifest, removed);
            removed = PayloadFiles(manifest, dll); removed.RemoveAll(f => f.Item1.StartsWith("Assemblies/", StringComparison.Ordinal));
            RejectZip("MissingAssembly", record, manifest, removed);
            files = PayloadFiles(Encoding.UTF8.GetBytes("{}"), dll);
            RejectZip("ManifestDigestMismatch", record, manifest, files);
            files = PayloadFiles(manifest, dll); files[0] = Tuple.Create("About/About.xml", Encoding.UTF8.GetBytes("<ModMetaData><packageId>other.mod</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>"));
            RejectZip("AboutMismatch", record, manifest, files);
            files[0] = Tuple.Create("About/About.xml", Encoding.UTF8.GetBytes("<!DOCTYPE ModMetaData [<!ENTITY x SYSTEM 'file:///does-not-exist'>]><ModMetaData><packageId>&x;</packageId></ModMetaData>"));
            RejectZip("InvalidAbout", record, manifest, files);
            files[0] = Tuple.Create("About/About.xml", Encoding.UTF8.GetBytes("<ModMetaData><packageId>test.payload</packageId><supportedVersions><li>1.5</li></supportedVersions></ModMetaData>"));
            RejectZip("AboutMismatch", record, manifest, files);
            foreach (string path in new[] { "../escape.txt", "/absolute.txt", "C:/drive.txt", "Defs/../escape.xml", "Defs\\escape.xml", "Defs//a.xml", "Defs/./a.xml", "Defs/CON.xml", "Defs/COM¹.xml", "Defs/a.xml.", "Defs/a.xml ", "Defs/a:stream.xml", "Defs/a\n.xml", "Defs/e\u0301.xml" })
            {
                files = PayloadFiles(manifest, dll); files.Add(Tuple.Create(path, new byte[0]));
                RejectZip("UnsafeArchivePath", record, manifest, files);
            }
            foreach (string path in new[] { "Assemblies/Extra.dll", "Defs/Disguised.dll", "install.sh", "About/post-install.ps1", "1.6/Assemblies/Fixture.Plugin.dll", "wrapper/About/About.xml", "LoadFolders.xml", "unknown/" })
            {
                files = PayloadFiles(manifest, dll); files.Add(Tuple.Create(path, new byte[0]));
                RejectZip("UnsupportedPackageLayout", record, manifest, files);
            }
            foreach (string path in new[] { "Defs/Example.xml", "Defs/example.xml", "defs/Other.xml", "Defs/Example.xml/child.xml" })
            {
                files = PayloadFiles(manifest, dll); files.Add(Tuple.Create(path, new byte[0]));
                RejectZip("ArchivePathConflict", record, manifest, files);
            }
            files = PayloadFiles(manifest, dll); files.Add(Tuple.Create("Defs/", new byte[0]));
            zip = Zip(files); Assert(ValidatePayload(LockPayload(record, manifest, zip), zip).Files.Count == 4, "Explicit parent directory may follow its children.");
            zip = Zip(files, entry => { if (entry.FullName == "Defs/Example.xml") entry.ExternalAttributes = unchecked((int)0xa1ff0000); });
            Expect("UnsafeArchiveEntry", () => ValidatePayload(LockPayload(record, manifest, zip), zip));
            zip = Zip(files, entry => { if (entry.FullName == "Defs/Example.xml") entry.ExternalAttributes = 0x400; });
            Expect("UnsafeArchiveEntry", () => ValidatePayload(LockPayload(record, manifest, zip), zip));
            zip = Zip(files, entry => { if (entry.FullName == "Defs/Example.xml") entry.ExternalAttributes = 0x10; });
            Expect("UnsafeArchiveEntry", () => ValidatePayload(LockPayload(record, manifest, zip), zip));
            files = PayloadFiles(manifest, dll); files.Add(Tuple.Create("Textures/bomb.png", new byte[2 * 1024 * 1024]));
            zip = Zip(files, null, CompressionLevel.Optimal);
            Expect("PayloadLimit", () => ValidatePayload(LockPayload(record, manifest, zip), zip));
            files = PayloadFiles(manifest, dll); files.Add(Tuple.Create("Textures/huge.png", new byte[0]));
            zip = PatchCentralLength(Zip(files), "Textures/huge.png", PayloadValidator.MaxEntryBytes + 1);
            Expect("PayloadLimit", () => ValidatePayload(LockPayload(record, manifest, zip), zip));
            files = PayloadFiles(manifest, dll); files.Add(Tuple.Create("Textures/padding.png", new byte[1024 * 1024]));
            for (int i = 0; i < 4; i++) files.Add(Tuple.Create("Textures/expanded" + i + ".png", new byte[0]));
            zip = Zip(files);
            for (int i = 0; i < 4; i++) zip = PatchCentralSizes(zip, "Textures/expanded" + i + ".png", PayloadValidator.MaxEntryBytes, 400 * 1024);
            Expect("PayloadLimit", () => ValidatePayload(LockPayload(record, manifest, zip), zip));
            files = PayloadFiles(manifest, dll);
            for (int i = files.Count; i <= PayloadValidator.MaxEntries; i++) files.Add(Tuple.Create("Defs/" + i + ".xml", new byte[0]));
            RejectZip("PayloadLimit", record, manifest, files);
            files = PayloadFiles(manifest, new byte[] { 0, 1, 2, 3 });
            RejectZip("InvalidAssemblyPayload", record, manifest, files);
            byte[] wrongName = PatchAssemblyName(dll, "Another.Plugin");
            RejectZip("AssemblyIdentityMismatch", record, manifest, PayloadFiles(manifest, wrongName));
            byte[] protectedName = PatchAssemblyName(dll, "System.PluginX");
            RejectZip("ProtectedAssembly", record, manifest, PayloadFiles(manifest, protectedName));
            byte[] native = (byte[])dll.Clone();
            int pe = BitConverter.ToInt32(native, 0x3c); native[pe + 23] &= 0xdf;
            RejectZip("InvalidAssemblyPayload", record, manifest, PayloadFiles(manifest, native));
            int cli = FixtureCliOffset(dll);
            native = (byte[])dll.Clone(); native[cli + 16] &= 0xfe;
            RejectZip("InvalidAssemblyPayload", record, manifest, PayloadFiles(manifest, native));
            native = (byte[])dll.Clone(); native[cli + 16] |= 0x10;
            RejectZip("InvalidAssemblyPayload", record, manifest, PayloadFiles(manifest, native));
            native = (byte[])dll.Clone(); Buffer.BlockCopy(BitConverter.GetBytes(uint.MaxValue), 0, native, 0x3c, 4);
            RejectZip("InvalidAssemblyPayload", record, manifest, PayloadFiles(manifest, native));
            record = PayloadRecord(); ((Dictionary<string, object>[])record["assemblies"])[0]["version"] = "9.9.9.9";
            manifest = PayloadManifest(record);
            RejectZip("AssemblyIdentityMismatch", record, manifest, PayloadFiles(manifest, dll));
            record = PayloadRecord(); Artifact(record)["payloadKind"] = "dll-with-manifest"; Artifact(record)["assetName"] = "Fixture.Plugin.dll";
            manifest = PayloadManifest(record); expected = LockPayload(record, manifest, dll);
            Assert(PayloadValidator.Validate(expected, new MemoryStream(dll), manifest, CancellationToken.None).Files.Single().Length == dll.Length, "DLL plus companion manifest is statically validated without a generated shell.");
            Expect("MissingManifest", () => ValidatePayload(expected, dll));
            Expect("ManifestDigestMismatch", () => PayloadValidator.Validate(expected, new MemoryStream(dll), Encoding.UTF8.GetBytes("{}"), CancellationToken.None));
            Expect("PayloadLimit", () => PayloadValidator.Validate(expected, new MemoryStream(dll), new byte[CatalogReader.MaxCatalogBytes + 1], CancellationToken.None));
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { PayloadValidator.Validate(expected, new MemoryStream(dll), manifest, cancelled.Token); throw new Exception("Expected payload cancellation."); }
                catch (OperationCanceledException) { assertions++; }
            }
            using (var cancelled = new CancellationTokenSource())
            using (var streaming = new ChunkedPayloadStream(dll, cancelled.Cancel))
            {
                try { PayloadValidator.Validate(expected, streaming, manifest, cancelled.Token); throw new Exception("Expected transfer cancellation."); }
                catch (OperationCanceledException) { assertions++; }
            }
            Assert(!File.Exists(sentinel) && !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fixture.Plugin"), "All payload mutations preserve metadata-only behavior.");
        }
        finally { Environment.SetEnvironmentVariable("PHINIX_PAYLOAD_TEST_SENTINEL", previous); if (File.Exists(sentinel)) File.Delete(sentinel); }
    }

    private static Dictionary<string, object> PayloadRecord()
    {
        var record = Package("payload"); var assembly = Assembly("Fixture.Plugin"); assembly["version"] = "1.2.3.4";
        record["assemblies"] = new[] { assembly }; return record;
    }
    private static byte[] PayloadManifest(Dictionary<string, object> record)
    {
        var copy = new Dictionary<string, object>(record); copy.Remove("artifact"); copy.Remove("state");
        return Serialize(new Dictionary<string, object> { { "schemaVersion", 1 }, { "package", copy } });
    }
    private static List<Tuple<string, byte[]>> PayloadFiles(byte[] manifest, byte[] dll)
    {
        return new List<Tuple<string, byte[]>>
        {
            Tuple.Create("About/About.xml", Encoding.UTF8.GetBytes("<ModMetaData><packageId>test.payload</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>")),
            Tuple.Create("phinix-package.json", manifest), Tuple.Create("Assemblies/Fixture.Plugin.dll", dll),
            Tuple.Create("Defs/Example.xml", Encoding.UTF8.GetBytes("<Defs/>"))
        };
    }
    private static byte[] Zip(List<Tuple<string, byte[]>> files, Action<ZipArchiveEntry> mutate = null, CompressionLevel level = CompressionLevel.NoCompression)
    {
        using (var output = new MemoryStream())
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                foreach (var file in files)
                {
                    var entry = zip.CreateEntry(file.Item1, level); if (mutate != null) mutate(entry);
                    using (Stream content = entry.Open()) content.Write(file.Item2, 0, file.Item2.Length);
                }
            return output.ToArray();
        }
    }
    private static PackageRecord LockPayload(Dictionary<string, object> record, byte[] manifest, byte[] bytes)
    {
        Artifact(record)["manifestSha256"] = Digest(manifest); Artifact(record)["sha256"] = Digest(bytes); Artifact(record)["sizeBytes"] = bytes.Length;
        return Read(record).Packages[0];
    }
    private static PayloadValidationReport ValidatePayload(PackageRecord expected, byte[] bytes)
    { using (var stream = new MemoryStream(bytes)) return PayloadValidator.Validate(expected, stream, null, CancellationToken.None); }
    private static void RejectZip(string code, Dictionary<string, object> record, byte[] manifest, List<Tuple<string, byte[]>> files)
    { byte[] zip = Zip(files); Expect(code, () => ValidatePayload(LockPayload(record, manifest, zip), zip)); }
    private static byte[] PatchAssemblyName(byte[] dll, string replacement)
    {
        byte[] original = Encoding.UTF8.GetBytes("Fixture.Plugin"), target = Encoding.UTF8.GetBytes(replacement);
        if (original.Length != target.Length) throw new Exception("Invalid test replacement.");
        byte[] result = (byte[])dll.Clone(); bool patched = false;
        for (int i = 0; i <= result.Length - original.Length; i++)
            if (original.Select((b, offset) => result[i + offset] == b).All(equal => equal))
            { Buffer.BlockCopy(target, 0, result, i, target.Length); patched = true; }
        if (!patched) throw new Exception("Fixture name was not found."); return result;
    }
    private static byte[] PatchCentralLength(byte[] zip, string name, int size)
    { return PatchCentralSizes(zip, name, size, null); }
    private static byte[] PatchCentralSizes(byte[] zip, string name, int size, int? compressedSize)
    {
        byte[] result = (byte[])zip.Clone();
        for (int i = 0; i < result.Length - 46; i++)
            if (BitConverter.ToUInt32(result, i) == 0x02014b50 && Encoding.UTF8.GetString(result, i + 46, BitConverter.ToUInt16(result, i + 28)) == name)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(size), 0, result, i + 24, 4);
                if (compressedSize.HasValue) Buffer.BlockCopy(BitConverter.GetBytes(compressedSize.Value), 0, result, i + 20, 4);
                return result;
            }
        throw new Exception("Fixture central directory entry was not found.");
    }
    private static int FixtureCliOffset(byte[] dll)
    {
        int pe = BitConverter.ToInt32(dll, 0x3c), optional = pe + 24;
        int data = BitConverter.ToUInt16(dll, optional) == 0x10b ? 96 : 112;
        uint rva = BitConverter.ToUInt32(dll, optional + data + 14 * 8);
        int sections = optional + BitConverter.ToUInt16(dll, pe + 20);
        for (int i = 0; i < BitConverter.ToUInt16(dll, pe + 6); i++)
        {
            int section = sections + i * 40;
            uint address = BitConverter.ToUInt32(dll, section + 12), size = BitConverter.ToUInt32(dll, section + 16);
            if (rva >= address && rva - address < size) return checked((int)(BitConverter.ToUInt32(dll, section + 20) + rva - address));
        }
        throw new Exception("Fixture CLI header was not found.");
    }
    private sealed class ChunkedPayloadStream : Stream
    {
        private readonly MemoryStream input;
        private readonly Action onRead;
        public ChunkedPayloadStream(byte[] bytes, Action onRead = null) { input = new MemoryStream(bytes); this.onRead = onRead; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { int read = input.Read(buffer, offset, Math.Min(count, 17)); if (onRead != null) onRead(); return read; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) input.Dispose(); base.Dispose(disposing); }
    }
}
