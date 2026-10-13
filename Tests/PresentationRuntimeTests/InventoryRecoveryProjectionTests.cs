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
    private sealed class RecoveryFacts : ITradeInventoryRecoveryApi
    {
        public long Revision {get;set;}
        public IReadOnlyList<TradeInventoryRecoveryRecord> Records=Array.Empty<TradeInventoryRecoveryRecord>();
        public IReadOnlyList<TradeInventoryRecoveryRecord> Capture()=>Records;
    }
    private static void CheckInventoryRecoveryProjection()
    {
        var facts=new RecoveryFacts{Records=new[]{new TradeInventoryRecoveryRecord("reservation","original-operation","Reserved","note","one","token",new[]{new TradeInventoryRecoveryLine("entry",7)})}};
        var fixture=new TradeFixture{Trades=new[]{new ClientTradeSnapshot("one",new ImmutableUser("peer","Peer",true,true),Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>())}};
        var renderer=new CapturePresentation();var points=new TradePoints();
        using(var list=new TradeList(fixture))
        {
            list.Start(new Module(),renderer,points,new Theme(),recovery:facts);list.ViewOpened();
            var recoveryRow=renderer.Page.Sections[1].List.Rows.Single(r=>r.Label=="Phinix_trade_inventoryRecoveryTitle");
            Check(renderer.Page.Sections[1].List.Rows.Count==2,"Current trade and journal reservation are separate facts, not two invented trades");
            renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,"trades",ClientPresentationInputKind.RowSelected,recoveryRow.Id));list.ViewUpdate();
            Check(points.Last.Actions.Count==0&&fixture.Cancels==0,"Selecting journal record cannot inherit open/cancel/retry actions from a matching active trade");
            Check(renderer.Page.Sections.Single(s=>s.Id=="reserved-lines").List.IsReadOnly&&renderer.Page.Sections.Single(s=>s.Id=="reserved-lines").List.Rows[0].Label=="entry","Reservation units use the same public readonly list");
            list.ViewClosed();fixture.Finish("one");list.ViewOpened();
            Check(renderer.Page.Sections[1].List.Rows.Count==1&&points.Last.Actions.Count==0,"Journal fact stays reachable after trade removal and reopening without refund");
            facts.Records=Array.Empty<TradeInventoryRecoveryRecord>();facts.Revision++;list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==0&&renderer.Page.Sections.All(s=>s.Id!="reserved-lines"),"Journal reload/clear invalidates old selected record and line projection");
            facts.Records=Enumerable.Range(0,101).Select(i=>new TradeInventoryRecoveryRecord("record-"+i,"operation-"+i,"Reserved","",null,null,Array.Empty<TradeInventoryRecoveryLine>())).ToArray();facts.Revision++;list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==100&&renderer.Page.Sections[0].Actions.Any(a=>a.Action.Id=="browse-next"),"Large journal has bounded pages with public navigation, not hidden records or Host limit failure");
            points.Last.Actions.Single(a=>a.Id=="browse-next").TryExecute();list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==1&&points.Last.Actions.Any(a=>a.Id=="browse-previous"),"Final recovery record remains reachable on next page without business mutation");
        }
    }
}
