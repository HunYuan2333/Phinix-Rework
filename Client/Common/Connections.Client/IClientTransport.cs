using System;
using Utils;
namespace Connections
{
    public interface IClientTransport : ILoggable, IDisposable
    {
        bool Connected { get; }
        string RemoteEndpoint { get; }
        event EventHandler OnConnecting;
        event EventHandler OnDisconnect;
        void Connect(string host, int port);
        void Disconnect();
        void Send(string module, byte[] payload);
        void RegisterPacketHandler(string module, NetCommon.PacketHandlerDelegate handler);
        void UnregisterPacketHandler(string module);
    }
}
