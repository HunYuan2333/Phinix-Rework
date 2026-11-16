using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Utils.Framework;

namespace Phinix.TradeExtension.Client
{
    internal static class TradeOperationQueryProtocol
    {
        internal const string Request="trade.operation.query.request",Response="trade.operation.query.response";
    }
    [DataContract]
    internal sealed class TradeOperationQueryRequest
    {
        [DataMember(Order=0)] public string OperationId;
        [DataMember(Order=1)] public string RequestFingerprint;
    }
    [DataContract]
    internal sealed class TradeOperationQueryResponse
    {
        [DataMember(Order=0)] public string OperationId;
        [DataMember(Order=1)] public string Status;
        [DataMember(Order=2)] public bool? Success;
        [DataMember(Order=3)] public FrameworkTradeFailureReason? Failure;
        [DataMember(Order=4)] public string TradeId;
        [DataMember(Order=5)] public long? SnapshotVersion;
        [DataMember(Order=6)] public bool Completed;
        [DataMember(Order=7)] public bool Cancelled;
    }

    internal sealed class TradeOperationQueryClient
    {
        private sealed class Pending
        {
            internal TradeIntentRecord Intent;
            internal string Session;
            internal long StartedAt;
        }
        private readonly TradeIntentJournal journal;
        private readonly Func<string> endpoint,principal,session;
        private readonly Func<bool> available;
        private readonly Func<FrameworkPacket,bool> send;
        private readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>(StringComparer.Ordinal);
        private string observedSession;
        private readonly object gate=new object();
        private readonly Func<long> clock;
        private const long TimeoutMilliseconds=30000;
        internal TradeOperationQueryClient(TradeIntentJournal journal,Func<string> endpoint,Func<string> principal,Func<string> session,Func<bool> available,Func<FrameworkPacket,bool> send)
            : this(journal,endpoint,principal,session,available,send,()=>(long)(System.Diagnostics.Stopwatch.GetTimestamp()*(1000.0/System.Diagnostics.Stopwatch.Frequency))) { }
        internal TradeOperationQueryClient(TradeIntentJournal journal,Func<string> endpoint,Func<string> principal,Func<string> session,Func<bool> available,Func<FrameworkPacket,bool> send,Func<long> clock)
        {this.journal=journal;this.endpoint=endpoint;this.principal=principal;this.session=session;this.available=available;this.send=send;this.clock=clock;}
        internal bool Available=>available();
        internal void Query(string operationId)
        {lock(gate) QueryCore(operationId);}
        private void Prune()
        {
            long now=clock();
            foreach(string id in pending.Where(p=>now-p.Value.StartedAt>=TimeoutMilliseconds).Select(p=>p.Key).ToArray())pending.Remove(id);
        }
        private void QueryCore(string operationId)
        {
            if(!Available)throw new InvalidOperationException("TradeOperationQueryUnavailable");
            string current=session();if(current!=observedSession){pending.Clear();observedSession=current;}
            Prune();
            var intent=journal.Read(endpoint(),principal()).SingleOrDefault(r=>r.OperationId==operationId)??throw new InvalidOperationException("TradeOperationIntentMissing");
            foreach(string stale in pending.Where(p=>p.Value.Intent.Checksum==intent.Checksum).Select(p=>p.Key).ToArray())pending.Remove(stale);
            if(pending.Count>=64)throw new InvalidOperationException("TradeOperationQueryLimit");
            string id=Guid.NewGuid().ToString("N");
            var packet=new FrameworkPacket {Flow=global::Phinix.Framework.FrameworkFlow.Command,CommandKind=global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType=TradeOperationQueryProtocol.Request,MessageId=id,SessionId=current,SenderUuid=principal(),
                PayloadJson=FrameworkSerialization.SerializePayload(new TradeOperationQueryRequest{OperationId=intent.OperationId,RequestFingerprint=intent.RequestFingerprint})};
            packet.SetCorrelationId(id);pending.Add(id,new Pending{Intent=intent,Session=current,StartedAt=clock()});
            try {if(!send(packet))throw new InvalidOperationException("TradeOperationQueryNotHandled");}
            catch (System.Exception boundaryError248) { global::Phinix.TradeExtension.Client.TradeDiagnostics.Report("TradeOperationQuery/QueryCore", boundaryError248); pending.Remove(id);throw;}
        }
        internal void Handle(FrameworkPacket packet)
        {lock(gate) {Prune();HandleCore(packet);}}
        private void HandleCore(FrameworkPacket packet)
        {
            if(packet?.MessageType!=TradeOperationQueryProtocol.Response||!Available||!pending.TryGetValue(packet.GetCorrelationId(),out var request))return;
            if(request.Session!=session()||packet.SessionId!=request.Session||packet.SenderUuid!=FrameworkProtocol.SystemSenderUuid||
                !string.Equals(request.Intent.Endpoint,endpoint(),StringComparison.OrdinalIgnoreCase)||!string.Equals(request.Intent.Principal,principal(),StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Operation query scope changed.");
            if(packet.PayloadJson==null||packet.PayloadJson.Length>4096)throw new InvalidOperationException("Operation query response exceeds budget.");
            var response=FrameworkSerialization.DeserializePayload<TradeOperationQueryResponse>(packet.PayloadJson);
            if(response==null||response.OperationId!=request.Intent.OperationId)throw new InvalidOperationException("Operation query result does not match request.");
            journal.RecordQuery(request.Intent,response);
            pending.Remove(packet.GetCorrelationId());
        }
    }
}
