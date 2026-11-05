using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Authentication;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using LiteNetLib;
using LiteNetLib.Utils;
using PhinixClient.Framework;
using UserManagement;
using Utils;
using Utils.Framework;

internal static class NetworkProbe
{
    public static void Run(int port)
    {
        RunCase(port, false);
        RunCase(port, true);
        Console.WriteLine("ADMISSION NETWORK CLIENT RESULT passed; cases=2; version-rejection/no-business-capability; no fallback/refund/replay");
    }
    private static void RunCase(int port, bool negotiate)
    {
        string session = null, principal = null, failure = null;
        string key = Guid.NewGuid().ToString("N");
        bool done = false;
        var policy = new FrameworkAdmissionControlState();
        var listener = new EventBasedNetListener();
        var client = new NetManager(listener, "Phinix");
        NetPeer peer = null;
        Action<string, byte[]> send = (module, bytes) => { var writer = new NetDataWriter(); writer.Put(module); writer.Put(bytes); peer.Send(writer, SendOptions.ReliableOrdered); };
        listener.NetworkReceiveEvent += (_, reader) =>
        {
            try
            {
                string module = reader.GetString(); byte[] bytes = reader.GetRemainingBytes();
                if (module == FrameworkProtocol.ModuleName)
                {
                    var packet = FrameworkSerialization.DeserializePacket(bytes);
                    if (packet.SessionId != session || packet.SenderUuid != FrameworkProtocol.SystemSenderUuid || packet.Version != 2) throw new Exception("Unmatched control envelope");
                    if (packet.Kind == FrameworkProtocol.KindCapabilities)
                    {
                        if (!negotiate || !policy.ObserveCapabilities(session, packet.GetCorrelationId())) throw new Exception("Uncorrelated capabilities");
                        var request = new FrameworkPacket { Kind = FrameworkProtocol.KindCommand, MessageId = Guid.NewGuid().ToString(), MessageType = "synthetic.unsupported", SessionId = session, SenderUuid = principal, PayloadJson = "{}" };
                        request.SetCorrelationId("synthetic-stable-operation"); policy.Track(request.MessageId, request.GetCorrelationId());
                        send(module, FrameworkSerialization.SerializePacket(request)); return;
                    }
                    if (packet.Kind != "control" || packet.MessageType != "framework.admission.rejected") throw new Exception("Unexpected framework packet");
                    var rejection = FrameworkSerialization.DeserializePayload<FrameworkAdmissionRejection>(packet.PayloadJson);
                    if (rejection.Code != (negotiate ? "unsupported_capability" : "unsupported_version") || !policy.ObserveRejection(session, packet.GetCorrelationId(), rejection) ||
                        policy.MayTimeoutFallback || policy.NegotiationRejected == negotiate) throw new Exception("Control refusal semantics mismatch");
                    done = true; return;
                }
                if (module == "UserManagement" && Encoding.UTF8.GetString(bytes).Contains(key)) throw new Exception("Credential leaked in public user packet");
                var envelope = Any.Parser.ParseFrom(bytes);
                if (envelope.Is(HelloPacket.Descriptor))
                {
                    session = envelope.Unpack<HelloPacket>().SessionId;
                    send("Authentication", ProtobufPacketHelper.Pack(new AuthenticatePacket { SessionId = session, AuthType = AuthTypes.ClientKey, Username = key }).ToByteArray());
                }
                else if (envelope.Is(AuthResponsePacket.Descriptor))
                {
                    if (!envelope.Unpack<AuthResponsePacket>().Success) throw new Exception("Authentication refused");
                    send("UserManagement", ProtobufPacketHelper.Pack(new LoginPacket { SessionId = session, DisplayName = "synthetic-control-probe", AcceptingTrades = true }).ToByteArray());
                }
                else if (envelope.Is(LoginResponsePacket.Descriptor))
                {
                    var login = envelope.Unpack<LoginResponsePacket>(); if (!login.Success) throw new Exception("Login refused"); principal = login.Uuid;
                    string id = Guid.NewGuid().ToString(); policy.Begin(session, id);
                    send(FrameworkProtocol.ModuleName, FrameworkSerialization.SerializePacket(new FrameworkPacket { MessageId = id, Kind = FrameworkProtocol.KindHello, Version = negotiate ? 2 : 999,
                        SessionId = session, SenderUuid = principal, PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkCapabilitiesPayload { Capabilities = new List<string>() }) }));
                }
            }
            catch { failure = "Independent client wire/policy assertion failed"; }
        };
        try
        {
            if (!client.Start()) throw new Exception("Client could not start");
            peer = client.Connect("127.0.0.1", port);
            var timer = Stopwatch.StartNew();
            while (!done && failure == null && timer.ElapsedMilliseconds < 10000) { client.PollEvents(); Thread.Sleep(5); }
            if (!done || failure != null) throw new Exception(failure ?? "Independent client network deadline");
        }
        finally { client.Stop(); }
    }
}
