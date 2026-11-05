using System;
using System.IO;
using PhinixClient.Framework;
using Utils.Framework;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static FrameworkAdmissionRejection Rejection(string stage = "Negotiation", string id = "hello", string correlation = "hello") => new FrameworkAdmissionRejection
    { Code = "unsupported_version", RequestMessageId = id, CorrelationId = correlation, Stage = stage, OperationOutcome = "Unknown", Retryable = false, RequestInvoked = false };
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--probe") { NetworkProbe.Run(int.Parse(args[1])); return 0; }
            if (args.Length == 4 && args[0] == "--trade-probe") { TradeNetworkProbe.Run(int.Parse(args[1]), args[2], args[3]); return 0; }
            if (args.Length == 6 && args[0] == "--intent-probe") { TradeNetworkProbe.RunIntents(int.Parse(args[1]), args[2], args[3], args[4], args[5]); return 0; }
            if (args.Length == 6 && args[0] == "--delivery-probe") { TradeNetworkProbe.RunDelivery(int.Parse(args[1]), args[2], args[3], args[4], args[5]); return 0; }
            var state = new FrameworkAdmissionControlState();
            Check(state.Begin("synthetic-session", "hello") && state.MayTimeoutFallback, "Fresh negotiation state");
            Check(!state.ObserveRejection("wrong-session", "hello", Rejection()) && state.MayTimeoutFallback, "Cross-session rejection accepted");
            Check(state.ObserveRejection("synthetic-session", "hello", Rejection()), "Correlated explicit rejection ignored");
            Check(state.NegotiationRejected && !state.MayTimeoutFallback, "Explicit rejection allowed protocol downgrade");
            Check(!state.Begin("synthetic-session", "again"), "Repeated begin erased rejection");
            Check(!state.ObserveCapabilities("synthetic-session", "hello"), "Late capabilities revived rejected negotiation");
            state.ResetConnection();
            Check(state.Begin("next-session", "next-hello") && state.ObserveCapabilities("next-session", "next-hello"), "Fresh reconnect not usable");
            Check(!state.MayTimeoutFallback, "Observed modern server allowed timeout downgrade");
            state.Track("operation-packet", "operation-stable");
            var refused = Rejection("Admission", "operation-packet", "operation-stable");
            Check(state.ObserveRejection("next-session", "operation-stable", refused) && !state.NegotiationRejected, "Admission failure destroyed independent requests");
            refused.OperationOutcome = "Succeeded";
            Check(!state.ObserveRejection("next-session", "operation-stable", refused), "Control forged business success");
            refused.OperationOutcome = "Unknown"; refused.Retryable = true;
            Check(!state.ObserveRejection("next-session", "operation-stable", refused), "Control authorized automatic retry");
            refused.Retryable = false; refused.RequestInvoked = true;
            Check(!state.ObserveRejection("next-session", "operation-stable", refused), "Control misrepresented side effects");
            refused.RequestInvoked = false;
            Check(!state.ObserveRejection("next-session", "unmatched", refused), "Unmatched correlation accepted");
            state.ResetConnection(); state.Begin("third-session", "third-hello");
            Check(!state.ObserveCapabilities("third-session", "stale-hello") && state.NegotiationRejected && !state.MayTimeoutFallback,
                "Uncorrelated modern handshake downgraded");
            state.ResetConnection(); state.Begin("fourth-session", "fourth-hello"); state.RejectMalformed("fourth-session");
            Check(state.NegotiationRejected && !state.MayTimeoutFallback, "Malformed control downgraded");
            if (args.Length == 2 && args[0] == "--server-fixture")
            {
                // Actual client-pinned serializer and production policy; no server assembly loaded.
                var packet = FrameworkSerialization.DeserializePacket(File.ReadAllBytes(args[1]));
                Check(packet.Kind == "control" && packet.MessageType == "framework.admission.rejected" && packet.Version == 2, "Wrong wire envelope");
                var payload = FrameworkSerialization.DeserializePayload<FrameworkAdmissionRejection>(packet.PayloadJson);
                state.ResetConnection(); state.Begin("fixture-session", "fixture-hello");
                Check(state.ObserveRejection(packet.SessionId, packet.GetCorrelationId(), payload) && state.NegotiationRejected && !state.MayTimeoutFallback,
                    "Independent client rejected server encoding");
            }
            Console.WriteLine("ADMISSION CLIENT RESULT passed; checks=" + checks + "; no business ACK/refund/replay");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
