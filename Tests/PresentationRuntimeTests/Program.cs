using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using UnityEngine;
using Utils.Framework;
using Verse;

internal static partial class Program
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if (!value) throw new Exception(message); }
    private static void Reject(Action action,string message) { bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message); }
    private sealed class Module : IPhinixExtensionModule { public string ExtensionId=>"test"; }
    private sealed class Lifetime : IClientLifetimeService,IClientLifetimeScope
    {
        public string OwnerId=>"test"; public bool IsClosed { get; private set; } private IDisposable resource;
        public IClientLifetimeScope ForModule(IPhinixExtensionModule m){IsClosed=false;return this;}
        public void Own(IDisposable r){resource=r;}
        public void Dispose(){if(IsClosed)return;IsClosed=true;resource?.Dispose();resource=null;}
    }
    private sealed class ActionSet : IClientActionSet
    { public int Disposals; public IReadOnlyList<IClientContributedAction> Actions { get; set; } public void Dispose(){Disposals++;} }
    private sealed class Command : IClientContributedAction
    { public int Calls; public string Label=>"execute"; public string DisabledReason=>null; public ClientActionExecutionResult TryExecute(){Calls++;return new ClientActionExecutionResult{Completed=true};} }
    private sealed class ClosingCommand : IClientContributedAction
    {public Action Close;public string Label=>"close-footer";public string DisabledReason=>null;public ClientActionExecutionResult TryExecute(){Close();return new ClientActionExecutionResult{Completed=true};}}
    private sealed class LongCommand : IClientContributedAction
    { public string Label => new string('x', 50); public string DisabledReason => null; public ClientActionExecutionResult TryExecute() => new ClientActionExecutionResult { Completed = true }; }
    private sealed class Region : IClientPresentationRegion
    { public float Measure(float width,IClientThemeReader t)=>float.NaN; public void Draw(IClientPresentationDrawContext context)=>throw new Exception("region"); }
    private sealed class Theme : IClientTheme
    {
        private long version;
        public long Version=>version; public event Action Changed;
        public void Change(){version++;Changed?.Invoke();}
        public PhinixClient.Framework.ClientRgba PrimaryText=>new ClientRgba(1,1,1);public PhinixClient.Framework.ClientRgba SecondaryText=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba Background=>default(ClientRgba);public PhinixClient.Framework.ClientRgba Surface=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba Separator=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba HoverHighlight=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba Pending=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba Error=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba Success=>new ClientRgba(.5f,.5f,.5f);public PhinixClient.Framework.ClientRgba Warning=>new ClientRgba(.5f,.5f,.5f);
        public PhinixClient.Framework.ClientRgba GetColor(string k)=>new ClientRgba(1,1,1);public bool TryGetColor(string k,out PhinixClient.Framework.ClientRgba c){c=new ClientRgba(1,1,1);return true;}public float GetFloat(string k,float d=0)=>d;
        public ClientThemeSnapshot Capture(){var colors=new Dictionary<string,ClientRgba>();foreach(var k in new[]{"ui.text.primary","ui.text.secondary","ui.background","ui.surface","ui.separator","ui.hover","ui.pending","ui.error","ui.success","ui.warning","ui.accent","ui.button.background","ui.button.hover","ui.button.pressed","ui.control.disabled","ui.input.background","ui.input.focus"})colors[k]=new ClientRgba(1,1,1);colors["ui.navigation.selected"]=new ClientRgba(.321f,.432f,.543f);colors["plugin.rule"]=new ClientRgba(.317f,.159f,.265f);colors["plugin.hover"]=new ClientRgba(.123f,.234f,.345f);colors["plugin.selected"]=new ClientRgba(.456f,.567f,.678f);return new ClientThemeSnapshot(version,colors,new Dictionary<string,float>());}
    }
    private static ClientPresentationPage Page(long revision,ClientPresentationAction binding=null,ClientPresentationRegion region=null)
    {return new ClientPresentationPage("page","title",revision,new[]{new ClientPresentationSection("section","","",binding==null?null:new[]{binding},fields:new[]{new ClientPresentationTextField("filter","Filter","")},region:region)});}
    public static void Main()
    {
        CheckInventoryInline(); CheckInventoryPlans();
        CheckWindowThemeDrawing();
        CheckLocalFileAdapter();
        CheckListThemeRoles();
        CheckTradeQuantityDraft();
        CheckTradeListProjection();
        CheckRowControlsAndSharedFooter();
        CheckInlineTradeOfferDrafts();
        CheckButtonStateContrast(); CheckCompactPresentationLayout(); CheckNicknameDraft(); CheckMemberSidebarDensity();
        CheckReadOnlyList();
        CheckImageLeases();
        CheckImageSlots();
        CheckDrawContext();
        CheckMenuSnapshots();
        var roleList=new ClientPresentationList("roles",new[]{new ClientPresentationRow("one","One")},"", "one");
        var colored=roleList.WithColors("plugin.hover","plugin.selected");
        Check(roleList.HoverColorKey==null&&colored.HoverColorKey=="plugin.hover"&&colored.SelectedId=="one","Public list roles are immutable and preserve selection");
        var readRoles=colored.WithReadOnly();
        Check(readRoles.IsReadOnly&&readRoles.SelectedId==null&&readRoles.SelectionColorKey=="plugin.selected","Readonly conversion preserves theme roles and clears interaction selection");
        Check(readRoles.WithColors(null,"unknown.key").IsReadOnly&&readRoles.HoverColorKey=="plugin.hover","Role replacement preserves readonly and unknown keys without mutating old projection");
        Reject(()=>roleList.WithColors("bad key",null),"Invalid role identity rejected");
        CheckInventoryRecoveryProjection();
        CheckTradeOfferOwner();
        CheckTradeRecoveryProjection();
        var life=new Lifetime();var theme=new Theme();bool current=true,mainThread=true;
        var service=new ClientPresentationService(life,theme,()=>mainThread,_=>{});
        var session=service.Open(new Module(),"page");var command=new Command();var set=new ActionSet{Actions=new[]{command}};
        var binding=new ClientPresentationAction("execute",command);
        session.Publish(Page(1,binding),set,()=>current);
        var menu = session.CreateActionMenuItems();
        Check(menu.Count == 1 && menu[0].Label == command.Label, "Native menu uses published owned binding");
        current = false; Event.current.type = EventType.MouseDown; menu[0].Request();
        Check(command.Calls == 0, "Native menu rejects stale context");
        Check(session.CreateActionMenuItems()[0].Request == null, "Stale menu options are disabled");
        current = true;
        var rejected=new ActionSet{Actions=new IClientContributedAction[0]};
        Reject(()=>session.Publish(Page(2,binding),rejected,()=>true),"Foreign action rejected before transfer");Check(set.Disposals==0&&rejected.Disposals==0,"Failed publication retains prior owner and caller set");
        Reject(()=>session.Publish(Page(1),null,()=>true),"Old revision rejected");
        Reject(()=>session.Publish(Page(2,binding),set,()=>true),"Owned set cannot be reused");
        Event.current.type=EventType.Layout;Widgets.ClickLabel="execute";session.Draw(new ClientPresentationBounds(0,0,600,500));Check(command.Calls==0,"Layout never executes commands");
        Event.current.type=EventType.MouseDown;Widgets.ClickLabel="execute";session.Draw(new ClientPresentationBounds(0,0,600,500));Check(command.Calls==1,"Explicit input executes once");
        Widgets.ClickLabel="execute";session.Draw(new ClientPresentationBounds(0,0,600,500));Check(command.Calls==1,"Same binding cannot execute twice");
        var draftSet=new ActionSet{Actions=new[]{command}};session.Publish(Page(2,binding),draftSet,()=>current);
        Event.current.type=EventType.KeyDown;Widgets.FieldValue="unconsumed";session.Draw(new ClientPresentationBounds(0,0,600,500));
        Event.current.type=EventType.MouseDown;Widgets.ClickLabel="execute";session.Draw(new ClientPresentationBounds(0,0,600,500));Check(command.Calls==1,"Unconsumed drafts cannot execute old projected arguments");
        session.Publish(Page(3),null,()=>current);Check(set.Disposals==1,"Replacement releases transferred actions");
        Event.current.type=EventType.KeyDown;Widgets.FieldValue="query";session.Draw(new ClientPresentationBounds(0,0,600,500));Check(session.TryDequeueInput(out var input)&&input.Value=="query"&&input.Revision==3,"Fields emit revision-scoped draft input");
        Widgets.FieldValue="old";session.Draw(new ClientPresentationBounds(0,0,600,500));session.Publish(Page(4),null,()=>current);Check(!session.TryDequeueInput(out input),"Publish invalidates queued input");
        Widgets.FieldValue="stale";current=false;session.Draw(new ClientPresentationBounds(0,0,600,500));Check(!session.TryDequeueInput(out input),"Stale context emits no usable input");current=true;
        session.Publish(Page(5,region:new ClientPresentationRegion("region",new Region())),null,()=>current);session.Draw(new ClientPresentationBounds(0,0,600,500));Check(session.FailureCode=="RegionMeasureFailed","Region failure isolated and reported");theme.Change();session.Draw(new ClientPresentationBounds(0,0,600,500));
        mainThread=false;Reject(()=>session.TryDequeueInput(out input),"Input drain enforces main thread");mainThread=true;
        var confirmSet=new ActionSet{Actions=new[]{command}};session.Publish(Page(6,new ClientPresentationAction("execute",command,confirmation:new ClientPresentationConfirmation("Confirm","Body","Accept","Cancel"))),confirmSet,()=>current);
        Widgets.ClickLabel="execute";Event.current.type=EventType.MouseDown;session.Draw(new ClientPresentationBounds(0,0,600,500));var confirmation=Find.WindowStack.Last;Check(confirmation!=null&&command.Calls==1,"Confirmation does not execute early");
        life.Dispose();Widgets.ClickLabel="Accept";confirmation.DoWindowContents(new Rect(0,0,500,350));Check(session.IsClosed&&command.Calls==1&&confirmSet.Disposals==1,"Module stop closes view and invalidates confirmation");
        session.Dispose();Check(confirmSet.Disposals==1,"Close is idempotent");
        var composerSession=service.Open(new Module(),"dock");var submitSet=new ActionSet{Actions=new[]{command}};
        composerSession.Publish(new ClientPresentationPage("dock","",1,null,new ClientPresentationComposer(new ClientPresentationTextField("draft","",""),binding)),submitSet,()=>current);
        GUI.FocusControl(composerSession.ComposerControlName);Input.compositionString="中";Event.current.type=EventType.KeyDown;Event.current.keyCode=KeyCode.Return;
        composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(!composerSession.TryDequeueInput(out input),"IME composition Enter does not submit");
        Input.compositionString="";Event.current.type=EventType.KeyDown;composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(composerSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.SubmitRequested,"Focused Enter emits explicit intent");
        Event.current.type=EventType.KeyDown;Widgets.ConsumeFieldEvent=true;composerSession.Draw(new ClientPresentationBounds(0,0,600,64));
        Check(composerSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.SubmitRequested&&!composerSession.TryDequeueInput(out input),"Text field consuming unchanged Return still emits exactly one submit intent");
        foreach(var key in new[]{KeyCode.Return,KeyCode.KeypadEnter})
        {
            Event.current.type=EventType.KeyDown;Event.current.keyCode=key;Widgets.ConsumeFieldEvent=true;Input.compositionString="中";
            composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(!composerSession.TryDequeueInput(out input),"Consumed IME confirmation does not submit: "+key);
            Input.compositionString="";Event.current.type=EventType.KeyDown;Event.current.keyCode=key;Widgets.ConsumeFieldEvent=true;
            composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(composerSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.SubmitRequested&&!composerSession.TryDequeueInput(out input),"Consumed submit works with either Enter key: "+key);
            composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(!composerSession.TryDequeueInput(out input),"Already consumed event cannot submit twice: "+key);
        }
        GUI.FocusControl("");Event.current.type=EventType.KeyDown;Widgets.ConsumeFieldEvent=true;
        composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(!composerSession.TryDequeueInput(out input),"Unfocused consumed Enter does not submit");
        GUI.FocusControl(composerSession.ComposerControlName);
        Event.current.type=EventType.KeyDown;Widgets.ConsumeFieldEvent=true;Widgets.FieldValue="中文";composerSession.Draw(new ClientPresentationBounds(0,0,600,64));
        Check(composerSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.TextChanged&&input.Value=="中文"&&!composerSession.TryDequeueInput(out input),"Return committing text retains it without submitting");
        foreach(var pass in new[]{EventType.Layout,EventType.Repaint})
        {
            Event.current.type=pass;Widgets.FieldValue=pass.ToString()+"中文";composerSession.Draw(new ClientPresentationBounds(0,0,600,64));
            Check(composerSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.TextChanged&&input.Value==composerSession.ComposerDraft,"Asynchronous bridge commit survives GUI pass: "+pass);
            composerSession.Draw(new ClientPresentationBounds(0,0,600,64));Check(!composerSession.TryDequeueInput(out input),"Unchanged pass does not duplicate bridge input: "+pass);
        }
        Check(command.Calls==1,"Async text acceptance never executes a business action");
        var fieldService=new ClientPresentationService(new Lifetime(),theme,()=>true,_=>{});
        var fieldSession=fieldService.Open(new Module(),"page");fieldSession.Publish(Page(1),null,()=>true);
        foreach(var pass in new[]{EventType.Layout,EventType.Repaint})
        {
            Event.current.type=pass;Widgets.FieldValue=pass.ToString()+"字段";fieldSession.Draw(new ClientPresentationBounds(0,0,600,500));
            Check(fieldSession.TryDequeueInput(out input)&&input.Value==pass.ToString()+"字段","Ordinary public field accepts bridge commit: "+pass);
        }
        fieldSession.Dispose();
        GUI.FocusControl("");
        Event.current.type=EventType.KeyDown;Widgets.FieldValue="latest";composerSession.Draw(new ClientPresentationBounds(0,0,600,64));
        Event.current.type=EventType.MouseDown;Widgets.ClickLabel="execute";composerSession.Draw(new ClientPresentationBounds(0,0,600,64));
        Check(command.Calls==1,"Composer input never dispatches business during Draw");
        bool submitted=false;while(composerSession.TryDequeueInput(out input)) if(input.Kind==ClientPresentationInputKind.SubmitRequested){submitted=input.Value=="latest";}
        Check(submitted,"Composer submit intent carries latest draft");
        var validatedSet=new ActionSet{Actions=new[]{command}};composerSession.Publish(new ClientPresentationPage("dock","",2,null,new ClientPresentationComposer(new ClientPresentationTextField("draft","","latest"),binding)),validatedSet,()=>current);
        composerSession.InvokeAction("execute");Check(command.Calls==2,"Validated intent invokes the existing owned action");
        composerSession.Dispose();Reject(()=>composerSession.SetComposerDraft("late"),"Closed composer rejects late UI changes");
        var rows=new List<ClientPresentationRow>{new ClientPresentationRow("one","One")};var list=new ClientPresentationList("list",rows,"Empty");rows.Clear();Check(list.Rows.Count==1,"Projection defensively copies rows");Reject(()=>new ClientPresentationList("list",list.Rows,"Empty","missing"),"Missing selection rejected");
        var feedService=new ClientPresentationService(new Lifetime(),theme,()=>true,_=>{});
        var feedSession=feedService.Open(new Module(),"feed");
        var feedRows=new List<ClientPresentationFeedRow>{new ClientPresentationFeedRow("row","Heading","time","Body", media:new[]{new ClientPresentationMedia("image",ClientPresentationMediaState.Idle,"Waiting")})};
        var feed=new ClientPresentationFeed("stream",feedRows,"Empty");feedRows.Clear();Check(feed.Rows.Count==1,"Feed defensively copies rows");
        Reject(()=>new ClientPresentationFeed("duplicate",new[]{feed.Rows[0],feed.Rows[0]},""),"Feed rejects duplicate row identity");
        Reject(()=>new ClientPresentationMedia("invalid",ClientPresentationMediaState.Ready,"",aspectRatio:float.NaN),"Media rejects nonfinite geometry");
        feedSession.Publish(new ClientPresentationPage("feed","",1,new[]{new ClientPresentationSection("stream-section",feed)}),null,()=>current);
        Event.current.type=EventType.Layout;Widgets.InvisibleClicks=1;feedSession.Draw(new ClientPresentationBounds(0,0,600,400));Check(!feedSession.TryDequeueInput(out input),"Feed Layout neither selects nor requests media");
        Widgets.InvisibleClicks=0;Event.current.type=EventType.Repaint;feedSession.Draw(new ClientPresentationBounds(0,0,600,400));
        Check(feedSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.FeedMediaVisible&&input.Value=="row"&&input.Target=="image","Visible media is a revision-scoped owner intent");
        feedSession.Draw(new ClientPresentationBounds(0,0,600,400));Check(!feedSession.TryDequeueInput(out input),"Repeated Repaint does not duplicate media requests");
        Event.current.type=EventType.MouseDown;Widgets.InvisibleClicks=1;feedSession.Draw(new ClientPresentationBounds(0,0,600,400));
        Check(feedSession.TryDequeueInput(out input)&&input.Kind==ClientPresentationInputKind.FeedHeadingSelected,"Feed heading emits a selection, not a business command");
        feedSession.ScrollToRow("stream","row");feedSession.ScrollToEnd();
        Event.current.type=EventType.MouseDown;Widgets.InvisibleClicks=1;feedSession.Draw(new ClientPresentationBounds(0,0,600,400));
        feedSession.Publish(new ClientPresentationPage("feed","",2,new[]{new ClientPresentationSection("stream-section",feed)}),null,()=>current);
        Check(!feedSession.TryDequeueInput(out input),"Replacing feed invalidates queued row input");
        current=false;Widgets.InvisibleClicks=1;feedSession.Draw(new ClientPresentationBounds(0,0,600,400));Check(!feedSession.TryDequeueInput(out input),"Stale feed emits no usable input");current=true;
        feedSession.Dispose();Reject(()=>feedSession.ScrollToEnd(),"Closed feed rejects late scroll intent");
        var mediaSession = service.Open(new Module(), "media-saturation");
        var manyMedia = new System.Collections.Generic.List<ClientPresentationMedia>();
        for (int i = 0; i < 32; i++) manyMedia.Add(new ClientPresentationMedia("media." + i, ClientPresentationMediaState.Idle, "idle"));
        var saturationFeed = new ClientPresentationFeed("media-feed", new[] {
            new ClientPresentationFeedRow("first", "", "", "", media: manyMedia),
            new ClientPresentationFeedRow("second", "", "", "", media: new[] { new ClientPresentationMedia("last", ClientPresentationMediaState.Idle, "idle") })
        }, "", false);
        mediaSession.Publish(new ClientPresentationPage("media-saturation", "", 1,
            new[] { new ClientPresentationSection("media-stream", saturationFeed) }), null, () => true);
        Event.current.type = EventType.Repaint; mediaSession.Draw(new ClientPresentationBounds(0, 0, 600, 8000));
        int delivered = 0; while (mediaSession.TryDequeueInput(out input)) delivered++;
        Check(delivered == 32, "Visible media delivery respects bounded input queue");
        mediaSession.Draw(new ClientPresentationBounds(0, 0, 600, 8000));
        Check(mediaSession.TryDequeueInput(out input) && input.Value == "second" && !mediaSession.TryDequeueInput(out input),
            "Undelivered visible media retries without republishing or duplicating delivered events");
        mediaSession.Dispose();
        var integerSession = service.Open(new Module(), "integer");
        integerSession.Publish(new ClientPresentationPage("integer", "", 1, new[] { new ClientPresentationSection("number", "", "",
            fields: new[] { ClientPresentationTextField.Integer("quantity", "Quantity", "1") }) }), null, () => true);
        Event.current.type = EventType.KeyDown; Widgets.FieldValue = "9223372036854775807";
        integerSession.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        Check(integerSession.TryDequeueInput(out input) && input.Value == long.MaxValue.ToString(), "Integer field retains full Int64 text without float conversion");
        Widgets.FieldValue = "9223372036854775808"; integerSession.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        Check(integerSession.TryDequeueInput(out input) && !long.TryParse(input.Value, out _), "Integer overflow remains an owner validation draft rather than clamping to another quantity");
        Widgets.FieldValue = "-"; integerSession.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        Check(integerSession.TryDequeueInput(out input) && input.Value == "-", "Integer syntax permits intermediate minus draft");
        Widgets.FieldValue = "abc"; integerSession.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        Check(!integerSession.TryDequeueInput(out input) && Widgets.LastValidator != null, "Host applies numeric syntax hint without business execution");
        integerSession.Dispose();
        var longSummary = new string('x', 5000);
        Check(new ClientPresentationSection("long", "", longSummary).Summary.Length == 5000, "Long package metadata stays intact in shared section summaries");
        Check(new ClientPresentationConfirmation("Plan", longSummary, "Confirm", "Cancel").Text.Length == 5000, "Frozen confirmation supports complete multi-package summaries");
        Reject(() => new ClientPresentationConfirmation("Plan", new string('x', 65537), "Confirm", "Cancel"), "Confirmation text remains bounded without silently truncating the transaction summary");
        Reject(() => new ClientPresentationSplitView(0, "Browse", "Details"), "Split requires nonempty primary pane");
        Reject(() => new ClientPresentationSplitView(1, "Browse", "Details", float.NaN), "Split rejects invalid geometry");
        Reject(() => Page(1).WithSplitView(new ClientPresentationSplitView(1, "Browse", "Details")), "Split requires both section groups");
        var splitSession = service.Open(new Module(), "split");
        var splitRows = new List<ClientPresentationRow>();
        for (int i = 0; i < 100; i++) splitRows.Add(new ClientPresentationRow("row." + i, "Row " + i));
        Func<long, string, ClientPresentationPage> splitPage = (revision, selected) => new ClientPresentationPage("split", "", revision, new[] {
            new ClientPresentationSection("browse", "Browse", "", list: new ClientPresentationList("catalog", splitRows, "", selected)),
            new ClientPresentationSection("detail", "Details", "Selected package", new[] { binding })
        }).WithSplitView(new ClientPresentationSplitView(1, "Browse", "Details"));
        splitSession.Publish(splitPage(1, null), new ActionSet { Actions = new[] { command } }, () => current);
        Widgets.FieldValue = null; Widgets.ClickLabel = null; Widgets.ScrollStarts.Clear(); Widgets.NextScrollY = 300;
        Event.current.type = EventType.Repaint; splitSession.Draw(new ClientPresentationBounds(0, 0, 1000, 400));
        Check(Widgets.ScrollStarts.Count == 2, "Wide split renders two independent scroll viewports");
        Widgets.ScrollStarts.Clear(); splitSession.Draw(new ClientPresentationBounds(0, 0, 1000, 400));
        Check(Widgets.ScrollStarts[0] == 300 && Widgets.ScrollStarts[1] == 0, "Catalogue scroll does not move details");
        splitSession.Publish(splitPage(2, "row.50"), new ActionSet { Actions = new[] { command } }, () => current);
        Widgets.ScrollStarts.Clear(); splitSession.Draw(new ClientPresentationBounds(0, 0, 1000, 400));
        Check(Widgets.ScrollStarts[0] == 300 && Widgets.ScrollStarts[1] == 0, "Selection refresh preserves catalogue position");
        Widgets.ScrollStarts.Clear(); splitSession.Draw(new ClientPresentationBounds(0, 0, 500, 400));
        Check(Widgets.ScrollStarts.Count == 1, "Narrow split shows one pane instead of stacked long page");
        int splitCalls = command.Calls;
        Widgets.ClickLabel = "execute"; Event.current.type = EventType.MouseDown; splitSession.Draw(new ClientPresentationBounds(0, 0, 500, 400));
        Check(command.Calls == splitCalls + 1, "Narrow details retain same owned action execution");
        current = false; splitSession.Publish(splitPage(3, "row.50"), new ActionSet { Actions = new[] { command } }, () => current);
        Widgets.ClickLabel = "execute"; splitSession.Draw(new ClientPresentationBounds(0, 0, 1000, 400));
        Check(command.Calls == splitCalls + 1, "Split actions reject stale owner context"); current = true;
        splitSession.Dispose();
        var footerSession = service.Open(new Module(), "footer-demo");
        var fieldSection = new ClientPresentationSection("draft", "", "", new[] { binding }, fields: new[] {
            new ClientPresentationTextField("amount", "Amount", "1"), new ClientPresentationTextField("portions", "Portions", "1") }).WithFieldColumns().WithPlainLayout();
        Check(fieldSection.WithWrappedActions().FieldColumns == 2, "Field column hints survive immutable section copies");
        Reject(() => fieldSection.WithFieldColumns(0), "Invalid field columns are rejected");
        Reject(() => new ClientPresentationSplitView(2,"Left","Right").WithFixedHeaders(0,1,0).WithFixedFooters(1,0), "Footer cannot consume the primary body");
        var dockedPage = new ClientPresentationPage("footer-demo", "", 1, new[] {
            new ClientPresentationSection("search", "Search", ""), new ClientPresentationSection("items", "", "", list:new ClientPresentationList("choices",splitRows,"")), fieldSection,
            new ClientPresentationSection("other", "Other", "") }).WithSplitView(new ClientPresentationSplitView(3,"Left","Right").WithFixedHeaders(0,1,0).WithFixedFooters(1,0));
        Check(dockedPage.SplitView.WithAlignedHeaders().PrimaryFooterSections == 1, "Split copies retain fixed footer hints");
        footerSession.Publish(dockedPage,new ActionSet { Actions=new[] { command } },()=>true);
        Event.current.type=EventType.Repaint;Widgets.Labels.Clear();Widgets.ScrollStarts.Clear();footerSession.Draw(new ClientPresentationBounds(0,0,1200,900));
        var amountLabel=Widgets.Labels.Find(l=>l.Text=="Amount");var portionLabel=Widgets.Labels.Find(l=>l.Text=="Portions");
        Check(amountLabel.Rect.y==portionLabel.Rect.y&&amountLabel.Rect.x<portionLabel.Rect.x,"Wide footer fields occupy the same row");
        Check(Widgets.ScrollStarts.Count==3,"Fixed footer, item body and secondary pane have independent scrolling");
        Widgets.Labels.Clear();footerSession.Draw(new ClientPresentationBounds(0,0,350,900));
        amountLabel=Widgets.Labels.Find(l=>l.Text=="Amount");portionLabel=Widgets.Labels.Find(l=>l.Text=="Portions");
        Check(portionLabel.Rect.y>amountLabel.Rect.y,"Field columns collapse at the declared minimum width");
        int footerCalls=command.Calls;Widgets.ClickLabel="execute";Event.current.type=EventType.MouseDown;footerSession.Draw(new ClientPresentationBounds(0,0,1200,900));
        Check(command.Calls==footerCalls+1,"Fixed footer executes the same public owned action");
        var closeFooter=new ClosingCommand {Close=footerSession.Dispose};var closeBinding=new ClientPresentationAction("close",closeFooter);
        footerSession.Publish(new ClientPresentationPage("footer-demo","",2,new[]{
            new ClientPresentationSection("body","Body",""), new ClientPresentationSection("dock","","",new[]{closeBinding}), new ClientPresentationSection("secondary","Other","")
        }).WithSplitView(new ClientPresentationSplitView(2,"Left","Right").WithFixedFooters(1,0)),new ActionSet {Actions=new[]{closeFooter}},()=>true);
        Widgets.ClickLabel="close-footer";footerSession.Draw(new ClientPresentationBounds(0,0,1200,900));Check(footerSession.IsClosed,"An action can close its view during fixed-footer Draw without dereferencing the disposed page");
        var plainRuleRow=new ClientPresentationFeedRow("rule-row","Name","time","Body");
        var ruledRow=plainRuleRow.WithLeadingSeparator("plugin.rule").WithMetadataLayout(96,12).WithMetadataPlacement(ClientPresentationMetadataPlacement.AfterHeading);
        Check(plainRuleRow.LeadingSeparatorKey==null && ruledRow.LeadingSeparatorKey=="plugin.rule","Feed decorations are opt-in immutable values preserved across metadata copies");
        Check(ClientPresentationFeedMetrics.FeedRowHeight(plainRuleRow,600)==ClientPresentationFeedMetrics.FeedRowHeight(ruledRow.WithMetadataLayout(),600),"Feed separator does not add height or change message layout");
        Reject(()=>plainRuleRow.WithLeadingSeparator("bad key"),"Feed decoration rejects invalid theme keys");
        using(var ruleSession=feedService.Open(new Module(),"rule-render"))
        {
            ruleSession.Publish(new ClientPresentationPage("rule-render","",1,new[]{new ClientPresentationSection("rules",new ClientPresentationFeed("rules",new[]{ruledRow},""))}),null,()=>true);
            Widgets.FilledBoxes.Clear();Event.current.type=EventType.Repaint;ruleSession.Draw(new ClientPresentationBounds(0,0,600,300));
            Check(Widgets.FilledBoxes.Any(b=>b.Item1.height==1 && b.Item2.r==.317f),"Actual feed renderer paints the owner separator in the existing gutter");
        }
        var rightMetadata = new ClientPresentationFeedRow("metadata", "Name", "12:34", "Body");
        var leftMetadata = rightMetadata.WithMetadataPlacement(ClientPresentationMetadataPlacement.AfterHeading);
        Check(rightMetadata.MetadataPlacement == ClientPresentationMetadataPlacement.Right && leftMetadata.MetadataPlacement == ClientPresentationMetadataPlacement.AfterHeading,
            "Metadata placement is an immutable projection with current right-side default");
        var rightRect = ClientPresentationFeedMetrics.FeedMetadataRect(rightMetadata, 12, 6, 600);
        var leftRect = ClientPresentationFeedMetrics.FeedMetadataRect(leftMetadata, 12, 6, 600);
        Check(leftRect.x < rightRect.x && leftRect.xMax <= 612 && rightRect.xMax == 612, "Left metadata follows heading while right metadata aligns to row edge");
        var longName = new ClientPresentationFeedRow("long-name", new string('n', 100), "12:34", "Body").WithMetadataPlacement(ClientPresentationMetadataPlacement.AfterHeading);
        var narrowRect = ClientPresentationFeedMetrics.FeedMetadataRect(longName, 0, 0, 40);
        Check(narrowRect.x >= 0 && narrowRect.xMax <= 40, "Long heading metadata remains contained in narrow viewport");
        Check(ClientPresentationFeedMetrics.FeedRowHeight(rightMetadata, 600) == ClientPresentationFeedMetrics.FeedRowHeight(leftMetadata, 600),
            "Changing metadata placement preserves shared heading measurement and row offsets");
        var beforeMetadata = longName.WithMetadataLayout(64, 12).WithMetadataPlacement(ClientPresentationMetadataPlacement.BeforeHeading);
        foreach (float viewportWidth in new[] { 1f, 40f, 120f, 600f })
        {
            var timeRect = ClientPresentationFeedMetrics.FeedMetadataRect(beforeMetadata, 12, 6, viewportWidth);
            var nameRect = ClientPresentationFeedMetrics.FeedHeadingRect(beforeMetadata, 12, 6, viewportWidth, 28, timeRect.width);
            Check(timeRect.x == 12 && timeRect.xMax <= nameRect.x && nameRect.xMax <= 12 + viewportWidth,
                "Before-heading timestamp retains its own area ahead of long names, including narrow viewports");
        }
        var unchangedRightHeading = ClientPresentationFeedMetrics.FeedHeadingRect(rightMetadata, 12, 6, 600, 28, rightRect.width);
        Check(unchangedRightHeading.x == 12 && unchangedRightHeading.width == 600 - rightRect.width - rightMetadata.MetadataGap && rightRect.xMax == 612,
            "Right-side timestamp and heading retain their existing geometry");
        Check(ClientPresentationFeedMetrics.FeedRowHeight(beforeMetadata, 600) == ClientPresentationFeedMetrics.FeedRowHeight(beforeMetadata.WithMetadataPlacement(ClientPresentationMetadataPlacement.Right), 600),
            "Moving timestamp before a long name preserves wrapping and row height");
        Check(beforeMetadata.WithLeadingSeparator("plugin.rule").WithMetadataLayout(96, 16).MetadataPlacement == ClientPresentationMetadataPlacement.BeforeHeading,
            "Before-heading placement survives immutable row decoration and layout updates");
        Reject(() => rightMetadata.WithMetadataPlacement((ClientPresentationMetadataPlacement)77), "Unsupported metadata placement is rejected");
        Check(rightRect.width >= 64, "Short time metadata retains readable reservation in a wide feed");
        var configuredMetadata = rightMetadata.WithMetadataLayout(96, 16).WithMetadataPlacement(ClientPresentationMetadataPlacement.AfterHeading);
        var configuredRect = ClientPresentationFeedMetrics.FeedMetadataRect(configuredMetadata, 0, 0, 600);
        Check(configuredRect.width == 96 && configuredMetadata.MetadataGap == 16 && rightMetadata.MetadataMinimumWidth == 64,
            "Public metadata geometry composes immutably with placement");
        Check(configuredMetadata.WithMetadataPlacement(ClientPresentationMetadataPlacement.Right).MetadataMinimumWidth == 96,
            "Moving metadata preserves owner layout preferences");
        Reject(() => rightMetadata.WithMetadataLayout(float.NaN), "Metadata rejects non-finite dimensions");
        Reject(() => rightMetadata.WithMetadataLayout(513), "Metadata reservation is bounded");
        Reject(() => rightMetadata.WithMetadataLayout(64, -1), "Metadata gap rejects negative dimensions");
        Check(ClientPresentationFeedMetrics.MetadataWidth(new ClientPresentationFeedRow("no-meta", "Name", "", "Body"), 600) == 0,
            "Empty metadata does not consume header width");
        var metadataSession = feedService.Open(new Module(), "metadata-render");
        metadataSession.Publish(new ClientPresentationPage("metadata-render", "", 1, new[] {
            new ClientPresentationSection("metadata-section", new ClientPresentationFeed("metadata-feed", new[] { rightMetadata }, ""))
        }), null, () => current);
        Widgets.Labels.Clear(); int ellipsisBefore = Widgets.EllipsisCalls;
        Event.current.type = EventType.Repaint; metadataSession.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        bool timeRendered = false;
        foreach (var label in Widgets.Labels) if (label.Text == "12:34") timeRendered = label.Font == GameFont.Tiny && label.Rect.width >= 64;
        Check(timeRendered && Widgets.EllipsisCalls == ellipsisBefore,
            "Feed sends full timestamp to explicit Tiny label without native ellipsis substitution");
        metadataSession.Publish(new ClientPresentationPage("metadata-render", "", 2, new[] {
            new ClientPresentationSection("metadata-section", new ClientPresentationFeed("metadata-feed", new[] { beforeMetadata }, ""))
        }), null, () => current);
        Widgets.Labels.Clear(); metadataSession.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        Check(Widgets.Labels.Any(label => label.Text == "12:34" && label.Font == GameFont.Tiny && label.Rect.width >= 64)
            && Widgets.Labels.Any(label => label.Text == beforeMetadata.Heading && label.Rect.width < 600 - 64),
            "Actual renderer keeps the full timestamp and clips the long nickname to its reserved remainder");
        metadataSession.Dispose();
        var selectedBinding = binding.WithSelection();
        Check(!binding.IsSelected && selectedBinding.IsSelected && ReferenceEquals(binding.Action, selectedBinding.Action),
            "Selected action projection is immutable and retains original owned authority");
        var badge = new ClientPresentationBadge("official", "Owner supplied", ClientTextureImage.FromTexture(Texture2D.whiteTexture));
        var plainRow = new ClientPresentationRow("badge-row", "Package");
        var badgeRow = plainRow.WithBadges(new[] { badge });
        Check(plainRow.Badges.Count == 0 && badgeRow.Badges.Count == 1 && ImageTexture(badgeRow.Badges[0].Image, Texture2D.whiteTexture), "Borrowed badges retain original icon without mutating row");
        var badgeSection = new ClientPresentationSection("badge-section", "Title", "").WithBadges(new[] { badge });
        Check(!badgeSection.WrapActions && badgeSection.WithWrappedActions().Badges.Count == 1, "Badge and action-wrap projections compose independently");
        Reject(() => plainRow.WithBadges(new[] { badge, badge }), "Duplicate badge identity rejected");
        var pinned = service.Open(new Module(), "pinned");
        Func<long, string, ClientPresentationPage> pinnedPage = (revision, contextId) => new ClientPresentationPage("pinned", "", revision, new[] {
            new ClientPresentationSection("global", "Store tools", ""),
            new ClientPresentationSection("search-tools", "Search", ""),
            new ClientPresentationSection("catalog-pane", "", "", list: new ClientPresentationList("pinned-catalog", splitRows, "")),
            new ClientPresentationSection("selected-header", "Selected plugin", "", new[] { binding }).WithBadges(new[] { badge }),
            new ClientPresentationSection("details-pane", "", "", list: new ClientPresentationList("long-details", splitRows, ""))
        }).WithSplitView(new ClientPresentationSplitView(3, "Browse", "Details").WithFixedHeaders(1, 1, 1, contextId).WithAlignedHeaders());
        pinned.Publish(pinnedPage(1, "entry.a"), new ActionSet { Actions = new[] { command } }, () => true);
        Event.current.type = EventType.Repaint; Widgets.ScrollStarts.Clear(); Widgets.ScrollWorldY.Clear(); Widgets.NextScrollY = 300;
        pinned.Draw(new ClientPresentationBounds(0, 0, 1000, 1000));
        Check(Widgets.ScrollStarts.Count == 2, "Global tools and both pane headers remain fixed while only content scrolls");
        Check(Widgets.ScrollWorldY[0] == Widgets.ScrollWorldY[1], "Explicit aligned headers give catalogue and detail body the same baseline");
        Event.current.type = EventType.MouseDown; Widgets.ClickLabel = "Details"; Widgets.NextScrollY = 200; pinned.Draw(new ClientPresentationBounds(0, 0, 500, 1000));
        Event.current.type = EventType.Repaint; Widgets.ScrollStarts.Clear(); pinned.Draw(new ClientPresentationBounds(0, 0, 500, 1000));
        Check(Widgets.ScrollStarts.Count == 1 && Widgets.ScrollStarts[0] == 200, "Details body scroll remains independent beneath fixed actions");
        pinned.Publish(pinnedPage(2, "entry.b"), new ActionSet { Actions = new[] { command } }, () => true);
        Widgets.ScrollStarts.Clear(); pinned.Draw(new ClientPresentationBounds(0, 0, 500, 1000));
        Check(Widgets.ScrollStarts[0] == 0, "Owner context identity resets details even when selected list row is absent or unchanged");
        Widgets.ScrollStarts.Clear(); pinned.Draw(new ClientPresentationBounds(0, 0, 1000, 1000));
        Check(Widgets.ScrollStarts[0] == 300, "New detail context preserves catalogue scroll position");
        pinned.Draw(new ClientPresentationBounds(0, 0, 500, 200)); Check(pinned.FailureCode == null, "Short-height fixed headers degrade without negative or failing layout");
        Reject(() => pinnedPage(3, "entry.c").WithSplitView(new ClientPresentationSplitView(3, "Browse", "Details").WithFixedHeaders(1, 1, 2)), "Pinned layout requires scrollable secondary content");
        pinned.Dispose();
        var emptyCard = service.Open(new Module(), "empty-card");
        emptyCard.Publish(new ClientPresentationPage("empty-card", "", 1, new[] { new ClientPresentationSection("empty", "", "") }), null, () => true);
        GUI.SurfaceCalls = 0; emptyCard.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        Check(GUI.SurfaceCalls == 0, "Empty sections do not produce blank surface cards"); emptyCard.Dispose();
        var menuModel = new ClientPresentationActionMenu("tools", "More", new[] { binding.Id });
        var menuSection = new ClientPresentationSection("menu", "", "", new[] { binding }).WithWrappedActions().WithPlainLayout().WithActionMenus(new[] { menuModel });
        Check(menuSection.Plain && menuSection.ActionMenus.Count == 1 && menuSection.Actions.Count == 1, "Grouped menus retain owned bindings in flat section projection");
        Reject(() => new ClientPresentationActionMenu("empty-menu", "More", Array.Empty<string>()), "Empty menu rejected before native window construction");
        Reject(() => menuSection.WithActionMenus(new[] { new ClientPresentationActionMenu("foreign", "More", new[] { "foreign" }) }), "Menu cannot point to foreign action binding");
        var groupView = service.Open(new Module(), "grouped");
        groupView.Publish(new ClientPresentationPage("grouped", "", 1, new[] { menuSection }), new ActionSet { Actions = new[] { command } }, () => current);
        Event.current.type = EventType.MouseDown; Widgets.ClickLabel = "More"; groupView.Draw(new ClientPresentationBounds(0, 0, 600, 400));
        var groupedMenu = Find.WindowStack.Last as FloatMenu;
        Check(groupedMenu != null && groupedMenu.Options.Count == 1, "Grouped menu opens populated native FloatMenu with selected owned actions");
        int menuCalls = command.Calls; current = false; groupedMenu.Options[0].Action();
        Check(command.Calls == menuCalls, "Menu option captured before owner invalidation refuses stale execution"); current = true; groupView.Dispose();
        Check(new ClientPresentationBadge("colored", "Origin").WithColor("ui.badge.development").ColorKey == "ui.badge.development", "Badge visual role is a registry key rather than Host business classification");
        var shortButton = ClientWidgets.MeasureButton("Refresh", 1000);
        var longButton = ClientWidgets.MeasureButton(new string('x', 40), 1000);
        var dropdownButton = ClientWidgets.MeasureButton(new string('x', 40), 1000, true);
        Check(longButton.x > 200 && dropdownButton.x > longButton.x, "Labels grow beyond former caps with separate dropdown arrow reserve");
        Check(ClientWidgets.MeasureButton(new string('x', 40), 100, true).x == 100, "Natural button size is constrained only by available viewport width");
        Text.Font = GameFont.Tiny; Text.WordWrap = false; Text.Anchor = TextAnchor.MiddleLeft;
        ClientWidgets.MeasureButton("Label", 400, true);
        Check(Text.Font == GameFont.Tiny && !Text.WordWrap && Text.Anchor == TextAnchor.MiddleLeft, "Button measurement preserves enclosing GUI state");
        Text.MeasureWrapping = true;
        Check(ClientWidgets.MeasureButton(new string('x',40),100,true).y > ClientWidgets.MeasureButton(new string('x',40),1000,true).y, "Narrow long labels gain height instead of smaller font or clipping");
        Text.MeasureWrapping = false; Text.Font = GameFont.Small; Text.WordWrap = true;
        var narrowForm = ResponsiveFormLayout.Calculate(new Rect(0,0,200,500),80,140,120,32,6,20);
        Check(narrowForm.InputRect.width == 200 && narrowForm.ActionRect.y >= narrowForm.InputRect.yMax + 6, "Narrow form moves action below input instead of reducing input width to zero");
        Check(narrowForm.ErrorRect.y >= narrowForm.ActionRect.yMax, "Reflow preserves error region below controls");
        var tinyForm = ResponsiveFormLayout.Calculate(new Rect(0,0,60,60),80,140,120,32,6,20);
        Check(tinyForm.InputRect.width >= 0 && tinyForm.ActionRect.height >= 0, "Short narrow form retains normalized geometry");
        var longSend = new LongCommand(); var longDock = service.Open(new Module(), "long-dock");
        longDock.Publish(new ClientPresentationPage("long-dock", "", 1, null, new ClientPresentationComposer(new ClientPresentationTextField("draft", "", ""), new ClientPresentationAction("long-submit", longSend))), new ActionSet { Actions = new[] { longSend } }, () => true);
        Widgets.FieldValue = null; Event.current.type = EventType.Repaint; longDock.Draw(new ClientPresentationBounds(0,0,600,240));
        Check(Widgets.LastFieldRect.width > 300, "Long composer action moves below draft instead of consuming its input width");
        Check(longDock.MeasureComposerHeight(600) > 64, "Owner docking query reserves the same responsive composer height as Host drawing"); longDock.Dispose();
        var singlePage = new ClientPresentationPage("single-header", "Single title", 1, new[] {
            new ClientPresentationSection("fixed-tools", "Fixed tools", ""),
            new ClientPresentationSection("flow-rows", "", "", list: new ClientPresentationList("single-rows", splitRows, ""))
        }).WithFixedHeaderSections(1);
        Check(singlePage.FixedHeaderSectionCount == 1 && singlePage.SplitView == null, "Public fixed header supports a single-column flow");
        Reject(() => singlePage.WithFixedHeaderSections(2), "Fixed headers cannot consume the entire body");
        Reject(() => singlePage.WithSplitView(new ClientPresentationSplitView(1, "A", "B")), "Single and split fixed header declarations cannot conflict");
        var singleView=service.Open(new Module(), "single-header");singleView.Publish(singlePage,null,()=>true);
        Event.current.type=EventType.Repaint;Widgets.Labels.Clear();Widgets.ScrollStarts.Clear();Widgets.NextScrollY=200;
        singleView.Draw(new ClientPresentationBounds(0,0,600,700));
        int titleCount=0;foreach(var label in Widgets.Labels) if(label.Text=="Single title") titleCount++;
        Check(Widgets.ScrollStarts.Count==1 && titleCount<=1, "Single fixed header leaves only body scrolling and does not duplicate title per pane");
        Widgets.ScrollStarts.Clear();singleView.Draw(new ClientPresentationBounds(0,0,600,700));
        Check(Widgets.ScrollStarts.Count==1 && Widgets.ScrollStarts[0]==200, "Single body retains its own scroll beneath fixed controls");singleView.Dispose();
        var rawRange=new ClientPresentationTextField("range", "Parameter", "unfinished");
        var ranged=rawRange.WithSlider(new ClientPresentationSlider(0,1,.5f));
        Check(rawRange.Slider==null && ranged.Value=="unfinished" && ranged.Slider.Value==.5f,"Slider composes immutably without replacing owner raw input");
        Reject(()=>new ClientPresentationSlider(0,1,float.NaN),"Slider rejects nonfinite value");Reject(()=>new ClientPresentationSlider(1,0,.5f),"Slider rejects inverted bounds");
        Reject(()=>ClientPresentationTextField.Integer("integer-range","","0").WithSlider(new ClientPresentationSlider(0,1,0)),"Integer fields do not silently become approximate floats");
        Reject(()=>new ClientPresentationComposer(ranged,binding),"Single-line composer rejects unsupported slider layout");
        var swatch=new ClientPresentationSwatch("sample","sample",new ClientRgba(.4f,.5f,.6f,.5f));
        var chipSection=new ClientPresentationSection("chips","","").WithSwatches(new[]{swatch}).WithPlainLayout();
        Check(chipSection.Swatches.Count==1,"Public swatches survive section composition");Reject(()=>chipSection.WithSwatches(new[]{swatch,swatch}),"Duplicate swatches rejected");
        Reject(()=>new ClientPresentationSwatch("bad","",new ClientRgba(float.NaN,0,0)),"Swatches reject invalid RGBA");
        var rangeView=service.Open(new Module(),"range-demo");rangeView.Publish(new ClientPresentationPage("range-demo","",1,new[]{new ClientPresentationSection("field","","",fields:new[]{ranged}),chipSection}),null,()=>true);
        Widgets.FieldValue=null;Widgets.SliderValue=.75f;Event.current.type=EventType.MouseDown;rangeView.Draw(new ClientPresentationBounds(0,0,400,500));
        Check(rangeView.TryDequeueInput(out var rangeInput)&&rangeInput.Id=="range"&&rangeInput.Kind==ClientPresentationInputKind.TextChanged&&rangeInput.Value=="0.75","Slider emits same raw text intent without committing business state");
        Check(Widgets.LastSliderRect.y>=Widgets.LastFieldRect.yMax,"Responsive slider stays below text input");rangeView.Dispose();
        Console.WriteLine("PASS: "+checks+" presentation contract/lifecycle/renderer checks (stub GUI, not game acceptance)");
    }
}
