using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PhinixClient.Trade;

namespace Phinix.TradeExtension.Client
{
    internal enum TradeOfferSettlement { Done, Blocked, Uncertain }
    // Business callbacks own escrow, never the view. Exactly one operation per trade until resolved.
    internal sealed class TradeOfferOperations : ITradeOfferOperationApi, IDisposable
    {
        private sealed class Pending
        {
            internal string TradeId,Token,Message;
            internal bool Reset,Attempted,Settling,MarkedUnknown,FinishedBeforeResult,AbortedBeforeSend;
            internal bool? Accepted;
            internal volatile bool Sending;
            internal int SendingThread,Forwarded;
            internal TradeOfferOperationState State;
            internal Func<TradeOfferSettlement> Commit,Restore,Abort;
            internal Action MarkUnknown;
            internal Func<string> Error;
        }
        private readonly IClientTradeService service;
        private readonly Action<Action> dispatch;
        private readonly Action<string> log;
        private readonly Func<bool> mainThread;
        private readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>(StringComparer.Ordinal);
        private bool active;
        private int generation;
        private long revision;
        private Pending[] portLeases=Array.Empty<Pending>();
        internal TradeOfferOperations(IClientTradeService service,Action<Action> dispatch,Action<string> log,Func<bool> mainThread)
        {this.service=service;this.dispatch=dispatch;this.log=log;this.mainThread=mainThread;}
        internal void Start()
        {
            if(active)return;active=true;generation++;
            service.OnTradeUpdateSuccess+=Updated;service.OnTradeUpdateFailure+=Updated;
            service.OnTradeCompleted+=Finished;service.OnTradeCancelled+=Finished;
        }
        private void Touch(){revision++;Volatile.Write(ref portLeases,pending.Values.ToArray());}
        // Atomic admission at the shared business port; it does not touch game data.
        internal bool CanSubmit(string tradeId,string token,bool offer)
        {
            var leases=Volatile.Read(ref portLeases).Where(p=>string.Equals(p.TradeId,tradeId,StringComparison.OrdinalIgnoreCase)).ToArray();
            return leases.Length==0||offer&&leases.Any(p=>p.Sending&&p.SendingThread==Thread.CurrentThread.ManagedThreadId&&string.Equals(p.Token,token,StringComparison.Ordinal)&&Interlocked.CompareExchange(ref p.Forwarded,1,0)==0);
        }
        private void MainThread(){if(!mainThread())throw new InvalidOperationException("Trade offer operations require main thread.");}
        public long Revision {get {MainThread();return revision;}}
        public IReadOnlyList<TradeOfferOperationInfo> GetPending()
        {MainThread();return pending.Values.Select(p=>new TradeOfferOperationInfo(p.TradeId,p.Token,p.Reset,p.State,p.Message,active&&p.Accepted.HasValue&&p.State==TradeOfferOperationState.LocalSettlementPending&&!p.Settling)).ToArray();}
        internal bool IsBusy(string tradeId){MainThread();return pending.Values.Any(p=>string.Equals(p.TradeId,tradeId,StringComparison.OrdinalIgnoreCase));}
        internal string Begin(string tradeId,bool reset,Func<TradeOfferSettlement> commit,Func<TradeOfferSettlement> restore,Action markUnknown,Func<TradeOfferSettlement> abort=null,Func<string> errorSource=null)
        {
            MainThread();if(!active||string.IsNullOrWhiteSpace(tradeId)||commit==null||restore==null)throw new InvalidOperationException("TradeOfferUnavailable");
            if(IsBusy(tradeId))throw new InvalidOperationException("TradeOfferAlreadyPending");
            if(pending.Count>=128)throw new InvalidOperationException("TradeOfferOperationLimit");
            var p=new Pending {TradeId=tradeId,Token=Guid.NewGuid().ToString(),Reset=reset,Commit=commit,Restore=restore,Abort=abort,Error=errorSource,MarkUnknown=markUnknown,State=TradeOfferOperationState.Preparing};
            pending.Add(p.Token,p);Touch();return p.Token;
        }
        internal bool Submit(string token,Action<string> send)
        {
            MainThread();if(!active||!pending.TryGetValue(token,out var p)||p.Attempted||p.State!=TradeOfferOperationState.Preparing)return false;
            p.Attempted=true;p.State=TradeOfferOperationState.AwaitingResult;Touch();
            p.SendingThread=Thread.CurrentThread.ManagedThreadId;p.Sending=true;
            try {send(token);return true;}
            catch(Exception error)
            {
                if(pending.ContainsKey(token)&&!p.Accepted.HasValue)Unknown(p,"TradeOfferSendUncertain: "+error.Message);
                else log?.Invoke("TradeOfferSendThrewAfterKnownResult: "+error.Message);
                return false;
            }
            finally {p.Sending=false;}
        }
        internal void AbortBeforeSend(string token)
        {
            MainThread();if(!pending.TryGetValue(token,out var p)||p.Attempted)return;
            p.Accepted=false;p.AbortedBeforeSend=true;p.Message="TradeOfferPreparationAborted";Settle(p);
        }
        private void Updated(object sender,TradeUpdateEventArgs args)
        {
            if(!active||args?.Trade==null||string.IsNullOrEmpty(args.Token))return;
            int ticket=generation;dispatch(()=>{if(active&&ticket==generation)Apply(args);});
        }
        private void Apply(TradeUpdateEventArgs args)
        {
            MainThread();if(!pending.TryGetValue(args.Token,out var p)||!p.Attempted||p.Accepted.HasValue||
                !string.Equals(p.TradeId,args.Trade.TradeId,StringComparison.OrdinalIgnoreCase))return;
            bool accepted=args.FailureReason==TradeFailureReason.None;
            if(accepted&&p.FinishedBeforeResult)return;
            if(accepted&&p.Reset&&args.Trade.ItemsOnOffer.Length!=0)
            {Unknown(p,"TradeResetResultNotEmpty");return;}
            // A matching known result may resolve a send exception; closure/timeouts never do.
            p.Accepted=accepted;p.Message=accepted?null:args.FailureMessage;Settle(p);
        }
        private void Finished(object sender,TradeCompletionEventArgs args)
        {
            if(!active||args==null)return;int ticket=generation;
            dispatch(()=>{if(!active||ticket!=generation)return;MainThread();foreach(var p in pending.Values.Where(p=>string.Equals(p.TradeId,args.TradeId,StringComparison.OrdinalIgnoreCase)).ToArray())
                if(!p.Accepted.HasValue){p.FinishedBeforeResult=true;Unknown(p,"TradeFinishedBeforeOfferResult");}});
        }
        private void Unknown(Pending p,string message)
        {
            p.State=TradeOfferOperationState.Unknown;p.Message=message;Touch();
            if(!p.MarkedUnknown){p.MarkedUnknown=true;try {p.MarkUnknown?.Invoke();}catch(Exception error){log?.Invoke("TradeOfferMarkUncertainFailed: "+error.Message);}}
            log?.Invoke(message+"; trade="+p.TradeId+"; token="+p.Token);
        }
        private void Settle(Pending p)
        {
            if(p.Settling)return;p.Settling=true;p.State=TradeOfferOperationState.LocalSettlementPending;Touch();
            try
            {
                var outcome=p.Accepted==true?p.Commit():p.AbortedBeforeSend&&p.Abort!=null?p.Abort():p.Restore();
                if(outcome==TradeOfferSettlement.Done){pending.Remove(p.Token);Touch();return;}
                if(outcome==TradeOfferSettlement.Uncertain){Unknown(p,"TradeOfferLocalSettlementUncertain"+ErrorSuffix(p));return;}
                p.Message="TradeOfferLocalSettlementBlocked"+ErrorSuffix(p);log?.Invoke(p.Message+"; trade="+p.TradeId+"; token="+p.Token);
            }
            catch(Exception error){Unknown(p,"TradeOfferLocalSettlementThrew: "+error.Message);}
            finally {p.Settling=false;}
        }
        private static string ErrorSuffix(Pending p){string reason=p.Error?.Invoke();return string.IsNullOrEmpty(reason)?"":": "+reason;}
        public bool RetryLocalSettlement(string token)
        {
            MainThread();if(!active||!pending.TryGetValue(token??"",out var p)||p.Settling||!p.Accepted.HasValue||p.State!=TradeOfferOperationState.LocalSettlementPending)return false;
            Settle(p);return !pending.ContainsKey(token);
        }
        public void Dispose()
        {
            if(!active)return;active=false;generation++;
            service.OnTradeUpdateSuccess-=Updated;service.OnTradeUpdateFailure-=Updated;
            service.OnTradeCompleted-=Finished;service.OnTradeCancelled-=Finished;
            // No view/stop refund and no manufactured outcome. Inventory uncertainty uses its existing ledger.
            foreach(var p in pending.Values.ToArray())Unknown(p,"TradeOfferOwnerStopped");
        }
    }
}
