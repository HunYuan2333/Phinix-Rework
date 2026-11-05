using System;
using System.Threading;
using PhinixClient.Framework;

namespace Phinix.TradeExtension.Client
{
    // One bounded recovery flow. The timer only submits work; game/ledger APIs
    // remain behind the host's module-owned main-thread task boundary.
    internal sealed class TradeRecoveryPump : IDisposable
    {
        private readonly object gate=new object();
        private readonly IClientTaskScope tasks;
        private readonly Action recover;
        private readonly Action<string> log;
        private Timer timer;
        private IClientTaskHandle pending;
        private Action inFlight,continuation;
        private bool requested,stopped,submissionFailureReported;

        internal TradeRecoveryPump(IClientTaskScope tasks,Action recover,Action<string> log)
        {this.tasks=tasks??throw new ArgumentNullException(nameof(tasks));this.recover=recover??throw new ArgumentNullException(nameof(recover));this.log=log;}
        internal void Start()
        {
            lock(gate)
            {if(stopped||timer!=null)return;timer=new Timer(_=>Pulse(),null,250,250);}
            Pulse();
        }
        internal void Request(){lock(gate){if(!stopped)requested=true;}}
        internal void Continue(Action next){lock(gate){if(!stopped)continuation=next??throw new ArgumentNullException(nameof(next));}}
        internal void Pulse()
        {
            string failure=null;
            lock(gate)
            {
                if(stopped)return;
                if(pending!=null)
                {
                    var state=pending.Capture();if(!state.Terminal)return;
                    pending=null;
                    if(state.State==ClientTaskState.Rejected&&state.Code=="QueueFull") { /* retain the exact page */ }
                    else
                    {
                        inFlight=null;
                        if(state.State==ClientTaskState.Failed)
                        {continuation=null;failure="Recovery callback failed; durable evidence retained.";}
                        else if(state.State==ClientTaskState.Canceled||state.State==ClientTaskState.Rejected)
                        {stopped=true;continuation=null;requested=false;timer?.Dispose();timer=null;return;}
                    }
                }
                if(inFlight==null)
                {
                    if(continuation!=null){inFlight=continuation;continuation=null;}
                    else if(requested){requested=false;inFlight=recover;}
                }
                if(inFlight!=null)
                {
                    Action work=inFlight;
                    try
                    {
                        pending=tasks.Submit(()=>{lock(gate){if(stopped)return;}work();});
                        submissionFailureReported=false;
                    }
                    catch(Exception error)
                    {
                        // Submission has not invoked business work. Keep this page,
                        // but report once until a submission succeeds.
                        if(!submissionFailureReported){submissionFailureReported=true;failure="Recovery scheduling failed: "+error.GetType().Name;}
                    }
                }
            }
            if(failure!=null){try{log?.Invoke(failure);}catch{/* diagnostics cannot escape the timer */}}
        }
        public void Dispose()
        {
            Timer owned;
            lock(gate)
            {stopped=true;owned=timer;timer=null;pending=null;inFlight=null;continuation=null;requested=false;}
            try{owned?.Dispose();}finally{tasks.Dispose();}
        }
    }
}
