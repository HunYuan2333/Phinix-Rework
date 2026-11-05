using System;
using System.Threading;
using PhinixClient.Framework;
using Phinix.TradeExtension.Client;

partial class Program
{
 static void CheckGameScopes()
 {
  int main=Thread.CurrentThread.ManagedThreadId,gameReads=0;
  object game=null;
  var service=new ClientGameScopeService(()=>{gameReads++;return game;},()=>Thread.CurrentThread.ManagedThreadId==main);
  Check(service.Capture()==null,"Unpublished host scope authorizes nothing");
  service.Publish();Check(service.Capture()==null,"Menu publication invents a game");
  game=new object();service.Publish();var a=service.Capture();Check(a.IsCurrent(),"Published game must be usable on the main thread");
  service.Publish();Check(ReferenceEquals(a,service.Capture()),"Normal frames must not expire active scope");
  Exception unexpected=null;int before=gameReads;
  var thread=new Thread(()=>{try{Check(ReferenceEquals(a,service.Capture()),"Receiver captures existing immutable token");Throws<InvalidOperationException>(()=>a.IsCurrent());Throws<InvalidOperationException>(()=>service.Publish());}catch(Exception error){unexpected=error;}});
  thread.Start();Check(thread.Join(5000),"Receiver probe must terminate");if(unexpected!=null)throw unexpected;
  Check(gameReads==before,"Receiving thread must not read a game API");
  string owner="owner",endpoint="server";
  var delivery=new TradeCompletionScope(owner,endpoint,(Func<bool>)a.IsCurrent,()=>owner,()=>endpoint,true);
  var original=game;game=new object();
  Check(!delivery.IsValid(),"Queue window game switch must reject before binding");
  game=original;Check(!a.IsCurrent()&&!delivery.IsValid(),"Old scope cannot revive after returning to the original object");
  service.Publish();var fresh=service.Capture();Check(!ReferenceEquals(a,fresh)&&fresh.IsCurrent(),"New receipt can get a new lifetime after invalidation");
  game=new object();service.Publish();Check(!fresh.IsCurrent(),"Publishing a replacement invalidates prior token");
  var b=service.Capture();game=null;service.Publish();Check(service.Capture()==null&&!b.IsCurrent(),"Menu transition invalidates prior token");
  var missing=new TradeCompletionScope(owner,endpoint,(Func<bool>)null,()=>owner,()=>endpoint,true);
  Check(!missing.IsValid(),"Missing host support cannot fall back to current game");
  var empty=new TradeCompletionScope(owner,endpoint,(Func<bool>)null,()=>owner,()=>endpoint,false);
  Check(empty.IsValid(),"Empty terminal event needs no game materialization scope");
  endpoint=null;Check(!empty.IsValid(),"Empty event still requires the original recipient and endpoint");
  game=new object();service.Publish();var last=service.Capture();service.Stop();
  Check(service.Capture()==null&&!last.IsCurrent(),"Shutdown clears publication and invalidates captures");
  service.Publish();Check(service.Capture()==null,"Stopped host cannot publish again");
 }
}
