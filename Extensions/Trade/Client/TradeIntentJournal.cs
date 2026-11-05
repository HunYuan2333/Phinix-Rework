using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    [DataContract]
    internal sealed class TradeIntentItem
    {
        [DataMember(Order=0)] public string MessageId;
        [DataMember(Order=1)] public string PayloadJson;
    }
    [DataContract]
    internal sealed class TradeIntentRecord
    {
        [DataMember(Order=0)] public int Schema=1;
        [DataMember(Order=1)] public string Endpoint;
        [DataMember(Order=2)] public string Principal;
        [DataMember(Order=3)] public string OperationId;
        [DataMember(Order=4)] public string RequestType;
        [DataMember(Order=5)] public string Kind;
        [DataMember(Order=6)] public string PayloadJson;
        [DataMember(Order=7)] public string CorrelationId;
        [DataMember(Order=8)] public long? ObservedVersion;
        [DataMember(Order=9)] public TradeIntentItem[] Items;
        [DataMember(Order=10)] public string RequestFingerprint;
        [DataMember(Order=11)] public string Checksum;
    }
    [DataContract]
    internal sealed class TradeIntentResult
    {
        [DataMember(Order=0)] public string IntentChecksum;
        [DataMember(Order=1)] public string ResponseType;
        [DataMember(Order=2)] public string ResponseJson;
        [DataMember(Order=3)] public bool Success;
        [DataMember(Order=4)] public string Checksum;
    }
    [DataContract]
    internal sealed class TradeIntentQueryEvidence
    {
        [DataMember(Order=0)] public string IntentChecksum;
        [DataMember(Order=1)] public TradeOperationQueryResponse Result;
        [DataMember(Order=2)] public string Checksum;
    }

    // Private, append-only intent and attempt evidence. No transport, retry, refund,
    // session credential, game mutation or interpretation of a missing server result.
    internal sealed partial class TradeIntentJournal
    {
        private const int MaxRecord=2*1024*1024, MaxTotal=64*1024*1024, MaxRecords=1024;
        private readonly string directory;
        private readonly object gate=new object();
        private long revision;
        internal long Revision=>Interlocked.Read(ref revision);
        private readonly Dictionary<string,string> observations=new Dictionary<string,string>(StringComparer.Ordinal);
        [DllImport("libc",SetLastError=true)] private static extern int chmod(string path,uint mode);
        internal TradeIntentJournal(string directory)
        {
            this.directory=Path.GetFullPath(directory);CheckEvidencePath(this.directory);Directory.CreateDirectory(this.directory);Private(this.directory,448);
            lock(gate)
            {
                ReadAll();ValidateReservationEvidence();
                foreach(string path in Directory.GetFiles(this.directory,"*.attempt"))
                {
                    string intent=Path.ChangeExtension(path,".intent");
                    if(!File.Exists(intent))throw new InvalidDataException("Orphan intent attempt.");
                    ReadAttempt(ReadFile(intent));
                }
                foreach(string path in Directory.GetFiles(this.directory,"*.result"))
                {
                    string intent=Path.ChangeExtension(path,".intent");
                    if(!File.Exists(intent))throw new InvalidDataException("Orphan intent result.");
                    ReadResult(ReadFile(intent));
                }
                foreach(string path in Directory.GetFiles(this.directory,"*.query"))
                {
                    string intent=Path.ChangeExtension(path,".intent");
                    if(!File.Exists(intent))throw new InvalidDataException("Orphan query evidence.");
                    ReadQuery(ReadFile(intent));
                }
            }
        }
        private static void CheckEvidencePath(string path)
        {
            // Inspect every lexical ancestor; never resolve a link into a trusted path.
            string current=Path.GetFullPath(path);int depth=0;
            while(current!=null)
            {
                try
                {
                    if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                        throw new IOException("Trade evidence path contains an unsupported filesystem link.");
                }
                catch(FileNotFoundException) { }
                catch(DirectoryNotFoundException) { }
                current=Directory.GetParent(current)?.FullName;
                if(++depth>256)throw new IOException("Trade evidence path cannot be verified.");
            }
        }
        private static void Private(string path,uint mode)
        {
            CheckEvidencePath(path);
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Intent journal links are unsupported.");
            if(Path.DirectorySeparatorChar=='/'&&chmod(path,mode)!=0)throw new IOException("Cannot protect intent journal.");
        }
        private static void Id(string value,int max=128)
        {if(string.IsNullOrWhiteSpace(value)||value.Length>max||value.Any(char.IsControl))throw new InvalidDataException("Invalid intent identity.");}
        private static string Hash(string value)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
        private static string Key(TradeIntentRecord r)=>Hash(r.Endpoint.ToLowerInvariant()+"\n"+r.Principal.ToUpperInvariant()+"\n"+r.OperationId);
        private string PathFor(TradeIntentRecord r)=>Path.Combine(directory,Key(r)+".intent");
        private static string Json(TradeIntentRecord r)=>FrameworkSerialization.SerializePayload(r);
        private static string Checksum(TradeIntentRecord r)
        {
            var copy=new TradeIntentRecord {Endpoint=r.Endpoint,Principal=r.Principal,OperationId=r.OperationId,RequestType=r.RequestType,Kind=r.Kind,PayloadJson=r.PayloadJson,
                CorrelationId=r.CorrelationId,ObservedVersion=r.ObservedVersion,Items=r.Items,RequestFingerprint=r.RequestFingerprint};
            return Hash(Json(copy));
        }
        private static string Canonical(string type,string json,long? observed,out string kind)
        {
            switch(type)
            {
                case FrameworkTradeProtocol.CreateRequestType:
                    kind="create";return FrameworkSerialization.SerializePayload(FrameworkSerialization.DeserializePayload<FrameworkTradeCreateRequest>(json)??throw new InvalidDataException("Missing create intent."));
                case FrameworkTradeProtocol.OfferUpdateRequestType:
                    kind="offer";return FrameworkSerialization.SerializePayload(FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(json)??throw new InvalidDataException("Missing offer intent."));
                case FrameworkTradeProtocol.StatusUpdateRequestType:
                    kind=observed.HasValue?"status@"+observed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"status";
                    return FrameworkSerialization.SerializePayload(FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateRequest>(json)??throw new InvalidDataException("Missing status intent."));
                default:throw new InvalidDataException("Unsupported intent kind.");
            }
        }
        private static void Validate(TradeIntentRecord r)
        {
            if(r==null||r.Schema!=1)throw new InvalidDataException("Unsupported intent schema.");
            Id(r.Endpoint,512);Id(r.Principal);Id(r.OperationId);
            if(r.CorrelationId!=null)Id(r.CorrelationId);
            string kind;string canonical=Canonical(r.RequestType,r.PayloadJson,r.ObservedVersion,out kind);
            if(canonical!=r.PayloadJson||kind!=r.Kind||Hash(kind+"\n"+canonical)!=r.RequestFingerprint||r.Items==null)
                throw new InvalidDataException("Intent fingerprint differs.");
            if(r.RequestType==FrameworkTradeProtocol.CreateRequestType)
            {
                string other=FrameworkSerialization.DeserializePayload<FrameworkTradeCreateRequest>(r.PayloadJson).OtherPartyUuid;Id(other);
                if(string.Equals(other,r.Principal,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Trade participants must differ.");
            }
            else Id(TradeId(r));
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in r.Items)
            {
                Id(item?.MessageId);
                var payload=FrameworkSerialization.DeserializePayload<FrameworkItemPayload>(item.PayloadJson);
                if(payload==null||string.IsNullOrWhiteSpace(payload.CodecId)||payload.Metadata?.Any(m=>m==null)==true||!ids.Add(item.MessageId))throw new InvalidDataException("Invalid intent item batch.");
            }
            if(r.RequestType==FrameworkTradeProtocol.OfferUpdateRequestType)
            {
                var offer=FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(r.PayloadJson);
                if(!(offer.ItemPacketRefs??new List<string>()).SequenceEqual(r.Items.Select(i=>i.MessageId)))throw new InvalidDataException("Intent does not retain the complete reference batch.");
            }
            else if(r.Items.Length!=0)throw new InvalidDataException("Unexpected intent item batch.");
            if(Checksum(r)!=r.Checksum)throw new InvalidDataException("Intent checksum differs.");
        }
        private TradeIntentRecord ReadFile(string path)
        {
            if(new FileInfo(path).Length>MaxRecord)throw new InvalidDataException("Intent record exceeds budget.");
            Private(path,384);var record=FrameworkSerialization.DeserializePayload<TradeIntentRecord>(File.ReadAllText(path,Encoding.UTF8));Validate(record);
            if(path!=PathFor(record))throw new InvalidDataException("Intent file identity differs.");return record;
        }
        private TradeIntentRecord[] ReadAll()
        {
            CheckEvidencePath(directory);
            var files=Directory.GetFiles(directory,"*.intent");
            if(files.Length>MaxRecords||EvidenceBytes()>MaxTotal)throw new InvalidDataException("Intent journal exceeds budget.");
            return files.OrderBy(f=>f,StringComparer.Ordinal).Select(ReadFile).ToArray();
        }
        private long EvidenceBytes()
        {
            CheckEvidencePath(directory);
            long total=0;
            // Include bindings, unknown files and interrupted staging files. None is
            // discarded or promoted simply to admit another operation.
            foreach(string path in Directory.GetFiles(directory))
            {CheckEvidencePath(path);total=checked(total+new FileInfo(path).Length);}
            return total;
        }
        private long MissingEvidenceBytes(TradeIntentRecord record)
        {
            string path=PathFor(record);long reserved=0;
            if(!File.Exists(Path.ChangeExtension(path,".attempt")))reserved+=64;
            if(!File.Exists(Path.ChangeExtension(path,".result")))reserved+=MaxRecord;
            if(!File.Exists(Path.ChangeExtension(path,".query")))reserved+=8192;
            if(record.RequestType==FrameworkTradeProtocol.OfferUpdateRequestType&&!File.Exists(Path.ChangeExtension(path,".reservation")))reserved+=MaxRecord;
            return reserved;
        }
        private void RequireAdmissionCapacity(TradeIntentRecord[] previous,TradeIntentRecord record,int bytes)
        {
            // Admission only: an existing journal may predate headroom reservation.
            // Always allow its original results to consume any actual free capacity.
            long required=checked(EvidenceBytes()+bytes+MissingEvidenceBytes(record));
            foreach(var existing in previous)required=checked(required+MissingEvidenceBytes(existing));
            if(required>MaxTotal)throw new IOException("Intent journal has insufficient result evidence headroom.");
        }
        internal TradeIntentRecord[] Read(string endpoint,string principal)
        {
            Id(endpoint,512);Id(principal);
            lock(gate)return ReadAll().Where(r=>string.Equals(r.Endpoint,endpoint,StringComparison.OrdinalIgnoreCase)&&string.Equals(r.Principal,principal,StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        private void Publish(string path,byte[] bytes)
        {
            CheckEvidencePath(path);
            if(EvidenceBytes()+bytes.Length>MaxTotal)throw new IOException("Intent evidence storage is full.");
            string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
            try
            {
                CheckEvidencePath(temporary);
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {Private(temporary,384);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                CheckEvidencePath(temporary);CheckEvidencePath(path);File.Move(temporary,path);
                Interlocked.Increment(ref revision);Changed?.Invoke();
            }
            finally{CheckEvidencePath(temporary);if(File.Exists(temporary))File.Delete(temporary);}
        }
        internal TradeIntentRecord Prepare(string endpoint,string principal,FrameworkPacket packet,IEnumerable<FrameworkPacket> items)
        {
            if(packet==null)throw new ArgumentNullException(nameof(packet));
            lock(gate)
            {
                long version;long? observed=packet.TryGetSnapshotVersion(out version)?(long?)version:null;string kind;
                string canonical=Canonical(packet.MessageType,packet.PayloadJson,observed,out kind);
                var record=new TradeIntentRecord {Endpoint=endpoint,Principal=principal,OperationId=packet.MessageId,RequestType=packet.MessageType,PayloadJson=canonical,Kind=kind,
                    CorrelationId=packet.GetCorrelationId(),ObservedVersion=observed,Items=(items??Array.Empty<FrameworkPacket>()).Select(i=>new TradeIntentItem {MessageId=i.MessageId,PayloadJson=i.PayloadJson}).ToArray(),RequestFingerprint=Hash(kind+"\n"+canonical)};
                record.Checksum=Checksum(record);Validate(record);byte[] bytes=Encoding.UTF8.GetBytes(Json(record));
                if(bytes.Length>MaxRecord)throw new IOException("Intent record exceeds budget.");
                string path=PathFor(record);
                if(File.Exists(path))
                {var existing=ReadFile(path);if(existing.Checksum!=record.Checksum)throw new InvalidDataException("Operation ID has conflicting durable intent.");return existing;}
                var previous=ReadAll();if(previous.Length>=MaxRecords)throw new IOException("Intent journal is full.");
                if(previous.Any(r=>string.Equals(r.Endpoint,endpoint,StringComparison.OrdinalIgnoreCase)&&string.Equals(r.Principal,principal,StringComparison.OrdinalIgnoreCase)&&r.CorrelationId==record.CorrelationId))
                    throw new InvalidDataException("Correlation already belongs to a durable operation; query its original result.");
                RequireAdmissionCapacity(previous,record,bytes.Length);
                Publish(path,bytes);return ReadFile(path);
            }
        }
        private TradeIntentRecord Resolve(TradeIntentRecord record)
        {var persisted=ReadFile(PathFor(record));if(persisted.Checksum!=record.Checksum)throw new InvalidDataException("Intent differs from committed record.");return persisted;}
        private void ReadAttempt(TradeIntentRecord record)
        {
            string path=Path.ChangeExtension(PathFor(record),".attempt");
            if(new FileInfo(path).Length!=64)throw new InvalidDataException("Invalid intent attempt.");
            Private(path,384);if(File.ReadAllText(path)!=Hash("intent-attempt-v1\n"+record.Checksum))throw new InvalidDataException("Intent attempt differs.");
        }
        internal void BeginAttempt(TradeIntentRecord record)
        {
            lock(gate)
            {
                record=Resolve(record);string path=Path.ChangeExtension(PathFor(record),".attempt");
                if(File.Exists(path)){ReadAttempt(record);throw new InvalidOperationException("Intent already attempted; query its original result.");}
                Publish(path,Encoding.ASCII.GetBytes(Hash("intent-attempt-v1\n"+record.Checksum)));
            }
        }
        internal string State(TradeIntentRecord record)
        {
            lock(gate)
            {
                record=Resolve(record);
                if(File.Exists(Path.ChangeExtension(PathFor(record),".query")))return ReadQuery(record).Success==true?"CommittedSuccess":"CommittedFailure";
                if(File.Exists(Path.ChangeExtension(PathFor(record),".result")))return ReadResult(record).Success?"ResponseSuccess":"ResponseFailure";
                if(observations.TryGetValue(record.Checksum,out string observed))return observed=="NotFound"?"NotFoundObserved":observed;
                if(!File.Exists(Path.ChangeExtension(PathFor(record),".attempt")))return "Prepared";ReadAttempt(record);return "OutcomeUnknown";
            }
        }

        private static TradeIntentResult Result(TradeIntentRecord record,FrameworkPacket packet)
        {
            string canonical,trade=null;bool success;
            switch(packet.MessageType)
            {
                case FrameworkTradeProtocol.CreateResponseType:
                    if(record.RequestType!=FrameworkTradeProtocol.CreateRequestType)throw new InvalidDataException("Response kind differs.");
                    var create=FrameworkSerialization.DeserializePayload<FrameworkTradeCreateResponse>(packet.PayloadJson)??throw new InvalidDataException("Missing create result.");
                    var request=FrameworkSerialization.DeserializePayload<FrameworkTradeCreateRequest>(record.PayloadJson);
                    if(!string.Equals(create.OtherPartyUuid,request.OtherPartyUuid,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Create result principal differs.");
                    if(create.Success)Id(create.TradeId);
                    success=create.Success;canonical=FrameworkSerialization.SerializePayload(create);break;
                case FrameworkTradeProtocol.OfferUpdateResponseType:
                    if(record.RequestType!=FrameworkTradeProtocol.OfferUpdateRequestType)throw new InvalidDataException("Response kind differs.");
                    var offer=FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateResponse>(packet.PayloadJson)??throw new InvalidDataException("Missing offer result.");
                    trade=offer.TradeId;success=offer.Success;canonical=FrameworkSerialization.SerializePayload(offer);break;
                case FrameworkTradeProtocol.StatusUpdateResponseType:
                    if(record.RequestType!=FrameworkTradeProtocol.StatusUpdateRequestType)throw new InvalidDataException("Response kind differs.");
                    var status=FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateResponse>(packet.PayloadJson)??throw new InvalidDataException("Missing status result.");
                    trade=status.TradeId;success=status.Success;canonical=FrameworkSerialization.SerializePayload(status);break;
                default:throw new InvalidDataException("Unsupported result kind.");
            }
            if(trade!=null)
            {
                string expected=record.RequestType==FrameworkTradeProtocol.OfferUpdateRequestType
                    ?FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(record.PayloadJson).TradeId
                    :FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateRequest>(record.PayloadJson).TradeId;
                if(!string.Equals(trade,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Result trade differs.");
            }
            else if(success && record.RequestType!=FrameworkTradeProtocol.CreateRequestType)throw new InvalidDataException("Successful result has no trade.");
            var result=new TradeIntentResult{IntentChecksum=record.Checksum,ResponseType=packet.MessageType,ResponseJson=canonical,Success=success};
            result.Checksum=Hash(result.IntentChecksum+"\n"+result.ResponseType+"\n"+canonical);return result;
        }
        private TradeIntentResult ReadResult(TradeIntentRecord record)
        {
            ReadAttempt(record);string path=Path.ChangeExtension(PathFor(record),".result");
            if(new FileInfo(path).Length>MaxRecord)throw new InvalidDataException("Intent result exceeds budget.");Private(path,384);
            var stored=FrameworkSerialization.DeserializePayload<TradeIntentResult>(File.ReadAllText(path,Encoding.UTF8))??throw new InvalidDataException("Missing result evidence.");
            var expected=Result(record,new FrameworkPacket{MessageType=stored.ResponseType,PayloadJson=stored.ResponseJson});
            if(expected.IntentChecksum!=stored.IntentChecksum||expected.ResponseJson!=stored.ResponseJson||expected.Success!=stored.Success||expected.Checksum!=stored.Checksum)throw new InvalidDataException("Intent result differs.");
            return stored;
        }
        // False suppresses an exact duplicate before existing business callbacks.
        // A missing historical intent grants no new commit/refund authority.
        internal bool RecordResponse(string endpoint,string principal,FrameworkPacket packet)
        {
            if(packet.MessageType!=FrameworkTradeProtocol.CreateResponseType&&packet.MessageType!=FrameworkTradeProtocol.OfferUpdateResponseType&&packet.MessageType!=FrameworkTradeProtocol.StatusUpdateResponseType)return true;
            lock(gate)
            {
                var matches=Read(endpoint,principal).Where(r=>r.CorrelationId==packet.GetCorrelationId()).ToArray();
                if(matches.Length==0)return true;
                if(matches.Length!=1)throw new InvalidDataException("Ambiguous operation response.");
                var record=matches[0];ReadAttempt(record);var result=Result(record,packet);string path=Path.ChangeExtension(PathFor(record),".result");
                if(File.Exists(Path.ChangeExtension(PathFor(record),".query")))CheckSameResult(result,ReadQuery(record));
                if(File.Exists(path))
                {if(ReadResult(record).Checksum!=result.Checksum)throw new InvalidDataException("Committed operation result changed.");return false;}
                byte[] bytes=Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(result));
                if(bytes.Length>MaxRecord)throw new IOException("Intent result budget exceeded.");
                Publish(path,bytes);return true;
            }
        }

        internal static string TradeId(TradeIntentRecord record)
        {
            if(record.RequestType==FrameworkTradeProtocol.OfferUpdateRequestType)return FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(record.PayloadJson).TradeId;
            if(record.RequestType==FrameworkTradeProtocol.StatusUpdateRequestType)return FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateRequest>(record.PayloadJson).TradeId;
            return null;
        }
        private static void ValidateQuery(TradeIntentRecord record,TradeOperationQueryResponse response)
        {
            if(response==null||response.OperationId!=record.OperationId||
                (response.Status!="Committed"&&response.Status!="NotFound"&&response.Status!="Conflict"&&response.Status!="InvalidRequest"))throw new InvalidDataException("Invalid operation query result.");
            if(response.Status!="Committed")
            {
                if(response.Success.HasValue||response.Failure.HasValue||response.TradeId!=null||response.SnapshotVersion.HasValue||response.Completed||response.Cancelled)throw new InvalidDataException("Unexpected query result fields.");
                return;
            }
            if(!response.Success.HasValue||!response.Failure.HasValue||!Enum.IsDefined(typeof(FrameworkTradeFailureReason),response.Failure.Value)||
                (response.Completed&&response.Cancelled)||(!response.Success.Value&&(response.Completed||response.Cancelled)))throw new InvalidDataException("Invalid committed summary.");
            if(response.Success.Value){Id(response.TradeId);if(!response.SnapshotVersion.HasValue||response.SnapshotVersion.Value<1)throw new InvalidDataException("Missing committed trade revision.");}
            string expected=TradeId(record);
            if(response.TradeId!=null&&expected!=null&&!string.Equals(response.TradeId,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Queried trade differs.");
        }
        private static void CheckSameResult(TradeIntentResult original,TradeOperationQueryResponse query)
        {
            string trade;FrameworkTradeFailureReason failure;
            if(original.ResponseType==FrameworkTradeProtocol.CreateResponseType){var r=FrameworkSerialization.DeserializePayload<FrameworkTradeCreateResponse>(original.ResponseJson);trade=r.TradeId;failure=r.FailureReason;}
            else if(original.ResponseType==FrameworkTradeProtocol.OfferUpdateResponseType){var r=FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateResponse>(original.ResponseJson);trade=r.TradeId;failure=r.FailureReason;}
            else {var r=FrameworkSerialization.DeserializePayload<FrameworkTradeStatusUpdateResponse>(original.ResponseJson);trade=r.TradeId;failure=r.FailureReason;}
            if(original.Success!=query.Success||failure!=query.Failure||(trade!=null&&query.TradeId!=null&&!string.Equals(trade,query.TradeId,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Server result conflicts with original committed response.");
        }
        private TradeOperationQueryResponse ReadQuery(TradeIntentRecord record)
        {
            string path=Path.ChangeExtension(PathFor(record),".query");if(new FileInfo(path).Length>8192)throw new InvalidDataException("Query evidence exceeds budget.");Private(path,384);
            var evidence=FrameworkSerialization.DeserializePayload<TradeIntentQueryEvidence>(File.ReadAllText(path,Encoding.UTF8))??throw new InvalidDataException("Missing query evidence.");
            ValidateQuery(record,evidence.Result);
            if(evidence.Result.Status!="Committed"||evidence.IntentChecksum!=record.Checksum||evidence.Checksum!=Hash(record.Checksum+"\n"+FrameworkSerialization.SerializePayload(evidence.Result)))throw new InvalidDataException("Query evidence differs.");
            if(File.Exists(Path.ChangeExtension(PathFor(record),".result")))CheckSameResult(ReadResult(record),evidence.Result);
            return evidence.Result;
        }
        internal string Observation(TradeIntentRecord record)
        {lock(gate)return observations.TryGetValue(record.Checksum,out string value)?value:null;}
        internal void RecordQuery(TradeIntentRecord record,TradeOperationQueryResponse response)
        {
            lock(gate)
            {
                record=Resolve(record);ValidateQuery(record,response);
                if(response.Status=="Committed")
                {
                    if(File.Exists(Path.ChangeExtension(PathFor(record),".result")))CheckSameResult(ReadResult(record),response);
                    string path=Path.ChangeExtension(PathFor(record),".query");
                    if(File.Exists(path))
                    {if(FrameworkSerialization.SerializePayload(ReadQuery(record))!=FrameworkSerialization.SerializePayload(response))throw new InvalidDataException("Original queried result changed.");}
                    else
                    {
                        var evidence=new TradeIntentQueryEvidence{IntentChecksum=record.Checksum,Result=response,Checksum=Hash(record.Checksum+"\n"+FrameworkSerialization.SerializePayload(response))};
                        byte[] bytes=Encoding.UTF8.GetBytes(FrameworkSerialization.SerializePayload(evidence));
                        if(bytes.Length>8192)throw new IOException("Query evidence budget exceeded.");
                        Publish(path,bytes);
                    }
                }
                observations[record.Checksum]=response.Status;
                Interlocked.Increment(ref revision);Changed?.Invoke();
            }
        }
    }
}
