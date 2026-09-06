using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UserManagement;
using Utils;
using Utils.Framework;

internal static class Program
{
    private static int Main()
    {
        try
        {
            AssertOnlyResponseConfirmsOffer();
            AssertRejectedOfferReportsFailure();
            AssertSendFailureReportsFailure();
            AssertInvalidItemsRejectWholeOffer();
            AssertEmptySnapshotIsAuthoritative();
            AssertTokenlessRequestsGetUniqueWireTokens();
            AssertEncodingFailureDoesNotReturnPartialItems();
            AssertUnknownSendOutcomeRemainsPending();
            Console.WriteLine("All 8 legacy trade runtime scenarios passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void AssertOnlyResponseConfirmsOffer()
    {
        var fixture = new Fixture();
        fixture.Send("request-one", VanillaItem(20));
        Assert(fixture.Successes.Count == 0, "Sending must not confirm an offer.");
        Assert(fixture.LocalCount == 3, "Sending must not replace authoritative state.");
        fixture.Transport.Receive(new Trading.UpdateTradeItemsPacket
        {
            TradeId = "trade", Token = "request-one", Items = { ProtoItem(7) }
        });
        Assert(fixture.Service.IsTradeUpdatePending("trade", "request-one"), "Snapshots must not settle operations.");
        Assert(fixture.Successes.Count == 0, "A snapshot token must not become a confirmation.");
        fixture.Reply("request-one", true, 20);
        Assert(fixture.Successes.SequenceEqual(new[] { "request-one" }), "A real response must confirm its token once.");
        Assert(fixture.LocalCount == 20, "Confirmed state must use response items.");
        fixture.Reply("request-one", true, 999);
        Assert(fixture.Successes.Count == 1 && fixture.LocalCount == 20, "Duplicate responses must not settle or overwrite state.");
        fixture.Reply("unknown-token", false, 999);
        Assert(fixture.Failures.Count == 0 && fixture.LocalCount == 20, "Unknown responses must be ignored.");
    }

    private static void AssertRejectedOfferReportsFailure()
    {
        var fixture = new Fixture();
        fixture.Send("rejected", VanillaItem(20));
        fixture.Reply("rejected", false, 3);
        Assert(fixture.Successes.Count == 0, "A rejected offer must never produce success.");
        Assert(fixture.Failures.Single().Token == "rejected", "Failure must preserve the original token for restoration.");
        Assert(fixture.Failures.Single().FailureReason == TradeFailureReason.SessionInvalid, "Legacy failure reasons must be normalized.");
        Assert(fixture.LocalCount == 3, "A rejected offer must retain server state.");
        fixture.Reply("rejected", false, 3);
        Assert(fixture.Failures.Count == 1, "Repeated failure must not restore twice.");
    }

    private static void AssertSendFailureReportsFailure()
    {
        var fixture = new Fixture();
        fixture.Transport.FailSend = true;
        fixture.Send("not-sent", VanillaItem(20));
        Assert(fixture.Successes.Count == 0 && fixture.Failures.Single().Token == "not-sent", "Send failure must reach the operation owner.");
        Assert(fixture.LocalCount == 3, "Send failure must not write a predicted offer.");
        Assert(!fixture.Service.IsTradeUpdatePending("trade", "not-sent"), "Definite send failure must settle the pending request.");
    }

    private static void AssertUnknownSendOutcomeRemainsPending()
    {
        var fixture = new Fixture();
        fixture.Transport.ThrowAfterSend = true;
        fixture.Send("unknown-result", VanillaItem(20));
        Assert(fixture.Transport.Sent.Count == 1, "The simulated transport accepted the packet before failing.");
        Assert(fixture.Successes.Count == 0 && fixture.Failures.Count == 0,
            "An uncertain send must not confirm or restore items.");
        Assert(fixture.Service.IsTradeUpdatePending("trade", "unknown-result"), "Uncertain operations must retain their pending token.");
        fixture.Reply("unknown-result", true, 20);
        Assert(fixture.Successes.Single() == "unknown-result", "A later server response must still resolve an uncertain send.");
    }

    private static void AssertInvalidItemsRejectWholeOffer()
    {
        foreach (FrameworkItemPayload invalidItem in new[]
        {
            new FrameworkItemPayload { CodecId = "unsupported.codec", PayloadBytes = VanillaItem(1).PayloadBytes },
            new FrameworkItemPayload { CodecId = "core.item.vanilla", PayloadBytes = new byte[] { 255 } },
            new FrameworkItemPayload { CodecId = "core.item.vanilla" }
        })
        {
            var fixture = new Fixture();
            fixture.Send("bad-item", VanillaItem(20), invalidItem);
            Assert(fixture.Transport.Sent.Count == 0, "One invalid item must reject the whole batch before sending.");
            Assert(fixture.Failures.Single().Token == "bad-item", "Conversion failure must preserve the operation token.");
            Assert(fixture.LocalCount == 3, "Conversion failure must not modify confirmed items.");
        }
    }

    private static void AssertEmptySnapshotIsAuthoritative()
    {
        var fixture = new Fixture();
        fixture.Transport.Receive(new Trading.UpdateTradeItemsPacket { TradeId = "trade" });
        Assert(fixture.LocalCount == 0, "An empty legacy Items list must clear the old offer.");
        fixture.Send("clear", VanillaItem(20));
        fixture.Reply("clear", true, 0);
        Assert(fixture.LocalCount == 0 && fixture.Successes.Single() == "clear", "An empty confirmed offer is valid.");
    }

    private static void AssertTokenlessRequestsGetUniqueWireTokens()
    {
        var fixture = new Fixture();
        fixture.Send(string.Empty);
        string firstToken = fixture.Transport.Sent.Single().Token;
        Assert(!string.IsNullOrEmpty(firstToken), "Tokenless UI operations still need a unique wire correlation.");
        fixture.Reply(firstToken, true, 0);
        fixture.Send(string.Empty);
        string secondToken = fixture.Transport.Sent.Last().Token;
        Assert(firstToken != secondToken, "A later operation must not reuse a completed token.");
        fixture.Reply(firstToken, true, 999);
        Assert(fixture.Service.IsTradeUpdatePending("trade", secondToken), "An old response must not confirm a new operation.");
        Assert(fixture.LocalCount == 0, "An old response must not overwrite the current offer.");
    }

    private static void AssertEncodingFailureDoesNotReturnPartialItems()
    {
        Type pipelineType = typeof(PhinixFrameworkTradeClientService).Assembly.GetType("Phinix.TradeExtension.Client.TradeClientItemPipeline", true);
        var pipeline = (ITradeItemPayloadEncoder)Activator.CreateInstance(pipelineType, null, FrameworkCompatibilityMode.Legacy, null);
        bool rejected = false;
        try
        {
            pipeline.EncodeTradeItems(new[] { new TradeItemSnapshot("Silver", 10, 100), null });
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Assert(rejected, "Encoding must reject the whole offer instead of returning only the valid items.");
        Assert(pipeline.EncodeTradeItems(new[] { new TradeItemSnapshot("Silver", 10, 100) }).Length == 1,
            "A rejected operation must not prevent a later valid encoding.");
    }

    private static FrameworkItemPayload VanillaItem(int count)
    {
        return new FrameworkItemPayload
        {
            CodecId = "core.item.vanilla",
            PayloadBytes = FrameworkSerialization.SerializeItemData(new Phinix.Framework.FrameworkVanillaItemData
            {
                DefName = "Silver", StackCount = count, HitPoints = 100
            })
        };
    }

    private static Trading.ProtoThing ProtoItem(int count)
    {
        return new Trading.ProtoThing { DefName = "Silver", StackCount = count, HitPoints = 100 };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        public readonly PhinixFrameworkTradeClientService Service;
        public readonly FakeTransport Transport = new FakeTransport();
        public readonly List<string> Successes = new List<string>();
        public readonly List<TradeUpdateEventArgs> Failures = new List<TradeUpdateEventArgs>();
        private readonly IClientOutgoingCommandHandler adapter;

        public Fixture()
        {
            Service = new PhinixFrameworkTradeClientService(null, new FakeUsers(), null);
            Type bridgeType = typeof(PhinixFrameworkTradeClientService).Assembly.GetType("Phinix.TradeExtension.Client.FrameworkLegacyTradeClientAdapter", true);
            object bridge = Activator.CreateInstance(bridgeType, Service);
            ((IFrameworkLegacyTradeRepositoryApi)bridge).UpsertTrade(new FrameworkTradeStateSnapshot
            {
                TradeId = "trade",
                Participants = new List<FrameworkTradeParticipantSnapshot>
                {
                    new FrameworkTradeParticipantSnapshot { Uuid = "local", ItemsOnOffer = new List<FrameworkItemPayload> { VanillaItem(3) } },
                    new FrameworkTradeParticipantSnapshot { Uuid = "remote" }
                }
            });
            Service.OnTradeUpdateSuccess += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Token)) Successes.Add(args.Token);
            };
            Service.OnTradeUpdateFailure += (sender, args) => Failures.Add(args);
            Type adapterType = typeof(Trading.ProtoThing).Assembly.GetType("Phinix.LegacyAdapter.Client.LegacyTradeProtocolAdapter", true);
            object instance = Activator.CreateInstance(adapterType, Transport, new FakeSink(), new FakeSession(), Service, bridge, bridge, null, null);
            adapter = (IClientOutgoingCommandHandler)instance;
            adapterType.GetMethod("RegisterHandlers").Invoke(instance, null);
        }

        public int LocalCount => Service.GetRepositoryTrades().Single().Participants
            .Single(participant => participant.Uuid == "local").ItemsOnOffer
            .Sum(item => FrameworkSerialization.DeserializeItemData(item.PayloadBytes).StackCount);

        public void Send(string token, params FrameworkItemPayload[] items)
        {
            var packet = new FrameworkPacket
            {
                MessageType = FrameworkTradeProtocol.OfferUpdateRequestType,
                PayloadJson = FrameworkSerialization.SerializePayload(new FrameworkTradeOfferUpdateRequest
                {
                    TradeId = "trade", Items = items.ToList()
                })
            };
            packet.SetCorrelationId(token);
            adapter.HandleOutgoingCommand(packet, new ClientFrameworkContext());
        }

        public void Reply(string token, bool success, int count)
        {
            var response = new Trading.UpdateTradeItemsResponsePacket
            {
                TradeId = "trade", Token = token, Success = success,
                FailureReason = Trading.TradeFailureReason.SessionId, FailureMessage = success ? "" : "Rejected"
            };
            if (count > 0) response.Items.Add(ProtoItem(count));
            Transport.Receive(response);
        }
    }

    private sealed class FakeTransport : ILegacyModuleTransport
    {
        private RawPacketHandlerDelegate handler;
        public bool FailSend;
        public bool ThrowAfterSend;
        public readonly List<Trading.UpdateTradeItemsPacket> Sent = new List<Trading.UpdateTradeItemsPacket>();
        public void RegisterHandler(string moduleName, RawPacketHandlerDelegate callback) => handler = callback;
        public void UnregisterHandler(string moduleName) => handler = null;
        public void Send(string moduleName, byte[] data)
        {
            if (FailSend) throw new Connections.NotConnectedException();
            Sent.Add(Google.Protobuf.WellKnownTypes.Any.Parser.ParseFrom(data).Unpack<Trading.UpdateTradeItemsPacket>());
            if (ThrowAfterSend) throw new InvalidOperationException("Transport failed after accepting the packet");
        }
        public void Receive(IMessage message) => handler("Trading", "server", ProtobufPacketHelper.Pack(message).ToByteArray());
    }

    private sealed class FakeUsers : IClientUserDirectory
    {
        public string Uuid => "local";
        public ImmutableUser[] GetUsers(bool loggedIn = false) => Array.Empty<ImmutableUser>();
        public bool TryGetUser(string uuid, out ImmutableUser user)
        {
            user = new ImmutableUser(uuid);
            return true;
        }
    }

    private sealed class FakeSession : IClientSessionContext
    {
        public bool Authenticated => true;
        public bool LoggedIn => true;
        public string SessionId => "session";
        public string Uuid => "local";
    }

    private sealed class FakeSink : IDisplayMessageSink
    {
        public void Enqueue(FrameworkDisplayMessage message) { }
    }
}
