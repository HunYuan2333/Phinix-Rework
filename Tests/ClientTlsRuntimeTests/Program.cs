using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using Authentication;
using Connections;
using UserManagement;
using Utils.Framework;
using PhinixServer.Framework;

internal static class Program
{
    static int checks;
    static readonly CancellationTokenSource Deadline=new(TimeSpan.FromSeconds(75));
    static void Check(bool yes,string why){checks++;if(!yes)throw new Exception(why);}
    static async Task Wait(Func<bool> yes){while(!yes())await Task.Delay(10,Deadline.Token);}
    static X509Certificate2 Root()
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var request=new CertificateRequest("CN=synthetic-client-tls-ca",key,HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
    }
    static X509Certificate2 Leaf(X509Certificate2 root)
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var request=new CertificateRequest("CN=localhost",key,HashAlgorithmName.SHA256);
        var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        using var pub=request.Create(root,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1),RandomNumberGenerator.GetBytes(16));return pub.CopyWithPrivateKey(key);
    }
    static async Task Main()
    {
        string directory=Path.Combine(Path.GetTempPath(),"phinix-real-client-tls-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        using var root=Root();using var other=Root();using var cert=Leaf(root);
        string ca=Path.Combine(directory,"ca.pem"),badCa=Path.Combine(directory,"wrong-ca.pem");File.WriteAllText(ca,root.ExportCertificatePem());File.WriteAllText(badCa,other.ExportCertificatePem());
        try
        {
            using var server=new TlsNetServer(new IPEndPoint(IPAddress.Loopback,0),cert,4,2);
            var serverAuth=new ServerAuthenticator(server,"synthetic-tls-client","synthetic",AuthTypes.ClientKey);
            var serverUsers=new ServerUserManager(server,serverAuth);
            serverAuth.Load(Path.Combine(directory,"server-credentials"));serverUsers.Load(Path.Combine(directory,"users"));
            var context=new ExtensionHostContext{HostKind="server",StorageProvider=new FileSystemExtensionStorageProvider(Path.Combine(directory,"plugins"))};
            context.AddService<IServerUserManager>(serverUsers);
            var framework=new PhinixFrameworkServer(server,serverAuth,serverUsers,context);
            server.RegisterPacketHandler("echo",(module,id,bytes)=>server.Send(id,module,bytes));
            server.RegisterPacketHandler("broken",(module,id,bytes)=>server.Send(id,module,bytes));
            server.Start();
            try
            {
                int attempts=0;string name="localhost",pin=Convert.ToBase64String(SHA256.HashData(cert.PublicKey.ExportSubjectPublicKeyInfo())),rootFile=ca;
                using var client=new ConfiguredClientTransport((_,_)=>{Interlocked.Increment(ref attempts);return new TlsNetClient(name,pin,rootFile,X509RevocationMode.NoCheck);});
                using var auth=new ClientAuthenticator(client,(_,_,_,_,_)=>throw new Exception("Password prompt is forbidden in ClientKey mode"),Path.Combine(directory,"client-credentials"));
                var users=new ClientUserManager(client,auth);auth.OnAuthenticationSuccess+=(_,_)=>users.SendLogin("synthetic-tls-client");
                var echo=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                var broken=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                client.RegisterPacketHandler("broken",(_,_,_)=>{broken.TrySetResult();throw new InvalidOperationException("Synthetic unrelated module failure");});
                var capabilities=new TaskCompletionSource<FrameworkPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
                client.RegisterPacketHandler("echo",(_,_,body)=>echo.TrySetResult(body));
                client.RegisterPacketHandler(FrameworkProtocol.ModuleName,(_,_,body)=>capabilities.TrySetResult(FrameworkSerialization.DeserializePacket(body)));
                foreach(string invalid in new[]{"pin","name","ca"})
                {
                    string goodPin=pin;pin=invalid=="pin"?Convert.ToBase64String(new byte[32]):goodPin;name=invalid=="name"?"wrong.invalid":"localhost";rootFile=invalid=="ca"?badCa:ca;
                    int before=attempts;bool refused=false;
                    try {client.Connect("127.0.0.1",server.LocalPort);}catch(AuthenticationException){refused=true;}
                    Check(refused && !client.Connected && !auth.Authenticated && attempts==before+1,"Trust failure silently fell back or reached client authentication");
                    pin=goodPin;
                }
                var store=(CredentialStore)typeof(ServerAuthenticator).GetField("credentialStore",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(serverAuth)!;
                Check(store.Credentials.Count==0,"A client credential reached the server before server trust succeeded");
                name="localhost";rootFile=ca;client.Connect("127.0.0.1",server.LocalPort);await Wait(()=>users.LoggedIn);
                string principal=users.Uuid;Check(auth.Authenticated && client.RemoteEndpoint.StartsWith("tls://localhost:"),"Real client did not authenticate on trusted TLS");
                client.Send("echo",new byte[]{17,0,255});Check((await echo.Task.WaitAsync(Deadline.Token)).SequenceEqual(new byte[]{17,0,255}),"Client module transfer changed payload");
                client.Send("broken",Array.Empty<byte>());await broken.Task.WaitAsync(Deadline.Token);
                echo=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);client.Send("echo",new byte[]{42});
                await Wait(()=>!client.Connected || echo.Task.IsCompleted);
                Check(client.Connected && echo.Task.IsCompletedSuccessfully && echo.Task.Result.SequenceEqual(new byte[]{42}),"One plugin handler error disconnected unrelated modules");
                client.Send(FrameworkProtocol.ModuleName,FrameworkSerialization.SerializePacket(new FrameworkPacket{Kind=FrameworkProtocol.KindHello,Version=2,MessageId=Guid.NewGuid().ToString(),SessionId=auth.SessionId,SenderUuid=users.Uuid,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkCapabilitiesPayload{Capabilities=new List<string>()})}));
                Check((await capabilities.Task.WaitAsync(Deadline.Token)).Kind==FrameworkProtocol.KindCapabilities,"Real client transport did not reach Framework pipeline");
                // Both ends normally impose a 30-second whole-frame read deadline. Real keepalive must keep an idle session alive.
                await Task.Delay(TimeSpan.FromSeconds(32),Deadline.Token);Check(client.Connected && auth.Authenticated && users.LoggedIn,"Idle TLS keepalive did not preserve the real authenticated session");
                client.Disconnect();await Wait(()=>!users.LoggedIn && !auth.Authenticated);
                client.Connect("127.0.0.1",server.LocalPort);await Wait(()=>users.LoggedIn);
                Check(users.Uuid==principal && store.Credentials.Count==1,"Real TLS reconnect changed UUID or created another credential");
                client.Disconnect();await Wait(()=>!users.LoggedIn);client.Dispose();client.Dispose();
                bool unknown=false;try {ClientTransportFactory.Create("unknown","localhost",pin,ca);}catch(ArgumentException){unknown=true;}
                Check(unknown,"Unknown configured transport fell back to UDP");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,profile="actual client/server TLS; existing passwordless auth",idleSeconds=32,newIdentityMigration=false,gameAssetProof=false,secretsInOutput=false}));
            }
            finally {server.Stop();framework.ShutdownExtensions();serverAuth.Stop();}
        }
        finally {Directory.Delete(directory,true);}
    }
}
