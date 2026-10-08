using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Phinix.PluginStore
{
    internal sealed class StoreValidationException : Exception
    {
        public StoreValidationException(string code, string message) : base(message) { Code = code; }
        public StoreValidationException(string code, string message, Exception inner) : base(message, inner) { Code = code; }
        public string Code { get; }
        internal string RequestId { get; set; }
        internal IEnumerable<string> ContextReasons { get; set; }
        internal LocalIdentityDiagnostic LocalIdentity { get; set; }
        internal Utils.Framework.ManagedExtensions.ManagedExtensionAssemblyReferenceFailure ReferenceFailure { get; set; }
    }

    internal enum StoreFailureScope { Target, Environment, Repository, Storage, Recovery, Action, Unexpected }

    // Presentation and safe diagnostics only. This never authorizes a retry or
    // changes the installation/ownership gates that produced the failure.
    internal sealed class StoreFailureInfo
    {
        private StoreFailureInfo(string code,string requestId,IEnumerable<string> contextReasons)
        {
            Code=SafeCode(code)??"StoreOperationFailed";
            RequestId=SafeRequestId(requestId);
            var reasons=new List<string>();
            if(contextReasons!=null) foreach(var reason in contextReasons)
            {
                string safe=SafeCode(reason);
                if(safe!=null && !reasons.Contains(safe)) reasons.Add(safe);
                if(reasons.Count==32) break;
            }
            ContextReasons=StoreCollections.Freeze(reasons);
            Classify();
        }
        internal static string SafeCode(string value)
            => value!=null && System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[A-Za-z][A-Za-z0-9]{0,63}\z")?value:null;
        internal static string SafeRequestId(string value)
            => value!=null && System.Text.RegularExpressions.Regex.IsMatch(value,@"\A[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}\z")?value:null;
        internal string Code { get; }
        internal string RequestId { get; }
        internal ReadOnlyCollection<string> ContextReasons { get; }
        internal LocalIdentityDiagnostic LocalIdentity { get; private set; }
        internal Utils.Framework.ManagedExtensions.ManagedExtensionAssemblyReferenceFailure ReferenceFailure { get; private set; }
        internal StoreFailureScope Scope { get; private set; }
        internal string MessageKey { get; private set; }
        internal string Diagnostic => Code+(RequestId==null?"":"\nRequest ID: "+RequestId)+
            (ContextReasons.Count==0?"":"\nCauses: "+string.Join(", ",ContextReasons));
        internal static StoreFailureInfo FromCode(string code,string requestId=null,IEnumerable<string> reasons=null)
            => new StoreFailureInfo(code,requestId,reasons);
        internal static StoreFailureInfo FromException(Exception error,string fallback="StoreOperationFailed")
        {
            var validation=error as StoreValidationException;
            var managed=error as Utils.Framework.ManagedExtensions.ManagedExtensionValidationException;
            string code=validation?.Code??managed?.Code;
            if(code==null)
            {
                if(error is System.IO.IOException || error is UnauthorizedAccessException) code="InstallationStorageFailed";
                else code=fallback;
            }
            var failure=new StoreFailureInfo(code,validation?.RequestId,validation?.ContextReasons)
                {LocalIdentity=validation?.LocalIdentity,ReferenceFailure=validation?.ReferenceFailure??managed?.ReferenceFailure};
            if(failure.Scope==StoreFailureScope.Unexpected && (failure.ReferenceFailure!=null || failure.LocalIdentity!=null))
                failure.Set(StoreFailureScope.Target,"compatibilityFriendly");
            return failure;
        }
        private bool Is(params string[] codes) { foreach(string code in codes) if(Code==code) return true; return false; }
        private void Set(StoreFailureScope scope,string key) { Scope=scope; MessageKey=key; }
        private void Classify()
        {
            if(Code=="ManagedInventoryUncertain") foreach(string reason in ContextReasons)
            {
                var cause=FromCode(reason);
                if(cause.Scope==StoreFailureScope.Recovery) { Set(cause.Scope,cause.MessageKey); return; }
            }
            if(Is("ManagedInstallRecoveryRequired","ManagedTransactionPending","InstallRecoveryStorageFailed","InstallationRecoveryRequired","ManagedInstallOutcomeUncertain","InstallationCleanupRequired") ||
                Code.StartsWith("InstallJournal",StringComparison.Ordinal) || Code.StartsWith("InstallWork",StringComparison.Ordinal) ||
                Code.StartsWith("UnknownManagedTransaction",StringComparison.Ordinal) || Code.StartsWith("OrphanInstall",StringComparison.Ordinal))
                { Set(StoreFailureScope.Recovery,"recoveryFriendly"); return; }
            if(Is("ManagedInstallPathTooLong")) { Set(StoreFailureScope.Storage,"pathTooLongFriendly"); return; }
            if(Code.Contains("StorageFailed") || Code.Contains("CleanupFailed")) { Set(StoreFailureScope.Storage,"storageFriendly"); return; }
            if(Is("IncompleteEnvironment","EnvironmentCaptureTimeout","ManagedManagementUnavailable","ManagedInventoryUnavailable"))
                { Set(StoreFailureScope.Environment,"environmentFriendly"); return; }
            if(Is("ManagedEnvironmentChanged")) { Set(StoreFailureScope.Environment,"environmentChangedFriendly"); return; }
            if(Is("ManagedInventoryUncertain","ManagedOwnershipUncertain","LocalIdentityUncertain") || Code.Contains("Ownership"))
                { Set(StoreFailureScope.Environment,"ownershipFriendly"); return; }
            if(Is("ManagedAllModulesDisabled")) { Set(StoreFailureScope.Target,"disabledModulesRecovery"); return; }
            if(Is("ManagedDependencyDisabled")) { Set(StoreFailureScope.Target,"disabledPackageRecovery"); return; }
            if(Is("CandidateAssemblyConflict","ManagedInstallPackageConflict","ManagedInstallTargetExists","CandidateModuleConflict"))
                { Set(StoreFailureScope.Target,"assemblyConflictRecovery"); return; }
            if(Code.Contains("Dependency") || Is("CandidateExternalModMissing","ExternalModMissing"))
                { Set(StoreFailureScope.Target,"dependencyFriendly"); return; }
            if(Code.StartsWith("CandidateHost",StringComparison.Ordinal) || Code.StartsWith("CandidateAssemblyReference",StringComparison.Ordinal) ||
                Is("ManagedHostAssemblyUnavailable","LocalLoadFolderInvalid","IncompatibleVersion"))
                { Set(StoreFailureScope.Target,"compatibilityFriendly"); return; }
            if(Is("RepositoryRateLimited")) { Set(StoreFailureScope.Repository,"rateLimitedFriendly"); return; }
            if(Is("RepositoryUnavailable","RepositoryTimeout")) { Set(StoreFailureScope.Repository,"networkFriendly"); return; }
            if(Is("RepositoryStale","SnapshotChanged","ManagedStateChanged","OnlineSnapshotRequired","CacheUnavailable"))
                { Set(StoreFailureScope.Action,"reviewFriendly"); return; }
            if(Is("InvalidEndpoint","InvalidIndexPath","InvalidSourceId")) { Set(StoreFailureScope.Action,"inputFriendly"); return; }
            if(Is("StoreBusy","OperationInProgress")) { Set(StoreFailureScope.Action,"busyFriendly"); return; }
            if(Is("PackageUnavailable","ManagedAlreadyInstalled","NoPackagesToDownload")) { Set(StoreFailureScope.Target,"unavailableFriendly"); return; }
            if(Code.Contains("Digest") || Code.Contains("Mismatch") || Code.StartsWith("Invalid",StringComparison.Ordinal) ||
                Code.StartsWith("Unsupported",StringComparison.Ordinal) || Code.StartsWith("AssemblyMetadata",StringComparison.Ordinal) ||
                Code.StartsWith("Archive",StringComparison.Ordinal) || Code.StartsWith("Payload",StringComparison.Ordinal))
                { Set(StoreFailureScope.Target,"contentFriendly"); return; }
            Set(StoreFailureScope.Unexpected,"failedFriendly");
        }
    }

    internal sealed class LocalIdentityDiagnostic
    {
        internal LocalIdentityDiagnostic(string modId,string relativePath,string reason)
        { ModId=modId; RelativePath=relativePath; Reason=reason; }
        internal string ModId { get; }
        internal string RelativePath { get; }
        internal string Reason { get; }
    }

    internal static class StoreCollections
    {
        public static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> items)
        {
            return new List<T>(items ?? new T[0]).AsReadOnly();
        }
    }

    internal sealed class PackageDependency
    {
        public PackageDependency(string id, PackageVersionRange range, bool optional)
        { Id = id; Range = range; Optional = optional; }
        public string Id { get; }
        public PackageVersionRange Range { get; }
        public bool Optional { get; }
    }

    internal sealed class PackageModule
    {
        public PackageModule(string id, IEnumerable<string> dependsOn)
        { Id = id; DependsOn = StoreCollections.Freeze(dependsOn); }
        public string Id { get; }
        public ReadOnlyCollection<string> DependsOn { get; }
    }

    internal sealed class PackageAssembly
    {
        public PackageAssembly(string name, string version, string fileName)
        { Name = name; Version = version; FileName = fileName; }
        public string Name { get; }
        public string Version { get; }
        public string FileName { get; }
    }

    internal sealed class ExternalModRequirement
    {
        public ExternalModRequirement(string packageId, string workshopId)
        { PackageId = packageId; WorkshopId = workshopId; }
        public string PackageId { get; }
        public string WorkshopId { get; }
        public string WorkshopUrl => WorkshopId == null ? null : "https://steamcommunity.com/sharedfiles/filedetails/?id=" + WorkshopId;
    }

    internal sealed class PackageCompatibility
    {
        public PackageCompatibility(IEnumerable<string> rimWorldVersions, PackageVersionRange phinixRange, PackageVersionRange abstractionsRange)
        { RimWorldVersions = StoreCollections.Freeze(rimWorldVersions); PhinixRange = phinixRange; AbstractionsRange = abstractionsRange; }
        public ReadOnlyCollection<string> RimWorldVersions { get; }
        public PackageVersionRange PhinixRange { get; }
        public PackageVersionRange AbstractionsRange { get; }
    }

    internal sealed class GitHubArtifact
    {
        public GitHubArtifact(string repository, string repositoryId, string ownerId, string sourceCommit,
            string tag, string releaseId, string assetId, string assetName, string payloadKind,
            string sha256, string manifestSha256, long sizeBytes)
        {
            Repository = repository; RepositoryId = repositoryId; OwnerId = ownerId; SourceCommit = sourceCommit;
            Tag = tag; ReleaseId = releaseId; AssetId = assetId; AssetName = assetName; PayloadKind = payloadKind;
            Sha256 = sha256; ManifestSha256 = manifestSha256; SizeBytes = sizeBytes;
        }
        public string Repository { get; }
        public string RepositoryId { get; }
        public string OwnerId { get; }
        public string SourceCommit { get; }
        public string Tag { get; }
        public string ReleaseId { get; }
        public string AssetId { get; }
        public string AssetName { get; }
        public string PayloadKind { get; }
        public string Sha256 { get; }
        public string ManifestSha256 { get; }
        public long SizeBytes { get; }
    }

    internal sealed class PackageRecord
    {
        public PackageRecord(string id, string name, string author, string license, string rimWorldPackageId,
            string integrationKind, string state, PackageVersion version, PackageCompatibility compatibility,
            IEnumerable<PackageDependency> dependencies, IEnumerable<PackageModule> modules,
            IEnumerable<PackageAssembly> assemblies, IEnumerable<ExternalModRequirement> externalMods,
            GitHubArtifact artifact, string workshopId)
        {
            Id = id; Name = name; Author = author; License = license; RimWorldPackageId = rimWorldPackageId;
            IntegrationKind = integrationKind; State = state; Version = version; Compatibility = compatibility;
            Dependencies = StoreCollections.Freeze(dependencies); Modules = StoreCollections.Freeze(modules);
            Assemblies = StoreCollections.Freeze(assemblies); ExternalMods = StoreCollections.Freeze(externalMods);
            Artifact = artifact; WorkshopId = workshopId;
        }
        public string Id { get; }
        public string Name { get; }
        public string Author { get; }
        public string License { get; }
        public string RimWorldPackageId { get; }
        public string IntegrationKind { get; }
        public string State { get; }
        public PackageVersion Version { get; }
        public PackageCompatibility Compatibility { get; }
        public ReadOnlyCollection<PackageDependency> Dependencies { get; }
        public ReadOnlyCollection<PackageModule> Modules { get; }
        public ReadOnlyCollection<PackageAssembly> Assemblies { get; }
        public ReadOnlyCollection<ExternalModRequirement> ExternalMods { get; }
        public GitHubArtifact Artifact { get; }
        public string WorkshopId { get; }
        public bool IsWorkshop => WorkshopId != null;
        public string WorkshopUrl => IsWorkshop ? "https://steamcommunity.com/sharedfiles/filedetails/?id=" + WorkshopId : null;
    }

    internal sealed class CatalogSnapshot
    {
        public CatalogSnapshot(string sourceId, string snapshotId, string sha256, IEnumerable<PackageRecord> packages)
        { SourceId = sourceId; SnapshotId = snapshotId; Sha256 = sha256; Packages = StoreCollections.Freeze(packages); }
        public string SourceId { get; }
        public string SnapshotId { get; }
        public string Sha256 { get; }
        public ReadOnlyCollection<PackageRecord> Packages { get; }
    }

    // Created from main-thread game facts. Null versions mean unknown, never "latest".
    internal sealed class StoreRuntimeFacts
    {
        public StoreRuntimeFacts(string rimWorldVersion, PackageVersion phinixVersion, PackageVersion abstractionsVersion,
            IEnumerable<string> providedModuleIds, IEnumerable<string> providedAssemblyNames)
        {
            RimWorldVersion = rimWorldVersion; PhinixVersion = phinixVersion; AbstractionsVersion = abstractionsVersion;
            ProvidedModuleIds = StoreCollections.Freeze(providedModuleIds);
            ProvidedAssemblyNames = StoreCollections.Freeze(providedAssemblyNames);
        }
        public string RimWorldVersion { get; }
        public PackageVersion PhinixVersion { get; }
        public PackageVersion AbstractionsVersion { get; }
        public ReadOnlyCollection<string> ProvidedModuleIds { get; }
        public ReadOnlyCollection<string> ProvidedAssemblyNames { get; }
    }

    internal sealed class InstalledPackage
    {
        // The future adapter must verify files and manifest before supplying a record.
        // A directory name, AssemblyVersion or old installation log is insufficient.
        public InstalledPackage(string sourceId, PackageRecord verifiedRecord, string rimWorldPackageId,
            IEnumerable<string> moduleIds, IEnumerable<string> assemblyNames, bool enabled)
        {
            SourceId = sourceId; VerifiedRecord = verifiedRecord; RimWorldPackageId = rimWorldPackageId;
            ModuleIds = StoreCollections.Freeze(moduleIds); AssemblyNames = StoreCollections.Freeze(assemblyNames); Enabled = enabled;
        }
        public string SourceId { get; }
        public PackageRecord VerifiedRecord { get; }
        public string RimWorldPackageId { get; }
        public ReadOnlyCollection<string> ModuleIds { get; }
        public ReadOnlyCollection<string> AssemblyNames { get; }
        public bool Enabled { get; }
    }

    internal sealed class PlannedPackage
    {
        public PlannedPackage(PackageRecord package, bool alreadyInstalled, bool needsEnable)
        { Package = package; AlreadyInstalled = alreadyInstalled; NeedsEnable = needsEnable; }
        public PackageRecord Package { get; }
        public bool AlreadyInstalled { get; }
        public bool NeedsEnable { get; }
    }

    internal sealed class InstallPlan
    {
        public InstallPlan(CatalogSnapshot snapshot, IEnumerable<PlannedPackage> packages, IEnumerable<ExternalModRequirement> externalMods)
        {
            SourceId = snapshot.SourceId; SnapshotId = snapshot.SnapshotId; CatalogSha256 = snapshot.Sha256;
            Packages = StoreCollections.Freeze(packages); ExternalMods = StoreCollections.Freeze(externalMods);
            long total = 0;
            foreach (PlannedPackage package in Packages)
                if (!package.AlreadyInstalled) total = checked(total + package.Package.Artifact.SizeBytes);
            DownloadBytes = total;
        }
        public string SourceId { get; }
        public string SnapshotId { get; }
        public string CatalogSha256 { get; }
        public ReadOnlyCollection<PlannedPackage> Packages { get; }
        public ReadOnlyCollection<ExternalModRequirement> ExternalMods { get; }
        public long DownloadBytes { get; }
    }
}
