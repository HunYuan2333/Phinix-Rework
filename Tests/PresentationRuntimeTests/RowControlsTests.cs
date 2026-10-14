using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
internal static partial class Program
{
    private static void CheckRowControlsAndSharedFooter()
    {
        var service=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});
        var session=service.Open(new Module(),"row-controls");
        var command=new Command();var binding=new ClientPresentationAction("row-open",command);
        var row=new ClientPresentationRow("row","Item","Available 50").WithControls(new[]{binding},ClientPresentationTextField.Integer("row-quantity","Quantity","0"));
        Check(row.WithBadges(new[]{new ClientPresentationBadge("source","Map")}).Field==row.Field&&row.WithImage(null).Actions.Count==1,"Row decoration preserves controls without mutating the original");
        Func<long,ClientPresentationPage> page=revision=>new ClientPresentationPage("row-controls","",revision,new[]{new ClientPresentationSection("items","","",list:new ClientPresentationList("items",new[]{row},""))});
        Reject(()=>session.Publish(page(1),new ActionSet{Actions=Array.Empty<IClientContributedAction>()},()=>true),"Row actions must belong to the transferred action set");
        bool current=true;var set=new ActionSet{Actions=new[]{command}};session.Publish(page(1),set,()=>current);
        Event.current.type=EventType.Layout;Widgets.ClickLabel="execute";session.Draw(new ClientPresentationBounds(0,0,800,500));
        Check(command.Calls==0,"Layout cannot execute row commands");
        Event.current.type=EventType.MouseDown;Widgets.ClickLabel="execute";session.Draw(new ClientPresentationBounds(0,0,800,500));
        Check(command.Calls==1,"Row command uses the same owned execution path");
        current=false;session.InvokeAction("row-open");Check(command.Calls==1,"Stale row context cannot execute again");
        current=true;session.Publish(page(2),new ActionSet{Actions=new[]{command}},()=>current);
        Event.current.type=EventType.KeyDown;Widgets.FieldValue="12";session.Draw(new ClientPresentationBounds(0,0,800,500));
        session.InvokeAction("row-open");Check(command.Calls==1,"Pending inline edits block commands with old arguments");
        Check(session.TryDequeueInput(out var input)&&input.Id=="row-quantity"&&input.Value=="12"&&input.Revision==2,"Inline quantity emits a revision-scoped owner intent");
        var many=Enumerable.Range(0,100).Select(i=>new ClientPresentationRow("item."+i,"Item").WithControls(null,ClientPresentationTextField.Integer("quantity."+i,"Quantity","0"))).ToArray();
        session.Publish(new ClientPresentationPage("row-controls","",3,new[]{new ClientPresentationSection("many","","",list:new ClientPresentationList("many",many,""))}),null,()=>true);
        Event.current.type=EventType.Repaint;Widgets.FieldValue=null;session.Draw(new ClientPresentationBounds(0,0,320,500));
        Check(session.FailureCode==null,"One hundred row quantities render through bounded virtualized input at narrow width");
        Reject(()=>session.Publish(new ClientPresentationPage("row-controls","",4,new[]{new ClientPresentationSection("readonly","","",list:new ClientPresentationList("readonly",new[]{row},"").WithReadOnly())}),new ActionSet{Actions=new[]{command}},()=>true),"Read-only quotes cannot hide editable controls");
        var duplicate=new ClientPresentationRow("second","Other").WithControls(null,row.Field);
        Reject(()=>session.Publish(new ClientPresentationPage("row-controls","",4,new[]{new ClientPresentationSection("duplicate","","",list:new ClientPresentationList("duplicate",new[]{row,duplicate},""))}),new ActionSet{Actions=new[]{command}},()=>true),"Row quantity identities cannot collide across items");
        session.Dispose();Check(set.Disposals==1,"Row action ownership is released on replacement");
        var split=new ClientPresentationSplitView(2,"Ours","Theirs",.5f).WithFixedHeaders(1,0,0).WithSharedFooter(2,.5f,1);
        Check(split.WithAlignedHeaders().SharedFooterHeaderSections==1&&split.WithFixedFooters(0,0).SharedFooterSections==2,"Split layout copies preserve shared footer and pinned toolbar");
        Reject(()=>split.WithSharedFooter(2,float.NaN),"Shared footer rejects invalid fractions");
        Reject(()=>split.WithSharedFooter(2,.5f,2),"Shared footer retains a scrolling body");
        Reject(()=>new ClientPresentationPage("layout","",1,new[]{new ClientPresentationSection("head","",""),new ClientPresentationSection("ours","",""),new ClientPresentationSection("theirs","",""),new ClientPresentationSection("items","","")}).WithSplitView(split),"Shared footer cannot consume the secondary quote");
        var compare=service.Open(new Module(),"compare");
        compare.Publish(new ClientPresentationPage("compare","",1,new[]{new ClientPresentationSection("head","Status",""),new ClientPresentationSection("ours","Our quote",""),new ClientPresentationSection("theirs","Their quote",""),new ClientPresentationSection("browse","Available items","",fields:new[]{new ClientPresentationTextField("query","Search","")}),new ClientPresentationSection("items","","",list:new ClientPresentationList("items",many,""))}).WithSplitView(split),null,()=>true);
        foreach(int width in new[]{1100,360}){Event.current.type=EventType.Repaint;compare.Draw(new ClientPresentationBounds(0,0,width,760));Check(compare.FailureCode==null,"Quotes and shared editable items render at width "+width);}
        compare.Dispose();
    }
}
