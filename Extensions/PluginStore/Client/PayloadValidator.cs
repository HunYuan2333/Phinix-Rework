using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Phinix.PluginStore
{
    internal sealed class ValidatedPayloadFile
    {
        public ValidatedPayloadFile(string path, long length, string sha256)
        { Path = path; Length = length; Sha256 = sha256; }
        public string Path { get; }
        public long Length { get; }
        public string Sha256 { get; }
    }

    // A report, not permission to install a different or subsequently changed file.
    internal sealed class PayloadValidationReport
    {
        public PayloadValidationReport(PackageRecord package, string sha256, IEnumerable<ValidatedPayloadFile> files)
        { Package = package; Sha256 = sha256; Files = StoreCollections.Freeze(files); }
        public PackageRecord Package { get; }
        public string Sha256 { get; }
        public ReadOnlyCollection<ValidatedPayloadFile> Files { get; }
    }

    internal static class PayloadValidator
    {
        public const int MaxEntries = 4096;
        public const int MaxEntryBytes = 64 * 1024 * 1024;
        public const long MaxExpandedBytes = 256 * 1024 * 1024;
        public const int MaxCompressionRatio = 200;
        private static readonly HashSet<string> ResourceRoots = new HashSet<string>(StringComparer.Ordinal)
        { "About", "Defs", "Patches", "Languages", "Textures", "Sounds" };
        private static readonly HashSet<string> ResourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".xml", ".txt", ".md", ".png", ".jpg", ".jpeg", ".dds", ".wav", ".ogg" };

        public static PayloadValidationReport Validate(PackageRecord expected, Stream asset,
            byte[] companionManifest, CancellationToken cancellationToken)
        {
            if (expected == null || expected.Artifact == null || expected.IsWorkshop)
                throw new ArgumentException("A locked GitHub record is required.", nameof(expected));
            if (asset == null || !asset.CanRead) throw new ArgumentException("A readable asset stream is required.", nameof(asset));
            if (expected.Artifact.SizeBytes < 1 || expected.Artifact.SizeBytes > CatalogReader.MaxPackageBytes)
                throw Error("PayloadLimit", "Asset size exceeds the package limit.");
            // Freeze bytes before hashing/parsing; never reopen a mutable download by path.
            byte[] bytes = ReadBounded(asset, expected.Artifact.SizeBytes, cancellationToken);
            if (bytes.LongLength != expected.Artifact.SizeBytes) throw Error("PayloadSizeMismatch", "Asset length differs from the locked index.");
            cancellationToken.ThrowIfCancellationRequested();
            string digest = CatalogReader.Hash(bytes);
            if (digest != expected.Artifact.Sha256) throw Error("PayloadDigestMismatch", "Asset SHA-256 differs from the locked index.");
            List<ValidatedPayloadFile> files;
            if (expected.Artifact.PayloadKind == "rimworld-mod-zip")
            {
                if (companionManifest != null) throw Error("UnexpectedManifest", "ZIP manifests must be inside the archive.");
                files = ValidateArchive(expected, bytes, cancellationToken);
            }
            else if (expected.Artifact.PayloadKind == "dll-with-manifest")
            {
                if (bytes.Length > MaxEntryBytes) throw Error("PayloadLimit", "DLL exceeds the per-file limit.");
                if (companionManifest == null) throw Error("MissingManifest", "DLL payload requires its locked companion manifest.");
                if (companionManifest.Length > CatalogReader.MaxCatalogBytes) throw Error("PayloadLimit", "Companion manifest is too large.");
                byte[] manifest = (byte[])companionManifest.Clone();
                CatalogReader.VerifyManifest(expected, manifest);
                ValidateAssembly(expected.Assemblies.Single(), bytes, cancellationToken);
                files = new List<ValidatedPayloadFile> { new ValidatedPayloadFile(expected.Artifact.AssetName, bytes.Length, digest) };
            }
            else throw Error("UnsupportedPayload", "Unsupported payload kind.");
            cancellationToken.ThrowIfCancellationRequested();
            return new PayloadValidationReport(expected, digest, files.OrderBy(f => f.Path, StringComparer.Ordinal));
        }

        private static List<ValidatedPayloadFile> ValidateArchive(PackageRecord expected, byte[] bytes, CancellationToken token)
        {
            try
            {
                using (var input = new MemoryStream(bytes, false))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read, false))
                {
                    if (zip.Entries.Count > MaxEntries) throw Error("PayloadLimit", "Archive contains too many entries.");
                    var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var explicitPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                    long expanded = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                        string path = ValidatePath(entry.FullName, directory);
                        if (directory && path.Split('/')[0] != "Assemblies" && !ResourceRoots.Contains(path.Split('/')[0]))
                            throw Error("UnsupportedPackageLayout", "Unsupported directory root: " + path);
                        int kind = (entry.ExternalAttributes >> 16) & 0xf000;
                        if ((entry.ExternalAttributes & 0x448) != 0 || (!directory && (entry.ExternalAttributes & 0x10) != 0) ||
                            (kind != 0 && kind != (directory ? 0x4000 : 0x8000)))
                            throw Error("UnsafeArchiveEntry", "Links and special filesystem entries are forbidden: " + path);
                        if (!explicitPaths.Add(path)) throw Error("ArchivePathConflict", "Duplicate archive path: " + path);
                        RegisterPath(paths, path, directory);
                        if (entry.Length < 0 || entry.Length > MaxEntryBytes || entry.CompressedLength < 0 ||
                            entry.CompressedLength > bytes.LongLength || (directory && entry.Length != 0))
                            throw Error("PayloadLimit", "Invalid entry length: " + path);
                        expanded = checked(expanded + entry.Length);
                        if (expanded > MaxExpandedBytes || entry.Length > Math.Max(1024 * 1024L, entry.CompressedLength * MaxCompressionRatio))
                            throw Error("PayloadLimit", "Archive expansion exceeds its limits: " + path);
                        if (!directory)
                        {
                            ValidateLayout(expected, path);
                            entries.Add(path, entry);
                        }
                    }
                    ZipArchiveEntry manifestEntry, aboutEntry;
                    if (!entries.TryGetValue("phinix-package.json", out manifestEntry)) throw Error("MissingManifest", "ZIP requires root phinix-package.json.");
                    if (!entries.TryGetValue("About/About.xml", out aboutEntry)) throw Error("MissingAbout", "ZIP requires About/About.xml.");
                    CatalogReader.VerifyManifest(expected, ReadEntry(manifestEntry, CatalogReader.MaxCatalogBytes, token));
                    ValidateAbout(expected, ReadEntry(aboutEntry, 1024 * 1024, token));
                    foreach (PackageAssembly assembly in expected.Assemblies)
                        if (!entries.ContainsKey("Assemblies/" + assembly.FileName)) throw Error("MissingAssembly", "Missing declared assembly: " + assembly.FileName);
                    var files = new List<ValidatedPayloadFile>();
                    foreach (var pair in entries)
                    {
                        byte[] content = ReadEntry(pair.Value, MaxEntryBytes, token);
                        if (pair.Key.StartsWith("Assemblies/", StringComparison.Ordinal))
                            ValidateAssembly(expected.Assemblies.Single(a => pair.Key == "Assemblies/" + a.FileName), content, token);
                        files.Add(new ValidatedPayloadFile(pair.Key, content.Length, CatalogReader.Hash(content)));
                    }
                    return files;
                }
            }
            catch (InvalidDataException ex) { throw new StoreValidationException("InvalidArchive", "Malformed or unsupported ZIP data.", ex); }
            catch (OverflowException ex) { throw new StoreValidationException("PayloadLimit", "Archive size overflow.", ex); }
        }

        internal static string ValidatePath(string fullName, bool directory)
        {
            if (string.IsNullOrEmpty(fullName) || fullName.Length > 240 || fullName.Contains("\\") || !fullName.IsNormalized(NormalizationForm.FormC))
                throw Error("UnsafeArchivePath", "Expected a portable, normalized relative archive path.");
            string path = directory ? fullName.Substring(0, fullName.Length - 1) : fullName;
            string[] parts = path.Split('/');
            if (parts.Length > 16) throw Error("UnsafeArchivePath", "Archive path is too deep.");
            foreach (string part in parts)
            {
                if (part.Length == 0 || part == "." || part == ".." || part.Length > 160 ||
                    part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                    part.Any(c => char.IsControl(c) || "<>:\"|?*".IndexOf(c) >= 0) ||
                    Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw Error("UnsafeArchivePath", "Invalid archive path component: " + part);
            }
            return path;
        }

        internal static void RegisterPath(Dictionary<string, string> paths, string path, bool directory)
        {
            string[] parts = path.Split('/');
            string prefix = "";
            for (int i = 0; i < parts.Length; i++)
            {
                prefix = i == 0 ? parts[i] : prefix + "/" + parts[i];
                string identity = prefix + (i < parts.Length - 1 || directory ? "/" : "");
                string previous;
                if (paths.TryGetValue(prefix, out previous) && previous != identity)
                    throw Error("ArchivePathConflict", "Case alias or file/directory collision: " + path);
                paths[prefix] = identity;
            }
        }

        private static void ValidateLayout(PackageRecord expected, string path)
        {
            if (path == "phinix-package.json" || path == "LICENSE" || path == "LICENSE.txt" || path == "README.md" || path == "README.txt") return;
            string[] parts = path.Split('/');
            if (parts.Length == 2 && parts[0] == "Assemblies" && expected.Assemblies.Any(a => parts[1] == a.FileName)) return;
            if (parts.Length >= 2 && ResourceRoots.Contains(parts[0]) && ResourceExtensions.Contains(System.IO.Path.GetExtension(path))) return;
            throw Error("UnsupportedPackageLayout", "Undeclared DLL, script or unsupported package path: " + path);
        }

        internal static byte[] ReadEntry(ZipArchiveEntry entry, int limit, CancellationToken token)
        {
            if (entry.Length > limit) throw Error("PayloadLimit", "Entry exceeds its content limit: " + entry.FullName);
            using (Stream stream = entry.Open())
            {
                byte[] bytes = ReadBounded(stream, entry.Length, token);
                if (bytes.LongLength != entry.Length) throw Error("InvalidArchive", "Decompressed entry length differs: " + entry.FullName);
                return bytes;
            }
        }

        internal static byte[] ReadBounded(Stream stream, long limit, CancellationToken token)
        {
            using (var result = new MemoryStream())
            {
                byte[] buffer = new byte[64 * 1024];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - result.Length + 1));
                    if (count == 0) return result.ToArray();
                    if (count > limit - result.Length) throw Error("PayloadLimit", "Stream exceeds its declared byte limit.");
                    result.Write(buffer, 0, count);
                }
            }
        }

        private static void ValidateAbout(PackageRecord expected, byte[] bytes)
        {
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
                XElement root;
                using (var input = new MemoryStream(bytes, false))
                using (XmlReader reader = XmlReader.Create(input, settings)) root = XElement.Load(reader);
                if (root.Name != "ModMetaData" || root.Elements("packageId").Count() != 1 ||
                    root.Element("packageId").Value != expected.RimWorldPackageId || root.Elements("supportedVersions").Count() != 1)
                    throw Error("AboutMismatch", "About package identity differs from the locked manifest.");
                string[] versions = root.Element("supportedVersions").Elements().Select(e => e.Name == "li" ? e.Value : "").ToArray();
                if (versions.Distinct(StringComparer.Ordinal).Count() != versions.Length ||
                    !new HashSet<string>(versions, StringComparer.Ordinal).SetEquals(expected.Compatibility.RimWorldVersions))
                    throw Error("AboutMismatch", "About supported versions differ from the locked manifest.");
            }
            catch (XmlException ex) { throw new StoreValidationException("InvalidAbout", "Invalid or unsafe About XML.", ex); }
        }

        private static void ValidateAssembly(PackageAssembly expected, byte[] bytes, CancellationToken token)
        {
            RequireManagedDll(bytes);
            // BCL reads the assembly manifest without loading it into the AppDomain.
            // Random filename, CreateNew, no archive path and no dependency resolution.
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phinix-metadata-" + Guid.NewGuid().ToString("N") + ".dll");
            bool created = false;
            try
            {
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { created = true; file.Write(bytes, 0, bytes.Length); }
                token.ThrowIfCancellationRequested();
                AssemblyName actual = AssemblyName.GetAssemblyName(path);
                if (string.IsNullOrEmpty(actual.Name)) throw Error("InvalidAssemblyPayload", "DLL has no assembly identity.");
                if (CatalogReader.IsProtectedAssembly(actual.Name)) throw Error("ProtectedAssembly", "Actual DLL identity is protected: " + actual.Name);
                if (actual.Name != expected.Name || actual.Version == null || actual.Version.ToString() != expected.Version ||
                    !string.IsNullOrEmpty(actual.CultureName))
                    throw Error("AssemblyIdentityMismatch", "DLL metadata differs from its declared name/version: " + expected.FileName);
            }
            catch (BadImageFormatException ex) { throw new StoreValidationException("InvalidAssemblyPayload", "DLL has invalid managed metadata.", ex); }
            catch (FileLoadException ex) { throw new StoreValidationException("InvalidAssemblyPayload", "DLL metadata cannot be read.", ex); }
            catch (ArgumentException ex) { throw new StoreValidationException("InvalidAssemblyPayload", "DLL metadata is invalid.", ex); }
            finally { if (created) File.Delete(path); }
        }

        // Bounded PE header inspection only. Metadata parsing belongs to the BCL.
        private static void RequireManagedDll(byte[] bytes)
        {
            try
            {
                if (U16(bytes, 0) != 0x5a4d) throw new InvalidDataException();
                uint pe = U32(bytes, 0x3c);
                if (U32(bytes, pe) != 0x00004550 || (U16(bytes, pe + 22L) & 0x2000) == 0) throw new InvalidDataException();
                int count = U16(bytes, pe + 6L), optionalSize = U16(bytes, pe + 20L);
                long optional = pe + 24L;
                int magic = U16(bytes, optional);
                int directoryOffset = magic == 0x10b ? 96 : magic == 0x20b ? 112 : 0;
                if (directoryOffset == 0 || optionalSize < directoryOffset + 15 * 8 ||
                    U32(bytes, optional + directoryOffset - 4) < 15) throw new InvalidDataException();
                uint cliRva = U32(bytes, optional + directoryOffset + 14 * 8), cliSize = U32(bytes, optional + directoryOffset + 14 * 8 + 4);
                if (cliSize < 72 || count == 0 || count > 96) throw new InvalidDataException();
                long cli = -1, sections = optional + optionalSize;
                for (int i = 0; i < count; i++)
                {
                    long section = sections + i * 40L;
                    uint virtualAddress = U32(bytes, section + 12), rawSize = U32(bytes, section + 16), rawOffset = U32(bytes, section + 20);
                    if (cliRva >= virtualAddress && (long)cliRva - virtualAddress <= (long)rawSize - 72)
                    {
                        if (cli != -1) throw new InvalidDataException();
                        cli = (long)rawOffset + cliRva - virtualAddress;
                    }
                }
                if (cli < 0 || cli > bytes.LongLength - 72 || U32(bytes, cli) < 72 ||
                    (U32(bytes, cli + 16) & 0x11) != 1) throw new InvalidDataException();
            }
            catch (InvalidDataException ex) { throw new StoreValidationException("InvalidAssemblyPayload", "Expected an IL-only managed PE DLL, without a native entry point.", ex); }
        }

        private static ushort U16(byte[] bytes, long offset)
        {
            if (offset < 0 || offset > bytes.LongLength - 2) throw new InvalidDataException();
            return (ushort)(bytes[(int)offset] | bytes[(int)offset + 1] << 8);
        }
        private static uint U32(byte[] bytes, long offset)
        {
            if (offset < 0 || offset > bytes.LongLength - 4) throw new InvalidDataException();
            return (uint)(bytes[(int)offset] | bytes[(int)offset + 1] << 8 | bytes[(int)offset + 2] << 16 | bytes[(int)offset + 3] << 24);
        }
        private static StoreValidationException Error(string code, string message) { return new StoreValidationException(code, message); }
    }
}
