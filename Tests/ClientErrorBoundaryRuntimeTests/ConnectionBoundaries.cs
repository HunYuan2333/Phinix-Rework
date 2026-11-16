using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Authentication;
using Connections;

internal sealed class UserLookupProbe : UserManagement.UserManager
{
    protected override UserManagement.UserStore userStore {get;set;} = new UserManagement.UserStore();
    protected override object userStoreLock {get;} = new object();
    public override event EventHandler<Utils.LogEventArgs> OnLogEntry;
    public override void RaiseLogEntry(Utils.LogEventArgs args) { }
    internal void Add(string id,string name){userStore.Users.Add(id,new UserManagement.User {Uuid=id,Username=name});}
}
public static partial class Program
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConnectionFaultOrigin() { throw new IOException("network-secret=never-copy-this"); }
    private static Exception ConnectionFault()
    { try { ConnectionFaultOrigin(); return null; } catch(Exception error) { return error; } }
    private static void ConnectionBoundaries()
    {
        var users=new UserLookupProbe();string userId;
        Check(!users.TryGetUserUuid("missing",out userId) && userId==null,"absent username remains a normal false lookup result");
        users.Add("first","same");Check(users.TryGetUserUuid("same",out userId) && userId=="first","unique username still resolves through the shared lookup");
        users.Add("second","same");Check(!users.TryGetUserUuid("same",out userId) && userId==null,"ambiguous username remains a normal false lookup result without catch-based fault suppression");
        var records = new List<Tuple<string,Exception>>();
        Action<string,Exception> report = (operation,error) => records.Add(Tuple.Create(operation,error));
        var original = ConnectionFault();
        Check(ReferenceEquals(new InvalidAddressException("test",original).InnerException,original),"DNS address wrapper preserves original resolution cause for upstream callers");
        new ClientConnectionDiagnostics(report).Report("test", original);
        Check(ReferenceEquals(records.Single().Item2,original) && original.StackTrace.Contains("ConnectionFaultOrigin"),"transport diagnostics passes original instance and cause stack to Host");
        var writer = new StringWriter(); var listener = new TextWriterTraceListener(writer);
        Trace.Listeners.Add(listener);
        try { new ClientConnectionDiagnostics((op,error) => { throw new Exception("observer-secret"); }).Report("test",original); Trace.Flush(); }
        finally { Trace.Listeners.Remove(listener); listener.Dispose(); }
        string fallback = writer.ToString();
        Check(fallback.Contains("ConnectionFaultOrigin") && !fallback.Contains("never-copy-this") && !fallback.Contains("observer-secret"),"diagnostic observer failure preserves frames via fallback without leaking arbitrary messages");
        var errors = Errors(); string id = errors.ReportSensitiveBoundary("transport","test",original);
        Check(errors.BuildReport(id).Contains("ConnectionFaultOrigin") && !errors.BuildReport(id).Contains("never-copy-this"),"transport original reaches copyable Host report under sensitive-message policy");

        records.Clear(); Exception creation = null;
        var invalidConfiguration = new ConfiguredClientTransport((host,port)=>{System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();return null;}, report);
        try { invalidConfiguration.Connect("test",1); } catch(Exception error) { creation=error; }
        Check(ReferenceEquals(creation,original) && records.Any(r=>r.Item1=="TransportCreationFailed" && ReferenceEquals(r.Item2,original)),"transport configuration failure is captured before a connection exists and never falls back");
        invalidConfiguration.Dispose();
        records.Clear(); var fake = new SendTransport { ConnectFailure = original, DisposeFailure = new Exception("cleanup-secret") };
        var selected = new ConfiguredClientTransport((host,port) => fake, report);
        Exception caught = null; try { selected.Connect("test",1); } catch(Exception error) { caught=error; }
        Check(ReferenceEquals(caught,original) && caught.StackTrace.Contains("ConnectionFaultOrigin"),"configured transport cleanup cannot replace original connect exception");
        Check(records.Any(r => r.Item1=="TransportConnectFailed" && ReferenceEquals(r.Item2,original)) && records.Any(r => r.Item1=="TransportConnectCleanupFailed" && ReferenceEquals(r.Item2,fake.DisposeFailure)),"primary and cleanup failures are retained separately");
        fake.DisposeFailure=null; selected.Dispose();
        records.Clear(); fake = new SendTransport { RegistrationFailure=original, DisposeFailure=new Exception("cleanup") };
        selected = new ConfiguredClientTransport((host,port) => fake, report); selected.RegisterPacketHandler("test",(module,peer,data)=>{});
        caught=null; try { selected.Connect("test",1); } catch(Exception error) { caught=error; }
        Check(ReferenceEquals(caught,original) && records.Any(r=>r.Item1=="TransportBindingCleanupFailed"),"binding rollback retains primary even if new transport disposal fails");
        selected.Dispose();
        records.Clear(); fake = new SendTransport(); selected = new ConfiguredClientTransport((host,port)=>fake, report);
        int disconnectObserver=0; selected.OnDisconnect += (sender,args)=>{throw new Exception("observer");}; selected.OnDisconnect += (sender,args)=>disconnectObserver++;
        selected.Connect("test",1); fake.RaiseDisconnect();
        Check(disconnectObserver==1 && records.Any(r=>r.Item1=="TransportDisconnectObserverFailed"),"configured disconnect observer failure does not skip healthy cleanup subscribers");
        selected.Dispose();
        records.Clear(); fake = new SendTransport { RegistrationFailure=original };
        caught=null; try {new ClientAuthenticator(fake,(session,name,description,kind,callback)=>{},"unused",true,report);}catch(Exception error){caught=error;}
        Check(ReferenceEquals(caught,original) && records.Any(r=>r.Item1=="AuthenticationBindingFailed"),"authentication binding retains primary exception through constructor cleanup");

        records.Clear();using(var incoming=new NetClient(report)) {
            var receive=typeof(NetCommon).GetMethod("packetHandlerCallbackWrapper",BindingFlags.NonPublic|BindingFlags.Instance);
            var arguments=receive.GetParameters();var peer=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(arguments[0].ParameterType);
            var reader=Activator.CreateInstance(arguments[1].ParameterType);
            receive.Invoke(incoming,new object[]{peer,reader});
            Check(records.Any(r=>r.Item1=="PacketEnvelopeReadFailed" && !string.IsNullOrEmpty(r.Item2.StackTrace)),"shared UDP envelope parse fault reaches original sensitive client diagnostic hook");
        }
        records.Clear(); using(var udp = new NetClient(report))
        {
            var type=typeof(NetClient);
            Exception escaped=null;
            var worker=new Thread(()=>{
                try {
                    type.GetField("pollThread",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(udp,Thread.CurrentThread);
                    type.GetMethod("RunWorker",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(udp,new object[]{"test-worker",new Action(()=>System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw())});
                } catch(Exception error) { escaped=error; }
            });
            worker.Start(); Check(worker.Join(3000) && escaped==null,"actual UDP worker boundary observes failure and completes without self-join");
            // An invalid owned thread simulates cleanup failure without opening a socket.
            type.GetField("probeThread",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(udp,new Thread(()=>{}));
            type.GetMethod("RunWorker",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(udp,new object[]{"test-worker-cleanup",new Action(()=>System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw())});
            type.GetField("probeThread",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(udp,null);
            Check(records.Any(r=>r.Item1=="test-worker" && ReferenceEquals(r.Item2,original)) && records.Any(r=>r.Item1=="ClientWorkerDisconnectFailed"),"UDP worker records primary before independent cleanup failure");
            Check((bool)type.GetField("pollThreadStopRequested",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(udp) && !udp.Connected,"failed UDP worker requests termination and disconnects");
        }
        records.Clear(); fake=new SendTransport { Failure=original, DisconnectFailure=new Exception("disconnect") };
        using(var auth=new ClientAuthenticator(fake,(session,name,description,kind,callback)=>{},Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()),true,report))
        {
            var type=typeof(ClientAuthenticator);
            type.GetProperty("Authenticated").GetSetMethod(true).Invoke(auth,new object[]{true});
            type.GetProperty("SessionId").GetSetMethod(true).Invoke(auth,new object[]{"synthetic-session"});
            type.GetMethod("RenewSession",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(auth,null);
            Check(records.Any(r=>r.Item1=="SessionRenewalFailed" && ReferenceEquals(r.Item2,original)) && records.Any(r=>r.Item1=="SessionRenewalDisconnectFailed"),"legacy session timer observes send and cleanup failures separately");
            Check(!auth.Authenticated && fake.DisconnectCalls==1,"failed session renewal revokes local authentication and requests disconnect once");
        }
        // Modern identity/TLS only exist in the 1.7 build; reflection keeps this suite dual-runtime.
        var configure=typeof(ClientAuthenticator).GetMethod("ConfigureModernIdentity");
        if(configure==null)return;
        records.Clear(); fake=new SendTransport { DisconnectFailure=new Exception("disconnect") };
        using(var auth=new ClientAuthenticator(fake,(session,name,description,kind,callback)=>{},Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()),true,report))
        {
            auth.OnAuthenticationFailure += (sender,args) => { throw new ApplicationException("observer"); };
            configure.Invoke(auth,new object[]{new Func<bool>(()=>true),Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())});
            var identity=typeof(ClientAuthenticator).GetField("modernIdentity",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(auth);
            var handler=identity.GetType().GetMethod("Handle",BindingFlags.NonPublic|BindingFlags.Instance);
            handler.Invoke(identity,new object[]{"identity","test",new byte[]{255}});
            Check(records.Any(r=>r.Item1=="IdentityResponseFailed" && r.Item2.StackTrace.Contains("Handle")),"modern identity response fault retains original stack before friendly refusal");
            Check(records.Any(r=>r.Item1=="IdentityFailureObserverFailed") && records.Any(r=>r.Item1=="IdentityDisconnectFailed") && fake.DisconnectCalls==1,"identity observer failure cannot prevent disconnect or erase primary diagnostics");
        }
        records.Clear(); fake=new SendTransport();
        using(var auth=new ClientAuthenticator(fake,(session,name,description,kind,callback)=>{},Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()),true,report))
        {
            configure.Invoke(auth,new object[]{new Func<bool>(()=>{System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();return false;}),"unused"});
            var identity=typeof(ClientAuthenticator).GetField("modernIdentity",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(auth);
            identity.GetType().GetMethod("Tick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(identity,new object[]{true});
            Check(records.Any(r=>r.Item1=="IdentityRenewalFailed" && ReferenceEquals(r.Item2,original)) && fake.DisconnectCalls==1,"actual identity timer entry catches failures before renewal logic and terminates safely");
        }
        var tls=typeof(NetClient).Assembly.GetType("Connections.TlsNetClient",true);
        caught=null;try{Activator.CreateInstance(tls,new object[]{"localhost","!",null,System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck});}catch(TargetInvocationException error){caught=error.InnerException;}
        Check(caught is ArgumentException && caught.InnerException is FormatException,"TLS invalid pin wrapper retains original FormatException cause");
        var lifetime=tls.GetNestedType("Lifetime",BindingFlags.NonPublic);
        {
            var state=lifetime.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)[0].Invoke(new object[]{null,null,"test"});
            var stop=(CancellationTokenSource)lifetime.GetField("Stop",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(state);
            using(stop){var client=Activator.CreateInstance(tls,new object[]{"localhost",Convert.ToBase64String(new byte[32]),null,System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck});
                var expected=tls.GetMethod("ExpectedCancellation",BindingFlags.NonPublic|BindingFlags.Instance);
                Check(!(bool)expected.Invoke(client,new object[]{state,new OperationCanceledException(new CancellationToken(true))}),"active TLS frame deadline is a fault rather than normal shutdown");
                using(var linked=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)) {
                    stop.Cancel();
                    Check((bool)expected.Invoke(client,new object[]{state,new OperationCanceledException(linked.Token)}) && !(bool)expected.Invoke(client,new object[]{state,new OperationCanceledException()}),"shutdown recognizes channel linked cancellation but does not discard non-canceled faults");
                }
                ((IDisposable)client).Dispose();
            }
        }
    }
}
