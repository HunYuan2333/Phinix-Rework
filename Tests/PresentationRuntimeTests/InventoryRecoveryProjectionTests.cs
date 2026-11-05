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
        public int Captures;
        public IReadOnlyList<TradeInventoryRecoveryRecord> Records=Array.Empty<TradeInventoryRecoveryRecord>();
        public IReadOnlyList<TradeInventoryRecoveryRecord> Capture(){Captures++;return Records;}
    }
    private static void CheckInventoryRecoveryProjection()
    {
        bool previous=Verse.Prefs.DevMode;
        var facts=new RecoveryFacts{Records=new[]{
            new TradeInventoryRecoveryRecord("reservation","operation","Reserved","private evidence","one","token",Array.Empty<TradeInventoryRecoveryLine>()),
            new TradeInventoryRecoveryRecord("completion","op2","ReturnedSuccess","private evidence","one",null,Array.Empty<TradeInventoryRecoveryLine>(),"Completion","OtherSave"),
            new TradeInventoryRecoveryRecord("intent","op3","Pending","private evidence","one",null,Array.Empty<TradeInventoryRecoveryLine>(),"Intent",null)}};
        var fixture=new TradeFixture{Trades=new[]{new ClientTradeSnapshot("one",new ImmutableUser("peer","Peer",true,true),Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>())}};
        var renderer=new CapturePresentation();var points=new TradePoints();
        try
        {
            Verse.Prefs.DevMode=false;
            using(var list=new TradeList(fixture))
            {
                list.Start(new Module(),renderer,points,new Theme(),recovery:facts);list.ViewOpened();
                Check(renderer.Page.Sections[1].List.Rows.Count==1&&facts.Captures==0,"Ordinary trade view never captures or exposes durable developer journals");
                Check(!renderer.Page.Sections[1].Title.Contains("recoveryRecordCount"),"Ordinary trade count omits development journal counters");
                Check(renderer.Page.Sections[1].List.Rows[0].Actions.Any(a=>a.Action.Id=="cancel"),"Removing diagnostics preserves actual player trade actions");
                Verse.Prefs.DevMode=true;list.ViewUpdate();
                Check(renderer.Page.Sections[1].List.Rows.Count==1&&facts.Captures==0,"Developer mode also omits the removed journal view");
                Check(renderer.Page.Sections.All(s=>s.Id!="details"),"Enabling developer mode does not reveal journal details");
                facts.Revision++;list.ViewUpdate();list.ViewClosed();list.ViewOpened();
                Check(facts.Records.Count==3&&fixture.Cancels==0,"Hiding, refreshing and reopening diagnostics neither deletes records nor submits a trade");
            }
        }
        finally {Verse.Prefs.DevMode=previous;}
    }

}
