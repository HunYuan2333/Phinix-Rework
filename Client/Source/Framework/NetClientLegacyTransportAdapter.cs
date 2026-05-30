using System;
using Connections;
using PhinixClient.Framework;

namespace PhinixClient.Framework
{
    /// <summary>
    /// 将 NetClient 的原始模块通信能力包装为 ILegacyModuleTransport，
    /// 使插件可以通过通用平台接口操作 NetClient 的 Send / RegisterPacketHandler / UnregisterPacketHandler。
    /// </summary>
    internal sealed class NetClientLegacyTransportAdapter : ILegacyModuleTransport
    {
        private readonly NetClient netClient;

        public NetClientLegacyTransportAdapter(NetClient netClient)
        {
            this.netClient = netClient ?? throw new ArgumentNullException(nameof(netClient));
        }

        public void Send(string moduleName, byte[] data)
            => netClient.Send(moduleName, data);

        public void RegisterHandler(string moduleName, RawPacketHandlerDelegate handler)
        {
            // RawPacketHandlerDelegate 与 NetCommon.PacketHandlerDelegate 签名一致，
            // 用 lambda 桥接即可
            netClient.RegisterPacketHandler(moduleName, (m, c, d) => handler(m, c, d));
        }

        public void UnregisterHandler(string moduleName)
            => netClient.UnregisterPacketHandler(moduleName);
    }
}
