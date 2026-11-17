using System;
using System.Collections.Generic;
using Utils;
namespace Connections
{
    // Select exactly one transport per explicit connection. Configuration/trust errors never select another.
    public sealed class ConfiguredClientTransport : IClientTransport, IClientRelayTransport
#if PHINIX_RIMWORLD_17
        , IValidatedClientTransport
#endif
    {
        private readonly object gate = new object(), lifecycle = new object();
        private readonly Dictionary<string, NetCommon.PacketHandlerDelegate> handlers = new Dictionary<string, NetCommon.PacketHandlerDelegate>();
        private readonly Func<string,int,IClientTransport> create;
        private IClientTransport active;
        private bool disposed;
        private readonly ClientConnectionDiagnostics diagnostics;
        public ConfiguredClientTransport(Func<string,int,IClientTransport> create) : this(create, null) { }
        public ConfiguredClientTransport(Func<string,int,IClientTransport> create, Action<string,Exception> report)
        { this.create = create ?? throw new ArgumentNullException(nameof(create)); diagnostics = new ClientConnectionDiagnostics(report); }
#if PHINIX_RIMWORLD_17
        public bool TryGetValidatedTrust(out ValidatedClientTrust trust)
        { lock(gate) { trust = null; return active is IValidatedClientTransport secure && secure.TryGetValidatedTrust(out trust); } }
#endif
        public bool Connected { get { lock(gate) return active != null && active.Connected; } }
        public string RemoteEndpoint { get { lock(gate) return active?.RemoteEndpoint; } }
        public event EventHandler OnConnecting;
        public event EventHandler OnDisconnect;
        public event EventHandler<LogEventArgs> OnLogEntry;
        public void RaiseLogEntry(LogEventArgs args) => OnLogEntry?.Invoke(this,args);
        private void Connecting(object sender,EventArgs args) => OnConnecting?.Invoke(this,args);
        private void Disconnected(object sender,EventArgs args)
        {
            var callbacks=OnDisconnect;
            if(callbacks!=null)foreach(EventHandler callback in callbacks.GetInvocationList())
                try {callback(this,args);}catch(Exception error){diagnostics.Report("TransportDisconnectObserverFailed",error);}
        }
        private void Log(object sender,LogEventArgs args) => RaiseLogEntry(args);
        public void RegisterPacketHandler(string module,NetCommon.PacketHandlerDelegate handler)
        { lock(gate) { if(handlers.ContainsKey(module)) throw new PacketHandlerAlreadyRegisteredException(module); active?.RegisterPacketHandler(module,handler);handlers.Add(module,handler); } }
        public void UnregisterPacketHandler(string module) { lock(gate) { active?.UnregisterPacketHandler(module);handlers.Remove(module); } }
        public void Connect(string host,int port)
        {
            lock(lifecycle)
            {
                if(disposed)throw new ObjectDisposedException(nameof(ConfiguredClientTransport));
                Release();
                IClientTransport next;
                try {next=create(host,port);} // No active connection or credentials yet.
                catch(Exception error){diagnostics.Report("TransportCreationFailed",error);throw;}
                try
                {
                    lock(gate)
                    {
                        foreach(var pair in handlers)next.RegisterPacketHandler(pair.Key,pair.Value);
                        next.OnConnecting+=Connecting;next.OnDisconnect+=Disconnected;next.OnLogEntry+=Log;active=next;
                    }
                }
                catch (Exception error) {
                    diagnostics.Report("TransportBindingFailed", error);
                    try { next.Dispose(); } catch (Exception cleanup) { diagnostics.Report("TransportBindingCleanupFailed", cleanup); }
                    throw;
                }
                try { next.Connect(host,port); }
                catch (Exception error) {
                    diagnostics.Report("TransportConnectFailed", error);
                    try { Release(); } catch (Exception cleanup) { diagnostics.Report("TransportConnectCleanupFailed", cleanup); }
                    throw;
                }
            }
        }
        // Safe from a packet callback: request disconnect without joining that callback's own worker.
        public void Disconnect() { IClientTransport selected;lock(gate)selected=active;selected?.Disconnect(); }
        public void Send(string module,byte[] payload)
        { IClientTransport selected;lock(gate)selected=active;if(selected==null)throw new NotConnectedException();selected.Send(module,payload); }
        public bool TrySendRelay(string module,byte[] payload)
        { IClientTransport selected;lock(gate)selected=active;return selected is IClientRelayTransport relay && relay.TrySendRelay(module,payload); }
        private void Release()
        {
            try {ReleaseCore();}
            catch(Exception error){diagnostics.Report("TransportReleaseFailed",error);throw;}
        }
        private void ReleaseCore()
        {
            IClientTransport old;lock(gate)old=active;
            if(old==null)return;
            old.Disconnect();old.Dispose(); // Keep ownership on timeout; never replace an unterminated transport.
            lock(gate) { old.OnConnecting-=Connecting;old.OnDisconnect-=Disconnected;old.OnLogEntry-=Log;if(ReferenceEquals(active,old))active=null; }
        }
        public void Dispose() { lock(lifecycle) { Release();disposed=true; } }
    }
    public static class ClientTransportFactory
    {
        public static IClientTransport Create(string mode,string serverName,string pin,string rootCertificateFile)
            => Create(mode, serverName, pin, rootCertificateFile, null);
        public static IClientTransport Create(string mode,string serverName,string pin,string rootCertificateFile,Action<string,Exception> report)
        {
            if(mode=="udp")return new NetClient(report);
            if(mode!="tls")throw new ArgumentException("Unknown configured transport mode.");
#if PHINIX_RIMWORLD_17
            return new TlsNetClient(report,serverName,pin,rootCertificateFile);
#else
            throw new PlatformNotSupportedException("Configured TLS transport requires the supported .NET 10 client runtime.");
#endif
        }
    }
}
