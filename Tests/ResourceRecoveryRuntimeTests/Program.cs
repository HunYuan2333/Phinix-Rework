using System;
using System.Linq;
using System.Threading;
using PhinixClient.Framework;
using Utils.Framework;

internal sealed class Module:IPhinixExtensionModule
{public string ExtensionId=>"tests.resource-recovery";public void Register(IExtensionBuilder builder){}}
internal static class Program
{
    private static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    private static void Main()
    {
        var dispatcher=new ClientMainThreadDispatcher();var module=new Module();
        dispatcher.SetModuleTypes(new[]{typeof(Module)});dispatcher.OnActivating(module);
        using(var scope=dispatcher.ForModule(module))
        using(var canceled=new CancellationTokenSource())
        {
            int calls=0;
            var handles=Enumerable.Range(0,500).Select(_=>scope.Submit(()=>throw new Exception("Canceled action ran"),canceled.Token)).ToArray();
            canceled.Cancel();var accepted=scope.Submit(()=>calls++);
            Check(accepted.Capture().State==ClientTaskState.Queued&&dispatcher.PendingCount==1,"Canceled queue entries must release slots before admission");
            Check(handles.All(h=>h.Capture().State==ClientTaskState.Canceled)&&dispatcher.DroppedCount==0,"Reclamation is cancellation, not capacity rejection");
            dispatcher.DrainPendingActions();Check(calls==1,"New action executes after reclamation");
            for(int i=0;i<500;i++)scope.Submit(()=>calls++);
            Check(scope.Submit(()=>{}).Capture().State==ClientTaskState.Rejected,"Live full queue must still refuse new work");
            for(int i=0;i<5;i++)dispatcher.DrainPendingActions();
            Check(calls==501,"Reclamation preserves all accepted live work");
            scope.Submit(()=>throw new Exception("Stopped action ran"));
        }
        Check(dispatcher.PendingCount==0,"Scope disposal reclaims queued actions");
        dispatcher.OnStopped(module);
        Console.WriteLine("RESOURCE QUEUE PASS 6 checks; production dispatcher");
    }
}
