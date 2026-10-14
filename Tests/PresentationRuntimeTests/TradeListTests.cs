using System;
using System.Collections.Generic;
using System.Linq;
using Phinix.TradeExtension;
using Phinix.TradeExtension.Client;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UnityEngine;
using UserManagement;
using Utils.Framework;
using Verse;
internal static partial class Program
{
    private sealed class TradeFixture : ITradeUiHostContext,IClientTradeService
    {
        public ClientTradeSnapshot[] Trades;public string CancelId;public int Cancels,Opens;public bool FailCancel;
        public IClientTradeService TradeService=>this;
        public IEnumerable<ClientTradeSnapshot> GetTrades()=>Trades;
        public void CancelTrade(string id){Cancels++;CancelId=id;if(FailCancel)throw new Exception("uncertain");}
        public void OpenTradeWindow(ClientTradeSnapshot trade){Opens++;}
        public void Log(Utils.LogEventArgs args){}
        public event EventHandler OnDisconnect;
        public event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;
        public event EventHandler<TradesSyncedEventArgs> OnTradesSynced;
        public event EventHandler<TradeCreationEventArgs> OnTradeCreationSuccess;
        public event EventHandler<TradeCompletionEventArgs> OnTradeCompleted,OnTradeCancelled;
        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess,OnTradeUpdateFailure;
        public void Finish(string id)=>OnTradeCancelled?.Invoke(this,new TradeCompletionEventArgs{TradeId=id});
        public void Response(ClientTradeSnapshot trade,string token,bool accepted=true)=>OnTradeUpdateSuccess?.Invoke(this,new TradeUpdateEventArgs{Trade=trade,Token=token,FailureReason=accepted?TradeFailureReason.None:TradeFailureReason.Rejected});
        public void Update(ClientTradeSnapshot trade)=>OnTradeUpdateSuccess?.Invoke(this,new TradeUpdateEventArgs{Trade=trade});
        public void Disconnect()=>OnDisconnect?.Invoke(this,EventArgs.Empty);
    }
    private sealed class TradePoints : IClientExtensionPointService,IClientExtensionPointScope,IClientActionPoint<TradePresentationActionContext>
    {
        public long Revision=>0;private Func<TradePresentationActionContext,IEnumerable<ClientActionDefinition>> factory;
        public LocalSet Last;public bool Closed;
        public IClientExtensionPointScope ForModule(IPhinixExtensionModule module)=>this;
        public IClientActionPoint<T> DefineActions<T>(string id) where T:class=>(IClientActionPoint<T>)(object)this;
        public void ContributeActions<T>(string id,Func<T,IEnumerable<ClientActionDefinition>> actionFactory,float order=0) where T:class=>factory=context=>actionFactory((T)(object)context);
        public IClientActionSet Collect(TradePresentationActionContext context,Func<bool> current=null)=>Last=new LocalSet(factory(context),current);
        public void Dispose(){Closed=true;Last?.Dispose();}
    }
    private sealed class CapturePresentation : IClientPresentationService,IClientPresentationSession
    {
        public ClientPresentationPage Page;private IClientActionSet set;public readonly Queue<ClientPresentationInput> Inputs=new Queue<ClientPresentationInput>();
        public IClientPresentationSession Open(IPhinixExtensionModule module,string page){IsClosed=false;PageId=page;return this;}
        public string OwnerId=>"test";public string PageId {get;private set;}public bool IsClosed {get;private set;}public string FailureCode=>null;
        public ClientActionExecutionResult LastActionResult=>null;
        public void Publish(ClientPresentationPage page,IClientActionSet actions,Func<bool> current){set?.Dispose();set=actions;Page=page;}
        public void Draw(ClientPresentationBounds bounds){}public float MeasureComposerHeight(float width)=>0;
        public bool TryDequeueInput(out ClientPresentationInput input){if(Inputs.Count==0){input=null;return false;}input=Inputs.Dequeue();return true;}
        public string ComposerDraft=>"";public string ComposerControlName=>"";public bool ComposerFocused=>false;
        public void SetComposerDraft(string value){}public void RequestComposerFocus(){}public void InvokeAction(string id){}
        public IReadOnlyList<ClientPresentationMenuItem> CreateActionMenuItems()=>Array.Empty<ClientPresentationMenuItem>();public void ScrollToRow(string feed,string row){}public void ScrollToEnd(){}
        public void Dispose(){IsClosed=true;set?.Dispose();set=null;Inputs.Clear();}
    }
    private static void CheckTradeListProjection()
    {
        var first=new ClientTradeSnapshot("one",new ImmutableUser("peer","One",true,true),new[]{new TradeItemSnapshot()},Array.Empty<TradeItemSnapshot>());
        var second=new ClientTradeSnapshot("two",new ImmutableUser("peer2","Two",true,true),Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>());
        var fixture=new TradeFixture{Trades=new[]{first,second}};var renderer=new CapturePresentation();var points=new TradePoints();var theme=new Theme();
        using(var list=new TradeList(fixture))
        {
            list.Start(new Module(),renderer,points,theme);list.ViewOpened();
            Check(renderer.Page.SplitView==null&&renderer.Page.FixedHeaderSectionCount==1&&renderer.Page.Sections[1].List.Rows.Count==2,"Trade list restores full-width rows with pinned search");
            Check(points.Last.Actions.Count==0,"No trade selection exposes no detail mutation actions");
            var direct=renderer.Page.Sections[1].List.Rows[1].Actions.Single(a=>a.Action.Id=="open");
            Check(direct.Action.TryExecute().Completed&&fixture.Opens==1,"Row open works without selecting a trade first");
            list.ViewUpdate();
            var directCancel=renderer.Page.Sections[1].List.Rows[0].Actions.Single(a=>a.Action.Id=="cancel");
            Check(directCancel.Confirmation!=null&&fixture.Cancels==0,"Each direct cancellation retains public confirmation");
            var row=renderer.Page.Sections[1].List.Rows[0];renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,"trades",ClientPresentationInputKind.RowSelected,row.Id));list.ViewUpdate();
            Check(renderer.Page.Sections.Single(s=>s.Id=="our-quote").List.Rows.Count==1 && renderer.Page.Sections.Single(s=>s.Id=="their-quote").List.Rows.Count==0,"Selected quote preserves separate actual sides including empty gift side");
            Check(renderer.Page.Sections.Single(s=>s.Id=="our-quote").List.IsReadOnly,"Quote browsing cannot submit row-selection or item mutation intent");
            var oldCancel=points.Last.Actions.Single(a=>a.Id=="cancel");var binding=renderer.Page.Sections[2].Actions.Single(a=>a.Action.Id=="cancel");
            Check(binding.Confirmation!=null&&fixture.Cancels==0,"Cancel intent requires public confirmation; projection submits nothing");
            fixture.Update(first);Check(!oldCancel.TryExecute().Completed&&fixture.Cancels==0,"Incoming snapshot invalidates old cancellation before view update");list.ViewUpdate();
            Check(points.Last.Actions.Single(a=>a.Id=="cancel").TryExecute().Completed&&fixture.Cancels==1&&fixture.CancelId=="one","Explicit cancel intent submits selected ID once");list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows[0].Actions.Single(a=>a.Action.Id=="cancel").Action.DisabledReason!=null,"Pending row cannot repeat cancellation while awaiting authority");
            Check(renderer.Page.Sections[1].List.Rows.Count==2&&renderer.Page.Sections[1].List.Rows[0].Badges.Count==1,"Submitted callback retains active trade and displays waiting; no completion inferred");
            var beforeClose=points.Last.Actions.Single(a=>a.Id=="cancel");list.ViewClosed();
            Check(!beforeClose.TryExecute().Completed&&fixture.Cancels==1,"Closing view invalidates action and submits no cancellation");list.ViewOpened();
            Check(renderer.Page.Sections[1].List.Rows[0].Badges.Count==1,"Reopening preserves request display without resubmission");
            int translated=TranslateStub.Calls;fixture.Finish("one");Check(TranslateStub.Calls==translated,"Completion callback changes owner data without translating or drawing");list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==1&&points.Last.Actions.Count==0,"Real completion removes only its row and clears stale selection");
            fixture.Update(null);list.ViewUpdate();Check(renderer.Page.Sections[1].List.Rows.Count==1,"Malformed update without trade does not crash or retarget");
            var finalRow=renderer.Page.Sections[1].List.Rows[0];renderer.Inputs.Enqueue(new ClientPresentationInput(renderer.Page.Revision,"trades",ClientPresentationInputKind.RowSelected,finalRow.Id));list.ViewUpdate();
            fixture.Update(new ClientTradeSnapshot("two",default(ImmutableUser),Array.Empty<TradeItemSnapshot>(),Array.Empty<TradeItemSnapshot>()));list.ViewUpdate();
            Check(points.Last.Actions.Single(a=>a.Id=="open").DisabledReason!=null&&renderer.Page.Sections[1].List.Rows.Count==1,"Missing peer facts retain identifiable trade while disabling offer editing");
            fixture.FailCancel=true;points.Last.Actions.Single(a=>a.Id=="cancel").TryExecute();list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==1&&renderer.Page.Sections[0].Status.Text.Contains("uncertain"),"Submission exception preserves trade and reports uncertainty");
            translated=TranslateStub.Calls;fixture.Disconnect();Check(TranslateStub.Calls==translated,"Disconnect callback does not run UI translation");list.ViewUpdate();
            Check(renderer.Page.Sections[1].List.Rows.Count==0&&points.Last.Actions.Count==0,"Disconnect clears projection without generating command or return");
        }
        Check(points.Closed&&renderer.IsClosed,"Module adapter disposal closes point and view");
    }
}
