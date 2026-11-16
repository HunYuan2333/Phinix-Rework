using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Authentication;
using Connections;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;

internal sealed class ManagementFaultService : IManagedExtensionManagementService
{
    public ManagedExtensionRuntimeSnapshot Snapshot => null;
    internal Func<CancellationToken,ManagedExtensionManagementSnapshot> Read;
    internal ManagedExtensionStateChangeResult ChangeResult;
    internal int Changes;
    public ManagedExtensionManagementSnapshot Refresh(IEnumerable<string> disabled,CancellationToken token) => Read(token);
    public ManagedExtensionStateChangeResult ChangeDesiredState(ManagedExtensionPackageSnapshot expected,ManagedExtensionDesiredState desired,IEnumerable<string> disabled,CancellationToken token)
    { Changes++; return ChangeResult; }
}
public static partial class Program
{
    private static void ControlBoundaries(string directory)
    {
        var assembly=Assembly.LoadFrom(System.IO.Path.Combine(directory,"PhinixClient.dll"));
        var type=assembly.GetType("PhinixClient.Framework.PhinixFrameworkClient",true);
        var client=FormatterServices.GetUninitializedObject(type); var errors=Errors();
        var failures=new List<Exception>();
        Action<string,object> set=(key,value)=>type.GetField(key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(client,value);
        set("parseDiagnostics",new ClientConnectionDiagnostics((operation,error)=>{failures.Add(error);errors.ReportSensitiveBoundary("framework",operation,error,false);}));
        set("extensionLogLock",new object());set("extensionLog",new List<FrameworkLogEntry>());
        set("displayMessagesLock",new object());set("displayMessages",new List<FrameworkDisplayMessage>());
        set("remoteCapabilities",new HashSet<string>());
        var context=new ExtensionHostContext();set("extensionHostContext",context);
        var runtimeType=assembly.GetType("PhinixClient.Framework.ClientExtensionRuntime",true);
        set("extensionRuntime",Activator.CreateInstance(runtimeType,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{context,new Func<bool>(()=>true)},null));
        var auth=(ClientAuthenticator)FormatterServices.GetUninitializedObject(typeof(ClientAuthenticator));
        typeof(ClientAuthenticator).GetProperty("SessionId").GetSetMethod(true).Invoke(auth,new object[]{"test-session"});
        typeof(ClientAuthenticator).GetProperty("Authenticated").GetSetMethod(true).Invoke(auth,new object[]{true});set("authenticator",auth);
        var admissionType=assembly.GetType("PhinixClient.Framework.FrameworkAdmissionControlState",true);
        Func<object> begin=()=>{
            var admission=Activator.CreateInstance(admissionType);admissionType.GetMethod("Begin").Invoke(admission,new object[]{"test-session","hello"});
            set("admissionControl",admission);return admission;
        };
        var method=type.GetMethod("packetHandler",BindingFlags.NonPublic|BindingFlags.Instance);
        using(var timer=new System.Timers.Timer()) {
            set("negotiationTimer",timer);
            method.Invoke(client,new object[]{"framework","test",new byte[]{255}});
            Check(failures.Count==1 && failures[0].StackTrace.Contains("packetHandler"),"malformed packet preserves original parser frames and returns through rejection boundary");
            var admission=begin();
            var packet=new FrameworkPacket {Kind=FrameworkProtocol.KindCapabilities,Version=FrameworkProtocol.Version,SenderUuid=FrameworkProtocol.SystemSenderUuid,SessionId="test-session",PayloadJson="{secret-payload"};packet.SetCorrelationId("hello");
            method.Invoke(client,new object[]{"framework","test",FrameworkSerialization.SerializePacket(packet)});
            Check(failures.Count==2 && (bool)admissionType.GetProperty("NegotiationRejected").GetValue(admission,null),"malformed capability payload retains cause and rejects negotiation without protocol fallback");
            admission=begin();var rejectionType=assembly.GetType("PhinixClient.Framework.FrameworkAdmissionRejection",true);
            packet.Kind=(string)rejectionType.GetField("PacketKind").GetRawConstantValue();packet.MessageType=(string)rejectionType.GetField("PacketType").GetRawConstantValue();
            method.Invoke(client,new object[]{"framework","test",FrameworkSerialization.SerializePacket(packet)});
            Check(failures.Count==3 && (bool)admissionType.GetProperty("NegotiationRejected").GetValue(admission,null),"malformed admission payload retains parser cause and original rejection state");
            Check(!errors.HasPending && errors.Capture().All(r=>errors.BuildReport(r.Id).Contains("Level: WARNING")),"bad-packet diagnostics are retained as warnings without automatically opening a popup");
            Check(errors.Capture().All(r=>!errors.BuildReport(r.Id).Contains("secret-payload")),"copyable parse diagnostics omit untrusted payload text");
        }
        ManagementBoundaries(assembly);
    }
    private static void ManagementBoundaries(Assembly assembly)
    {
        var type=assembly.GetType("PhinixClient.Framework.ManagedExtensionManagerController",true);
        var fault=ConnectionFault();var service=new ManagementFaultService();
        Func<Action<Exception>,object> create=report=>Activator.CreateInstance(type,new object[]{report});
        Action<object> refresh=controller=>type.GetMethod("Refresh").Invoke(controller,new object[]{service,new string[0]});
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var observed=new ManualResetEventSlim()) {
            Exception saved=null;service.Read=token=>{entered.Set();release.Wait();System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();return null;};
            var controller=create(error=>{saved=error;observed.Set();});refresh(controller);Check(entered.Wait(3000),"management operation starts on worker");
            var task=(Task)type.GetField("task",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            ((IDisposable)controller).Dispose();release.Set();
            Check(task.Wait(3000) && observed.Wait(3000) && ReferenceEquals(saved,fault),"disposed management view still records original background failure before any Poll");
        }
        service.Read=token=>{throw new OperationCanceledException();};Exception unexpected=null;
        var live=create(error=>unexpected=error);refresh(live);
        var pending=(Task)type.GetField("task",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(live);
        Check(pending.Wait(3000) && (bool)type.GetMethod("Poll").Invoke(live,null) && unexpected is OperationCanceledException,"unrequested cancellation remains an observable failure");
        ((IDisposable)live).Dispose();
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
            int reported=0;service.Read=token=>{entered.Set();release.Wait();token.ThrowIfCancellationRequested();return null;};
            var controller=create(error=>Interlocked.Increment(ref reported));refresh(controller);Check(entered.Wait(3000),"cancel test enters worker");
            var task=(Task)type.GetField("task",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            ((IDisposable)controller).Dispose();release.Set();Check(task.Wait(3000) && reported==0,"requested management cancellation completes quietly without false error notification");
        }
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()) {
            Exception cancelFailure=null;service.Read=token=>{using(token.Register(()=>{throw new InvalidOperationException("cancel observer");})) {entered.Set();release.Wait();token.ThrowIfCancellationRequested();return null;}};
            var controller=create(error=>cancelFailure=error);refresh(controller);Check(entered.Wait(3000),"cancellation callback failure test enters worker");
            var task=(Task)type.GetField("task",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            ((IDisposable)controller).Dispose();release.Set();
            Check(task.Wait(3000) && cancelFailure is AggregateException && !(bool)type.GetProperty("Busy").GetValue(controller,null),"cancellation callback failure is recorded without interrupting controller disposal");
        }
        service.Read=token=>{System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();return null;};
        service.ChangeResult=(ManagedExtensionStateChangeResult)Activator.CreateInstance(typeof(ManagedExtensionStateChangeResult),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{true,"Saved",null,"test"},null);
        var changed=create(error=>{});var expected=(ManagedExtensionPackageSnapshot)FormatterServices.GetUninitializedObject(typeof(ManagedExtensionPackageSnapshot));
        type.GetMethod("Change").Invoke(changed,new object[]{service,expected,ManagedExtensionDesiredState.Unknown,new string[0]});
        var complete=(Task)type.GetField("task",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(changed);Check(complete.Wait(3000),"changed management operation completes");type.GetMethod("Poll").Invoke(changed,null);
        Check((string)type.GetProperty("MessageCode").GetValue(changed,null)=="ManagedStateSavedRefreshFailed" && service.Changes==1 && ReferenceEquals(type.GetProperty("LastChange").GetValue(changed,null),service.ChangeResult),"refresh failure after state save preserves saved result without retry or rollback");
        ((IDisposable)changed).Dispose();
        service.Read=token=>(ManagedExtensionManagementSnapshot)Activator.CreateInstance(typeof(ManagedExtensionManagementSnapshot),BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{new ManagedExtensionManagementPackage[0],new string[0]},null);
        var controlsType=assembly.GetType("PhinixClient.Framework.ClientExtensionControlService",true);bool moduleSaved=false;Exception savedRefresh=null;
        var installation=(IManagedExtensionInstallationService)FormatterServices.GetUninitializedObject(typeof(ManagedExtensionRuntime));
        var controls=Activator.CreateInstance(controlsType,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{service,installation,new Func<bool>(()=>true),new Func<int>(()=>0),new Func<IEnumerable<string>>(()=>new string[0]),new Action<string,bool>((id,disabled)=>moduleSaved=true),new Func<IEnumerable<string>>(()=>{if(moduleSaved)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();return new string[0];}),new Func<IEnumerable<string>>(()=>new string[0]),new Action<Exception>(error=>savedRefresh=error)},null);
        controlsType.GetMethod("Refresh").Invoke(controls,new object[]{new string[0],CancellationToken.None});
        var snapshot=controlsType.GetMethod("Capture").Invoke(controls,null);long revision=(long)snapshot.GetType().GetProperty("Revision").GetValue(snapshot,null);
        var result=controlsType.GetMethod("SetModuleEnabled").Invoke(controls,new object[]{"test.module",false,revision});
        Check(moduleSaved && (bool)result.GetType().GetProperty("Succeeded").GetValue(result,null) && (string)result.GetType().GetProperty("Code").GetValue(result,null)=="ManagedStateSavedRefreshFailed" && ReferenceEquals(savedRefresh,fault),"module setting save remains successful when later projection fails, with original diagnostic and no rollback");
        ((IDisposable)controls).Dispose();

    }
}
