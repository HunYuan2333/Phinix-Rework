using System;
using System.Net;
using System.Reflection;
using System.Threading;
using Connections;
class Program
{
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static void Stop(NetClient client,string method)
 {
  try{typeof(NetClient).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(client,null);}
  catch(TargetInvocationException error){throw error.InnerException;}
 }
 static void Main()
 {
  ReceiveBudgetTests.Run();
  foreach(string name in new[]{"pollThread","probeThread"})
  {
   var client=new NetClient();using(var release=new ManualResetEventSlim())using(var entered=new ManualResetEventSlim())
   {
    var worker=new Thread(()=>{entered.Set();release.Wait();}){IsBackground=true};worker.Start();Check(entered.Wait(5000),"Worker entered");
    var field=typeof(NetClient).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(client,worker);
    string method=name=="pollThread"?"stopPollThread":"stopProbeThread";
    try
    {
     bool refused=false;try{Stop(client,method);}catch(TimeoutException){refused=true;}
     Check(refused,"Live worker stop timeout must remain explicit");Check(worker.IsAlive&&ReferenceEquals(field.GetValue(client),worker),"Timed-out worker remains owned");
    }
    finally{release.Set();Check(worker.Join(5000),"Synthetic worker exited");}
    Stop(client,method);Check(field.GetValue(client)==null,"Later stop confirms exit and releases reference");
    bool connectRefused=false,disposeRefused=false;
    using(var done=new ManualResetEventSlim())
    {
     var self=new Thread(()=>
     {
      try{client.Connect(new IPEndPoint(IPAddress.Loopback,16200));}catch(InvalidOperationException){connectRefused=true;}
      try{client.Dispose();}catch(InvalidOperationException){disposeRefused=true;}
      Stop(client,method);done.Set();
     }){IsBackground=true};field.SetValue(client,self);self.Start();Check(done.Wait(5000)&&self.Join(5000),"Self-stop returns without deadlock");
     Check(connectRefused&&disposeRefused,"Worker cannot restart or destroy its own transport");
     Check(ReferenceEquals(field.GetValue(client),self),"Self-stop retains owner until an external join");
     Stop(client,method);Check(field.GetValue(client)==null,"External caller reaps self-stopped worker");
    }
    client.Dispose();client.Dispose();
   }
  }
  Console.WriteLine("Client transport lifetime passed: "+checks+" assertions; real worker barriers and compiled client; lifecycle assertions use synthetic worker barriers.");
 }
}
