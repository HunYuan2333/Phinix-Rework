using System;
using System.IO;
using Connections;
internal static partial class Program
{
    private static void CheckTransportSelection()
    {
        bool refused=false;
        try {ClientTransportFactory.Create("unrecognized","localhost","","");}catch(ArgumentException){refused=true;}
        Assert(refused,"Unknown transport configuration must never select UDP.");
        bool modern=typeof(NetClient).Assembly.GetType("Connections.TlsNetClient")!=null;
        Exception failure=null;try {ClientTransportFactory.Create("tls","localhost","","");}catch(Exception error){failure=error;}
        Assert(modern?failure is ArgumentException:failure is PlatformNotSupportedException,
            "Invalid TLS trust or unsupported runtime must fail before creating a network transport.");
        int attempts=0;
        using(var selected=new ConfiguredClientTransport((host,port)=>{attempts++;throw new IOException("Synthetic explicit transport configuration rejection");}))
        {
            selected.RegisterPacketHandler("test",(module,id,body)=>{});
            try {selected.Connect("synthetic.invalid",1);}catch(IOException){}
            Assert(attempts==1 && !selected.Connected,"One selected transport failure must not trigger fallback/retry.");
            bool notSent=false;try {selected.Send("test",new byte[]{1});}catch(NotConnectedException){notSent=true;}
            Assert(notSent,"Unconnected configured transport cannot claim ownership of a packet.");
            selected.Disconnect();selected.Dispose();selected.Dispose();
            bool disposed=false;try {selected.Connect("synthetic.invalid",1);}catch(ObjectDisposedException){disposed=true;}
            Assert(disposed && attempts==1,"Disposed router cannot create an unowned new transport.");
        }
    }
}
