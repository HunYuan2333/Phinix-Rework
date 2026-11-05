using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Authentication;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using LiteNetLib;
using LiteNetLib.Utils;
using Phinix.TradeExtension;
using UserManagement;
using Utils;
using Utils.Framework;

internal static partial class TradeNetworkProbe
{
    private sealed class State
    {
        public string[] Keys { get; set; }
        public string[] Principals { get; set; }
        public string Trade { get; set; }
        public string AdapterReference { get; set; }
        public string AdapterOperation { get; set; }
        public string[] IntentIds { get; set; }
    }
    public static void Run(int port, string phase, string path)
    {
        if (phase == "login-failure")
        {
            using var failed = new Peer(port, Guid.NewGuid().ToString("N"), true);
            failed.Until(() => failed.LoginFailed);
            Check(failed.Principal == null && !failed.Ready, "Identity persistence failure published login");
            Console.WriteLine("TRADE WIRE RESULT passed; phase=login-failure; no stable identity was published"); return;
        }
        bool initial = phase == "initial";
        var state = initial ? new State { Keys = new[] { Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N") } } : JsonSerializer.Deserialize<State>(File.ReadAllText(path));
        using var a = new Peer(port, state.Keys[0]); using var b = new Peer(port, state.Keys[1]);
        Wait(a, b, () => a.Ready && b.Ready);
        if (initial)
        {
            state.Principals = new[] { a.Principal, b.Principal };
            a.Command(FrameworkTradeProtocol.CreateRequestType, new FrameworkTradeCreateRequest { OtherPartyUuid = b.Principal }, "wire-create");
            Wait(a, b, () => a.Find(FrameworkTradeProtocol.CreateResponseType, "wire-create") != null);
            var created = FrameworkSerialization.DeserializePayload<FrameworkTradeCreateResponse>(a.Find(FrameworkTradeProtocol.CreateResponseType, "wire-create").PayloadJson);
            Check(created.Success, "Wire create refused"); state.Trade = created.TradeId;
            string fixture = Environment.GetEnvironmentVariable("PHINIX_TEST_ADAPTER_WIRE");
            if (!string.IsNullOrEmpty(fixture))
            {
                // Actual packaged adapter output, without filling in a missing capability.
                // Only authenticated envelope and dynamically created trade are rebound by the test.
                var item = FrameworkSerialization.DeserializePacket(File.ReadAllBytes(Path.Combine(fixture, "item.bin")));
                var offer = FrameworkSerialization.DeserializePacket(File.ReadAllBytes(Path.Combine(fixture, "offer.bin")));
                var body = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateRequest>(offer.PayloadJson);
                Check(body.ItemPacketRefs.Single() == item.MessageId, "Adapter fixture references a different packet");
                state.AdapterReference = item.MessageId;
                state.AdapterOperation = offer.MessageId;
                body.TradeId = state.Trade; offer.PayloadJson = FrameworkSerialization.SerializePayload(body);
                // The production command transport sets Kind from Flow before sending.
                offer.Kind = FrameworkProtocol.KindCommand;
                a.Send(item); a.Send(offer);
            }
            else
                a.Send(new FrameworkPacket { Kind = FrameworkProtocol.KindItem, MessageType = FrameworkTradeProtocol.Capability, MessageId = "wire-item", PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkItemPayload { CodecId = "synthetic.opaque", PayloadBytes = new byte[] { 17, 0, 255 } }) });
        }
        else
        {
            Check(a.Principal == state.Principals[0] && b.Principal == state.Principals[1], "Identity mapping changed after server kill");
            Wait(a, b, () => a.Packets.Any(p => p.MessageType == FrameworkTradeProtocol.SnapshotType));
            var snapshot = FrameworkSerialization.DeserializePayload<FrameworkTradeStateCollectionSnapshot>(a.Packets.Last(p => p.MessageType == FrameworkTradeProtocol.SnapshotType).PayloadJson);
            var trade = snapshot.Trades.Single(t => t.TradeId == state.Trade);
            Check(trade.Participants.Single(p => p.Uuid == a.Principal).ItemsOnOffer.Single().PayloadBytes.SequenceEqual(new byte[] { 17, 0, 255 }), "Ready snapshot lost exact offer");
        }
        if (!initial || state.AdapterReference == null)
        {
            var replay = new FrameworkPacket { Kind = FrameworkProtocol.KindCommand, CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request,
                MessageType = FrameworkTradeProtocol.OfferUpdateRequestType, MessageId = state.AdapterOperation ?? "wire-offer",
                PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateRequest { TradeId = state.Trade, ItemPacketRefs = new List<string> { state.AdapterReference ?? "wire-item" } }) };
            replay.SetCorrelationId("wire-offer"); a.Send(replay);
        }
        Wait(a, b, () => a.Find(FrameworkTradeProtocol.OfferUpdateResponseType, "wire-offer") != null);
        var offered = FrameworkSerialization.DeserializePayload<FrameworkTradeOfferUpdateResponse>(a.Find(FrameworkTradeProtocol.OfferUpdateResponseType, "wire-offer").PayloadJson);
        Check(offered.Success && offered.Items.Single().PayloadBytes.SequenceEqual(new byte[] { 17, 0, 255 }), "Wire offer/replay lost original result");
        if (initial)
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var stream = new FileStream(path, options); JsonSerializer.Serialize(stream, state);
        }
        Check(!a.Packets.Any(p => p.MessageType == FrameworkTradeProtocol.CompletedEventType), "Replay unexpectedly delivered assets");
        Console.WriteLine("TRADE WIRE RESULT passed; phase=" + phase + "; client-pinned serializers/real UDP; no game receipt");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Wait(Peer a, Peer b, Func<bool> done)
    { var timer = Stopwatch.StartNew(); while (!done() && timer.ElapsedMilliseconds < 10000) { a.Pump(); b.Pump(); Thread.Sleep(5); } Check(done(), "Trade wire deadline"); }
    private sealed class Peer : IDisposable
    {
        private readonly NetManager client;
        private NetPeer peer;
        private string session;
        private Exception error;
        public string Principal;
        public string SessionId => session;
        public bool Ready, LoginFailed;
        public readonly List<FrameworkPacket> Packets = new();
        public Peer(int port, string key, bool expectLoginFailure = false, bool queries = false, bool receipts = false)
        {
            var listener = new EventBasedNetListener(); client = new NetManager(listener, "Phinix");
            listener.NetworkReceiveEvent += (_, reader) =>
            {
                try
                {
                    string module = reader.GetString(); byte[] bytes = reader.GetRemainingBytes();
                    if (module == FrameworkProtocol.ModuleName)
                    {
                        var packet = FrameworkSerialization.DeserializePacket(bytes);
                        Check(packet.Version == 2 && packet.SessionId == session && packet.SenderUuid == FrameworkProtocol.SystemSenderUuid, "Cross-recipient envelope");
                        if (packet.Kind == FrameworkProtocol.KindCapabilities) Ready = true;
                        Packets.Add(packet); return;
                    }
                    var envelope = Any.Parser.ParseFrom(bytes);
                    if (envelope.Is(HelloPacket.Descriptor))
                    {
                        session = envelope.Unpack<HelloPacket>().SessionId;
                        Raw("Authentication", ProtobufPacketHelper.Pack(new AuthenticatePacket { SessionId = session, AuthType = AuthTypes.ClientKey, Username = key }).ToByteArray());
                    }
                    else if (envelope.Is(AuthResponsePacket.Descriptor))
                    {
                        Check(envelope.Unpack<AuthResponsePacket>().Success, "Wire authentication refused");
                        Raw("UserManagement", ProtobufPacketHelper.Pack(new LoginPacket { SessionId = session, DisplayName = "synthetic-trade-wire", AcceptingTrades = true }).ToByteArray());
                    }
                    else if (envelope.Is(LoginResponsePacket.Descriptor))
                    {
                        var login = envelope.Unpack<LoginResponsePacket>();
                        if (!login.Success) { Check(expectLoginFailure, "Wire login refused"); LoginFailed = true; return; }
                        Check(!expectLoginFailure, "Failed identity snapshot published login success"); Principal = login.Uuid;
                        Raw("UserManagement", ProtobufPacketHelper.Pack(new UserUpdatePacket { SessionId = session, Uuid = Principal,
                            User = new User { Uuid = Principal, DisplayName = "synthetic-trade-wire", AcceptingTrades = true } }).ToByteArray());
                        var caps = new List<string> { FrameworkTradeProtocol.Capability, FrameworkTradeProtocol.CreateRequestType, FrameworkTradeProtocol.CreateResponseType, FrameworkTradeProtocol.SnapshotType,
                          FrameworkTradeProtocol.OfferUpdateRequestType, FrameworkTradeProtocol.OfferUpdateResponseType, FrameworkTradeProtocol.CompletedEventType };
                        if(queries)caps.AddRange(new[]{"trade.operation.query.request","trade.operation.query.response"});
                        if(receipts)caps.AddRange(new[]{FrameworkTradeProtocol.StatusUpdateRequestType,FrameworkTradeProtocol.StatusUpdateResponseType,FrameworkTradeProtocol.CancelledEventType,"trade.delivery.receipt.request","trade.delivery.receipt.response"});
                        Send(new FrameworkPacket { Kind = FrameworkProtocol.KindHello, PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkCapabilitiesPayload { Capabilities = caps }) });
                    }
                }
                catch (Exception failure) { error = new Exception(failure.Message == "Cross-recipient envelope" ? "Cross-recipient envelope" : "Independent Trade wire assertion failed"); }
            };
            Check(client.Start(), "Trade probe socket start"); peer = client.Connect("127.0.0.1", port);
        }
        private void Raw(string module, byte[] bytes) { var writer = new NetDataWriter(); writer.Put(module); writer.Put(bytes); peer.Send(writer, SendOptions.ReliableOrdered); }
        public void Send(FrameworkPacket packet) { packet.SessionId = session; packet.SenderUuid = Principal; Raw(FrameworkProtocol.ModuleName, FrameworkSerialization.SerializePacket(packet)); }
        public void Command<T>(string type, T payload, string id) => Send(new FrameworkPacket { Kind = FrameworkProtocol.KindCommand, CommandKind = global::Phinix.Framework.FrameworkCommandKind.Request, MessageType = type, MessageId = id, PayloadJson = FrameworkSerialization.SerializePayload(payload) });
        public FrameworkPacket Find(string type, string correlation) => Packets.FirstOrDefault(p => p.MessageType == type && p.GetCorrelationId() == correlation);
        public void Pump() { client.PollEvents(); if (error != null) throw error; }
        public void Until(Func<bool> done) { var t = Stopwatch.StartNew(); while (!done() && t.ElapsedMilliseconds < 10000) { Pump(); Thread.Sleep(5); } Check(done(), "Identity failure probe deadline"); }
        public void Dispose() => client.Stop();
    }
}
