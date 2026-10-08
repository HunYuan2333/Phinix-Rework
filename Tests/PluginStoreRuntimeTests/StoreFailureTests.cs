using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phinix.PluginStore;

internal static partial class Program
{
    private static void StoreFailureRegression()
    {
        var cases=new[]{
            new[]{"ManagedInstallRecoveryRequired","Recovery","recoveryFriendly"},
            new[]{"ManagedTransactionPending","Recovery","recoveryFriendly"},
            new[]{"ManagedInstallOutcomeUncertain","Recovery","recoveryFriendly"},
            new[]{"ManagedInstallPathTooLong","Storage","pathTooLongFriendly"},
            new[]{"InstallationStorageFailed","Storage","storageFriendly"},
            new[]{"ManagedInstallStorageFailed","Storage","storageFriendly"},
            new[]{"IncompleteEnvironment","Environment","environmentFriendly"},
            new[]{"EnvironmentCaptureTimeout","Environment","environmentFriendly"},
            new[]{"ManagedInventoryUnavailable","Environment","environmentFriendly"},
            new[]{"ManagedEnvironmentChanged","Environment","environmentChangedFriendly"},
            new[]{"ManagedInventoryUncertain","Environment","ownershipFriendly"},
            new[]{"ManagedOwnershipUncertain","Environment","ownershipFriendly"},
            new[]{"ManagedAllModulesDisabled","Target","disabledModulesRecovery"},
            new[]{"ManagedDependencyDisabled","Target","disabledPackageRecovery"},
            new[]{"CandidateAssemblyConflict","Target","assemblyConflictRecovery"},
            new[]{"CandidatePackageDependencyUnavailable","Target","dependencyFriendly"},
            new[]{"CandidateExternalModMissing","Target","dependencyFriendly"},
            new[]{"CandidateHostIncompatible","Target","compatibilityFriendly"},
            new[]{"CandidateAssemblyReferenceUnavailable","Target","compatibilityFriendly"},
            new[]{"RepositoryRateLimited","Repository","rateLimitedFriendly"},
            new[]{"RepositoryUnavailable","Repository","networkFriendly"},
            new[]{"RepositoryTimeout","Repository","networkFriendly"},
            new[]{"RepositoryStale","Action","reviewFriendly"},
            new[]{"SnapshotChanged","Action","reviewFriendly"},
            new[]{"ManagedStateChanged","Action","reviewFriendly"},
            new[]{"CacheUnavailable","Action","reviewFriendly"},
            new[]{"InvalidIndexPath","Action","inputFriendly"},
            new[]{"StoreBusy","Action","busyFriendly"},
            new[]{"PackageUnavailable","Target","unavailableFriendly"},
            new[]{"FileDigestMismatch","Target","contentFriendly"},
            new[]{"InvalidManifest","Target","contentFriendly"},
            new[]{"FutureFailure","Unexpected","failedFriendly"}
        };
        foreach(var item in cases)
        {
            var failure=StoreFailureInfo.FromCode(item[0]);
            Assert(failure.Code==item[0] && failure.Scope.ToString()==item[1] && failure.MessageKey==item[2],"Common error classification preserves code: "+item[0]);
        }
        const string request="12345678-1234-1234-1234-123456789abc";
        var rejected=new StoreValidationException("ManagedInventoryUncertain","SECRET /private/path https://SECRET")
            {RequestId=request,ContextReasons=new[]{"ReceiptInvalid","ReceiptInvalid","/private/SECRET","SECRET token"}};
        var detail=StoreFailureInfo.FromException(rejected);
        var pending=StoreFailureInfo.FromCode("ManagedInventoryUncertain",reasons:new[]{"ManagedTransactionPending"});
        Assert(pending.Scope==StoreFailureScope.Recovery && pending.MessageKey=="recoveryFriendly" && pending.Code=="ManagedInventoryUncertain",
            "Pending transactions receive recovery guidance while preserving the original gate code.");
        Assert(detail.RequestId==request && detail.ContextReasons.SequenceEqual(new[]{"ReceiptInvalid"}),"Safe context preserves correlation and deduplicates root causes.");
        Assert(!detail.Diagnostic.Contains("SECRET") && detail.Diagnostic.Contains(request),"Player details exclude raw exception content.");
        Assert(StoreFailureInfo.FromCode("SECRET token","bad request").Code=="StoreOperationFailed","Unsafe codes use the generic fallback.");
        Assert(StoreFailureInfo.FromCode("FutureFailure",reasons:Enumerable.Range(0,100).Select(i=>"Cause"+i)).ContextReasons.Count==32,"Error cause lists are bounded.");
        var logs=new List<string>();
        new RepositoryDiagnostics(logs.Add) {Operation="Planning"}.Failure("managed.operation_failed","ManagedStore",rejected);
        Assert(logs.Single().Contains("\"failureScope\":\"Environment\"") && logs.Single().Contains("ReceiptInvalid") && !logs.Single().Contains("SECRET"),"Safe audit records scope and actual causes.");
        var warnings=new List<string>();
        new RepositoryDiagnostics(warnings.Add).Warning("BadgeIcons","StoreBadgeUnavailable","Official:IOException:/private/SECRET");
        Assert(warnings.Single().Contains("store.presentation_warning") && !warnings.Single().Contains("SECRET"),"Presentation warnings share the bounded structured audit format.");

    }
}
