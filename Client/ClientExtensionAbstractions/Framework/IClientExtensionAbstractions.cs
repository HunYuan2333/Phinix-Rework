using System;
using System.Collections.Generic;
using UserManagement;
using Utils.Framework;
using Verse;

namespace PhinixClient.Framework
{
    public interface IFrameworkClientTransport
    {
        bool HasRemoteCapability(string capability);

        void SendFrameworkPacket(FrameworkPacket packet);

        /// <summary>
        /// 将原始文本消息通过完整 handler 管线路由，而非直连底层传输。
        /// 设计哲学 §3.7：所有出站通信必须走 handler 管线，保证 Priority 排序、拦截、替换、回退机制正常工作。
        /// </summary>
        /// <returns>true 表示消息已被某个 handler 处理并发送；false 表示无 handler 处理该消息</returns>
        bool TryHandleOutgoingMessage(string rawMessage);
    }

    public interface IFrameworkClientCommandTransport
    {
        /// <summary>
        /// 出站命令管线。按 Priority 顺序遍历所有 IClientOutgoingCommandHandler，
        /// 首个 CanHandleOutgoingCommand 返回 true 的 handler 处理该命令。
        /// 设计哲学 §3.7：插件不得绕过管线直连传输层。
        /// </summary>
        /// <returns>true 表示命令已被某个 handler 处理（可能已发送或翻译后发送）；false 表示无 handler 处理该命令</returns>
        bool TryHandleOutgoingCommand(FrameworkPacket command);
    }

    public interface IClientDisplayMessageStore
    {
        int UnreadMessages { get; }

        void MarkAsRead();

        FrameworkDisplayMessage[] GetUnreadDisplayMessages(bool markAsRead = true);

        FrameworkDisplayMessage[] GetDisplayMessages();
    }

    public interface IClientDisplayMessageFeed
    {
        event EventHandler<FrameworkDisplayMessageEventArgs> DisplayMessageReceived;
    }

    public sealed class FrameworkCompatibilityModeChangedEventArgs : EventArgs
    {
        public FrameworkCompatibilityModeChangedEventArgs(FrameworkCompatibilityMode compatibilityMode)
        {
            CompatibilityMode = compatibilityMode;
        }

        public FrameworkCompatibilityMode CompatibilityMode { get; }
    }

    public interface IFrameworkClientLifecycle
    {
        FrameworkCompatibilityMode CompatibilityMode { get; }

        event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;
    }

    public interface IClientSessionContext
    {
        bool Authenticated { get; }

        bool LoggedIn { get; }

        string SessionId { get; }

        string Uuid { get; }
    }

    public interface IClientSettingsContext
    {
        T Get<T>(string key, T defaultValue = default);

        void Set<T>(string key, T value);

        IEnumerable<string> BlockedUsers { get; }

        bool PlayNoiseOnMessageReceived { get; }

        bool CollapseBlockedUsers { get; set; }

        void BlockUser(string uuid);

        void UnBlockUser(string uuid);

        event Action<string, object> OnSettingChanged;
    }

    public interface IClientUserDirectory
    {
        string Uuid { get; }

        ImmutableUser[] GetUsers(bool loggedIn = false);

        bool TryGetUser(string uuid, out ImmutableUser user);
    }

    public interface IClientUserEventStream
    {
        event EventHandler Disconnected;

        event EventHandler UsersChanged;

        event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;

        event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
    }

    public interface IClientMainThreadDispatcher
    {
        void Enqueue(Action action);
    }

    public interface IClientWindowService
    {
        void Open(Window window);

        void OpenSettingsWindow();
    }

    public interface IClientSoundService
    {
        void Enqueue(SoundDef soundDef);
    }

    /// <summary>
    /// 原始模块数据包处理器委托 —— 与 Connections.NetCommon.PacketHandlerDelegate 签名兼容，
    /// 但定义在此以避免 ClientExtensionAbstractions 直接依赖 Connections。
    /// </summary>
    /// <param name="module">目标模块名</param>
    /// <param name="connectionId">来源连接 ID</param>
    /// <param name="data">原始数据字节</param>
    public delegate void RawPacketHandlerDelegate(string module, string connectionId, byte[] data);

    /// <summary>
    /// 给需要直接操作 NetClient 原始模块通信的插件使用。
    /// 任何插件都能用此接口发送/接收任意模块的原始字节数据。
    /// </summary>
    public interface ILegacyModuleTransport
    {
        /// <summary>向指定模块发送原始字节（对应 NetClient.Send）</summary>
        void Send(string moduleName, byte[] data);

        /// <summary>注册原始模块的数据包处理器（对应 NetClient.RegisterPacketHandler）</summary>
        void RegisterHandler(string moduleName, RawPacketHandlerDelegate handler);

        /// <summary>取消注册原始模块的数据包处理器（对应 NetClient.UnregisterPacketHandler）</summary>
        void UnregisterHandler(string moduleName);
    }

    /// <summary>
    /// 给需要向消息显示系统注入 FrameworkDisplayMessage 的插件使用。
    /// 任何插件都能用此接口推送消息到统一的消息队列。
    /// </summary>
    public interface IDisplayMessageSink
    {
        /// <summary>将一条显示消息注入到框架消息队列中</summary>
        void Enqueue(FrameworkDisplayMessage message);
    }

    /// <summary>
    /// 插件化设置面板提供者。host 设置窗口通过此接口动态收集各插件的设置 UI，
    /// 不再硬编码 Chat/Trade 等业务设置项。
    /// 设计哲学 §1.3：host 只做通用服务，业务设置由业务插件自行声明。
    /// </summary>
    public interface IClientSettingsPanelProvider
    {
        /// <summary>设置分组标识，用于在设置窗口中排序和分组。推荐格式 "plugin.category"（如 "chat.display"）。</summary>
        string SectionId { get; }

        /// <summary>显示顺序，数值越小越靠前。host 核心设置在 0-100，插件设置在 100+。</summary>
        float Order { get; }

        /// <summary>绘制设置面板内容。host 提供当前 listing 以保持布局连续，以及 <see cref="IClientSettingsContext"/> 供读写。</summary>
        void DrawSettings(Verse.Listing_Standard listing, IClientSettingsContext settings);

        /// <summary>当前是否应该显示此设置组。可用于根据兼容模式等条件隐藏设置。</summary>
        bool IsVisible(IClientSettingsContext settings);
    }
}
