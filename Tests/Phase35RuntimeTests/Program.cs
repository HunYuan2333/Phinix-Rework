using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Authentication;
using Connections;
using Google.Protobuf;
using Utils.Framework;

internal static class Program
{
    private static int Main()
    {
        try
        {
            AssertLegacyApisRemoved();
            AssertClientKeyGenerationStillWorks();
            AssertPreHandleInterceptorCanRewriteMessageBeforeDefaultHandler();
            AssertPreHandleInterceptorCanBlockDefaultHandler();
            AssertPreHandleCommandInterceptorCanBlockDefaultHandler();
            AssertOutboundInterceptorCanFilterRecipientsAndRewritePacket();
            Console.WriteLine("All framework runtime tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void AssertLegacyApisRemoved()
    {
        string repoRoot = GetRepositoryRoot();
        AssertFileDoesNotContain(Path.Combine(repoRoot, "Common", "Connections", "NetClient.cs"), "Abort(");
        AssertFileDoesNotContain(Path.Combine(repoRoot, "Common", "Connections", "NetServer.cs"), "Abort(");
        AssertFileDoesNotContain(Path.Combine(repoRoot, "Common", "Authentication", "ClientAuthenticator.cs"), "RNGCryptoServiceProvider");
    }

    private static void AssertClientKeyGenerationStillWorks()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "PhinixPhase35RuntimeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);

        try
        {
            string credentialStorePath = Path.Combine(testDirectory, "credentials.bin");
            NetClient netClient = new NetClient();
            ClientAuthenticator authenticator = new ClientAuthenticator(
                netClient,
                (sessionId, serverName, serverDescription, authType, callback) => { },
                credentialStorePath
            );

            Assert(File.Exists(credentialStorePath), "ClientAuthenticator should create its credential store on first use.");

            CredentialStore initialStore = ReadCredentialStore(credentialStorePath);
            Assert(!string.IsNullOrEmpty(initialStore.ClientKey), "Generated client key should not be empty.");
            Assert(Convert.FromBase64String(initialStore.ClientKey).Length == 64, "Generated client key should decode to 64 random bytes.");

            ClientAuthenticator secondAuthenticator = new ClientAuthenticator(
                netClient,
                (sessionId, serverName, serverDescription, authType, callback) => { },
                credentialStorePath
            );

            CredentialStore secondStore = ReadCredentialStore(credentialStorePath);
            Assert(initialStore.ClientKey == secondStore.ClientKey, "Existing credential store should preserve the original client key.");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    private static void AssertPreHandleInterceptorCanRewriteMessageBeforeDefaultHandler()
    {
        string observedPayload = null;
        ServerPipelineRunner runner = new ServerPipelineRunner();
        runner.InboundMessageInterceptors.Add(new DelegateServerInboundMessageInterceptor(
            canIntercept: message => message.MessageType == "chat",
            handle: (message, context) => new ServerIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Replace,
                Message = new FrameworkPacket
                {
                    Kind = message.Kind,
                    Flow = message.Flow,
                    MessageType = message.MessageType,
                    SenderUuid = message.SenderUuid,
                    SessionId = message.SessionId,
                    PayloadJson = "rewritten"
                }
            }));
        runner.DefaultMessageHandlers.Add(new DelegateServerDefaultMessageHandler(
            canHandle: message => message.MessageType == "chat",
            handle: (message, context) =>
            {
                observedPayload = message.PayloadJson;
                return new ServerIncomingMessageResult { Action = MessageHandlingResultAction.Handle };
            }));

        runner.ProcessIncomingMessage(
            new FrameworkPacket
            {
                Kind = FrameworkProtocol.KindMessage,
                Flow = global::Phinix.Framework.FrameworkFlow.Message,
                MessageType = "chat",
                SenderUuid = "sender",
                SessionId = "session",
                PayloadJson = "original"
            },
            CreateServerContext());

        Assert(observedPayload == "rewritten", "Pre-handle interceptor should rewrite the message before default processing.");
    }

    private static void AssertPreHandleInterceptorCanBlockDefaultHandler()
    {
        bool defaultHandlerCalled = false;
        ServerPipelineRunner runner = new ServerPipelineRunner();
        runner.InboundMessageInterceptors.Add(new DelegateServerInboundMessageInterceptor(
            canIntercept: message => true,
            handle: (message, context) => new ServerIncomingMessageResult
            {
                Action = MessageHandlingResultAction.Block
            }));
        runner.DefaultMessageHandlers.Add(new DelegateServerDefaultMessageHandler(
            canHandle: message => true,
            handle: (message, context) =>
            {
                defaultHandlerCalled = true;
                return new ServerIncomingMessageResult { Action = MessageHandlingResultAction.Handle };
            }));

        runner.ProcessIncomingMessage(
            new FrameworkPacket
            {
                Kind = FrameworkProtocol.KindMessage,
                Flow = global::Phinix.Framework.FrameworkFlow.Message,
                MessageType = "chat"
            },
            CreateServerContext());

        Assert(!defaultHandlerCalled, "Blocked inbound message should not reach default handlers.");
    }

    private static void AssertPreHandleCommandInterceptorCanBlockDefaultHandler()
    {
        bool defaultHandlerCalled = false;
        ServerPipelineRunner runner = new ServerPipelineRunner();
        runner.InboundCommandInterceptors.Add(new DelegateServerInboundCommandInterceptor(
            canIntercept: command => true,
            handle: (command, context) => new ServerIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Block
            }));
        runner.DefaultCommandHandlers.Add(new DelegateServerDefaultCommandHandler(
            canHandle: command => true,
            handle: (command, context) =>
            {
                defaultHandlerCalled = true;
                return new ServerIncomingCommandResult { Action = MessageHandlingResultAction.Handle };
            }));

        runner.ProcessIncomingCommand(
            new FrameworkPacket
            {
                Kind = FrameworkProtocol.KindCommand,
                Flow = global::Phinix.Framework.FrameworkFlow.Command,
                MessageType = "trade.request"
            },
            CreateServerContext());

        Assert(!defaultHandlerCalled, "Blocked inbound command should not reach default handlers.");
    }

    private static void AssertOutboundInterceptorCanFilterRecipientsAndRewritePacket()
    {
        List<(string ConnectionId, FrameworkPacket Packet)> sentPackets = new List<(string ConnectionId, FrameworkPacket Packet)>();
        ServerPipelineRunner runner = new ServerPipelineRunner();
        runner.OutboundPacketInterceptors.Add(new DelegateServerOutboundPacketInterceptor(
            canIntercept: packet => packet.MessageType == "chat",
            handle: (packet, context) => new ServerOutgoingPacketResult
            {
                Action = MessageHandlingResultAction.Replace,
                Packet = new FrameworkPacket
                {
                    Kind = packet.Kind,
                    Flow = packet.Flow,
                    MessageType = packet.MessageType,
                    PayloadJson = "outbound-rewritten"
                },
                TargetConnectionIds = context.TargetConnectionIds.Where(id => id != "blocked").ToArray()
            }));

        runner.DispatchOutbound(
            new FrameworkPacket
            {
                Kind = FrameworkProtocol.KindMessage,
                Flow = global::Phinix.Framework.FrameworkFlow.Message,
                MessageType = "chat",
                PayloadJson = "outbound-original"
            },
            new ServerOutboundPacketContext
            {
                TargetConnectionIds = new[] { "allowed", "blocked" },
                DeliverToConnection = (connectionId, packet) => sentPackets.Add((connectionId, packet))
            });

        Assert(sentPackets.Count == 1, "Outbound interceptor should be able to filter broadcast recipients.");
        Assert(sentPackets[0].ConnectionId == "allowed", "Only the allowed recipient should receive the outbound packet.");
        Assert(sentPackets[0].Packet.PayloadJson == "outbound-rewritten", "Outbound interceptor should be able to rewrite the outbound packet.");
    }

    private static CredentialStore ReadCredentialStore(string credentialStorePath)
    {
        using (FileStream stream = File.OpenRead(credentialStorePath))
        using (CodedInputStream input = new CodedInputStream(stream))
        {
            return CredentialStore.Parser.ParseFrom(input);
        }
    }

    private static void AssertFileDoesNotContain(string path, string forbiddenText)
    {
        string content = File.ReadAllText(path);
        Assert(!content.Contains(forbiddenText), $"Expected '{path}' to stop using '{forbiddenText}'.");
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class DelegateServerInboundMessageInterceptor : IServerInboundMessageInterceptor
    {
        private readonly Func<FrameworkPacket, bool> canIntercept;
        private readonly Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingMessageResult> handle;

        public DelegateServerInboundMessageInterceptor(Func<FrameworkPacket, bool> canIntercept, Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingMessageResult> handle)
        {
            this.canIntercept = canIntercept;
            this.handle = handle;
        }

        public int Priority => 0;

        public bool CanInterceptIncomingMessage(FrameworkPacket message) => canIntercept(message);

        public ServerIncomingMessageResult InterceptIncomingMessage(FrameworkPacket message, ServerFrameworkContext context) => handle(message, context);
    }

    private sealed class DelegateServerDefaultMessageHandler : IServerDefaultMessageHandler
    {
        private readonly Func<FrameworkPacket, bool> canHandle;
        private readonly Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingMessageResult> handle;

        public DelegateServerDefaultMessageHandler(Func<FrameworkPacket, bool> canHandle, Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingMessageResult> handle)
        {
            this.canHandle = canHandle;
            this.handle = handle;
        }

        public int Priority => 0;

        public bool CanHandleIncomingMessage(FrameworkPacket message) => canHandle(message);

        public ServerIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ServerFrameworkContext context) => handle(message, context);
    }

    private sealed class DelegateServerInboundCommandInterceptor : IServerInboundCommandInterceptor
    {
        private readonly Func<FrameworkPacket, bool> canIntercept;
        private readonly Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingCommandResult> handle;

        public DelegateServerInboundCommandInterceptor(Func<FrameworkPacket, bool> canIntercept, Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingCommandResult> handle)
        {
            this.canIntercept = canIntercept;
            this.handle = handle;
        }

        public int Priority => 0;

        public bool CanInterceptIncomingCommand(FrameworkPacket command) => canIntercept(command);

        public ServerIncomingCommandResult InterceptIncomingCommand(FrameworkPacket command, ServerFrameworkContext context) => handle(command, context);
    }

    private sealed class DelegateServerDefaultCommandHandler : IServerDefaultCommandHandler
    {
        private readonly Func<FrameworkPacket, bool> canHandle;
        private readonly Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingCommandResult> handle;

        public DelegateServerDefaultCommandHandler(Func<FrameworkPacket, bool> canHandle, Func<FrameworkPacket, ServerFrameworkContext, ServerIncomingCommandResult> handle)
        {
            this.canHandle = canHandle;
            this.handle = handle;
        }

        public int Priority => 0;

        public bool CanHandleIncomingCommand(FrameworkPacket command) => canHandle(command);

        public ServerIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ServerFrameworkContext context) => handle(command, context);
    }

    private sealed class DelegateServerOutboundPacketInterceptor : IServerOutboundPacketInterceptor
    {
        private readonly Func<FrameworkPacket, bool> canIntercept;
        private readonly Func<FrameworkPacket, ServerOutboundPacketContext, ServerOutgoingPacketResult> handle;

        public DelegateServerOutboundPacketInterceptor(Func<FrameworkPacket, bool> canIntercept, Func<FrameworkPacket, ServerOutboundPacketContext, ServerOutgoingPacketResult> handle)
        {
            this.canIntercept = canIntercept;
            this.handle = handle;
        }

        public int Priority => 0;

        public bool CanInterceptOutgoingPacket(FrameworkPacket packet, ServerOutboundPacketContext context) => canIntercept(packet);

        public ServerOutgoingPacketResult InterceptOutgoingPacket(FrameworkPacket packet, ServerOutboundPacketContext context) => handle(packet, context);
    }

    private static ServerFrameworkContext CreateServerContext()
    {
        return new ServerFrameworkContext
        {
            ConnectionId = "connection-1",
            SessionId = "session-1",
            SenderUuid = "sender-1",
            RemoteCapabilities = Array.Empty<string>(),
            ServerCapabilities = Array.Empty<string>(),
            HasRemoteCapability = _ => false,
            ConnectionHasCapability = (_, __) => false,
            SendMessage = (_, __) => { },
            BroadcastMessage = (_, __) => { },
            Log = (_, __) => { }
        };
    }
}
