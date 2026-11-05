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
    private static bool baseline;
    private static int assertions;
    private static int Main(string[] args)
    {
        baseline = args.Contains("--baseline");
        try
        {
            var consentFixture = new Fixture();
            consentFixture.Confirm();
            Assert(consentFixture.Transport.Status.Single().Accepted &&
                !consentFixture.Transport.Status.Single().Cancelled &&
                consentFixture.Transport.Status.Single().TradeId == "trade",
                "Unversioned Legacy consent must encode the original status packet.");
            if (args.Contains("--consent-only"))
            {
                Console.WriteLine("LEGACY CONSENT WIRE RESULT passed; actual adapter/serializer/original status shape");
                return 0;
            }
            AssertOutgoingFailureStopsPipeline();
            AssertFullSyncIsAtomicReplacement();
            AssertRegistryComposition();
            AssertEarlyLegacyChatHistory();
            AssertOnlyResponseConfirmsOffer();
            AssertRejectedOfferReportsFailure();
            AssertSendFailureReportsFailure();
            AssertInvalidItemsRejectWholeOffer();
            AssertEmptySnapshotIsAuthoritative();
            AssertTokenlessRequestsGetUniqueWireTokens();
            AssertEncodingFailureDoesNotReturnPartialItems();
            AssertUnknownSendOutcomeRemainsPending();
            AssertStatefulItemsRoundTripThroughLegacyServerShape();
            Console.WriteLine("Legacy adapter runtime scenarios passed: " + assertions + " assertions; " + (baseline ? "before" : "after"));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void AssertOutgoingFailureStopsPipeline()
    {
        // Isolate authentication/network setup only; exercise the actual Host dispatch loops.
        object Bare(Type type) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        var client = new PhinixFrameworkClient(null,
            (Authentication.ClientAuthenticator)Bare(typeof(Authentication.ClientAuthenticator)),
            (UserManagement.ClientUserManager)Bare(typeof(UserManagement.ClientUserManager)));
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        object runtime = typeof(PhinixFrameworkClient).GetField("extensionRuntime", flags).GetValue(client);
        var discovered = (DiscoveredPhinixExtensions)runtime.GetType().GetProperty("Extensions", flags).GetValue(runtime);
        var first = new DispatchProbe(10, true);
        var next = new DispatchProbe(20, false);
        discovered.Extensions.Add(first); discovered.Extensions.Add(next);
        discovered.ClientMessageHandlers.Add(first); discovered.ClientMessageHandlers.Add(next);
        discovered.ClientOutgoingItemHandlers.Add(first); discovered.ClientOutgoingItemHandlers.Add(next);
        foreach (Action dispatch in new Action[] {
            () => client.TryHandleOutgoingCommand(new FrameworkPacket { MessageType = "probe.command" }),
            () => client.TryHandleOutgoingMessage("probe text"),
            () => client.TryHandleOutgoingItem(new FrameworkItemPayload { CodecId = "probe" }) })
        {
            bool failed = false;
            try { dispatch(); } catch (InvalidOperationException error) { failed = error.Message == "sent-then-failed"; }
            Assert(failed && next.Calls == 0, "A possibly sent operation must propagate failure without invoking another handler.");
        }
        Assert(first.Calls == 3, "All three actual outgoing Host loops exercise the failure boundary.");
    }

    private sealed class DispatchProbe : IPhinixExtension, IClientOutgoingCommandHandler, IClientMessageHandler, IClientOutgoingItemHandler
    {
        public string ExtensionId => "probe." + Priority;
        public int Priority { get; }
        private readonly bool fails;
        public int Calls;
        public DispatchProbe(int priority, bool fails) { Priority = priority; this.fails = fails; }
        private void Dispatch() { Calls++; if (fails) throw new InvalidOperationException("sent-then-failed"); }
        public bool CanHandleOutgoingCommand(FrameworkPacket packet) => true;
        public ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket packet, ClientFrameworkContext context)
        { Dispatch(); return new ClientOutgoingCommandResult { Action = MessageHandlingResultAction.Handled }; }
        public bool CanHandleOutgoingText(string text) => true;
        public ClientOutgoingMessageResult HandleOutgoingText(string text, ClientFrameworkContext context)
        { Dispatch(); return new ClientOutgoingMessageResult { Action = MessageHandlingResultAction.Handled }; }
        public bool CanHandleIncomingMessage(FrameworkPacket packet) => false;
        public ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket packet, ClientFrameworkContext context) => null;
        public bool CanHandleOutgoingItem(FrameworkItemPayload item) => true;
        public ClientOutgoingItemResult HandleOutgoingItem(FrameworkItemPayload item, ClientFrameworkContext context)
        { Dispatch(); return new ClientOutgoingItemResult { Action = ItemHandlingResultAction.Handled }; }
    }

    private static void AssertFullSyncIsAtomicReplacement()
    {
        var fixture = new Fixture();
        var sync = new Trading.SyncTradesPacket();
        sync.Trades.Add(new Trading.TradeProto { TradeId = "replacement", OtherPartyUuid = "remote" });
        fixture.Transport.Receive(sync);
        Assert(fixture.Service.GetRepositoryTrades().Single().TradeId == "replacement", "Original full sync removes stale trades.");
        sync.Trades.Add(new Trading.TradeProto { TradeId = "invalid", OtherPartyUuid = "local" });
        fixture.Transport.Receive(sync);
        Assert(fixture.Service.GetRepositoryTrades().Single().TradeId == "replacement", "Malformed later sync entry cannot partially replace state.");
        fixture.Transport.Receive(new Trading.SyncTradesPacket());
        Assert(!fixture.Service.GetRepositoryTrades().Any(), "Empty original full sync clears stale trades.");
    }

    private static void AssertRegistryComposition()
    {
        Type type = typeof(Phinix.LegacyAdapter.Client.BuiltInLegacyAdapterClientExtension);
        Assert(System.IO.Path.GetFullPath(type.Assembly.Location) ==
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LegacyAdapter.Client.dll"),
            "Probe must load its isolated requested module DLL.");
        Console.WriteLine("MODULE " + type.Assembly.Location);
        var host = new ExtensionHostContext();
        host.AddService<IExtensionDiscoveryPolicy>(new LegacyDiscovery(type));
        var disabled = PhinixExtensionRegistry.DiscoverExtensions(host, new DisabledLegacy());
        Assert(disabled.Modules.Count == 0 && disabled.ExtensionResults.Single().State == ExtensionModuleState.Disabled,
            "Disabled adapter is discovered without composition services or activation.");
        using (var factory = new ClientCompositionFactory(() => true, error => { throw error; }))
        {
            host.AddService<IClientCompositionFactory>(factory);
            var found = PhinixExtensionRegistry.DiscoverExtensions(host);
            Assert(found.Modules.Count == 1 && found.ClientMessageHandlers.Count == 1 && found.ClientCommandHandlers.Count == 1,
                "Ordinary registry publishes exactly the original handlers.");
            Assert(((IClientMessageHandler)found.Modules.Single()).Priority == 500,
                "Legacy adapter priority remains 500.");
            if (!baseline) Assert(found.Modules.Single() is ClientExtensionModule, "Candidate uses the ordinary Compose bridge.");
            PhinixExtensionRegistry.ActivateExtensions(found, host);
            Assert(found.ExtensionResults.Single().State == ExtensionModuleState.Failed,
                "Missing endpoint service follows ordinary activation failure policy.");
            PhinixExtensionRegistry.ShutdownExtensions(found, host);
        }
    }
    private sealed class LegacyDiscovery : IExtensionDiscoveryPolicy
    {
        private readonly Type type;
        internal LegacyDiscovery(Type type) { this.type = type; }
        public bool ShouldScanAssembly(System.Reflection.Assembly assembly) => assembly == type.Assembly;
        public bool ShouldDiscoverType(Type candidate) => candidate == type;
    }
    private sealed class DisabledLegacy : IExtensionActivationPolicy
    {
        public IReadOnlyCollection<string> DisabledExtensions => new[] { "builtin.legacy-adapter" };
        public bool ShouldActivate(string id, out string reason) { reason = "test disabled"; return false; }
    }

    private static void AssertEarlyLegacyChatHistory()
    {
        var transport = new ChatTransport();
        var lifecycle = new ChatLifecycle();
        var sink = new ChatSink();
        var host = new ExtensionHostContext { Log = (_, __) => { } };
        var cleanupErrors = new List<Exception>();
        using (var factory = new ClientCompositionFactory(() => true, cleanupErrors.Add))
        {
        host.AddService<IClientCompositionFactory>(factory);
        host.AddService<ILegacyModuleTransport>(transport);
        host.AddService<IFrameworkClientLifecycle>(lifecycle);
        host.AddService<IDisplayMessageSink>(sink);
        host.AddService<IClientSessionContext>(new FakeSession());
        var tradeService = new PhinixFrameworkTradeClientService(null, new FakeUsers(), null);
        host.ApiRegistry.RegisterApi<IFrameworkTradeClientApi>("builtin.trade", tradeService);
        host.ApiRegistry.RegisterApi<ITradeStateInput>("builtin.trade", tradeService);
        int syncs = 0; tradeService.OnTradesSynced += (_, __) => syncs++;
        var originalScope = new ReceiveScope();
        var scopes = new ReceiveScopes { Current = originalScope };
        tradeService.GetType().GetMethod("ConfigureGameScopes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(tradeService, new object[] { scopes });
        int completions = 0;
        tradeService.OnTradeCompleted += (_, args) =>
        {
            completions++;
            var captured = args.GetType().GetField("GameScope", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(args);
            Assert(ReferenceEquals(captured, originalScope) && !((IClientGameScope)captured).IsCurrent(),
                "Deferred login delivery retains the receive-time game token instead of binding to the later game.");
        };
        var module = new Phinix.LegacyAdapter.Client.BuiltInLegacyAdapterClientExtension();
        module.Activate(host);
        if (!baseline) module.Activate(host);
        Assert(transport.Registrations == 2, "Repeated activation does not register twice.");
        Assert(transport.Handlers.ContainsKey("Chat") && transport.Handlers.ContainsKey("Trading"), "Unknown mode captures login packets without enabling Trade dispatch.");
        var history = new Chat.ChatHistoryPacket();
        history.ChatMessages.Add(new Chat.ChatMessagePacket { MessageId = "before-negotiation", Uuid = "remote", Message = "old history", Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(new DateTime(2026,11,5,0,0,0,DateTimeKind.Utc)) });
        transport.Receive(history);
        Assert(sink.Messages.Single().MessageId == "before-negotiation", "Login-time history arrives before Legacy negotiation completes.");
        Assert(sink.Messages.Single().TimestampUtcTicks == new DateTime(2026,11,5,0,0,0,DateTimeKind.Utc).Ticks,
            "Original protobuf timestamp includes the Unix epoch offset.");
        // A packet arriving during negotiation must be replayed only after Legacy selection.
        transport.ReceiveTrade(new Trading.SyncTradesPacket());
        transport.ReceiveTrade(new Trading.CompleteTradePacket { TradeId = "early-completion", Success = true, OtherPartyUuid = "remote" });
        Assert(completions == 0, "Unknown protocol selection cannot publish a buffered completion.");
        originalScope.Valid = false; scopes.Current = new ReceiveScope();
        lifecycle.Change(FrameworkCompatibilityMode.Legacy);
        lifecycle.Change(FrameworkCompatibilityMode.Legacy);
        Assert(completions == 1, "Selected original completion is published exactly once.");
        Assert(syncs == 1, "Negotiation replays the real early full sync exactly once.");
        Assert(transport.Handlers.Count == 2 && transport.Registrations == 2, "Legacy transition does not register Chat twice.");
        module.HandleOutgoingText("hello original", null);
        Assert(transport.Sent.Single().Message == "hello original", "Original chat dispatch does not require a running Verse logger.");
        var structured = new global::Phinix.Framework.BuiltInChatMessagePayload { MessageId = "structured", Message = "reply text", ReplyToMessageId = "parent", ReplyToSnippet = "quote" };
        structured.MentionedUuids.Add("remote");
        module.HandleOutgoingMessage(new FrameworkPacket { MessageType = Phinix.ChatExtension.FrameworkChatProtocol.MessageType, PayloadBytes = structured.ToByteArray() }, null);
        Assert(transport.Sent.Last().Message.Contains("quote") && transport.Sent.Last().Message.Contains("remote") && transport.Sent.Last().Message.Contains("reply text"), "Legacy projection preserves reply and mention context as explicit text.");
        lifecycle.Change(FrameworkCompatibilityMode.Unknown);
        Assert(transport.Handlers.ContainsKey("Chat") && transport.Handlers.ContainsKey("Trading"), "Reconnect keeps login handlers ready for the next login history.");
        transport.Receive(history);
        Assert(sink.Messages.Count(m => m.MessageId == "before-negotiation") == 2, "Reconnect login-time history reaches the display sink again.");
        transport.ReceiveTrade(new Trading.SyncTradesPacket());
        lifecycle.Change(FrameworkCompatibilityMode.FrameworkV2);
        Assert(syncs == 1, "Modern selection discards buffered original packets.");
        Assert(transport.Handlers.Count == 0, "FrameworkV2 relinquishes legacy Chat and Trade handlers.");
        lifecycle.Change(FrameworkCompatibilityMode.Unknown);
        Assert(transport.Handlers.ContainsKey("Chat"), "A new negotiation prepares Chat again.");
        RawPacketHandlerDelegate staleChat = transport.Handlers["Chat"];
        module.Shutdown(host); module.Shutdown(host);
        if (!baseline) {
        staleChat("Chat", "server", ProtobufPacketHelper.Pack(history).ToByteArray());
        Assert(sink.Messages.Count(m => m.MessageId == "before-negotiation") == 2, "Detached Chat callback cannot publish after shutdown.");
        Assert(module.HandleOutgoingText("stale", null).Action == MessageHandlingResultAction.Continue,
            "Stopped module does not swallow or echo outgoing text.");
        }
        lifecycle.Change(FrameworkCompatibilityMode.Legacy);
        Assert(transport.Handlers.Count == 0, "Shutdown removes early Chat and ignores later lifecycle changes.");
        if (!baseline) {
        module.Activate(host);
        Assert(transport.Handlers.Count == 2, "Fresh activation restores the current Legacy mode.");
        module.Shutdown(host);
        foreach (string failureModule in new[] { "Chat", "Trading" })
        {
            transport.FailRegistrationModule = failureModule;
            bool failed = false;
            try { module.Activate(host); } catch (Exception) { failed = true; }
            Assert(failed && transport.Handlers.Count == 0, "Partial registration failure cleans all handlers: " + failureModule);
        }
        transport.FailRegistrationModule = null;
        module.Activate(host);
        Assert(transport.Handlers.Count == 2, "Activation can retry after rollback.");
        Assert(cleanupErrors.Count == 0, "Successful cleanup has no hidden scope failures.");
        transport.FailUnregistration = true;
        module.Shutdown(host);
        Assert(transport.Handlers.Count == 0 && cleanupErrors.Count == 1,
            "Cleanup continues through both unregister failures and reports the aggregate.");
        Assert(transport.Disposals == 0 && lifecycle.Disposals == 0,
            "Scope never disposes borrowed endpoint services.");
        transport.FailUnregistration = false;
        module.Shutdown(host);
        }
        }
    }

    private sealed class ReceiveScope : IClientGameScope
    {
        internal bool Valid = true;
        public bool IsCurrent() => Valid;
    }
    private sealed class ReceiveScopes : IClientGameScopeService
    {
        internal IClientGameScope Current;
        public IClientGameScope Capture() => Current;
    }

    private sealed class ChatTransport : ILegacyModuleTransport, IDisposable
    {
        internal readonly Dictionary<string, RawPacketHandlerDelegate> Handlers = new Dictionary<string, RawPacketHandlerDelegate>();
        internal int Registrations;
        internal string FailRegistrationModule;
        internal bool FailUnregistration;
        internal int Disposals;
        public void Dispose() { Disposals++; }
        public void RegisterHandler(string module, RawPacketHandlerDelegate handler)
        {
            Handlers.Add(module, handler); Registrations++;
            if (FailRegistrationModule == module) throw new InvalidOperationException("Injected registration failure after add");
        }
        public void UnregisterHandler(string module)
        {
            Handlers.Remove(module);
            if (FailUnregistration) throw new InvalidOperationException("Injected unregister failure after removal");
        }
        internal readonly List<Chat.ChatMessagePacket> Sent = new List<Chat.ChatMessagePacket>();
        public void Send(string module, byte[] data) { Sent.Add(Google.Protobuf.WellKnownTypes.Any.Parser.ParseFrom(data).Unpack<Chat.ChatMessagePacket>()); }
        internal void ReceiveTrade(IMessage packet) { Handlers["Trading"]("Trading", "server", ProtobufPacketHelper.Pack(packet).ToByteArray()); }
        internal void Receive(IMessage packet) { Handlers["Chat"]("Chat", "server", ProtobufPacketHelper.Pack(packet).ToByteArray()); }
    }
    private sealed class ChatLifecycle : IFrameworkClientLifecycle, IDisposable
    {
        internal int Disposals;
        public void Dispose() { Disposals++; }
        public FrameworkCompatibilityMode CompatibilityMode { get; private set; }
        public event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;
        internal void Change(FrameworkCompatibilityMode mode) { CompatibilityMode = mode; CompatibilityModeChanged?.Invoke(this, new FrameworkCompatibilityModeChangedEventArgs(mode)); }
    }
    private sealed class ChatSink : IDisplayMessageSink
    {
        internal readonly List<FrameworkDisplayMessage> Messages = new List<FrameworkDisplayMessage>();
        public void Enqueue(FrameworkDisplayMessage message) { Messages.Add(message); }
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
        Assert(fixture.LastSendError is Connections.NotConnectedException, "Definite dispatch failure propagates out of the adapter.");
        Assert(fixture.Successes.Count == 0 && fixture.Failures.Single().Token == "not-sent", "Send failure must reach the operation owner.");
        Assert(fixture.LocalCount == 3, "Send failure must not write a predicted offer.");
        bool refused = false;
        try { fixture.Confirm(); } catch (Connections.NotConnectedException) { refused = true; }
        Assert(refused, "Status send failure cannot be swallowed as handled success.");
        Assert(!fixture.Service.IsTradeUpdatePending("trade", "not-sent"), "Definite send failure must settle the pending request.");
    }

    private static void AssertUnknownSendOutcomeRemainsPending()
    {
        var fixture = new Fixture();
        fixture.Transport.ThrowAfterSend = true;
        fixture.Send("unknown-result", VanillaItem(20));
        Assert(fixture.LastSendError != null, "Unknown dispatch failure must propagate to the caller.");
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

        TradeItemSnapshot statefulSnapshot = new TradeItemSnapshot(
            "QING_TraitDisk", 1, 50, stateCodecId: StatefulTradeItemProtocol.ScribeCodecId,
            statePayload: new byte[] { 1, 2, 3 });
        FrameworkItemPayload encodedStateful = pipeline.EncodeTradeItems(new[] { statefulSnapshot }).Single();
        Assert(encodedStateful.CodecId == StatefulTradeItemProtocol.ScribeCodecId,
            "The stateful codec must run before the vanilla fallback.");
    }

    private static void AssertStatefulItemsRoundTripThroughLegacyServerShape()
    {
        var fixture = new Fixture();
        FrameworkItemPayload statefulItem = StatefulItem(1, new byte[] { 31, 41, 59, 26 });
        fixture.Send("stateful", statefulItem);

        Trading.UpdateTradeItemsPacket sent = fixture.Transport.Sent.Single();
        Trading.ProtoThing tunneled = sent.Items.Single();
        Assert(tunneled.DefName == "QING_TraitDisk", "The legacy-visible preview must remain the real item.");
        Assert(tunneled.InnerProtoThing != null, "Stateful data must be carried in a legacy ProtoThing tunnel.");

        // Simulate an old server parsing, storing, and serializing only the message
        // shape it knows. The tunnel uses known ProtoThing fields, so it survives.
        Trading.ProtoThing relayed = Trading.ProtoThing.Parser.ParseFrom(tunneled.ToByteArray());
        var response = new Trading.UpdateTradeItemsResponsePacket
        {
            TradeId = "trade",
            Token = "stateful",
            Success = true
        };
        response.Items.Add(relayed);
        fixture.Transport.Receive(response);

        FrameworkItemPayload confirmed = fixture.Service.GetRepositoryTrades().Single().Participants
            .Single(participant => participant.Uuid == "local").ItemsOnOffer.Single();
        Assert(confirmed.CodecId == StatefulTradeItemProtocol.ScribeCodecId,
            "The legacy adapter must restore the original stateful codec payload.");
        Assert(confirmed.PayloadBytes.SequenceEqual(statefulItem.PayloadBytes),
            "The old-server round trip must preserve the opaque item state byte-for-byte.");

        Assert(fixture.Service.TryGetItemsOnOffer("trade", "local", out IEnumerable<TradeItemSnapshot> snapshots),
            "The confirmed stateful offer must remain readable.");
        TradeItemSnapshot snapshot = snapshots.Single();
        Assert(snapshot.DefName == "QING_TraitDisk" && snapshot.StatePayload.SequenceEqual(statefulItem.PayloadBytes),
            "The UI snapshot must retain the same state payload for later updates and reset recovery.");
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

    private static FrameworkItemPayload StatefulItem(int count, byte[] state)
    {
        var payload = new FrameworkItemPayload
        {
            CodecId = StatefulTradeItemProtocol.ScribeCodecId,
            PayloadBytes = state.ToArray()
        };
        StatefulTradeItemProtocol.SetPreview(payload, new TradeItemSnapshot("QING_TraitDisk", count, 50));
        return payload;
    }

    private static Trading.ProtoThing ProtoItem(int count)
    {
        return new Trading.ProtoThing { DefName = "Silver", StackCount = count, HitPoints = 100 };
    }

    private static void Assert(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        public readonly PhinixFrameworkTradeClientService Service;
        public readonly FakeTransport Transport = new FakeTransport();
        public readonly List<string> Successes = new List<string>();
        public readonly List<TradeUpdateEventArgs> Failures = new List<TradeUpdateEventArgs>();
        private readonly IClientOutgoingCommandHandler adapter;
        public Exception LastSendError;

        public Fixture()
        {
            Service = new PhinixFrameworkTradeClientService(null, new FakeUsers(), null);
            ((ITradeStateInput)Service).UpsertTrade(new FrameworkTradeStateSnapshot
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
            object instance = Activator.CreateInstance(adapterType, Transport, new FakeSink(), new FakeSession(), Service, Service, null, null);
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
            LastSendError = null;
            try { adapter.HandleOutgoingCommand(packet, new ClientFrameworkContext()); }
            catch (Exception error) { LastSendError = error; }
        }

        public void Confirm()
        {
            adapter.HandleOutgoingCommand(Service.CreateStatusUpdateRequest("trade", true, null,
                new ClientFrameworkContext()), new ClientFrameworkContext());
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
        public readonly List<Trading.UpdateTradeStatusPacket> Status = new List<Trading.UpdateTradeStatusPacket>();
        public readonly List<Trading.UpdateTradeItemsPacket> Sent = new List<Trading.UpdateTradeItemsPacket>();
        public void RegisterHandler(string moduleName, RawPacketHandlerDelegate callback) => handler = callback;
        public void UnregisterHandler(string moduleName) => handler = null;
        public void Send(string moduleName, byte[] data)
        {
            if (FailSend) throw new Connections.NotConnectedException();
            var envelope = Google.Protobuf.WellKnownTypes.Any.Parser.ParseFrom(data);
            if (envelope.TypeUrl.EndsWith("UpdateTradeStatusPacket", StringComparison.Ordinal))
                Status.Add(envelope.Unpack<Trading.UpdateTradeStatusPacket>());
            else Sent.Add(envelope.Unpack<Trading.UpdateTradeItemsPacket>());
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
