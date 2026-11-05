using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PhinixClient.Framework
{
    // Versioned JSON control contract over the existing FrameworkPacket envelope.
    // It is never forwarded as a business result, ACK, refund, or retry instruction.
    [DataContract]
    public sealed class FrameworkAdmissionRejection
    {
        public const string PacketKind = "control";
        public const string PacketType = "framework.admission.rejected";
        [DataMember(Order = 0)] public string Code { get; set; }
        [DataMember(Order = 1)] public string RequestMessageId { get; set; }
        [DataMember(Order = 2)] public string CorrelationId { get; set; }
        [DataMember(Order = 3)] public string Stage { get; set; }
        [DataMember(Order = 4)] public string OperationOutcome { get; set; }
        [DataMember(Order = 5)] public bool Retryable { get; set; }
        [DataMember(Order = 6)] public bool RequestInvoked { get; set; }
    }

    public sealed class FrameworkAdmissionRejectedEventArgs : EventArgs
    {
        public string Code { get; }
        public string RequestMessageId { get; }
        public string CorrelationId { get; }
        public bool NegotiationRejected { get; }
        public FrameworkAdmissionRejectedEventArgs(FrameworkAdmissionRejection rejection)
        { Code = rejection.Code; RequestMessageId = rejection.RequestMessageId; CorrelationId = rejection.CorrelationId; NegotiationRejected = rejection.Stage == "Negotiation"; }
    }

    // Game-independent policy, shared by the production endpoint and focused tests.
    public sealed class FrameworkAdmissionControlState
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, string> requests = new Dictionary<string, string>();
        private readonly Queue<string> order = new Queue<string>();
        private string session, hello;
        private bool modernSeen, rejected;
        public bool NegotiationRejected { get { lock (gate) return rejected; } }
        public bool MayTimeoutFallback { get { lock (gate) return !modernSeen && !rejected; } }
        public bool Begin(string sessionId, string helloId)
        {
            lock (gate)
            {
                if (rejected || string.IsNullOrEmpty(sessionId) || !Bound(helloId)) return false;
                if (session != null && session != sessionId) { rejected = true; return false; }
                session = sessionId; hello = helloId; Track(helloId, helloId); return true;
            }
        }
        // Called only by a successful authenticated session replacement on the same connection.
        public bool ReplaceSession(string previous, string current)
        {
            lock (gate)
            {
                if (session == null) return true;
                if (rejected || session != previous || string.IsNullOrEmpty(current)) { rejected = true; return false; }
                session = current; return true;
            }
        }
        public void Track(string request, string correlation)
        {
            lock (gate)
            {
                if (!Bound(request) || !Bound(correlation) || requests.ContainsKey(request)) return;
                requests.Add(request, correlation); order.Enqueue(request);
                if (order.Count > 256) requests.Remove(order.Dequeue());
            }
        }
        public bool ObserveCapabilities(string sessionId, string correlation)
        {
            lock (gate)
            {
                if (session == null || sessionId != session) return false;
                modernSeen = true;
                if (rejected || correlation != hello) { rejected = true; return false; }
                return true;
            }
        }
        public bool ObserveRejection(string sessionId, string correlation, FrameworkAdmissionRejection message)
        {
            lock (gate)
            {
                if (session == null || sessionId != session) return false;
                modernSeen = true;
                if (message == null || !Bound(message.RequestMessageId) || !Bound(message.CorrelationId) || message.CorrelationId != correlation ||
                    message.OperationOutcome != "Unknown" || message.Retryable || message.RequestInvoked || !KnownCode(message.Code)) return false;
                if (message.Stage == "Negotiation")
                {
                    if (message.RequestMessageId != hello || message.CorrelationId != hello) return false;
                    rejected = true; return true;
                }
                return message.Stage == "Admission" && requests.TryGetValue(message.RequestMessageId, out string expected) && expected == correlation;
            }
        }
        public void RejectMalformed(string sessionId)
        { lock (gate) { if (session == sessionId && session != null) { modernSeen = true; rejected = true; } } }
        public void ResetConnection()
        { lock (gate) { session = hello = null; modernSeen = rejected = false; requests.Clear(); order.Clear(); } }
        private static bool Bound(string value) => !string.IsNullOrEmpty(value) && value.Length <= 128;
        private static bool KnownCode(string code) => code == "unsupported_version" || code == "unsupported_kind" || code == "not_authenticated" ||
            code == "invalid_hello" || code == "hello_conflict" || code == "not_ready" || code == "unsupported_capability";
    }
}
