using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Phinix.PluginStore
{
    // Fixed fields only: no endpoint URL, local path, headers, body or exception stack.
    internal sealed class RepositoryDiagnostics
    {
        private readonly Action<string> sink;
        private readonly string source;
        internal RepositoryDiagnostics(Action<string> sink = null, string source = null)
        { this.sink = sink; this.source = source; ClientRequestId = Guid.NewGuid().ToString("N"); }
        internal string ClientRequestId { get; }
        internal string CauseType { get; set; }
        internal string FailureScope { get; set; }
        internal string Operation { get; set; }
        internal string ExceptionType { get; set; }
        internal string[] ContextReasons { get; set; }
        internal string TransactionId { get; set; }
        internal string AccessMethod { get; set; }
        internal string RepositoryIdentity { get; set; }
        internal static string RequestId(string value)
        { return StoreFailureInfo.SafeRequestId(value); }
        internal static string Code(string value)
        { return StoreFailureInfo.SafeCode(value); }
        internal void Warning(string stage,string reason,string diagnostic)
        {
            var parts=(diagnostic??"").Split(':');
            Operation="Presentation"; ExceptionType=parts.Length==0?null:parts[parts.Length-1];
            ContextReasons=parts; FailureScope=StoreFailureInfo.FromCode(reason).Scope.ToString();
            Event("store.presentation_warning",stage,reason);
        }
        internal StoreFailureInfo Failure(string name,string stage,Exception error,string fallback="StoreOperationFailed")
        {
            var failure=StoreFailureInfo.FromException(error,fallback);
            var validation=error as StoreValidationException;
            var reference=validation?.ReferenceFailure??(error as Utils.Framework.ManagedExtensions.ManagedExtensionValidationException)?.ReferenceFailure;
            ExceptionType=error.GetType().Name; CauseType=error.InnerException?.GetType().Name; FailureScope=failure.Scope.ToString(); ContextReasons=failure.ContextReasons.ToArray();
            Event(name,stage,failure.Code,failure.RequestId,localIdentity:validation?.LocalIdentity,referenceFailure:reference);
            return failure;
        }
        internal void Event(string name, string stage = null, string reason = null, string requestId = null, int status = 0, long bytes = 0, PackageRecord package = null, ManagedStoreRecord managed = null, string snapshot = null, string catalogHash = null,LocalIdentityDiagnostic localIdentity=null,Utils.Framework.ManagedExtensions.ManagedExtensionAssemblyReferenceFailure referenceFailure=null,string githubRequestId=null,long? rateLimitRemaining=null,long? rateLimitReset=null,long? retryAfterSeconds=null)
        {
            if (sink == null) return;
            try
            {
                string id=package?.Id??managed?.Id; var artifact=package?.Artifact??managed?.Artifact;
                var record = new Record { SchemaVersion = 1, Time = DateTime.UtcNow.ToString("O"), Component = "client",
                    CauseType = Code(CauseType), FailureScope = Code(FailureScope), Operation = Code(Operation), ExceptionType = Code(ExceptionType),
                    ContextReasons = ContextReasons?.Select(Code).Where(c=>c!=null).Distinct().Take(32).ToArray(),
                    Event = name, ClientRequestId = ClientRequestId, Source = source,
                    AccessMethod=AccessMethod=="GitHub" || AccessMethod=="Cloudflare"?AccessMethod:null,
                    RepositoryIdentitySha256=RepositoryIdentity!=null && Regex.IsMatch(RepositoryIdentity,@"\A[a-f0-9]{64}\z")?RepositoryIdentity:null,
                    GitHubRequestId=githubRequestId!=null && githubRequestId.Length<=128 && Regex.IsMatch(githubRequestId,@"\A[A-Za-z0-9:._-]+\z")?githubRequestId:null,
                    RateLimitRemaining=Bound(rateLimitRemaining,1000000000),RateLimitReset=Bound(rateLimitReset,9999999999),RetryAfterSeconds=Bound(retryAfterSeconds,86400), Stage = Code(stage), Reason = Code(reason),
                    TransactionId = TransactionId != null && Regex.IsMatch(TransactionId, @"\A[a-f0-9]{32}\z") ? TransactionId : null,
                    RequestId = RequestId(requestId), Status = Math.Max(0, status), Bytes = Math.Max(0, bytes),
                    Package = id != null && Regex.IsMatch(id,@"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z") && id.Length<=128?id:null,
                    Version = package?.Version?.ToString()??managed?.Manifest?.Version?.ToString(),
                    Snapshot = snapshot!=null && Regex.IsMatch(snapshot,@"\A[a-f0-9]{40}\z")?snapshot:null,
                    LocalMod = localIdentity?.ModId!=null && localIdentity.ModId.Length<=128 && Regex.IsMatch(localIdentity.ModId,@"\A[a-z0-9]+(?:[._-][a-z0-9]+)*\z")?localIdentity.ModId:null,
                    LocalFile = LocalFile(localIdentity?.RelativePath), LocalReason = Code(localIdentity?.Reason),
                    ReferencingAssembly=referenceFailure?.ReferencingAssembly,RequiredReference=referenceFailure?.RequiredReference,
                    AvailableReferences=referenceFailure==null?null:System.Linq.Enumerable.ToArray(referenceFailure.AvailableReferences),
                    CatalogSha256 = catalogHash!=null && Regex.IsMatch(catalogHash,@"\A[a-f0-9]{64}\z")?catalogHash:null,
                    Sha256 = artifact?.Sha256!=null && Regex.IsMatch(artifact.Sha256,@"\A[a-f0-9]{64}\z")?artifact.Sha256:null };
                using (var output = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Record)).WriteObject(output, record);
                    sink(Encoding.UTF8.GetString(output.ToArray()));
                }
            }
            catch { /* Diagnostics cannot change download/cache authority or task outcome. */ }
        }
        private static long? Bound(long? value,long maximum) { return value>=0 && value<=maximum?value:null; }
        private static string LocalFile(string relative)
        {
            if(relative==null) return null;
            string normalized=relative.Replace('\\','/');
            string name=normalized.Substring(normalized.LastIndexOf('/')+1);
            return name.Length>0 && name.Length<=128 && Regex.IsMatch(name,@"\A[A-Za-z0-9_$][A-Za-z0-9_$ .-]*\z")?name:null;
        }
        [DataContract]
        private sealed class Record
        {
            [DataMember(Name = "schemaVersion")] public int SchemaVersion;
            [DataMember(Name = "time")] public string Time;
            [DataMember(Name = "component")] public string Component;
            [DataMember(Name = "event")] public string Event;
            [DataMember(Name = "clientRequestId")] public string ClientRequestId;
            [DataMember(Name = "causeType", EmitDefaultValue = false)] public string CauseType;
            [DataMember(Name = "failureScope", EmitDefaultValue = false)] public string FailureScope;
            [DataMember(Name = "operation", EmitDefaultValue = false)] public string Operation;
            [DataMember(Name = "exceptionType", EmitDefaultValue = false)] public string ExceptionType;
            [DataMember(Name = "contextReasons", EmitDefaultValue = false)] public string[] ContextReasons;
            [DataMember(Name = "transactionId", EmitDefaultValue = false)] public string TransactionId;
            [DataMember(Name = "source", EmitDefaultValue = false)] public string Source;
            [DataMember(Name = "accessMethod", EmitDefaultValue = false)] public string AccessMethod;
            [DataMember(Name = "repositoryIdentitySha256", EmitDefaultValue = false)] public string RepositoryIdentitySha256;
            [DataMember(Name = "githubRequestId", EmitDefaultValue = false)] public string GitHubRequestId;
            [DataMember(Name = "rateLimitRemaining", EmitDefaultValue = false)] public long? RateLimitRemaining;
            [DataMember(Name = "rateLimitReset", EmitDefaultValue = false)] public long? RateLimitReset;
            [DataMember(Name = "retryAfterSeconds", EmitDefaultValue = false)] public long? RetryAfterSeconds;
            [DataMember(Name = "stage", EmitDefaultValue = false)] public string Stage;
            [DataMember(Name = "reason", EmitDefaultValue = false)] public string Reason;
            [DataMember(Name = "requestId", EmitDefaultValue = false)] public string RequestId;
            [DataMember(Name = "status")] public int Status;
            [DataMember(Name = "bytes")] public long Bytes;
            [DataMember(Name = "package", EmitDefaultValue = false)] public string Package;
            [DataMember(Name = "version", EmitDefaultValue = false)] public string Version;
            [DataMember(Name = "sha256", EmitDefaultValue = false)] public string Sha256;
            [DataMember(Name = "snapshot", EmitDefaultValue = false)] public string Snapshot;
            [DataMember(Name = "catalogSha256", EmitDefaultValue = false)] public string CatalogSha256;
            [DataMember(Name = "localMod", EmitDefaultValue = false)] public string LocalMod;
            [DataMember(Name = "localFile", EmitDefaultValue = false)] public string LocalFile;
            [DataMember(Name = "localReason", EmitDefaultValue = false)] public string LocalReason;
            [DataMember(Name = "referencingAssembly", EmitDefaultValue = false)] public string ReferencingAssembly;
            [DataMember(Name = "requiredReference", EmitDefaultValue = false)] public string RequiredReference;
            [DataMember(Name = "availableReferences", EmitDefaultValue = false)] public string[] AvailableReferences;
        }
    }
}
