using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.InventoryExtension;
using Phinix.InventoryExtension.Client;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using Verse;

internal static partial class Program
{
    private sealed class InventoryPoints : IClientExtensionPointService, IClientExtensionPointScope, IClientActionPoint<InventoryPresentationActionContext>
    {
        public long Revision => 0;
        private Func<InventoryPresentationActionContext,IEnumerable<ClientActionDefinition>> factory;
        public IClientExtensionPointScope ForModule(IPhinixExtensionModule owner) => this;
        public IClientActionPoint<T> DefineActions<T>(string id) where T:class => (IClientActionPoint<T>)(object)this;
        public void ContributeActions<T>(string id,Func<T,IEnumerable<ClientActionDefinition>> callback,float order=0) where T:class => factory = c => callback((T)(object)c);
        public IClientActionSet Collect(InventoryPresentationActionContext context,Func<bool> current=null)
        {
            var definitions = factory(context).ToArray();
            Check(definitions.Length <= 32 && definitions.All(d=>d.Id.Length<=64), "Inventory respects real contribution budgets on every projection");
            return new LocalSet(definitions,current);
        }
        public void Dispose() {}
    }
    private static void CheckInventoryInline()
    {
        Current.Game = new object();
        var owner = new BuiltInInventoryClientExtension();
        for (int i=0;i<45;i++) owner.Entries.Add(new InventoryEntry { EntryId="record-"+i,Label="item-"+i,Quantity=100,CodecId="test",CodecVersion=1 });
        var renderer = new CapturePresentation(); var points = new InventoryPoints();
        using (var tab = new InventoryTab(owner,null))
        {
            tab.Start(new Module(),renderer,points);tab.ViewOpened();
            Func<ClientPresentationRow[]> rows=()=>renderer.Page.Sections.Single(s=>s.Id=="stock").List.Rows.ToArray();
            Func<string,ClientPresentationAction> command=id=>renderer.Page.Sections.SelectMany(s=>s.Actions).Single(a=>a.Action.Id==id);
            Action<string,string> edit=(id,text)=> { renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,id,ClientPresentationInputKind.TextChanged,text));tab.ViewUpdate(); };
            Check(rows().Length==20 && rows().All(r=>r.Field!=null&&r.Actions.Count==1),"Inventory bounded page exposes quantity and extract directly on every record");
            Check(renderer.Page.FixedHeaderSectionCount==renderer.Page.Sections.Count-1,"Search/navigation/status stay above the independent scrolling stock");
            string firstField=rows()[0].Field.Id,secondField=rows()[1].Field.Id;
            edit(firstField,"7");edit(secondField,"9");
            var stale=rows()[0].Actions[0].Action;
            Check(owner.Extractions==0 && rows()[0].Field.Value=="7" && rows()[1].Field.Value=="9","Two inline quantities are independent local drafts");
            command("page-next").Action.TryExecute();tab.ViewUpdate();
            Check(rows().Length==20 && rows()[0].Label=="item-20" && !stale.TryExecute().Completed,"Paging cannot execute an old row command");
            command("page-next").Action.TryExecute();tab.ViewUpdate();Check(rows().Length==5,"Final short page exposes all remaining entries");
            command("page-previous").Action.TryExecute();tab.ViewUpdate();command("page-previous").Action.TryExecute();tab.ViewUpdate();
            Check(rows()[0].Field.Value=="7"&&rows()[1].Field.Value=="9","Paging preserves each record's quantity");
            edit(firstField,"101");Check(rows()[0].Actions[0].Action.DisabledReason!=null&&rows()[0].Field.ValidationText.Length>0,"Over-balance value is visible and disabled, never silently clamped");
            edit(firstField,"7");rows()[0].Actions[0].Action.TryExecute();tab.ViewUpdate();
            Check(owner.LastEntry=="record-0"&&owner.LastQuantity==7&&owner.Entries[0].Quantity==93&&rows()[1].Field.Value=="9","Inline extract uses stable record identity, updates balance, and keeps other drafts");
            Check(renderer.Page.Sections.Single(s=>s.Id=="stock").List.SelectedId==null&&rows()[0].Detail.Contains("Phinix_inventory_extractSucceeded"),"Extraction result stays on its row without selection-driven scroll reset");
            owner.DeliverNext=3;edit(firstField,"8");rows()[0].Actions[0].Action.TryExecute();tab.ViewUpdate();
            Check(rows()[0].Field.Value=="5"&&owner.LastQuantity==8,"Confirmed partial delivery leaves only the outstanding request");
            rows()[0].Actions[0].Action.TryExecute();tab.ViewUpdate();Check(owner.LastQuantity==5,"Explicit retry never repeats the three confirmed delivered units");
            owner.DeliverNext=2;owner.UncertainNext=true;edit(firstField,"6");rows()[0].Actions[0].Action.TryExecute();tab.ViewUpdate();
            Check(renderer.Page.Sections.Any(s=>s.Id=="resolution"),"Uncertain delivery replaces extraction controls with reconciliation");
            command("mark-delivered").Action.TryExecute();tab.ViewUpdate();
            Check(rows()[0].Actions[0].Action.DisabledReason!=null&&rows()[0].Field.Value=="4","Resolving unknown delivery cannot automatically retry an unreviewed amount");
            edit(firstField,"3");Check(rows()[0].Actions[0].Action.DisabledReason==null,"Explicitly correcting the remaining quantity clears the review block");
            foreach(var text in new[]{"item","item-2"})renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,"inventory-search",ClientPresentationInputKind.TextChanged,text));tab.ViewUpdate();
            Check(renderer.Page.Sections[0].Fields.Single().Value=="item-2"&&rows().All(r=>r.Label.Contains("item-2")),"Inventory keeps the final same-batch search word and matching identities");
            edit("inventory-search","");
            renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,secondField,ClientPresentationInputKind.TextChanged,"12"));tab.ViewClosed();tab.ViewOpened();
            Check(rows()[1].Field.Value=="12","Closing before an update preserves the final row quantity draft");
            Current.Game=new object();owner.Change();tab.ViewUpdate();
            Check(rows()[0].Field.Value=="1"&&rows()[1].Field.Value=="1","A new game cannot inherit old per-record drafts");
            var realRenderer=new ClientPresentationService(new Lifetime(),new Theme(),()=>true,_=>{});var real=realRenderer.Open(new Module(),"inventory");
            // Exercise model validation with actual Host, including unique field/action ownership and row budgets.
            var fakePage=renderer.Page;var definitions=fakePage.Sections.SelectMany(s=>s.List?.Rows??Array.Empty<ClientPresentationRow>()).SelectMany(r=>r.Actions).Select(a=>new ClientActionDefinition(a.Action.Id,a.Action.Label,()=>{})).ToArray();
            var set=new LocalSet(definitions,()=>true);var all=set.Actions.ToDictionary(a=>a.Id);
            var projected=fakePage.Sections.Select(s=>s.List==null?s:new ClientPresentationSection(s.Id,s.Title,s.Summary,list:new ClientPresentationList(s.List.Id,s.List.Rows.Select(r=>r.WithControls(r.Actions.Select(a=>new ClientPresentationAction(a.Id,all[a.Action.Id])),r.Field)),s.List.EmptyText))).ToArray();
            // Navigation lives in a different owner set in this fake page; test the row surface independently.
            real.Publish(new ClientPresentationPage("inventory","",1,projected.Where(s=>s.List!=null)),set,()=>true);
            real.ScrollToRow("entries",rows()[0].Id);real.Draw(new ClientPresentationBounds(0,0,360,280));
            Check(real.FailureCode==null,"Real Host validates and draws inline stock at a narrow viewport with scroll-to-list support");
            var longList=new ClientPresentationList("plain",Enumerable.Range(0,100).Select(i=>new ClientPresentationRow("row-"+i,"row "+i)),"").WithReadOnly();
            var scrollPage=new[]{new ClientPresentationSection("body","","",list:longList)};
            real.Publish(new ClientPresentationPage("inventory","",2,scrollPage),null,()=>true);real.ScrollToRow("plain","row-60");real.Draw(new ClientPresentationBounds(0,0,360,280));
            var scrollField=real.GetType().GetField("scroll",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            float scrollY=((UnityEngine.Vector2)scrollField.GetValue(real)).y;
            Check(scrollY>0,"Ordinary-list scroll request reaches a row beyond the viewport");
            real.Publish(new ClientPresentationPage("inventory","",3,scrollPage),null,()=>true);real.Draw(new ClientPresentationBounds(0,0,360,280));
            Check(((UnityEngine.Vector2)scrollField.GetValue(real)).y==scrollY,"A same-list result refresh keeps its scroll offset");
            real.ScrollToRow("plain","row-0");real.Draw(new ClientPresentationBounds(0,0,360,280));
            Check(((UnityEngine.Vector2)scrollField.GetValue(real)).y<scrollY,"Explicit page navigation can return a list to its first row");real.Dispose();
        }
        var whole = new BuiltInInventoryClientExtension { ItemsPerUnit = 10 };
        whole.Entries.Add(new InventoryEntry { EntryId="opaque", Label="whole-stack", Quantity=4, CodecId="opaque", CodecVersion=1 });
        var capture = new CapturePresentation();
        using (var tab = new InventoryTab(whole,null))
        {
            tab.Start(new Module(),capture,new InventoryPoints());tab.ViewOpened();
            Func<ClientPresentationRow> row=()=>capture.Page.Sections.Single(s=>s.Id=="stock").List.Rows.Single();
            string field=row().Field.Id;
            Check(row().Field.Label=="Phinix_inventory_inlineWholeUnits","Opaque units explicitly distinguish whole units from item count");
            capture.Inputs.Enqueue(new ClientPresentationInput(capture.Page.Revision,field,ClientPresentationInputKind.TextChanged,"2"));tab.ViewUpdate();
            var old=row().Actions[0].Action;whole.Available=1;whole.Change();tab.ViewUpdate();
            Check(row().Field.Value=="2"&&row().Actions[0].Action.DisabledReason!=null&&!old.TryExecute().Completed,"Reservation balance changes retain drafts, reject stale action and require explicit correction");
            whole.Available=0;whole.Change();tab.ViewUpdate();
            Check(row().Field.ValidationText=="Phinix_inventory_inlineUnavailable","Zero availability explains why extraction is blocked instead of offering an impossible 1-to-0 range");
            whole.Available=-1;whole.Change();tab.ViewUpdate();row().Actions[0].Action.TryExecute();tab.ViewUpdate();
            Check(whole.LastQuantity==2&&whole.Entries[0].Quantity==2,"UI extracts codec units without multiplying the authoritative request");
            whole.ItemsPerUnit=1;whole.Change();tab.ViewUpdate();
            Check(row().Field.Value=="1"&&row().Actions[0].Action.DisabledReason!=null,"Changed unit semantics invalidate the old quantity until reviewed");
            whole.Entries.Clear();whole.Change();tab.ViewUpdate();
            Check(capture.Page.Sections.Single(s=>s.Id=="stock").List.Rows.Count==0&&capture.Page.Sections.Any(s=>s.Id=="result"),"Depleted/removed entries disappear while the last verified result remains visible");
        }
        var draft = new InventoryInlineDraft { Text=long.MaxValue.ToString() };
        draft.Complete(false,long.MaxValue,long.MaxValue-1,"partial",false);
        Check(draft.Text=="1"&&draft.TryQuantity(1,out _),"Partial accounting uses exact Int64 subtraction");
        draft.Edit("0");Check(!draft.TryQuantity(100,out _),"Zero cannot extract");
        draft.Edit("9223372036854775808");Check(!draft.TryQuantity(long.MaxValue,out _),"Overflow cannot become a clamped extraction");
    }
}
namespace Phinix.InventoryExtension { public interface IInventoryCodec {} public sealed class InventoryMaterializationResult {} }
namespace Verse { public static class Current {public static object Game;} }
namespace Phinix.InventoryExtension.Client
{
    internal sealed class InventoryTheme { public IClientThemeReader Current=>null; }
    internal sealed class InventoryIconCache {public void Clear(){} public IClientPresentationImage Get(string id)=>null;}
    // Fake only the authoritative delivery port. UI projection, grouping, drafts, public models and Host are production sources.
    internal sealed class BuiltInInventoryClientExtension
    {
        public readonly List<InventoryEntry> Entries=new List<InventoryEntry>();
        public event EventHandler InventoryChanged,AvailabilityChanged;
        internal long ItemsPerUnit=1,Available=-1;
        internal long Version;internal bool HasPendingExtraction,HasPendingRecovery;internal string Fault;
        internal InventoryBatchProgress BatchProgress;internal int Extractions;internal long LastQuantity,DeliverNext=-1;internal string LastEntry;internal bool UncertainNext;
        internal void Change(){Version++;InventoryChanged?.Invoke(this,EventArgs.Empty);AvailabilityChanged?.Invoke(this,EventArgs.Empty);}
        public IReadOnlyList<InventoryEntry> GetSnapshot()=>Entries.ToArray();
        public IReadOnlyList<InventoryEntry> GetAvailableSnapshot()=>Entries.Select(e=>new InventoryEntry{EntryId=e.EntryId,Quantity=Available<0?e.Quantity:Available}).ToArray();
        public IReadOnlyList<InventoryReservation> GetReservations()=>Array.Empty<InventoryReservation>();
        public readonly List<InventorySchedule> Schedules = new List<InventorySchedule>();
        public int SavedPlans,RemovedPlans; public string LastPlanEntry; public long LastPlanQuantity; public int LastPlanTick;
        public IReadOnlyList<InventorySchedule> GetSchedules()=>Schedules.ToArray();
        internal InventorySourcePresentation PresentSource(InventoryEntry e)=>new InventorySourcePresentation{Summary="source"};
        internal int PresentationReads;
        internal InventoryItemPresentation PresentItem(InventoryEntry e){PresentationReads++;return new InventoryItemPresentation{Label=e.Label,ItemsPerUnit=ItemsPerUnit};}
        internal bool TryExtractForPresentation(string id,long quantity,out long delivered,out string reason)
        {
            Extractions++;LastEntry=id;LastQuantity=quantity;delivered=DeliverNext<0?quantity:DeliverNext;bool success=delivered==quantity;
            Entries.Single(e=>e.EntryId==id).Quantity-=delivered;HasPendingExtraction=UncertainNext;DeliverNext=-1;UncertainNext=false;reason=success?null:"stopped";Change();return success;
        }
        public bool TryExtract(string id,long q,out string reason)=>TryExtractForPresentation(id,q,out _,out reason);
        internal bool ApproveRecovery()=>true;internal bool RejectRecovery()=>true;
        internal bool ResolvePendingExtraction(bool delivered){HasPendingExtraction=false;Change();return true;}
        internal string PlanError;
        internal bool CanSetDailySchedule(string id,long q,int tick,out string reason){reason=PlanError;return reason==null;}
        public bool SetDailySchedule(string id,long q,int tick,out string reason)
        {
            reason=null;SavedPlans++;LastPlanEntry=id;LastPlanQuantity=q;LastPlanTick=tick;
            Schedules.RemoveAll(p=>p.EntryId==id);Schedules.Add(new InventorySchedule{EntryId=id,QuantityPerRun=q,DayTick=tick});Change();return true;
        }
        public bool RemoveDailySchedule(string id){RemovedPlans++;Schedules.RemoveAll(p=>p.EntryId==id);Change();return true;}
        public bool StartBatchExtraction(IReadOnlyList<InventoryBatchItem> plan,long version,out string reason){reason=null;return true;}
        public void StopBatchExtraction(){}
    }
}
