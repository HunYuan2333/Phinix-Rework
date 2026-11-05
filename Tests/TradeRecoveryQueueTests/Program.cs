using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using Phinix.InventoryExtension;
using Phinix.TradeExtension;
using PhinixClient.Framework;
using Utils.Framework;

[PhinixExtension("test.recovery.queue")]
public sealed class RecoveryModule:IPhinixExtensionModule
{public string ExtensionId=>"test.recovery.queue";public void Register(IExtensionBuilder builder){}}
static class Program
{
 static int checks;
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly Assembly Trade=Assembly.Load("TradeExtension.Client");
 static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
 static object Call(object target,string method,params object[] args)
 {try{return target.GetType().GetMethod(method,Flags).Invoke(target,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static object New(Type type,params object[] args)=>Activator.CreateInstance(type,Flags,null,args,null);
 static object Field(object value,string name)=>value.GetType().GetField(name,Flags)?.GetValue(value);
 static object Host(out RecoveryModule module)
 {
  int main=Thread.CurrentThread.ManagedThreadId;
  object host=New(Assembly.Load("PhinixClient").GetType("PhinixClient.Framework.ClientMainThreadDispatcher",true),null,(Func<bool>)(()=>Thread.CurrentThread.ManagedThreadId==main));
  module=new RecoveryModule();Call(host,"SetModuleTypes",new object[]{new[]{typeof(RecoveryModule)}});Call(host,"OnActivating",module);return host;
 }
 static int Pending(object host)=>(int)host.GetType().GetProperty("PendingCount").GetValue(host);
 static void Fill(object host){while(Pending(host)<500)((IClientMainThreadDispatcher)host).Enqueue(()=>{});}
 static void Drain(object host){for(int i=0;i<6;i++)Call(host,"DrainPendingActions");}
 static object Pump(object host,RecoveryModule module,Action work,Action<string> log=null)=>New(Trade.GetType("Phinix.TradeExtension.Client.TradeRecoveryPump",true),((IClientTaskService)host).ForModule(module),work,log);
 static void Pulse(object coordinator)
 {object pump=Field(coordinator,"pump");if(pump!=null)Call(pump,"Pulse");else Call(coordinator,"Schedule");}
 static void CheckPump()
 {
  object host=Host(out var module);int calls=0,pages=0,main=Thread.CurrentThread.ManagedThreadId;object pump=null;
  pump=Pump(host,module,()=>{Check(Thread.CurrentThread.ManagedThreadId==main,"Recovery business runs on main thread");calls++;Call(pump,"Continue",(Action)(()=>pages++));});
  Fill(host);Call(pump,"Request");for(int i=0;i<20;i++)Call(pump,"Pulse");
  Check(calls==0&&Pending(host)==500,"Rejected recovery never executes or evicts other work");
  Drain(host);for(int i=0;i<20;i++)Call(pump,"Pulse");Check(Pending(host)==1,"Queue rejection recovers to one coalesced task");Drain(host);
  Check(calls==1&&pages==0,"Only one bounded root page executes");
  Fill(host);Call(pump,"Pulse");Drain(host);Call(pump,"Pulse");Drain(host);
  Check(pages==1&&calls==1,"Rejected continuation resumes original page rather than restarting or losing it");
  for(int i=0;i<20;i++)Call(pump,"Pulse");Check(Pending(host)==0,"Completed sweep does not poll business repeatedly");
  var workers=Enumerable.Range(0,8).Select(_=>new Thread(()=>{for(int n=0;n<100;n++)Call(pump,"Request");})).ToArray();
  foreach(var w in workers)w.Start();foreach(var w in workers)Check(w.Join(5000),"Concurrent notifier finished");
  Call(pump,"Pulse");Check(Pending(host)==1,"Concurrent notifications merge into one sweep");Drain(host);Call(pump,"Pulse");Drain(host);Call(pump,"Pulse");
  Check(calls==2&&pages==2&&Pending(host)==0,"Concurrent notifications preserve bounded continuation");
  Call(pump,"Request");Call(pump,"Pulse");((IDisposable)pump).Dispose();((IDisposable)pump).Dispose();
  Check(Pending(host)==0,"Stop removes queued owned recovery immediately");Call(pump,"Request");Call(pump,"Pulse");Drain(host);Check(calls==2,"Late notification cannot revive stopped recovery");
  int failures=0,logs=0;var failed=Pump(host,module,()=>{failures++;throw new IOException("synthetic recovery failure");},_=>logs++);
  Call(failed,"Request");Call(failed,"Pulse");Drain(host);for(int i=0;i<10;i++)Call(failed,"Pulse");
  Check(failures==1&&logs==1,"Failed business callback is reported once without automatic replay");
  Call(failed,"Request");Call(failed,"Pulse");Drain(host);Call(failed,"Pulse");Check(failures==2,"Explicit subsequent recovery notification remains possible");((IDisposable)failed).Dispose();
  var canceled=Pump(host,module,()=>calls++);Call(canceled,"Request");Call(canceled,"Pulse");Call(host,"OnStopped",module);Call(canceled,"Pulse");Call(canceled,"Request");Call(canceled,"Pulse");Drain(host);
  Check(calls==2&&Pending(host)==0,"Module cancellation closes recovery rather than retrying an expired owner");((IDisposable)canceled).Dispose();
  Call(host,"OnActivating",module);int timedCalls=0;var timed=Pump(host,module,()=>timedCalls++);
  try
  {
   Call(timed,"Request");Call(timed,"Start");Drain(host); // First page submitted at Start.
   Call(timed,"Request");
   Check(SpinWait.SpinUntil(()=>Pending(host)>0,3000),"Owned timer submits a later notification without UI interaction");
   Check(timedCalls==1,"Timer submits but does not execute business on its worker");Drain(host);Check(timedCalls==2,"Timer submission executes through the real main-thread pump");
  }
  finally{((IDisposable)timed).Dispose();Call(host,"OnStopped",module);}
 }
 static void CheckCoordinator(bool delivery)
 {
  string root=Path.Combine(Path.GetTempPath(),"phinix-recovery-queue-"+Guid.NewGuid().ToString("N"));
  object host=Host(out var module);var read=new ReadBoundary();object coordinator=null;
  try
  {
   object journal=New(Trade.GetType("Phinix.TradeExtension.Client."+(delivery?"TradeCompletionInbox":"TradeIntentJournal"),true),root);
   for(int n=0;n<17;n++)
   {
    if(delivery)Call(journal,"Record","server","owner","trade-"+n,true,"other",Array.Empty<FrameworkItemPayload>());
    else Call(journal,"Prepare","server","owner",new FrameworkPacket{MessageId="operation-"+n.ToString("D2"),MessageType=FrameworkTradeProtocol.CreateRequestType,PayloadJson=FrameworkSerialization.SerializePayload(new FrameworkTradeCreateRequest{OtherPartyUuid="other"})},Array.Empty<FrameworkPacket>());
   }
   Fill(host);
   var type=Trade.GetType("Phinix.TradeExtension.Client."+(delivery?"TradeDeliveryRecovery":"TradeIntentSettlement"),true);
   // Historical negative control uses its actual legacy dispatcher constructor;
   // both versions must satisfy the same eventual recovery expectation.
   bool modern=type.GetConstructors(Flags).Single().GetParameters().Any(p=>p.ParameterType==typeof(IClientTaskScope));
   object schedule=modern?(object)((IClientTaskService)host).ForModule(module):(IClientMainThreadDispatcher)host;
   coordinator=delivery?New(type,journal,read,null,null,schedule,null,(Func<string>)(()=>"server"),(Func<string>)(()=>"owner"),(Action<string>)(_=>{}))
    :New(type,journal,read,null,schedule,(Func<string>)(()=>"server"),(Func<string>)(()=>"owner"),null,(Action<string>)(_=>{}));
   Pulse(coordinator);Check(read.Reads==0,"Full queue did not run recovery inline");Drain(host);
   for(int i=0;i<4;i++){Pulse(coordinator);Drain(host);}
   Check(read.Reads==3,(delivery?"Delivery":"Intent")+" recovery progresses through all three real-file pages after queue rejection");
   read.Raise();Pulse(coordinator);Drain(host);int before=read.Reads;
   Fill(host);Pulse(coordinator);Drain(host);Pulse(coordinator);Drain(host);Pulse(coordinator);Drain(host);
   Check(read.Reads==before+2,"Actual coordinator retains middle and final pages across queue rejection");
   ((IDisposable)coordinator).Dispose();coordinator=null;
   Check(read.Subscribers==0,"Coordinator detaches borrowed inventory subscriptions");
   before=read.Reads;read.Raise();Drain(host);Check(read.Reads==before&&Pending(host)==0,"Disposed coordinator owns no queued callback");
  }
  finally{((IDisposable)coordinator)?.Dispose();Call(host,"OnStopped",module);if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 sealed class ReadBoundary:IInventoryReadApi
 {
  public int Reads;private EventHandler availability,changed;
  public int Subscribers=>(availability?.GetInvocationList().Length??0)+(changed?.GetInvocationList().Length??0);
  public event EventHandler AvailabilityChanged{add{availability+=value;}remove{availability-=value;}}
  public event EventHandler InventoryChanged{add{changed+=value;}remove{changed-=value;}}
  public void Raise()=>availability?.Invoke(this,EventArgs.Empty);
  public InventoryStatus GetStatus(){Reads++;return new InventoryStatus{Availability=InventoryAvailability.Ready,SaveIdentity=new string('a',64),JournalIdentity=new string('b',32)};}
  public IReadOnlyList<InventoryEntry> GetSnapshot()=>throw new NotSupportedException("No asset simulation");
  public InventoryCapabilities GetCapabilities()=>throw new NotSupportedException("No asset simulation");
 }
 static void Main(string[] args)
 {
  if(args.Contains("--old")){CheckCoordinator(args.Contains("--delivery"));return;}
  CheckPump();CheckCoordinator(false);CheckCoordinator(true);
  Console.WriteLine("Recovery queue passed: "+checks+" checks; real dispatcher, actual recovery coordinators, isolated real evidence; no game assets.");
 }
}
