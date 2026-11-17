using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Connections;
using Utils;
using Utils.Framework;
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Origin(){throw new InvalidOperationException("synthetic private payload");}
    private static Exception Error(){try{Origin();return null;}catch(Exception e){return e;}}
    private static void Main()
    {
        var caps=new ClientCommandCapabilities();using(var declaration=caps.Declare("sample.owner","sample.plugin","sample.command"))
        {
            var reports=new List<Exception>();bool fail=false;FrameworkPacket snapshot=null;var original=Error();
            using(var relay=new ClientCommandRelay(caps,p=>{if(p.Kind==CommandRelayProtocol.SnapshotKind){snapshot=p;return true;}if(fail)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();return true;},null,(op,e)=>reports.Add(e)))
            {
                relay.Begin("epoch-one");var offered=FrameworkSerialization.DeserializePayload<CommandCapabilitySnapshot>(snapshot.PayloadJson);
                relay.Acknowledge(new CommandCapabilityAck{Epoch=offered.Epoch,Revision=offered.Revision});
                Check(relay.SyncState==CapabilitySyncState.Synced,"capabilities synchronize through actual serialized snapshot/ACK");
                fail=true;var unknown=relay.SendRelay("sample.plugin","sample.command","{}").GetAwaiter().GetResult();
                Check(unknown.Error=="Unknown"&&reports.Any(e=>ReferenceEquals(e,original)&&e.StackTrace.Contains("Origin")),"dispatch exception stays Unknown and retains original instance/frames");
                fail=false;var pending=relay.SendRelay("sample.plugin","sample.command","{}");relay.Disconnect();
                Check(pending.GetAwaiter().GetResult().Error=="Unknown","disconnect never fabricates rejection or success for admitted request");
                relay.Begin("epoch-two");relay.Acknowledge(new CommandCapabilityAck{Epoch="epoch-one",Revision=offered.Revision});
                Check(relay.SyncState==CapabilitySyncState.Pending,"old epoch ACK cannot revive old declarations");
            }
            using(var relay=new ClientCommandRelay(caps,p=>{System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();return false;},null,(op,e)=>{throw new Exception("broken reporter");}))
            {relay.Begin("diagnostic-failure");Check(relay.SyncError=="NotSent","broken observer cannot replace original capability-send outcome");}
        }
        var plain=new Plain();using(var selected=new ConfiguredClientTransport((h,p)=>plain))
        {selected.Connect("synthetic",1);Check(!selected.TrySendRelay("FrameworkV2",new byte[1])&&plain.Sends==0,"unsupported relay admission never falls back to ordinary Send");}
        var bounded=new Bounded();using(var selected=new ConfiguredClientTransport((h,p)=>bounded))
        {selected.Connect("synthetic",1);Check(!selected.TrySendRelay("FrameworkV2",new byte[1])&&bounded.Attempts==1&&bounded.Sends==0,"configured transport respects rejection at bounded port");bounded.Admit=true;Check(selected.TrySendRelay("FrameworkV2",new byte[1])&&bounded.Attempts==2&&bounded.Sends==0,"configured transport forwards one admitted relay through bounded port only");}
        TlsAdmission();
        Console.WriteLine("RELAY INTEGRATION PASS "+checks+" checks; original causes, Unknown, epochs and configured admission");
    }
    private static void TlsAdmission()
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;
        var client=new TlsNetClient("synthetic.example",Convert.ToBase64String(new byte[32]));
        var type=typeof(TlsNetClient).GetNestedType("Lifetime",System.Reflection.BindingFlags.NonPublic);var socket=new System.Net.Sockets.TcpClient();
        var state=Activator.CreateInstance(type,flags,null,new object[]{socket,null,"synthetic"},null);var current=typeof(TlsNetClient).GetField("current",flags);current.SetValue(client,state);
        var queue=(System.Threading.Channels.Channel<byte[]>)type.GetField("Outgoing",flags).GetValue(state);var bytes=type.GetField("Bytes",flags);
        try
        {
            Check(!client.TrySendRelay("FrameworkV2",new byte[65537]),"TLS rejects oversized relay before queue admission");
            for(int i=0;i<16;i++)Check(client.TrySendRelay("FrameworkV2",new byte[1]),"TLS admits within actual bounded channel");
            Check(!client.TrySendRelay("FrameworkV2",new byte[1]),"TLS frame watermark rejects without ordinary Send fallback");
            while(queue.Reader.TryRead(out var ignored)){}bytes.SetValue(state,4*1024*1024);
            Check(!client.TrySendRelay("FrameworkV2",new byte[1]),"TLS byte watermark independently refuses admission");
            bytes.SetValue(state,0);Check(client.TrySendRelay("FrameworkV2",new byte[1]),"TLS admission recovers after actual queue drain");
            type.GetField("Closed",flags).SetValue(state,true);Check(!client.TrySendRelay("FrameworkV2",new byte[1]),"closed TLS generation never accepts relay");
        }
        finally{current.SetValue(client,null);socket.Dispose();((IDisposable)type.GetField("Stop",flags).GetValue(state)).Dispose();client.Dispose();}
    }
    private class Plain : IClientTransport
    {
        public int Sends;public bool Connected{get;private set;}public string RemoteEndpoint=>"synthetic";
        public event EventHandler OnConnecting;public event EventHandler OnDisconnect;public event EventHandler<LogEventArgs> OnLogEntry;
        public void Connect(string h,int p){Connected=true;OnConnecting?.Invoke(this,EventArgs.Empty);}public void Disconnect(){Connected=false;OnDisconnect?.Invoke(this,EventArgs.Empty);}public void Dispose(){Disconnect();}
        public void Send(string m,byte[] b){Sends++;}public void RaiseLogEntry(LogEventArgs e){OnLogEntry?.Invoke(this,e);}
        public void RegisterPacketHandler(string m,NetCommon.PacketHandlerDelegate h){}public void UnregisterPacketHandler(string m){}
    }
    private sealed class Bounded:Plain,IClientRelayTransport
    {public bool Admit;public int Attempts;public bool TrySendRelay(string m,byte[] b){Attempts++;return Admit;}}
}
