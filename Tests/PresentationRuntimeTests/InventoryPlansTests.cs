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
    private sealed class PlanRenderer : IClientPresentationService
    {
        internal readonly List<CapturePresentation> Views = new List<CapturePresentation>();
        public IClientPresentationSession Open(IPhinixExtensionModule module,string pageId)
        {var view=new CapturePresentation();view.Open(module,pageId);Views.Add(view);return view;}
    }
    private sealed class PlanWindows : IClientGameWindowService
    {
        internal Window Last;internal int Opens;
        public void Open(Window window){Last=window;Opens++;window.PreOpen();}
    }
    private static void CheckInventoryPlans()
    {
        Current.Game=new object();var inventory=new BuiltInInventoryClientExtension();
        for(int i=0;i<15;i++)inventory.Entries.Add(new InventoryEntry{EntryId="id-"+i,Label="item-"+i,Quantity=100,CodecId="test",CodecVersion=1});
        for(int i=0;i<11;i++)inventory.Schedules.Add(new InventorySchedule{EntryId="id-"+i,QuantityPerRun=3,DayTick=12000,RemainingQuantity=i==0?2:0});
        var renderer=new PlanRenderer();var windows=new PlanWindows();
        using(var tab=new InventoryTab(inventory,null))
        {
            tab.Start(new Module(),renderer,new InventoryPoints(),windows);tab.ViewOpened();var main=renderer.Views[0];
            Func<ClientPresentationPage,string,ClientPresentationAction> action=(page,id)=>page.Sections.SelectMany(s=>s.Actions.Concat(s.List==null?Array.Empty<ClientPresentationAction>():s.List.Rows.SelectMany(r=>r.Actions))).Single(a=>a.Action.Id==id);
            action(main.Page,"manage-plans").Action.TryExecute();var window=windows.Last;var view=renderer.Views.Last();
            Func<ClientPresentationRow[]> rows=()=>view.Page.Sections.Single(s=>s.Id=="plans").List.Rows.ToArray();
            Check(windows.Opens==1&&rows().Length==10,"Daily plans have a separate native window with bounded rows for existing schedules");
            Check(main.Page.Sections.All(s=>s.Id!="selected"),"Opening plans leaves no heavyweight selected-item editor in the inventory header");
            Check(rows()[0].Actions.Count==2&&rows()[0].Detail.Contains("Phinix_inventory_planRemaining"),"Each existing plan shows edit/remove and already-due remaining work");
            action(view.Page,"plans-next").Action.TryExecute();window.WindowUpdate();Check(rows().Length==1,"The eleventh existing plan is reachable without exceeding action budgets");
            action(view.Page,"edit.10").Action.TryExecute();window.WindowUpdate();
            Check(view.Page.Sections.Single(s=>s.Id=="editor").Fields.Count==2&&view.Page.FixedHeaderSectionCount==0,"Plan editing is a compact two-field page without reserving half the window for a fixed header");
            var remove=action(view.Page,"remove-selected");
            Check(remove.Kind==ClientButtonKind.Danger&&remove.Confirmation!=null,"Removing a plan retains an explicit themed confirmation");
            int reads=inventory.PresentationReads;
            foreach(var text in new[]{"4","45"})view.Inputs.Enqueue(new ClientPresentationInput(view.Page.Revision,"quantity",ClientPresentationInputKind.TextChanged,text));
            view.Inputs.Enqueue(new ClientPresentationInput(view.Page.Revision,"tick",ClientPresentationInputKind.TextChanged,"25000"));window.WindowUpdate();
            Check(inventory.PresentationReads==reads,"Editing a plan draft does not decode inventory display metadata again");
            var save=action(view.Page,"save-plan").Action;save.TryExecute();window.WindowUpdate();
            Check(inventory.LastPlanEntry=="id-10"&&inventory.LastPlanQuantity==45&&inventory.LastPlanTick==25000,"Same-batch quantity and time reach the existing schedule service with the correct record identity");
            Check(view.Page.Sections.Any(s=>s.Id=="plans")&&inventory.SavedPlans==1,"Saving returns to the plan list with feedback rather than enlarging the inventory header");
            action(view.Page,"plans-previous").Action.TryExecute();window.WindowUpdate();
            var stale=action(view.Page,"edit.0").Action;inventory.Change();Check(!stale.TryExecute().Completed,"A changed inventory invalidates plan window actions before the next draw");window.WindowUpdate();
            action(view.Page,"edit.0").Action.TryExecute();window.WindowUpdate();
            Check(view.Page.Sections.All(s=>s.Id!="source"),"Source IDs and details are collapsed by default in the editor");
            action(view.Page,"plan-details").Action.TryExecute();window.WindowUpdate();Check(view.Page.Sections.Any(s=>s.Id=="source"),"Source details remain available on demand");
            var preBatchSave=action(view.Page,"save-plan").Action;
            inventory.BatchProgress=new InventoryBatchProgress(true,1,0,0,null);
            Check(!preBatchSave.TryExecute().Completed,"A batch that starts without a ledger version change still invalidates an old plan save");
            window.WindowUpdate();Check(action(view.Page,"save-plan").Action.DisabledReason!=null,"The window refreshes its read-only state for batch-only changes");
            inventory.BatchProgress=null;window.WindowUpdate();
            inventory.HasPendingExtraction=true;inventory.Change();window.WindowUpdate();Check(action(view.Page,"save-plan").Action.DisabledReason!=null&&action(view.Page,"remove-selected").Action.DisabledReason!=null,"Pending reconciliation makes plan mutations read only");
            inventory.HasPendingExtraction=false;inventory.Change();window.WindowUpdate();
            var oldSave=action(view.Page,"save-plan").Action;window.Close();Check(view.IsClosed&&!oldSave.TryExecute().Completed,"Closing the modal releases its session and rejects old save intent");
            tab.ViewUpdate();var stock=main.Page.Sections.Single(s=>s.Id=="stock").List;
            main.Inputs.Enqueue(new ClientPresentationInput(main.Page.Revision,"entries",ClientPresentationInputKind.RowSelected,stock.Rows[1].Id));tab.ViewUpdate();
            var editor=renderer.Views.Last();Check(windows.Opens==2&&editor.Page.Sections.Any(s=>s.Id=="editor")&&main.Page.Sections.All(s=>s.Id!="selected"),"Clicking an inventory item opens its plan directly without injecting a large top card");
            Check(action(editor.Page,"save-plan").Action.DisabledReason==null,"Existing quantities are editable in the direct plan entry");
            inventory.PlanError="source plugin cannot read item";inventory.Change();windows.Last.WindowUpdate();
            Check(action(editor.Page,"save-plan").Action.DisabledReason==inventory.PlanError,"Codec rejection disables plan saving before the click");
            Check(editor.Page.Sections.Single(s=>s.Id=="editor").Status.Text==inventory.PlanError,"The actual service rejection is visible in the editor");
            inventory.PlanError=null;inventory.Change();windows.Last.WindowUpdate();
            Check(action(editor.Page,"save-plan").Action.DisabledReason==null,"A recovered source plugin re-enables the preserved plan draft");
            Current.Game=new object();windows.Last.WindowUpdate();Check(editor.IsClosed,"Changing games closes plan editing before any stale item can be saved");
            tab.ViewUpdate();action(main.Page,"manage-plans").Action.TryExecute();var orphan=renderer.Views.Last();tab.Dispose();
            Check(orphan.IsClosed,"Disposing the inventory owner closes any remaining plan window");
        }
    }
}
