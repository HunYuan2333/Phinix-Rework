using System;
using Phinix.TradeExtension;
using PhinixClient.Framework;
using Utils.Framework;
using Verse;

namespace Phinix.LegacyAdapter.Client
{
    /// <summary>
    /// Legacy Adapter 插件入口。
    /// 在 Legacy 模式下通过 Priority=500 劫持 Chat/Trade 通信，做协议翻译。
    /// 在 FrameworkV2 模式下完全透明（CanHandle 返回 false）。
    /// </summary>
    [PhinixExtension("builtin.legacy-adapter")]
    public class BuiltInLegacyAdapterClientExtension :
        IPhinixExtensionModule,
        IActivatablePhinixExtensionModule,
        IClientMessageHandler,
        IClientCommandHandler
    {
        private ILegacyModuleTransport legacyTransport;
        private IFrameworkClientLifecycle lifecycle;
        private IDisplayMessageSink displaySink;
        private IClientSessionContext sessionContext;
        private IFrameworkTradeClientApi tradeApi;

        private LegacyChatProtocolAdapter chatAdapter;
        private LegacyTradeProtocolAdapter tradeAdapter;
        private bool legacyHandlersRegistered;

        public string ExtensionId => "builtin.legacy-adapter";

        /// <summary>
        /// Priority=500，高于 Chat(1000) 和 Trade(1100)，
        /// 保证在 Legacy 模式下先于它们拦截消息。
        /// </summary>
        public int Priority => 500;

        public void Register(IExtensionBuilder builder)
        {
            builder.AddClientMessageHandler(this);
            builder.AddClientCommandHandler(this);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            legacyTransport = hostContext.GetRequiredService<ILegacyModuleTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            displaySink = hostContext.GetRequiredService<IDisplayMessageSink>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();

            if (!hostContext.ApiRegistry.TryResolve<IFrameworkTradeClientApi>(out tradeApi))
            {
                Verse.Log.Warning("[LegacyAdapter] IFrameworkTradeClientApi not registered — legacy trade sync disabled.");
            }

            chatAdapter = new LegacyChatProtocolAdapter(legacyTransport, displaySink, sessionContext);
            tradeAdapter = new LegacyTradeProtocolAdapter(legacyTransport, displaySink, sessionContext, tradeApi, hostContext.Log);

            lifecycle.CompatibilityModeChanged += OnCompatibilityModeChanged;

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.Legacy)
                RegisterLegacyHandlers();
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            UnregisterLegacyHandlers();

            if (lifecycle != null)
                lifecycle.CompatibilityModeChanged -= OnCompatibilityModeChanged;
        }

        private void OnCompatibilityModeChanged(object sender, FrameworkCompatibilityModeChangedEventArgs e)
        {
            switch (e.CompatibilityMode)
            {
                case FrameworkCompatibilityMode.Legacy:
                    RegisterLegacyHandlers();
                    break;
                default:
                    UnregisterLegacyHandlers();
                    break;
            }
        }

        private void RegisterLegacyHandlers()
        {
            if (legacyHandlersRegistered) return;
            chatAdapter?.RegisterHandlers();
            tradeAdapter?.RegisterHandlers();
            legacyHandlersRegistered = true;
            Verse.Log.Message("[LegacyAdapter] Registered legacy protocol handlers (Chat + Trading).");
        }

        private void UnregisterLegacyHandlers()
        {
            if (!legacyHandlersRegistered) return;
            chatAdapter?.UnregisterHandlers();
            tradeAdapter?.UnregisterHandlers();
            legacyHandlersRegistered = false;
            Verse.Log.Message("[LegacyAdapter] Unregistered legacy protocol handlers.");
        }

        // ========== IClientMessageHandler (Chat) ==========

        public bool CanHandleOutgoingText(string rawMessage)
        {
            // 仅在 Legacy 模式下拦截。FrameworkV2 模式返回 false，传递给 Chat handler。
            return lifecycle?.CompatibilityMode == FrameworkCompatibilityMode.Legacy
                   && !string.IsNullOrWhiteSpace(rawMessage);
        }

        public ClientOutgoingMessageResult HandleOutgoingText(
            string rawMessage, ClientFrameworkContext context)
        {
            // 旧版服务器不广播回发送者，需要本地注入回声消息
            displaySink?.Enqueue(new FrameworkDisplayMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SenderUuid = sessionContext?.Uuid ?? string.Empty,
                Text = rawMessage,
                TimestampUtcTicks = DateTime.UtcNow.Ticks,
                Source = "builtin_chat"
            });

            chatAdapter.SendChatMessage(rawMessage);
            // 始终返回 Handled，防止后续 Chat handler 在 Legacy 模式下显示错误提示
            return new ClientOutgoingMessageResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }

        public bool CanHandleIncomingMessage(FrameworkPacket message)
        {
            return false; // Legacy 入站不走 Framework pipeline
        }

        public ClientIncomingMessageResult HandleIncomingMessage(
            FrameworkPacket message, ClientFrameworkContext context)
        {
            return null;
        }

        // ========== IClientCommandHandler (Trade) ==========

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return false; // Legacy Trade 入站不走 Framework pipeline
        }

        public ClientIncomingCommandResult HandleIncomingCommand(
            FrameworkPacket command, ClientFrameworkContext context)
        {
            return null;
        }
    }
}
