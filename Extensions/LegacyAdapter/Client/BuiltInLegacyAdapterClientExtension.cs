using System;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;

namespace Phinix.LegacyAdapter.Client
{
    /// <summary>
    /// Legacy Adapter 插件入口。
    /// 在 Legacy 模式下通过 Priority=500 劫持 Chat/Trade 通信，做协议翻译。
    /// 在 FrameworkV2 模式下完全透明（CanHandle 返回 false）。
    ///
    /// 实现 IClientOutgoingCommandHandler 以接入出站命令管线：
    /// Legacy 模式下抢在 Trade handler (P=1100) 前面拦截并翻译 trade 出站命令。
    /// </summary>
    [PhinixExtension("builtin.legacy-adapter")]
    public class BuiltInLegacyAdapterClientExtension :
        ClientExtensionModule,
        IActivatablePhinixExtensionModule,
        IClientMessageHandler,
        IClientCommandHandler,
        IClientOutgoingCommandHandler
    {
        private ILegacyModuleTransport legacyTransport;
        private IFrameworkClientLifecycle lifecycle;
        private IDisplayMessageSink displaySink;
        private IClientSessionContext sessionContext;
        private IFrameworkTradeClientApi tradeApi;
        private IFrameworkLegacyTradeRepositoryApi legacyTradeRepositoryApi;
        private IFrameworkLegacyTradeDeliveryApi legacyTradeDeliveryApi;
        private Action<string, LogLevel> log;

        private LegacyChatProtocolAdapter chatAdapter;
        internal LegacyTradeProtocolAdapter tradeAdapter;
        private IClientCompositionScope composition;
        private LegacyAdapterSession session;

        public override string ExtensionId => "builtin.legacy-adapter";

        /// <summary>
        /// Priority=500，高于 Chat(1000) 和 Trade(1100)，
        /// 保证在 Legacy 模式下先于它们拦截消息和命令。
        /// </summary>
        public int Priority => 500;

        public override void Compose(IExtensionBuilder builder)
        {
            builder.AddClientMessageHandler(this);
            builder.AddClientCommandHandler(this);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (composition != null) return;
            try
            {
                legacyTransport = hostContext.GetRequiredService<ILegacyModuleTransport>();
                lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
                displaySink = hostContext.GetRequiredService<IDisplayMessageSink>();
                sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
                log = hostContext.Log;

                if (!hostContext.ApiRegistry.TryResolve<IFrameworkTradeClientApi>(out tradeApi))
                {
                    log?.Invoke("[LegacyAdapter] IFrameworkTradeClientApi not registered — legacy trade sync disabled.", LogLevel.WARNING);
                }

                hostContext.ApiRegistry.TryResolve<IFrameworkLegacyTradeRepositoryApi>(out legacyTradeRepositoryApi);
                if (!hostContext.ApiRegistry.TryResolve<IFrameworkLegacyTradeDeliveryApi>(out legacyTradeDeliveryApi))
                {
                    log?.Invoke("[LegacyAdapter] IFrameworkLegacyTradeDeliveryApi not registered — legacy trade completion disabled.", LogLevel.ERROR);
                }
                composition = hostContext.GetRequiredService<IClientCompositionFactory>().CreateScope(local =>
                {
                    local.Borrow(legacyTransport);
                    local.Borrow(displaySink);
                    local.Borrow(sessionContext);
                    local.Borrow(lifecycle);
                    local.Borrow(new LegacyTradeDependencies
                    {
                        Api = tradeApi, Repository = legacyTradeRepositoryApi,
                        Delivery = legacyTradeDeliveryApi, Log = log
                    });
                    local.Register<LegacyAdapterSession, LegacyAdapterSession>();
                });
                session = composition.Resolve<LegacyAdapterSession>();
                chatAdapter = session.Chat;
                tradeAdapter = session.Trade;
                session.Start();
            }
            catch (Exception error)
            {
                try { Shutdown(hostContext); }
                catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            var owned = composition;
            composition = null;
            chatAdapter = null; tradeAdapter = null; session = null;
            legacyTransport = null; lifecycle = null; displaySink = null; sessionContext = null;
            tradeApi = null; legacyTradeRepositoryApi = null; legacyTradeDeliveryApi = null; log = null;
            owned?.Dispose();
        }

        // ========== IClientMessageHandler (Chat) ==========

        public bool CanHandleOutgoingText(string rawMessage)
        {
            // 仅在 Legacy 模式下拦截。FrameworkV2 模式返回 false，传递给 Chat handler。
            bool canHandle = lifecycle?.CompatibilityMode == FrameworkCompatibilityMode.Legacy
                   && !string.IsNullOrWhiteSpace(rawMessage);
            log?.Invoke(
                $"[LegacyAdapter] CanHandleOutgoingText: mode={lifecycle?.CompatibilityMode}, canHandle={canHandle}, textLen={rawMessage?.Length ?? 0}",
                LogLevel.DEBUG);
            return canHandle;
        }

        public ClientOutgoingMessageResult HandleOutgoingText(
            string rawMessage, ClientFrameworkContext context)
        {
            if (chatAdapter == null)
                return new ClientOutgoingMessageResult { Action = MessageHandlingResultAction.Continue };
            log?.Invoke(
                $"[LegacyAdapter] HandleOutgoingText: sending via legacy Chat protocol, textLen={rawMessage?.Length ?? 0}",
                LogLevel.DEBUG);

            // 旧版服务器不广播回发送者，需要本地注入回声消息
            displaySink?.Enqueue(new FrameworkDisplayMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SenderUuid = sessionContext?.Uuid ?? string.Empty,
                Text = rawMessage,
                TimestampUtcTicks = DateTime.UtcNow.Ticks,
                Source = "builtin_chat"
            });

            bool sent = chatAdapter.SendChatMessage(rawMessage);
            log?.Invoke(
                $"[LegacyAdapter] HandleOutgoingText: chatAdapter.SendChatMessage returned {sent}",
                LogLevel.DEBUG);

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

        // ========== IClientCommandHandler (Trade 入站) ==========

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return false; // Legacy Trade 入站不走 Framework pipeline
        }

        public ClientIncomingCommandResult HandleIncomingCommand(
            FrameworkPacket command, ClientFrameworkContext context)
        {
            return null;
        }

        // ========== IClientOutgoingCommandHandler (Trade 出站) ==========

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            var currentMode = lifecycle?.CompatibilityMode ?? FrameworkCompatibilityMode.Unknown;
            bool canHandle = currentMode == FrameworkCompatibilityMode.Legacy
                && tradeAdapter != null
                && tradeAdapter.CanHandleOutgoingCommand(command);

            log?.Invoke(
                $"[LegacyAdapter] BuiltInExtension.CanHandleOutgoingCommand: lifecycle={currentMode}, " +
                $"tradeAdapter={tradeAdapter != null}, msgType={command?.MessageType ?? "null"} → {canHandle}",
                LogLevel.DEBUG);

            return canHandle;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(
            FrameworkPacket command, ClientFrameworkContext context)
        {
            log?.Invoke(
                $"[LegacyAdapter] BuiltInExtension.HandleOutgoingCommand: msgType={command?.MessageType ?? "null"}",
                LogLevel.DEBUG);

            return tradeAdapter?.HandleOutgoingCommand(command, context)
                ?? new ClientOutgoingCommandResult { Action = MessageHandlingResultAction.Continue };
        }
    }
}
