using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using PhinixClient.Storage;
using PhinixClient.Trade;
using Phinix.TradeExtension.Client;

internal static partial class Program
{
    private sealed class HostileMessageFailure : Exception
    { public override string Message => throw new Exception("broken Message getter"); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DatabaseFaultOrigin(){throw new ClientDatabaseException(DatabaseFailure.Unavailable,"private native detail");}
    private static void ErrorDiagnostics(IClientDatabaseService storage,string root)
    {
        var reports=new List<Tuple<string,Exception>>();string directory=Path.Combine(root,"error-records"),file=Path.Combine(directory,"entry.plan");
        using(var records=new TradeSqliteRecords(storage,directory,"diagnostics",message=>{throw new IOException("bad warning observer");},(operation,error)=>reports.Add(Tuple.Create(operation,error)))) {
            records.Put(file,new byte[]{1,2,3});Exception original=null;
            try{DatabaseFaultOrigin();}catch(Exception error){original=error;}
            Exception caught=null;try{records.Quarantine(file,original);}catch(Exception error){caught=error;}
            Check(ReferenceEquals(caught,original) && caught.StackTrace.Contains("DatabaseFaultOrigin"),"Trade quarantine preserves original database instance/frames instead of named rethrow");
            Check(records.Read(file).SequenceEqual(new byte[]{1,2,3}),"Unavailable database evidence is not falsely quarantined as corrupt business data");
            var corrupt=new InvalidDataException("private record payload");records.Quarantine(file,corrupt);
            Check(reports.Any(r=>r.Item1=="TradeRecordReadFailed" && ReferenceEquals(r.Item2,corrupt)) && reports.Any(r=>r.Item1=="TradeRecordWarningObserverFailed"),"Trade corrupt evidence and independent warning observer failure both retain original causes");
            Check(records.Exists(file) && records.Keys(".plan").Length==0,"Corrupt record stays retained and excluded from normal work after observer failure");
        }
    }
    private static void OfferDiagnostics()
    {
        var service=DispatchProxy.Create<IClientTradeService,TradeProxy>();var reports=new List<Tuple<string,Exception>>();
        using(var offers=new TradeOfferOperations(service,action=>action(),message=>{throw new IOException("log observer");},()=>true,(operation,error)=>reports.Add(Tuple.Create(operation,error)))) {
            offers.Start();int restored=0,unknown=0,healthyCleanup=0;var primary=new HostileMessageFailure();
            offers.Settled+=token=>{throw new IOException("cleanup observer");};offers.Settled+=token=>healthyCleanup++;
            string token=offers.Begin("diagnostic-trade",false,()=>TradeOfferSettlement.Done,()=>TradeOfferSettlement.Done,()=>unknown++,()=>{restored++;return TradeOfferSettlement.Done;});
            Check(!offers.Submit(token,value=>{throw new TradeRequestNotSentException(primary);}) && restored==1 && offers.GetPending().Count==0,"Local preparation fault never reads hostile Message and restores known NotSent exactly once");
            Check(healthyCleanup==1 && reports.Any(r=>r.Item1=="TradeTerminalCleanupFailed") && reports.Any(r=>r.Item1=="TradeOfferLogObserverFailed"),"Terminal cleanup/log failures are recorded without blocking healthy cleanup or resurrecting a finished operation");
            Check(reports.Any(r=>r.Item1=="TradeOfferPreparationAborted" && ReferenceEquals(r.Item2.InnerException,primary)),"Trade preparation wrapper retains its original cause");
            token=offers.Begin("diagnostic-trade",false,()=>TradeOfferSettlement.Done,()=>{restored++;return TradeOfferSettlement.Done;},()=>{unknown++;throw new IOException("mark callback");});
            var sent=new IOException("private transport content");
            Check(!offers.Submit(token,value=>{throw sent;}) && restored==1 && unknown==1 && offers.GetPending().Count==1,"Ambiguous send/log/mark faults retain pending operation without automatic refund");
            Check(reports.Any(r=>r.Item1=="TradeOfferSendUncertain" && ReferenceEquals(r.Item2,sent)) && reports.Any(r=>r.Item1=="TradeOfferMarkUncertainFailed"),"Original send and secondary uncertain-mark failures are both observable");
        }
    }
}
