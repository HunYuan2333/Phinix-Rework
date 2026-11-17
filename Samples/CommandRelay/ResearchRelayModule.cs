using System;
using System.Threading.Tasks;
using PhinixClient.Framework;
using Utils.Framework;

namespace CommandRelay.Sample
{
    public interface IResearchRelaySample
    {
        event Action<string, string> Received;
        Task<RelayResult> RequestPlayer(string playerUuid, string payloadJson);
        Task<RelayResult> Announce(string payloadJson);
    }

    [PhinixExtension("sample.research-relay")]
    public sealed class ResearchRelayModule : ClientExtensionModule, IActivatablePhinixExtensionModule,
        IClientCommandHandler, IResearchRelaySample
    {
        private const string PluginId = "sample.research-relay";
        private IClientCommandRelay relay;
        private IClientMainThreadDispatcher dispatcher;
        private IDisposable requestDeclaration, announcementDeclaration;
        private volatile int generation;
        public override string ExtensionId => PluginId;
        public int Priority => 100;
        public event Action<string, string> Received;

        public override void Compose(IExtensionBuilder builder)
        {
            builder.AddClientCommandHandler(this);
            builder.RegisterApi<IResearchRelaySample>(this);
        }
        public void Activate(ExtensionHostContext host)
        {
            relay = host.GetRequiredService<IClientCommandRelay>();
            dispatcher = host.GetRequiredService<IClientMainThreadDispatcher>();
            var capabilities = host.GetRequiredService<IClientCommandCapabilities>();
            requestDeclaration = capabilities.Declare(ExtensionId, PluginId, "request", 1);
            announcementDeclaration = capabilities.Declare(ExtensionId, PluginId, "announcement", 1);
            generation++;
        }
        public void Shutdown(ExtensionHostContext host)
        {
            generation++;
            requestDeclaration?.Dispose();
            announcementDeclaration?.Dispose();
            requestDeclaration = announcementDeclaration = null;
            relay = null;
            dispatcher = null;
            Received = null;
        }
        public Task<RelayResult> RequestPlayer(string playerUuid, string payloadJson)
        {
            if (relay == null) throw new InvalidOperationException("Sample module is inactive.");
            return relay.SendRelay(PluginId, "request", payloadJson, playerUuid, 1);
        }
        public Task<RelayResult> Announce(string payloadJson)
        {
            if (relay == null) throw new InvalidOperationException("Sample module is inactive.");
            return relay.SendRelay(PluginId, "announcement", payloadJson);
        }
        public bool CanHandleIncomingCommand(FrameworkPacket packet)
        {
            var route = packet.CommandRoute;
            return route?.Mode == CommandProcessingMode.Relay && route.PluginId == PluginId &&
                route.Version == 1 && (route.CommandType == "request" || route.CommandType == "announcement");
        }
        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket packet, ClientFrameworkContext context)
        {
            int capturedGeneration = generation;
            string sender = packet.SenderUuid, payload = packet.PayloadJson;
            dispatcher?.Enqueue(() =>
            {
                if (capturedGeneration == generation) Received?.Invoke(sender, payload);
            });
            return new ClientIncomingCommandResult { Action = MessageHandlingResultAction.Handled };
        }
    }
}
