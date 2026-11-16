using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PhinixClient.Framework;
namespace Utils { public enum LogLevel { WARNING, ERROR } }
namespace Utils.Framework {
public interface IPhinixExtensionModule { string ExtensionId {get;} }
public interface IExtensionModuleLifecycleObserver { void OnActivating(IPhinixExtensionModule m); void OnStopped(IPhinixExtensionModule m); }
}
namespace PhinixClient.Framework {
public interface IClientMainThreadDispatcher { void Enqueue(Action a); }
public interface IClientDispatcherDiagnostics { int PendingCount {get;} long DroppedCount {get;} }
}
class Module:Utils.Framework.IPhinixExtensionModule { public string ExtensionId=>"probe.owner"; }
class Program {
[MethodImpl(MethodImplOptions.NoInlining)] static void OriginalCallback()=>throw new InvalidOperationException("probe-original-cause");
static void Check(bool x,string m) { if(!x)throw new Exception(m); Console.WriteLine("PASS: "+m); }
static void Main(){
var logs=new List<string>(); var d=new ClientMainThreadDispatcher((s,l)=>logs.Add(s));var m=new Module();d.SetModuleTypes(new[]{typeof(Module)});d.OnActivating(m);using var scope=d.ForModule(m);
var h=scope.Submit(OriginalCallback);int next=0;scope.Submit(()=>next++);d.DrainPendingActions();
Check(h.Capture().State==ClientTaskState.Failed && next==1,"failed task is isolated and next task runs");
Check(logs.Count==1 && logs[0].Contains("InvalidOperationException") && !logs[0].Contains("OriginalCallback") && !logs[0].Contains("probe-original-cause"),"production dispatcher loses original cause and stack");
Check(typeof(ClientTaskSnapshot).GetProperty("ErrorId")==null && typeof(ClientTaskSnapshot).GetProperty("Exception")==null,"task snapshot has no diagnostic reference or exception");
var broken=new ClientMainThreadDispatcher((s,l)=>throw new Exception("sink-failed"));broken.Enqueue(OriginalCallback);broken.Enqueue(()=>next++);broken.DrainPendingActions();Check(next==2,"broken log sink is isolated but diagnostics disappear");
}
}