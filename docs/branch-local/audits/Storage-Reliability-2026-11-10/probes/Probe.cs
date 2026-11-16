using System;using System.Collections.Generic;using System.IO;using Phinix.TradeExtension.Client;using PhinixClient.Trade;
namespace PhinixClient.Trade {
 enum TradeOfferOperationState { Preparing,AwaitingResult,Unknown,LocalSettlementPending }
 enum TradeFailureReason {None,Rejected}
 interface ITradeOfferOperationApi {}
 sealed class TradeOfferOperationInfo {public TradeOfferOperationState State;public TradeOfferOperationInfo(string a,string b,bool c,TradeOfferOperationState s,string e,bool f){State=s;}}
 sealed class Snapshot {public string TradeId;public object[] ItemsOnOffer=Array.Empty<object>();}
 sealed class TradeUpdateEventArgs:EventArgs {public string Token;public Snapshot Trade;public TradeFailureReason FailureReason;public string FailureMessage;}
 sealed class TradeCompletionEventArgs:EventArgs {public string TradeId;}
 interface IClientTradeService {event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess;event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure;event EventHandler<TradeCompletionEventArgs> OnTradeCompleted;event EventHandler<TradeCompletionEventArgs> OnTradeCancelled;}
 sealed class Fake:IClientTradeService {
 public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess; public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure; public event EventHandler<TradeCompletionEventArgs> OnTradeCompleted; public event EventHandler<TradeCompletionEventArgs> OnTradeCancelled;
 public void Finish()=>OnTradeCompleted?.Invoke(this,new TradeCompletionEventArgs{TradeId="synthetic-trade"});
 public void Accept(string t)=>OnTradeUpdateSuccess?.Invoke(this,new TradeUpdateEventArgs{Token=t,Trade=new Snapshot{TradeId="synthetic-trade"},FailureReason=TradeFailureReason.None});
 }
}
class Program {
 static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine(s);}
 static void Main(){
  var s=new Fake();var o=new TradeOfferOperations(s,a=>a(),null,()=>true);o.Start();int restore=0,unknown=0;
  var t=o.Begin("synthetic-trade",false,()=>TradeOfferSettlement.Done,()=>{restore++;return TradeOfferSettlement.Done;},()=>unknown++);
  bool ok=o.Submit(t,_=>throw new IOException("synthetic pre-send admission capacity failure"));
  Check(!ok&&o.GetPending()[0].State==TradeOfferOperationState.Unknown&&restore==0&&unknown==1,"P1 OBSERVED: known pre-send exception classified Unknown; no restore");
  var s2=new Fake();var o2=new TradeOfferOperations(s2,a=>a(),null,()=>true);o2.Start();int commit=0;
  var t2=o2.Begin("synthetic-trade",false,()=>{commit++;return TradeOfferSettlement.Done;},()=>TradeOfferSettlement.Done,()=>{});
  o2.Submit(t2,_=>{});s2.Finish();s2.Accept(t2);
  Check(o2.GetPending().Count==1&&o2.GetPending()[0].State==TradeOfferOperationState.Unknown&&commit==0,"P2 OBSERVED: terminal event then matching accepted event remains Unknown");
  var payload=new byte[3*1024*1024];Array.Fill(payload,(byte)255);
  using(var stream=new MemoryStream()){
   new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(byte[])).WriteObject(stream,payload);
   Check(payload.Length<8*1024*1024&&stream.Length>8*1024*1024,"P4 OBSERVED: legal-size 3 MiB raw payload expands beyond 8 MiB JSON journal record; JSON bytes="+stream.Length);
  }
  long budget=64L*1024*1024;long perOffer=4L*1024*1024+8192+64;
  Check(15*perOffer<=budget&&16*perOffer>budget,"P5 MODEL: 16 offers missing result/binding slots exceed 64 MiB before payload files");
  o2.ApplyDurableResult("synthetic-trade",t2,true);
  Check(o2.GetPending().Count==0&&commit==1,"P3 CONTROL: durable result resolves same pending operation");
 }
}
