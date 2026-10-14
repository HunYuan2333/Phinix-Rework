using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UserManagement;
using Verse;
internal static partial class Program
{
    private sealed class OfferPoints:IClientExtensionPointService,IClientExtensionPointScope,IClientActionPoint<TradeOfferPresentationActionContext>
    {
        public long Revision=>0;
        private Func<TradeOfferPresentationActionContext,IEnumerable<ClientActionDefinition>> factory;
        public IClientExtensionPointScope ForModule(Utils.Framework.IPhinixExtensionModule owner)=>this;
        public IClientActionPoint<T> DefineActions<T>(string id) where T:class=>(IClientActionPoint<T>)(object)this;
        public void ContributeActions<T>(string id,Func<T,IEnumerable<ClientActionDefinition>> callback,float order=0) where T:class=>factory=context=>callback((T)(object)context);
        public IClientActionSet Collect(TradeOfferPresentationActionContext context,Func<bool> current=null)=>new LocalSet(factory(context),current);
        public void Dispose(){}
    }
    private static void CheckInlineTradeOfferDrafts()
    {
        var renderer=new CapturePresentation();var points=new OfferPoints();
        using(var factory=new TradeOfferPresentationFactory(new Module(),renderer,points,new Theme()))
        {
            var first=new TradeAvailableItem{Label="A",Count=50};var second=new TradeAvailableItem{Label="B",Count=20};
            var window=new TradeWindow(factory,new[]{first,second});window.TestOpen();
            Func<ClientPresentationRow[]> rows=()=>renderer.Page.Sections.Single(s=>s.Id=="items").List.Rows.ToArray();
            Func<string,ClientPresentationAction> action=id=>renderer.Page.Sections.SelectMany(s=>s.Actions).Single(a=>a.Action.Id==id);
            Check(renderer.Page.SplitView.PrimarySectionCount==2&&renderer.Page.SplitView.SharedFooterSections==2&&renderer.Page.Sections[1].Id=="our-quote"&&renderer.Page.Sections[2].Id=="their-quote","Real offer presenter places both authoritative quotes above shared item area");
            Check(renderer.Page.Title==""&&renderer.Page.Sections[0].InlineToolbar&&renderer.Page.SplitView.GlobalHeaderSections==1&&renderer.Page.SplitView.SharedFooterFraction>=.6f,"Dense offer prioritizes items and removes duplicated title/command headers");
            Check(renderer.Page.Sections.Single(s=>s.Id=="items").List.CompactRows,"Available items use the same public compact list mode");
            Check(rows().All(r=>r.Field!=null)&&rows().Length==2,"Every available item has an inline quantity without selection");
            Action<string,string> edit=(id,value)=>{renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,id,ClientPresentationInputKind.TextChanged,value));window.TestUpdate();};
            string firstField=rows()[0].Field.Id,secondField=rows()[1].Field.Id;
            edit(firstField,"30");edit(secondField,"10");
            Check(first.Selected==30&&second.Selected==10&&window.TestSubmissions==0,"Editing two rows changes local drafts without sending any offer");
            edit(secondField,"21");
            Check(second.Selected==10&&rows()[1].Field.Value=="21"&&rows()[1].Field.ValidationText.Length>0&&action("submit").Action.DisabledReason!=null,"Out-of-range row preserves last valid quantity and blocks quote submission");
            Check(action("cancel").Action.DisabledReason==null&&action("reset").Action.DisabledReason==null,"Invalid local quantity cannot trap cancellation or reset of the acknowledged quote");
            edit("search","A");
            Check(rows().Length==1&&action("submit").Action.DisabledReason!=null,"Hidden invalid row remains a submission blocker");
            edit("search","");edit(secondField,"0");
            Check(action("submit").Action.DisabledReason==null,"Correcting a row restores explicit submission");
            Check(action("submit").Action.TryExecute().Completed&&window.TestSubmissions==1,"Explicit submit invokes the existing owner command once");window.TestUpdate();
            var old=action("submit").Action;
            edit("search","B");
            Check(rows()[0].Field.Id==secondField&&!old.TryExecute().Completed&&window.TestSubmissions==1,"Filtering retains item field identity and invalidates the previous command snapshot");
            renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision-1,secondField,ClientPresentationInputKind.TextChanged,"19"));window.TestUpdate();
            Check(second.Selected==0,"Input from replaced page cannot retarget a quantity");
            edit("search","");window.TestBusy=true;window.TestInvalidate();window.TestUpdate();
            Check(rows().All(r=>r.Field.DisabledReason!=null),"Pending owner operation disables every row editor");
            edit(firstField,"49");Check(first.Selected==30,"Busy editor cannot alter draft quantities");
            window.TestBusy=false;window.TestRefresh(new[]{new TradeAvailableItem{Label="Replacement",Count=5}});window.TestUpdate();
            Check(rows()[0].Field.Id!=firstField&&rows()[0].Field.Value=="0","Rebuilt item source cannot inherit old input control identity or draft");
            Check(action("accept").Confirmation!=null&&action("cancel").Confirmation!=null&&action("reset").Confirmation!=null,"Confirmation remains on acceptance, reset and cancellation");
            var beforeClose=action("cancel").Action;window.Close();
            Check(!beforeClose.TryExecute().Completed&&window.TestCancels==0,"Closing quote editor does not cancel business or execute stale actions");
        }
    }
}
namespace Phinix.TradeExtension.Client
{
    // Only game/business ports are fakes; the production partial presenter and factory are compiled above.
    internal sealed class TradeAvailableItem
    { public string Label;public int Count,Selected;public bool IsInventory;public ThingDef ThingDef; }
    internal sealed class ClientTradeUiHostContext
    {public TradeOfferPresentationFactory OfferPresentation;public OfferRevision OfferOperations=new OfferRevision();}
    internal sealed class OfferRevision {public long Revision;}
    internal sealed class OfferGameScope {public bool IsValid()=>true;}
    public partial class TradeWindow:Window
    {
        private ClientTradeUiHostContext clientHostContext;
        private OfferGameScope gameScope=new OfferGameScope();
        private ClientTradeSnapshot trade=new ClientTradeSnapshot("offer",new ImmutableUser("peer","Peer",true,true),new[]{new TradeItemSnapshot()},Array.Empty<TradeItemSnapshot>());
        private bool closed,shouldClose,tradeUpdated;
        private bool OfferBusy=>TestBusy;
        private bool? getPendingAccepted()=>null;
        private List<TradeAvailableItem> availableItems,filteredAvailableItems;
        private string searchText="";
        internal int TestSubmissions,TestCancels;
        internal bool TestBusy;
        internal TradeWindow(TradeOfferPresentationFactory factory,IEnumerable<TradeAvailableItem> items){clientHostContext=new ClientTradeUiHostContext{OfferPresentation=factory};availableItems=items.ToList();RebuildAvailableItemFilter();}
        internal void TestOpen()=>OpenPresentation();
        internal void TestUpdate()=>UpdatePresentation();
        internal void TestInvalidate()=>presentationDirty=true;
        internal void TestRefresh(IEnumerable<TradeAvailableItem> items){ClearQuantityDrafts();availableItems=items.ToList();selectedItem=null;RebuildAvailableItemFilter();}
        private void RebuildAvailableItemFilter(){filteredAvailableItems=availableItems.Where(i=>i.Label.Contains(searchText)).ToList();presentationDirty=true;}
        private void refreshAvailableItems(){ClearQuantityDrafts();foreach(var item in availableItems)item.Selected=0;selectedItem=null;RebuildAvailableItemFilter();}
        private void UpdateTradeItems()=>TestSubmissions++;
        private void ResetTradeItems(){foreach(var item in availableItems)item.Selected=0;ClearQuantityDrafts();}
        private void sendTradeStatusUpdate(bool accepted){}
        private void sendCancelTradeRequest()=>TestCancels++;
        public new void Close(){closed=true;ClosePresentation();}
    }
    internal static class TradeItemConverter { public static List<string> FindMissingDependencies(TradeItemSnapshot[] items)=>new List<string>(); }
    internal static class TradeLifetimeCleanup
    {
        public static void Try(Action callback,List<Exception> errors){try{callback();}catch(Exception error){errors.Add(error);}}
        public static void ThrowIfFailed(List<Exception> errors){if(errors.Count>0)throw new AggregateException(errors);}
    }
}
