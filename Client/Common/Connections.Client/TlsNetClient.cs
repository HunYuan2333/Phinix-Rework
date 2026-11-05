// EXPERIMENTAL: TCP/TLS and modern identity are not recommended for deployment.
// Production identity migration is deferred (TODO); UDP/ClientKey remains the default.
#if PHINIX_RIMWORLD_17
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ServerTransport;
using Utils;
namespace Connections
{
    public sealed class TlsNetClient : IClientTransport, IValidatedClientTransport
    {
        private readonly object gate=new(),lifecycle=new();
        private readonly Dictionary<string,NetCommon.PacketHandlerDelegate> handlers=new();
        private readonly string serverName,rootCertificateFile;
        private readonly byte[] pin;
        private readonly X509RevocationMode revocation;
        private Lifetime current;
        private bool disposed;
        [ThreadStatic] private static bool dispatching;
        private sealed class Lifetime(TcpClient socket,TlsPacketChannel channel,string endpoint)
        {
            internal readonly TcpClient Socket=socket;
            internal readonly TlsPacketChannel Channel=channel;
            internal readonly string Endpoint=endpoint;
            internal readonly CancellationTokenSource Stop=new();
            internal readonly Channel<byte[]> Outgoing=System.Threading.Channels.Channel.CreateBounded<byte[]>(16);
            internal readonly TaskCompletionSource Start=new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task Runner;
            internal Task Cancellation=Task.CompletedTask;
            internal int Bytes;
            internal bool Closed;
        }
        private const int Maximum=1024*1024,QueueBytes=4*1024*1024;
        public TlsNetClient(string serverName,string base64Pin,string rootCertificateFile=null,X509RevocationMode revocation=X509RevocationMode.Online)
        {
            if(string.IsNullOrWhiteSpace(serverName)||serverName.Length>253)throw new ArgumentException("Trusted TLS server name required.");
            try {pin=Convert.FromBase64String(base64Pin);}catch(FormatException){throw new ArgumentException("Trusted TLS SPKI SHA-256 pin is invalid.");}
            if(pin.Length!=32)throw new ArgumentException("Trusted TLS SPKI SHA-256 pin is invalid.");
            this.serverName=serverName;this.rootCertificateFile=rootCertificateFile;this.revocation=revocation;
        }
        public bool TryGetValidatedTrust(out ValidatedClientTrust trust)
        { lock(gate) { trust = current != null && !current.Closed ? new ValidatedClientTrust(serverName, pin) : null; return trust != null; } }
        public bool Connected {get {lock(gate)return current!=null&&!current.Closed;}}
        public string RemoteEndpoint {get {lock(gate)return current!=null&&!current.Closed?current.Endpoint:null;}}
        public event EventHandler OnConnecting;
        public event EventHandler OnDisconnect;
        public event EventHandler<LogEventArgs> OnLogEntry;
        public void RaiseLogEntry(LogEventArgs args)=>OnLogEntry?.Invoke(this,args);
        private void Report(Exception error){try {RaiseLogEntry(new LogEventArgs("TLS connection failed: "+error.GetType().Name,LogLevel.WARNING));}catch {}}
        public void RegisterPacketHandler(string module,NetCommon.PacketHandlerDelegate handler)
        {lock(gate){if(module.StartsWith("$transport.",StringComparison.Ordinal))throw new ArgumentException("Reserved transport module.");if(handlers.ContainsKey(module))throw new PacketHandlerAlreadyRegisteredException(module);handlers.Add(module,handler);}}
        public void UnregisterPacketHandler(string module){lock(gate)handlers.Remove(module);}
        public void Connect(string host,int port)
        {
            if(dispatching)throw new InvalidOperationException("Connect must run outside a packet callback.");
            lock(lifecycle)
            {
                if(disposed)throw new ObjectDisposedException(nameof(TlsNetClient));
                CloseAndWait();
                using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var socket=new TcpClient();TlsPacketChannel channel=null;
                try
                {
                    X509Certificate2 root=null;
                    try
                    {
                        if(!string.IsNullOrEmpty(rootCertificateFile))
                        {
                            if(new FileInfo(rootCertificateFile).Length>64*1024)throw new InvalidDataException("Trusted CA file exceeds limit.");
                            root=X509Certificate2.CreateFromPem(File.ReadAllText(rootCertificateFile));
                        }
                        socket.ConnectAsync(host,port,deadline.Token).AsTask().GetAwaiter().GetResult();
                        channel=TlsPacketChannel.ConnectAsync(socket.GetStream(),serverName,pin,root,
                            revocation:revocation,maximum:Maximum,cancellation:deadline.Token).GetAwaiter().GetResult();
                    }
                    finally {root?.Dispose();}
                    var state=new Lifetime(socket,channel,"tls://"+serverName+":"+port);
                    lock(gate){current=state;state.Runner=Run(state);}
                    try {OnConnecting?.Invoke(this,EventArgs.Empty);}
                    finally {state.Start.TrySetResult();}
                }
                catch {channel?.DisposeAsync().GetAwaiter().GetResult();socket.Dispose();throw;}
            }
        }
        private async Task Write(Lifetime state)
        {
            try
            {
                await foreach(byte[] body in state.Outgoing.Reader.ReadAllAsync(state.Stop.Token))
                {try {await state.Channel.WriteAsync(body,state.Stop.Token);}finally {lock(gate)state.Bytes-=body.Length;}}
            }
            finally {await state.Channel.DisposeAsync();}
        }
        private async Task Ping(Lifetime state)
        {
            try {while(true){await Task.Delay(TimeSpan.FromSeconds(5),state.Stop.Token);Send("$transport.ping",Array.Empty<byte>());}}
            finally {await state.Channel.DisposeAsync();}
        }
        private async Task Run(Lifetime state)
        {
            await state.Start.Task;
            var writing=Write(state);var pinging=Ping(state);
            try
            {
                while(true)
                {
                    byte[] body=await state.Channel.ReadAsync(state.Stop.Token);if(body==null)break;
                    var packet=TlsModulePacket.Decode(body);
                    if(packet.Module=="$transport.pong")
                    {if(packet.Payload.Length!=0)throw new InvalidDataException("Invalid transport pong.");continue;}
                    NetCommon.PacketHandlerDelegate handler;lock(gate)handlers.TryGetValue(packet.Module,out handler);
                    if(handler!=null)
                    {
                        dispatching=true;
                        try {handler(packet.Module,"tls-server",packet.Payload);}
                        catch(Exception error){Report(error);} // A consumed frame's plugin error must not corrupt unrelated modules.
                        finally {dispatching=false;}
                    }
                }
            }
            catch(Exception)when(state.Stop.IsCancellationRequested){ }
            catch(Exception error){Report(error);}
            finally
            {
                RequestStop(state);
                try {await Task.WhenAll(writing,pinging);}catch(Exception)when(state.Stop.IsCancellationRequested){ }
                dispatching=true;try {OnDisconnect?.Invoke(this,EventArgs.Empty);}catch(Exception error){Report(error);}finally {dispatching=false;}
                try {await state.Cancellation;}finally {state.Stop.Dispose();}
            }
        }
        private void RequestStop(Lifetime state)
        {
            lock(gate)
            {
                if(state.Closed)return;state.Closed=true;
                state.Cancellation=state.Stop.CancelAsync();state.Outgoing.Writer.TryComplete();state.Socket.Dispose();
            }
        }
        public void Disconnect(){Lifetime state;lock(gate)state=current;if(state!=null)RequestStop(state);}
        private void CloseAndWait()
        {
            Lifetime state;lock(gate)state=current;if(state==null)return;
            RequestStop(state);state.Runner.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            lock(gate){if(ReferenceEquals(current,state))current=null;}
        }
        public void Send(string module,byte[] payload)
        {
            byte[] body=TlsModulePacket.Encode(module,payload,Maximum);
            lock(gate)
            {
                var state=current;if(state==null||state.Closed)throw new NotConnectedException();
                if(state.Bytes>QueueBytes-body.Length||!state.Outgoing.Writer.TryWrite(body))throw new IOException("TLS send queue is full; packet was not admitted.");
                state.Bytes+=body.Length;
            }
        }
        public void Dispose(){if(dispatching)throw new InvalidOperationException("Dispose must run outside a packet callback.");lock(lifecycle){CloseAndWait();disposed=true;}}
    }
}
#endif
