using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UserManagement;
internal static partial class Program
{
    private static void CheckTradeOfferOwner()
    {
        var empty=new ClientTradeSnapshot("one",new ImmutableUser("peer","One",true,true),Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>());
        var nonempty=new ClientTradeSnapshot("one",empty.OtherParty,new[]{new TradeItemSnapshot()},Array.Empty<TradeItemSnapshot>());
        var other=new ClientTradeSnapshot("two",empty.OtherParty,Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>());
        var fixture=new TradeFixture{Trades=new[]{empty}};var queue=new Queue<Action>();var logs=new List<string>();bool main=true;
        Action drain=()=>{while(queue.Count>0)queue.Dequeue()();};int commits=0,restores=0,unknown=0,sends=0;
        using(var owner=new TradeOfferOperations(fixture,queue.Enqueue,logs.Add,()=>main))
        {
            owner.Start();owner.Start();
            string token=owner.Begin("one",true,()=>{commits++;return TradeOfferSettlement.Done;},()=>{restores++;return TradeOfferSettlement.Done;},()=>unknown++);
            Check(owner.GetPending().Single().State==TradeOfferOperationState.Preparing&&commits==0,"Reset preparation returns nothing before submission");
            Reject(()=>owner.Begin("ONE",false,()=>TradeOfferSettlement.Done,()=>TradeOfferSettlement.Done,null),"Different view cannot stage concurrent operation for same trade");
            owner.Submit(token,t=>{Check(owner.CanSubmit("one",t,true),"Only staged owner submit can pass its matching shared port guard");sends++;});Check(sends==1&&owner.GetPending().Single().State==TradeOfferOperationState.AwaitingResult&&commits==0,"Sending reset preserves return until result");
            Check(!owner.CanSubmit("one",token,true)&&!owner.CanSubmit("one",null,false)&&owner.CanSubmit("other",null,false),"Owned pending operation blocks external update/cancel even with copied token, without blocking another trade");
            Check(!owner.Submit(token,t=>sends++)&&sends==1,"One operation submits only once");
            fixture.Response(other,token);fixture.Response(empty,"other-token");drain();
            Check(commits==0&&owner.GetPending().Count==1,"Foreign trade and token do not release return");
            fixture.Response(empty,token);Check(commits==0,"Business settlement waits for dispatcher");drain();
            Check(commits==1&&owner.GetPending().Count==0,"Matching acknowledged empty offer releases frozen return once");
            fixture.Response(empty,token);drain();Check(commits==1,"Duplicate result does not duplicate return");
            token=owner.Begin("one",true,()=>{commits++;return TradeOfferSettlement.Done;},()=>{restores++;return TradeOfferSettlement.Done;},()=>unknown++);owner.Submit(token,t=>sends++);
            fixture.Response(nonempty,token);drain();Check(owner.GetPending().Single().State==TradeOfferOperationState.Unknown&&commits==1,"Nonempty reset snapshot cannot prove reset; no return");
            Check(!owner.RetryLocalSettlement(token),"Unknown outcome cannot be retried as local return");
            fixture.Response(empty,token,false);drain();Check(restores==1&&commits==1&&owner.GetPending().Count==0,"Known reset rejection ends request without refunding old offer");
            bool allow=false;int attempts=0;
            token=owner.Begin("one",false,()=>{attempts++;if(!allow)return TradeOfferSettlement.Blocked;commits++;return TradeOfferSettlement.Done;},()=>TradeOfferSettlement.Done,null);
            owner.Submit(token,t=>sends++);fixture.Response(empty,token);drain();
            Check(owner.GetPending().Single().CanRetry&&attempts==1,"Known result blocked by local settlement retains operation");
            fixture.Response(empty,token);drain();Check(attempts==1,"Duplicate result does not automatically retry local effects");
            allow=true;int submitted=sends;Check(owner.RetryLocalSettlement(token)&&sends==submitted&&attempts==2,"Explicit local retry settles known result without sending command");
            token=owner.Begin("one",false,()=>TradeOfferSettlement.Uncertain,()=>TradeOfferSettlement.Done,()=>unknown++);owner.Submit(token,t=>sends++);fixture.Response(empty,token);drain();
            Check(!owner.GetPending().Single().CanRetry&&!owner.RetryLocalSettlement(token),"Uncertain local delivery is retained and cannot be blindly repeated");
            // Separate trade can progress while the uncertain operation remains held.
            int failures=0;
            string token2=owner.Begin("two",false,()=>TradeOfferSettlement.Done,()=>{failures++;return TradeOfferSettlement.Done;},()=>unknown++);
            owner.Submit(token2,t=>{throw new Exception("send uncertain");});Check(owner.GetPending().Single(p=>p.Token==token2).State==TradeOfferOperationState.Unknown&&failures==0,"Send exception does not refund physical escrow");
            fixture.Response(other,token2,false);drain();Check(failures==1&&!owner.GetPending().Any(p=>p.Token==token2),"Late matching rejection resolves send uncertainty");
            token2=owner.Begin("two",true,()=>{commits++;return TradeOfferSettlement.Done;},()=>TradeOfferSettlement.Done,null);owner.Submit(token2,t=>sends++);fixture.Finish("two");drain();fixture.Response(other,token2);drain();
            Check(owner.GetPending().Single(p=>p.Token==token2).State==TradeOfferOperationState.Unknown,"Completion before offer result cannot manufacture reset return");
            fixture.Response(other,token2,false);drain();Check(!owner.GetPending().Any(p=>p.Token==token2),"Known rejection after completion can close rejected reset without returning old quote");
            token2=owner.Begin("two",false,()=>TradeOfferSettlement.Done,()=>{restores++;return TradeOfferSettlement.Done;},null);owner.AbortBeforeSend(token2);
            Check(!owner.GetPending().Any(p=>p.Token==token2)&&restores==2,"Preparation failure before send may restore staged resources");
            token2=owner.Begin("two",true,()=>{commits++;return TradeOfferSettlement.Done;},()=>TradeOfferSettlement.Done,()=>unknown++);owner.Submit(token2,t=>sends++);fixture.Response(other,token2);
            int beforeStop=commits;owner.Dispose();drain();Check(commits==beforeStop&&owner.GetPending().All(p=>!p.CanRetry),"Stop suppresses queued callbacks, preserves unknown records and refunds nothing");
            Check(!owner.RetryLocalSettlement(token2),"Stopped owner cannot settle through borrowed status API");
            using(var inline=new TradeOfferOperations(fixture,a=>a(),logs.Add,()=>true))
            {
                inline.Start();int settled=0,marked=0;
                string immediate=inline.Begin("one",true,()=>{settled++;return TradeOfferSettlement.Done;},()=>TradeOfferSettlement.Done,()=>marked++);
                inline.Submit(immediate,t=>{fixture.Response(empty,t);throw new Exception("after known result");});
                Check(settled==1&&marked==0&&inline.GetPending().Count==0,"Send exception after synchronous known result does not revoke settlement or fabricate uncertainty");
            }
            main=false;Reject(()=>owner.GetPending(),"Operation status reads enforce main-thread boundary");main=true;
        }
    }
}
