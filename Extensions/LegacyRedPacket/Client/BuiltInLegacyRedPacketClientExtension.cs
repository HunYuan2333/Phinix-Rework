using System;
using PhinixClient;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;

namespace Phinix.LegacyRedPacketExtension.Client
{
    /// <summary>
    /// 红包插件入口（Rework 插件化：IPhinixExtensionModule）。
    ///
    /// 设计哲学 §1.1 插件平权：与 Chat/Trade 同级注册；依赖 Trade（物品快照/转换器/掉落语义）。
    /// 设计哲学 §1.2 host 不依赖插件：本插件只引用 ClientExtensionAbstractions 与 Trade 契约。
    /// 老方案兼容：协议 v1 + HTTP 中继（RedPacketRelay）与老 submod 客户端互通。
    /// </summary>
    [PhinixExtension("builtin.legacy-redpacket", DependsOn = new[] { "builtin.trade" })]
    public sealed class BuiltInLegacyRedPacketClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        private ExtensionHostContext hostContext;
        private IClientSessionContext session;
        private IClientUserDirectory userDirectory;
        private IClientMainThreadDispatcher mainThreadDispatcher;
        private IClientSettingsContext settingsContext;
        private IFrameworkClientTransport transport;
        private IClientUserEventStream userEventStream;

        private RedPacketSettings settings;
        private RedPacketRelay relay;
        private RedPacketStateMachine stateMachine;
        private RedPacketTab tab;
        private RedPacketUnreadBadge unreadBadge;
        private RedPacketSettingsPanel settingsPanel;

        private EventHandler disconnectedHandler;

        public string ExtensionId => "builtin.legacy-redpacket";

        public void Register(IExtensionBuilder builder)
        {
            hostContext = builder.HostContext;
            session = builder.HostContext.GetRequiredService<IClientSessionContext>();
            userDirectory = builder.HostContext.GetRequiredService<IClientUserDirectory>();
            mainThreadDispatcher = builder.HostContext.GetRequiredService<IClientMainThreadDispatcher>();
            settingsContext = builder.HostContext.GetRequiredService<IClientSettingsContext>();
            transport = builder.HostContext.GetRequiredService<IFrameworkClientTransport>();
            userEventStream = builder.HostContext.GetRequiredService<IClientUserEventStream>();

            settings = new RedPacketSettings();
            settings.Load(settingsContext);
            relay = new RedPacketRelay(builder.HostContext.Log);
            stateMachine = new RedPacketStateMachine(
                session,
                userDirectory,
                mainThreadDispatcher,
                settingsContext,
                settings,
                relay,
                builder.HostContext.Log);
            tab = new RedPacketTab(session, userDirectory, settingsContext, settings, transport, stateMachine);
            unreadBadge = new RedPacketUnreadBadge(session, stateMachine);
            settingsPanel = new RedPacketSettingsPanel(settingsContext, settings);

            builder.RegisterApi<IMainTabProvider>(tab);
            builder.RegisterApi<IBadgeProvider>(unreadBadge);
            builder.RegisterApi<IClientSettingsPanelProvider>(settingsPanel);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            this.hostContext = hostContext;

            disconnectedHandler = (sender, args) => stateMachine?.Clear();
            if (userEventStream != null)
            {
                userEventStream.Disconnected += disconnectedHandler;
            }

            stateMachine?.Initialize();
            hostContext.Log?.Invoke("[RedPacket] Red packet extension activated (v1 protocol + relay, legacy compatible).", LogLevel.INFO);
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (userEventStream != null && disconnectedHandler != null)
            {
                userEventStream.Disconnected -= disconnectedHandler;
            }
            disconnectedHandler = null;

            stateMachine?.Shutdown();
            hostContext.Log?.Invoke("[RedPacket] Red packet extension shut down.", LogLevel.INFO);
        }
    }
}
