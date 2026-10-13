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
    private static void CheckTradeRecoveryProjection()
    {
        var trade=new ClientTradeSnapshot("one",new ImmutableUser("peer","One",true,true),Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>());
        var fixture=new TradeFixture{Trades=new[]{trade}};var queue=new Queue<Action>();int attempts=0;bool ready=false;
        using(var operations=new TradeOfferOperations(fixture,queue.Enqueue,_=>{},()=>true))
        using(var list=new TradeList(fixture))
        {
            operations.Start();var renderer=new CapturePresentation();var points=new TradePoints();list.Start(new Module(),renderer,points,new Theme(),operations);list.ViewOpened();
            renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,"trades",ClientPresentationInputKind.RowSelected,renderer.Page.Sections[1].List.Rows[0].Id));list.ViewUpdate();
            var oldCancel=points.Last.Actions.Single(a=>a.Id=="cancel");
            string token=operations.Begin("one",true,()=>{attempts++;return ready?TradeOfferSettlement.Done:TradeOfferSettlement.Blocked;},()=>TradeOfferSettlement.Done,null);
            Check(!oldCancel.TryExecute().Completed,"New operation revision invalidates prior public cancellation");list.ViewUpdate();
            Check(points.Last.Actions.Single(a=>a.Id=="cancel").DisabledReason!=null,"Pending offer disables conflicting cancellation through ordinary public action");
            operations.Submit(token,t=>{});fixture.Response(trade,token);while(queue.Count>0)queue.Dequeue()();list.ViewUpdate();
            Check(points.Last.Actions.Any(a=>a.Id=="retry-settlement")&&renderer.Page.Sections[2].Actions.Single(a=>a.Action.Id=="retry-settlement").Confirmation!=null,"Known local settlement failure exposes public confirmed retry");
            list.ViewClosed();fixture.Finish("one");while(queue.Count>0)queue.Dequeue()();list.ViewOpened();
            Check(renderer.Page.Sections[1].List.Rows.Count==1&&renderer.Page.Sections[1].List.Rows[0].Label=="Phinix_trade_listOfferRecovery","Operation remains independently reachable after view close and trade removal");
            renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,"trades",ClientPresentationInputKind.RowSelected,renderer.Page.Sections[1].List.Rows[0].Id));list.ViewUpdate();
            Check(points.Last.Actions.All(a=>a.Id=="retry-settlement"),"Recovery projection does not expose open/cancel for removed trade");
            ready=true;Check(points.Last.Actions.Single(a=>a.Id=="retry-settlement").TryExecute().Completed&&attempts==2,"Explicit retry delegates to same business owner and completes local handling");list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==0&&operations.GetPending().Count==0,"Only successful local settlement removes recovery record");
        }
    }
}
