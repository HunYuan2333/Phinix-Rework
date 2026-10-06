using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Phinix.PluginStore
{
    internal static class CatalogReader
    {
        public const int SchemaVersion = 1;
        public const int MaxCatalogBytes = 2 * 1024 * 1024;
        public const long MaxPackageBytes = 128 * 1024 * 1024;
        private static readonly Regex IdPattern = new Regex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex AssemblyPattern = new Regex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex RepositoryPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant);

        public static CatalogSnapshot Read(byte[] utf8, string expectedSourceId)
        {
            XElement root = ReadJson(utf8);
            Dictionary<string, XElement> fields = Object(root, "catalog", "schemaVersion", "sourceId", "snapshotId", "packages");
            RequireSchema(fields);
            string sourceId = Identifier(Required(fields, "sourceId"), "sourceId");
            if (!string.Equals(sourceId, expectedSourceId, StringComparison.Ordinal))
                throw Error("SourceMismatch", "Catalog sourceId does not match the selected source.");
            string snapshotId = Hex(Required(fields, "snapshotId"), "snapshotId", 40);
            List<PackageRecord> packages = new List<PackageRecord>();
            foreach (XElement item in Array(Required(fields, "packages"), "packages", 1024)) packages.Add(ReadPackage(item));
            ValidateCatalog(packages);
            return new CatalogSnapshot(sourceId, snapshotId, Hash(utf8), packages);
        }

        // Package manifests contain the same record, under a versioned envelope.
        // The installer must separately compare it to the locked catalog record.
        public static PackageRecord ReadManifest(byte[] utf8)
        {
            Dictionary<string, XElement> fields = Object(ReadJson(utf8), "manifest", "schemaVersion", "package");
            RequireSchema(fields);
            PackageRecord package = ReadPackage(Required(fields, "package"), true);
            if (package.IsWorkshop) throw Error("InvalidManifest", "Workshop listings are not downloadable package manifests.");
            return package;
        }

        // Local ownership records retain one complete locked record, never a new
        // repository authority. Re-read the result with the same strict parser.
        internal static byte[] OwnershipCatalog(byte[] catalog, PackageRecord package, string source)
        {
            Read(catalog, source);
            XElement root = ReadJson(catalog);
            foreach (XElement item in root.Element("packages").Elements().ToArray())
                if (item.Element("id").Value != package.Id || item.Element("version").Value != package.Version.ToString()) item.Remove();
            using (var output = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(output, Encoding.UTF8, false)) root.WriteTo(writer);
                byte[] result = output.ToArray();
                var selected = Read(result, source);
                if (selected.Packages.Count != 1 || !SameManifest(package, selected.Packages[0]))
                    throw Error("SnapshotChanged", "The ownership record must contain the locked package.");
                return result;
            }
        }

        public static PackageRecord VerifyManifest(PackageRecord expected, byte[] utf8)
        {
            if (expected == null || expected.Artifact == null) throw new ArgumentException("A locked GitHub record is required.", nameof(expected));
            if (utf8 == null || utf8.Length > MaxCatalogBytes) throw Error("DocumentLimit", "Manifest is absent or too large.");
            if (Hash(utf8) != expected.Artifact.ManifestSha256) throw Error("ManifestDigestMismatch", expected.Id + ": manifest SHA-256 differs from the locked index.");
            PackageRecord actual = ReadManifest(utf8);
            if (!SameManifest(expected, actual)) throw Error("ManifestMismatch", expected.Id + ": manifest identity, dependencies or compatibility differ from the locked index.");
            return actual;
        }

        public static bool SameManifest(PackageRecord a, PackageRecord b)
        {
            if (a.Id != b.Id || a.Name != b.Name || a.Author != b.Author || a.License != b.License ||
                a.RimWorldPackageId != b.RimWorldPackageId || a.IntegrationKind != b.IntegrationKind ||
                a.Version == null || b.Version == null || a.Version.CompareTo(b.Version) != 0 ||
                !a.Compatibility.RimWorldVersions.SequenceEqual(b.Compatibility.RimWorldVersions) ||
                RangeText(a.Compatibility.PhinixRange) != RangeText(b.Compatibility.PhinixRange) ||
                RangeText(a.Compatibility.AbstractionsRange) != RangeText(b.Compatibility.AbstractionsRange) ||
                a.Dependencies.Count != b.Dependencies.Count || a.Modules.Count != b.Modules.Count ||
                a.Assemblies.Count != b.Assemblies.Count || a.ExternalMods.Count != b.ExternalMods.Count) return false;
            for (int i = 0; i < a.Dependencies.Count; i++)
                if (a.Dependencies[i].Id != b.Dependencies[i].Id || a.Dependencies[i].Range.Text != b.Dependencies[i].Range.Text ||
                    a.Dependencies[i].Optional != b.Dependencies[i].Optional) return false;
            for (int i = 0; i < a.Modules.Count; i++)
                if (a.Modules[i].Id != b.Modules[i].Id || !a.Modules[i].DependsOn.SequenceEqual(b.Modules[i].DependsOn)) return false;
            for (int i = 0; i < a.Assemblies.Count; i++)
                if (a.Assemblies[i].Name != b.Assemblies[i].Name || a.Assemblies[i].Version != b.Assemblies[i].Version ||
                    a.Assemblies[i].FileName != b.Assemblies[i].FileName) return false;
            for (int i = 0; i < a.ExternalMods.Count; i++)
                if (a.ExternalMods[i].PackageId != b.ExternalMods[i].PackageId || a.ExternalMods[i].WorkshopId != b.ExternalMods[i].WorkshopId) return false;
            return true;
        }

        private static string RangeText(PackageVersionRange range) { return range == null ? null : range.Text; }

        internal static XElement ReadJson(byte[] utf8)
        {
            if (utf8 == null || utf8.Length == 0 || utf8.Length > MaxCatalogBytes)
                throw Error("DocumentLimit", "JSON must contain 1 to " + MaxCatalogBytes + " bytes.");
            ValidateDocumentBoundary(utf8);
            XmlDictionaryReaderQuotas quotas = new XmlDictionaryReaderQuotas
            {
                MaxDepth = 32, MaxStringContentLength = 65536, MaxArrayLength = 4096,
                MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384
            };
            try
            {
                // BCL JSON reader is available on net472; no new runtime DLL is bundled.
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                    utf8, 0, utf8.Length, new UTF8Encoding(false, true), quotas, null))
                {
                    XElement root = XElement.Load(reader);
                    if (reader.MoveToContent() != XmlNodeType.None) throw Error("InvalidJson", "Trailing JSON content is forbidden.");
                    foreach (XElement node in root.DescendantsAndSelf())
                        if (node.Name.NamespaceName.Length != 0 || node.Attributes().Any(a => a.Name != "type"))
                            throw Error("InvalidJson", "Unsupported JSON property name or type metadata.");
                    return root;
                }
            }
            catch (XmlException ex) { throw new StoreValidationException("InvalidJson", "Malformed JSON or reader quota exceeded.", ex); }
            catch (DecoderFallbackException ex) { throw new StoreValidationException("InvalidJson", "JSON is not valid UTF-8.", ex); }
        }

        internal static void ValidateDocumentBoundary(byte[] bytes)
        {
            // The BCL JSON reader can stop at the first root and ignore trailing bytes.
            // Locate the one complete object without treating braces inside strings as structure.
            int start = 0;
            while (start < bytes.Length && IsJsonWhitespace(bytes[start])) start++;
            if (start == bytes.Length || bytes[start] != '{') throw Error("InvalidJson", "Expected one JSON object.");
            int depth = 0;
            bool quoted = false, escaped = false;
            for (int i = start; i < bytes.Length; i++)
            {
                byte c = bytes[i];
                if (quoted)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    else if (c < 32) throw Error("InvalidJson", "Unescaped control character in JSON string.");
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '{' || c == '[')
                {
                    if (++depth > 32) throw Error("InvalidJson", "JSON exceeds depth 32.");
                }
                else if (c == '}' || c == ']')
                {
                    int previous = i - 1;
                    while (previous >= start && IsJsonWhitespace(bytes[previous])) previous--;
                    if (previous >= start && bytes[previous] == ',') throw Error("InvalidJson", "Trailing JSON commas are forbidden.");
                    if (--depth != 0) continue;
                    for (int j = i + 1; j < bytes.Length; j++)
                        if (!IsJsonWhitespace(bytes[j])) throw Error("InvalidJson", "Trailing JSON content is forbidden.");
                    return;
                }
                else if (c == '/' || c == '\'') throw Error("InvalidJson", "JSON comments and single-quoted strings are forbidden.");
            }
            throw Error("InvalidJson", "Incomplete JSON object.");
        }

        private static bool IsJsonWhitespace(byte c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }

        private static void RequireSchema(Dictionary<string, XElement> fields)
        {
            if (Integer(Required(fields, "schemaVersion"), "schemaVersion", 1, int.MaxValue) != SchemaVersion)
                throw Error("UnsupportedSchema", "Only schemaVersion 1 is supported.");
        }

        private static PackageRecord ReadPackage(XElement node, bool manifest = false)
        {
            Dictionary<string, XElement> f = Object(node, "package", "id", "name", "author", "license", "channel",
                "rimWorldPackageId", "integrationKind", "state", "version", "compatibility", "dependencies",
                "modules", "assemblies", "externalMods", "artifact", "workshopId");
            string id = Identifier(Required(f, "id"), "id");
            string name = Text(Required(f, "name"), "name", 160);
            string author = Text(Required(f, "author"), "author", 160);
            string license = Text(Required(f, "license"), "license", 128);
            string modId = Identifier(Required(f, "rimWorldPackageId"), id + ".rimWorldPackageId");
            string integration = Choice(Required(f, "integrationKind"), "integrationKind", "phinix-extension", "rimworld-mod");
            string state = manifest ? "active" : Choice(Required(f, "state"), "state", "active", "withdrawn", "unmaintained");
            bool workshop = Choice(Required(f, "channel"), "channel", "github-release", "steam-workshop") == "steam-workshop";
            if (manifest) Forbid(f, "artifact", "workshopId", "state");
            PackageVersion version = null;
            GitHubArtifact artifact = null;
            string workshopId = null;
            if (workshop)
            {
                Forbid(f, "version", "artifact");
                workshopId = PositiveId(Required(f, "workshopId"), "workshopId");
            }
            else
            {
                Forbid(f, "workshopId");
                if (!PackageVersion.TryParse(Text(Required(f, "version"), "version", 32), out version))
                    throw Error("InvalidVersion", id + ": version must be stable MAJOR.MINOR.PATCH.");
                if (!manifest) artifact = ReadArtifact(Required(f, "artifact"), version);
            }
            PackageCompatibility compatibility = ReadCompatibility(Required(f, "compatibility"), workshop, integration);
            List<PackageDependency> dependencies = new List<PackageDependency>();
            HashSet<string> dependencyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement item in Array(Required(f, "dependencies"), "dependencies", 64))
            {
                Dictionary<string, XElement> d = Object(item, "dependency", "packageId", "versionRange", "optional");
                string depId = Identifier(Required(d, "packageId"), "dependency.packageId");
                if (!dependencyIds.Add(depId)) throw Error("DuplicateDependency", id + ": duplicate dependency " + depId + ".");
                dependencies.Add(new PackageDependency(depId, Range(Required(d, "versionRange"), "versionRange"),
                    Boolean(Required(d, "optional"), "optional")));
            }
            List<PackageModule> modules = new List<PackageModule>();
            HashSet<string> moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement item in Array(Required(f, "modules"), "modules", 64))
            {
                Dictionary<string, XElement> m = Object(item, "module", "id", "dependsOn");
                string moduleId = Identifier(Required(m, "id"), "module.id");
                if (!moduleIds.Add(moduleId)) throw Error("DuplicateModule", id + ": duplicate module " + moduleId + ".");
                modules.Add(new PackageModule(moduleId, Strings(Required(m, "dependsOn"), "dependsOn", 64, true)));
            }
            List<PackageAssembly> assemblies = new List<PackageAssembly>();
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement item in Array(Required(f, "assemblies"), "assemblies", 64))
            {
                Dictionary<string, XElement> a = Object(item, "assembly", "name", "version", "fileName");
                string assemblyName = Text(Required(a, "name"), "assembly.name", 128);
                if (!AssemblyPattern.IsMatch(assemblyName)) throw Error("InvalidAssembly", id + ": invalid assembly name.");
                if (IsProtectedAssembly(assemblyName))
                    throw Error("ProtectedAssembly", id + ": may not bundle " + assemblyName + ".");
                string assemblyVersion = Text(Required(a, "version"), "assembly.version", 40);
                Version parsedVersion;
                if (!Version.TryParse(assemblyVersion, out parsedVersion) || parsedVersion.Build < 0 || parsedVersion.Revision < 0 ||
                    parsedVersion.ToString() != assemblyVersion)
                    throw Error("InvalidAssembly", id + ": assembly version must contain four canonical components.");
                string fileName = FileName(Required(a, "fileName"), "assembly.fileName", ".dll");
                if (!names.Add(assemblyName) || !files.Add(fileName)) throw Error("DuplicateAssembly", id + ": duplicate assembly name/file.");
                assemblies.Add(new PackageAssembly(assemblyName, assemblyVersion, fileName));
            }
            List<ExternalModRequirement> externalMods = new List<ExternalModRequirement>();
            HashSet<string> externalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement item in Array(Required(f, "externalMods"), "externalMods", 64))
            {
                Dictionary<string, XElement> e = Object(item, "externalMod", "packageId", "workshopId");
                string externalId = Identifier(Required(e, "packageId"), "externalMod.packageId");
                if (!externalIds.Add(externalId) || externalId == modId) throw Error("InvalidExternalMod", id + ": duplicate/self external mod.");
                externalMods.Add(new ExternalModRequirement(externalId,
                    e.ContainsKey("workshopId") ? PositiveId(e["workshopId"], "externalMod.workshopId") : null));
            }
            if (workshop && (dependencies.Count != 0 || modules.Count != 0 || assemblies.Count != 0 || externalMods.Count != 0))
                throw Error("WorkshopMetadataOnly", id + ": Workshop dependencies and content are managed by Steam, not this planner.");
            if (!workshop && (assemblies.Count == 0 || (integration == "phinix-extension" && modules.Count == 0)))
                throw Error("MissingEntry", id + ": GitHub code packages must declare assemblies and applicable modules.");
            if (integration == "rimworld-mod" && modules.Count != 0)
                throw Error("InvalidIntegration", id + ": a plain RimWorld mod must not declare Phinix modules.");
            if (!workshop && !manifest && artifact.PayloadKind == "dll-with-manifest" &&
                (assemblies.Count != 1 || !string.Equals(assemblies[0].FileName, artifact.AssetName, StringComparison.Ordinal)))
                throw Error("InvalidDllPayload", id + ": DLL payload must name its single declared assembly file.");
            return new PackageRecord(id, name, author, license, modId, integration, state, version,
                compatibility, dependencies, modules, assemblies, externalMods, artifact, workshopId);
        }

        private static PackageCompatibility ReadCompatibility(XElement node, bool workshop, string integration)
        {
            Dictionary<string, XElement> f = Object(node, "compatibility", "targetFramework", "rimWorldVersions", "phinixRange", "abstractionsRange");
            List<string> versions = Strings(Required(f, "rimWorldVersions"), "rimWorldVersions", 16, false);
            if (versions.Count == 0 || versions.Any(v => !Regex.IsMatch(v, "^[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)))
                throw Error("InvalidCompatibility", "rimWorldVersions must list major.minor versions.");
            if (workshop) Forbid(f, "targetFramework", "phinixRange", "abstractionsRange");
            else if (Text(Required(f, "targetFramework"), "targetFramework", 16) != "net472")
                throw Error("InvalidCompatibility", "Only net472 client packages are supported.");
            PackageVersionRange phinix = f.ContainsKey("phinixRange") ? Range(f["phinixRange"], "phinixRange") : null;
            PackageVersionRange abstractions = f.ContainsKey("abstractionsRange") ? Range(f["abstractionsRange"], "abstractionsRange") : null;
            if (!workshop && integration == "phinix-extension" && (phinix == null || abstractions == null))
                throw Error("InvalidCompatibility", "Phinix extensions require phinixRange and abstractionsRange.");
            return new PackageCompatibility(versions, phinix, abstractions);
        }

        internal static GitHubArtifact ReadArtifact(XElement node, PackageVersion version, bool managed = false)
        {
            Dictionary<string, XElement> f = Object(node, "artifact", "repository", "repositoryId", "ownerId", "sourceCommit",
                "tag", "releaseId", "assetId", "assetName", "payloadKind", "sha256", "manifestSha256", "sizeBytes");
            string repository = Text(Required(f, "repository"), "repository", 140);
            if (!RepositoryPattern.IsMatch(repository) || repository.Split('/')[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                throw Error("InvalidRepository", "Artifact repository must be owner/repo, never a URL or path.");
            string tag = Text(Required(f, "tag"), "tag", 40);
            if (tag != version.ToString() && tag != "v" + version)
                throw Error("InvalidTag", "Tag must match the package version, optionally prefixed with v.");
            string payload = managed ? Choice(Required(f, "payloadKind"), "payloadKind", "managed-dll-zip") :
                Choice(Required(f, "payloadKind"), "payloadKind", "rimworld-mod-zip", "dll-with-manifest");
            return new GitHubArtifact(repository, PositiveId(Required(f, "repositoryId"), "repositoryId"),
                PositiveId(Required(f, "ownerId"), "ownerId"), Hex(Required(f, "sourceCommit"), "sourceCommit", 40), tag,
                PositiveId(Required(f, "releaseId"), "releaseId"), PositiveId(Required(f, "assetId"), "assetId"),
                FileName(Required(f, "assetName"), "assetName", payload == "dll-with-manifest" ? ".dll" : ".zip"), payload,
                Hex(Required(f, "sha256"), "sha256", 64), Hex(Required(f, "manifestSha256"), "manifestSha256", 64),
                Integer(Required(f, "sizeBytes"), "sizeBytes", 1, MaxPackageBytes));
        }

        private static void ValidateCatalog(List<PackageRecord> packages)
        {
            HashSet<string> versions = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, PackageRecord> first = new Dictionary<string, PackageRecord>(StringComparer.Ordinal);
            Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (PackageRecord package in packages)
            {
                if (!versions.Add(package.Id + "@" + (package.IsWorkshop ? "workshop" : package.Version.ToString())))
                    throw Error("DuplicateVersion", package.Id + ": duplicate version/listing.");
                int count; counts.TryGetValue(package.Id, out count); counts[package.Id] = count + 1;
                if (count >= 32) throw Error("CandidateLimit", package.Id + ": more than 32 catalog versions.");
                PackageRecord previous;
                if (first.TryGetValue(package.Id, out previous))
                {
                    if (package.IsWorkshop != previous.IsWorkshop || package.RimWorldPackageId != previous.RimWorldPackageId ||
                        package.IntegrationKind != previous.IntegrationKind || (!package.IsWorkshop &&
                        (package.Artifact.RepositoryId != previous.Artifact.RepositoryId || package.Artifact.OwnerId != previous.Artifact.OwnerId)))
                        throw Error("IdentityChanged", package.Id + ": identity/channel changed within one snapshot.");
                }
                else first.Add(package.Id, package);
                Claim(identities, "mod:" + package.RimWorldPackageId, package.Id);
                foreach (PackageModule module in package.Modules) Claim(identities, "module:" + module.Id, package.Id);
                foreach (PackageAssembly assembly in package.Assemblies) Claim(identities, "assembly:" + assembly.Name, package.Id);
            }
        }

        private static void Claim(Dictionary<string, string> identities, string key, string owner)
        {
            string existing;
            if (identities.TryGetValue(key, out existing) && existing != owner)
                throw Error("IdentityConflict", key + " is declared by both " + existing + " and " + owner + ".");
            identities[key] = owner;
        }

        internal static Dictionary<string, XElement> Object(XElement node, string path, params string[] allowed)
        {
            Type(node, "object", path);
            Dictionary<string, XElement> result = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (XElement field in node.Elements())
            {
                string name = field.Name.LocalName;
                if (!allowed.Contains(name)) throw Error("UnknownField", path + ": unsupported field " + name + ".");
                if (result.ContainsKey(name)) throw Error("DuplicateField", path + ": repeated field " + name + ".");
                result.Add(name, field);
            }
            return result;
        }

        internal static XElement Required(Dictionary<string, XElement> fields, string name)
        {
            XElement result;
            if (!fields.TryGetValue(name, out result)) throw Error("MissingField", "Missing required field " + name + ".");
            return result;
        }

        internal static void Forbid(Dictionary<string, XElement> fields, params string[] names)
        {
            foreach (string name in names) if (fields.ContainsKey(name)) throw Error("UnexpectedField", "Field " + name + " does not apply to this channel.");
        }

        internal static List<XElement> Array(XElement node, string path, int maximum)
        {
            Type(node, "array", path);
            List<XElement> items = node.Elements().ToList();
            if (items.Count > maximum) throw Error("CollectionLimit", path + ": too many entries.");
            return items;
        }

        private static List<string> Strings(XElement node, string path, int maximum, bool identifiers)
        {
            List<string> result = new List<string>();
            foreach (XElement item in Array(node, path, maximum))
            {
                string value = identifiers ? Identifier(item, path) : Text(item, path, 128);
                if (result.Contains(value)) throw Error("DuplicateValue", path + ": repeated value " + value + ".");
                result.Add(value);
            }
            return result;
        }

        private static void Type(XElement node, string type, string path)
        {
            if ((string)node.Attribute("type") != type) throw Error("WrongType", path + " must be a JSON " + type + ".");
        }

        internal static string Text(XElement node, string path, int maximum)
        {
            Type(node, "string", path);
            string value = node.Value;
            if (value.Length == 0 || value.Length > maximum || value.Trim() != value || value.Any(char.IsControl))
                throw Error("InvalidText", path + ": invalid length, whitespace or control characters.");
            return value;
        }

        internal static string Identifier(XElement node, string path)
        {
            string value = Text(node, path, 128);
            if (!IdPattern.IsMatch(value)) throw Error("InvalidId", path + ": expected a lowercase dotted identifier.");
            return value;
        }

        internal static string Hex(XElement node, string path, int length)
        {
            string value = Text(node, path, length);
            if (value.Length != length || value.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
                throw Error("InvalidDigest", path + ": expected " + length + " lowercase hexadecimal characters.");
            return value;
        }

        internal static string PositiveId(XElement node, string path)
        {
            string value = Text(node, path, 20);
            ulong parsed;
            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed == 0 ||
                parsed.ToString(CultureInfo.InvariantCulture) != value)
                throw Error("InvalidSourceId", path + ": expected a nonzero canonical UInt64 decimal string.");
            return value;
        }

        internal static long Integer(XElement node, string path, long minimum, long maximum)
        {
            Type(node, "number", path);
            long value;
            if (!long.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < minimum || value > maximum ||
                value.ToString(CultureInfo.InvariantCulture) != node.Value)
                throw Error("InvalidNumber", path + ": integer is out of range.");
            return value;
        }

        internal static bool Boolean(XElement node, string path)
        {
            Type(node, "boolean", path);
            if (node.Value != "true" && node.Value != "false") throw Error("WrongType", path + ": invalid boolean.");
            return node.Value == "true";
        }

        internal static string Choice(XElement node, string path, params string[] options)
        {
            string value = Text(node, path, 64);
            if (!options.Contains(value)) throw Error("UnsupportedValue", path + ": unsupported value " + value + ".");
            return value;
        }

        private static PackageVersionRange Range(XElement node, string path)
        {
            PackageVersionRange range;
            if (!PackageVersionRange.TryParse(Text(node, path, 80), out range))
                throw Error("InvalidRange", path + ": use an exact version or >=x.y.z <x.y.z.");
            return range;
        }

        private static string FileName(XElement node, string path, string extension)
        {
            string value = Text(node, path, 160);
            // Portable single path component: avoid Windows devices, ADS and trailing dots.
            if (!Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant) ||
                value.Contains("..") || !value.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(value, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw Error("InvalidFileName", path + ": expected a portable " + extension + " filename.");
            return value;
        }

        internal static bool IsProtectedAssembly(string name)
        {
            return Utils.Framework.ManagedExtensions.ManagedExtensionManifestReader.IsProtectedAssembly(name);
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static StoreValidationException Error(string code, string message) { return new StoreValidationException(code, message); }
    }
}
