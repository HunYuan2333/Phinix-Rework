using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using PhinixClient;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UserManagement;
using Utils;
using Utils.Framework;
using Utils.Framework.ManagedExtensions;
using UnityEngine;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public class TradeList : IDisposable
    {
        private readonly ITradeUiHostContext host;
        private readonly IClientTradeService service;
        private readonly object sync=new object();
        private readonly List<ClientTradeSnapshot> trades=new List<ClientTradeSnapshot>();
        private readonly HashSet<string> waiting=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,string> rows=new Dictionary<string,string>();
        private long changed,shown=-1,revision,themeVersion=-1,pointVersion=-1,operationVersion=-1;
        private ITradeOfferOperationApi operations;
        private ITradeInventoryRecoveryApi recovery;
        private long recoveryVersion=-1;
        private const int BrowsePageSize=100;
        private int browsePage,browsePages=1;
        private string selectedRecovery;
        private readonly Dictionary<string,string> recoveryRows=new Dictionary<string,string>();
        private string selectedOperation;
        private readonly Dictionary<string,string> operationRows=new Dictionary<string,string>();
        private string selected,search="",notice="",noticeKey;
        private bool dirty=true,disposed;
        private object language;
        private IPhinixExtensionModule owner;
        private IClientPresentationService renderer;
        private IClientExtensionPointService points;
        private IClientTheme theme;
        private IClientExtensionPointScope scope;
        private IClientActionPoint<TradePresentationActionContext> point;
        private IClientPresentationSession view;
        private ClientActionExecutionResult result;
        public TradeList(ITradeUiHostContext context)
        {
            host=context;service=host.TradeService;
            host.OnDisconnect+=Disconnected;host.OnUserDisplayNameChanged+=Renamed;
            service.OnTradesSynced+=Synced;service.OnTradeCreationSuccess+=Created;
            service.OnTradeCompleted+=Finished;service.OnTradeCancelled+=Finished;
            service.OnTradeUpdateSuccess+=Updated;service.OnTradeUpdateFailure+=Updated;
            lock(sync){trades.AddRange(service.GetTrades());changed++;}
        }
        internal void Start(IPhinixExtensionModule module,IClientPresentationService renderer,IClientExtensionPointService points,IClientTheme theme,ITradeOfferOperationApi operations=null,ITradeInventoryRecoveryApi recovery=null)
        {
            if(disposed||scope!=null)throw new InvalidOperationException("TradePresentationAlreadyStarted");
            owner=module;this.renderer=renderer;this.points=points;this.theme=theme;this.operations=operations;this.recovery=recovery;
            scope=points.ForModule(module);
            point=scope.DefineActions<TradePresentationActionContext>(TradePresentationActionContext.PointId);
            scope.ContributeActions<TradePresentationActionContext>(TradePresentationActionContext.PointId,Actions);
        }
        public void ViewOpened()
        {
            if(disposed||owner==null||view!=null&&!view.IsClosed)return;
            view=renderer.Open(owner,"trade-list");dirty=true;
            try {Refresh();}catch {ViewClosed();throw;}
        }
        public void ViewClosed(){var old=view;view=null;old?.Dispose();dirty=true;}
        public void ViewUpdate()
        {
            if(view==null||view.IsClosed)return;
            while(view.TryDequeueInput(out var input))
            {
                if(input.Revision!=revision)continue;
                if(input.Id=="search"){search=input.Value;browsePage=0;}
                else if(input.Id=="trades"&&recoveryRows.TryGetValue(input.Value,out var reservationId)){selectedRecovery=reservationId;selected=null;selectedOperation=null;}
                else if(input.Id=="trades"&&operationRows.TryGetValue(input.Value,out var token)){selectedOperation=token;selected=null;selectedRecovery=null;}
                else if(input.Id=="trades"&&rows.TryGetValue(input.Value,out var id)){selected=id;selectedOperation=null;selectedRecovery=null;}
                dirty=true;
            }
            Refresh();
        }
        public void Draw(Rect rect){if(!disposed)view?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height));}
        public void Dispose()
        {
            if(disposed)return;disposed=true;ViewClosed();
            host.OnDisconnect-=Disconnected;host.OnUserDisplayNameChanged-=Renamed;
            service.OnTradesSynced-=Synced;service.OnTradeCreationSuccess-=Created;
            service.OnTradeCompleted-=Finished;service.OnTradeCancelled-=Finished;
            service.OnTradeUpdateSuccess-=Updated;service.OnTradeUpdateFailure-=Updated;
            scope?.Dispose();scope=null;owner=null;
        }
        private static string L(string key)=>("Phinix_trade_"+key).Translate();
        private static string Safe(string value,int limit=4096)
        {value=TextHelper.StripRichText(value??"");return value.Length>limit?value.Substring(0,limit):value;}
        private static string OperationMessage(TradeOfferOperationInfo info)
        {
            if(string.IsNullOrEmpty(info.Message))return "";
            int separator=info.Message.IndexOf(':');string code=separator<0?info.Message:info.Message.Substring(0,separator);
            string key="Phinix_trade_operation_"+code;
            string text=Translator.CanTranslate(key)?key.Translate().ToString():Safe(code);
            if(separator>=0)
            {
                string reason=info.Message.Substring(separator+1).Trim();
                string reasonKey="Phinix_trade_operation_"+reason;
                text+="\n"+(Translator.CanTranslate(reasonKey)?reasonKey.Translate().ToString():Safe(reason,2048));
            }
            return Safe(text);
        }
        private static string OperationStatus(TradeOfferOperationInfo info)=>Safe(L("listOffer_"+info.State)+"\n"+L("listOfferPending")+"\n"+OperationMessage(info));
        private static string PartyName(ClientTradeSnapshot trade)=>string.IsNullOrWhiteSpace(trade.OtherPartyDisplayName)?trade.TradeId:trade.OtherPartyDisplayName;
        private static string RowId(string id)=>"trade."+ManagedExtensionPaths.Hash(Encoding.UTF8.GetBytes(id));
        private IEnumerable<ClientActionDefinition> Actions(TradePresentationActionContext context)
        {
            ClientTradeSnapshot expected;
            lock(sync)expected=trades.FirstOrDefault(t=>t.TradeId==context.TradeId);
            var actions=new List<ClientActionDefinition>();
            if(context.PageId=="trade-list"&&expected!=null)
            {
                bool busy;lock(sync)busy=waiting.Contains(expected.TradeId);
                busy=busy||operations?.GetPending().Any(p=>string.Equals(p.TradeId,expected.TradeId,StringComparison.OrdinalIgnoreCase))==true;
                actions.Add(new ClientActionDefinition("open",L("activeTrade_openButton"),()=>host.OpenTradeWindow(expected),string.IsNullOrWhiteSpace(expected.OtherPartyUuid)?L("listPartyUnavailable"):null));
                actions.Add(new ClientActionDefinition("cancel",L("activeTrade_cancelButton"),()=>Cancel(expected.TradeId),busy?L("listOfferPending"):null));
            }
            if(context.CanRetrySettlement&&context.OperationToken!=null)
                actions.Add(new ClientActionDefinition("retry-settlement",L("listRetrySettlement"),()=>{operations.RetryLocalSettlement(context.OperationToken);dirty=true;}));
            if(browsePage>0)actions.Add(new ClientActionDefinition("browse-previous",L("browsePrevious"),()=>ChangeBrowsePage(-1)));
            if(browsePage+1<browsePages)actions.Add(new ClientActionDefinition("browse-next",L("browseNext"),()=>ChangeBrowsePage(1)));
            return actions;
        }
        private void ChangeBrowsePage(int delta)
        {browsePage=Math.Max(0,Math.Min(browsePages-1,browsePage+delta));selected=null;selectedOperation=null;selectedRecovery=null;dirty=true;}
        private bool BrowseAction(ClientPresentationAction binding)=>binding.Action.OwnerId==owner.ExtensionId&&(binding.Action.Id=="browse-next"||binding.Action.Id=="browse-previous");

        private void Cancel(string id)
        {
            lock(sync){waiting.Add(id);noticeKey=null;notice=L("listCancelPending");changed++;}dirty=true;
            // A callback result is only command submission; completion/return stays with Trade owner.
            try {service.CancelTrade(id);}
            catch(Exception error){lock(sync){notice=Safe(L("listCancelUncertain")+"\n"+Safe(error.Message,2048));changed++;}host.Log(new Utils.LogEventArgs("Trade cancel submission failed: "+error,Utils.LogLevel.ERROR));}
        }
        private void Refresh()
        {
            long epoch;ClientTradeSnapshot[] snapshot;HashSet<string> pending;string message,messageKey;
            long ov=operations?.Revision??0,rv=recovery?.Revision??0;
            lock(sync){epoch=changed;if(!dirty&&operationVersion==ov&&recoveryVersion==rv&&shown==epoch&&themeVersion==theme.Version&&pointVersion==points.Revision&&ReferenceEquals(language,LanguageDatabase.activeLanguage)&&ReferenceEquals(result,view.LastActionResult))return;snapshot=trades.Where(t=>t!=null&&!string.IsNullOrWhiteSpace(t.TradeId)).GroupBy(t=>t.TradeId,StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToArray();pending=new HashSet<string>(waiting,StringComparer.OrdinalIgnoreCase);message=notice;messageKey=noticeKey;}
            if(messageKey!=null)message=L(messageKey);
            var picked=snapshot.FirstOrDefault(t=>t.TradeId==selected);if(picked==null)selected=null;
            var operationInfo=operations?.GetPending()??Array.Empty<TradeOfferOperationInfo>();
            var pickedOperation=selectedOperation==null?(picked==null?null:operationInfo.FirstOrDefault(p=>string.Equals(p.TradeId,picked.TradeId,StringComparison.OrdinalIgnoreCase))):operationInfo.FirstOrDefault(p=>p.Token==selectedOperation);
            if(selectedOperation!=null&&pickedOperation==null)selectedOperation=null;
            var recovered=recovery?.Capture()??Array.Empty<TradeInventoryRecoveryRecord>();
            var pickedRecovery=recovered.FirstOrDefault(r=>r.ReservationId==selectedRecovery);
            if(pickedRecovery==null)selectedRecovery=null;
            rows.Clear();operationRows.Clear();recoveryRows.Clear();var list=new List<ClientPresentationRow>();string selectedRow=null;
            foreach(var trade in snapshot)
            {
                string name=Safe(PartyName(trade),256);
                if(search.Length>0&&(name+" "+trade.TradeId).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                string id=RowId(trade.TradeId);rows[id]=trade.TradeId;if(trade.TradeId==selected)selectedRow=id;
                string state=L(trade.OtherPartyAccepted?"activeTrade_theyHaveAccepted":"activeTrade_theyHaveNotAccepted");
                var row=new ClientPresentationRow(id,string.Format(L("activeTrade_tradeWithLabel"),name),state+" · "+string.Format(L("listStackCounts"),trade.ItemsOnOffer.Length,trade.OtherPartyItemsOnOffer.Length));
                if(pending.Contains(trade.TradeId))row=row.WithBadges(new[]{new ClientPresentationBadge("pending",L("listWaiting")).WithColor("ui.warning")});
                list.Add(row);
            }
            foreach(var operation in operationInfo.Where(p=>!snapshot.Any(t=>string.Equals(t.TradeId,p.TradeId,StringComparison.OrdinalIgnoreCase))))
            {
                string id=RowId("operation:"+operation.Token);operationRows[id]=operation.Token;
                if(operation.Token==selectedOperation)selectedRow=id;
                list.Add(new ClientPresentationRow(id,L("listOfferRecovery"),Safe(operation.TradeId)+"\n"+L("listOffer_"+operation.State)).WithBadges(new[]{new ClientPresentationBadge("recovery",L("listOffer_"+operation.State)).WithColor("ui.warning")}));
            }
            foreach(var record in recovered)
            {
                if(search.Length>0&&(record.ReservationId+" "+record.OperationId).IndexOf(search,StringComparison.OrdinalIgnoreCase)<0)continue;
                string id=RowId("inventory:"+record.ReservationId);recoveryRows[id]=record.ReservationId;
                if(record.ReservationId==selectedRecovery)selectedRow=id;
                list.Add(new ClientPresentationRow(id,L("inventoryRecoveryTitle"),Safe(record.TradeId??record.OperationId)+"\n"+L("inventoryRecovery_"+record.State))
                    .WithBadges(new[]{new ClientPresentationBadge("journal",L("inventoryRecoveryJournal")).WithColor("ui.warning")}));
            }
            browsePages=Math.Max(1,(list.Count+BrowsePageSize-1)/BrowsePageSize);browsePage=Math.Min(browsePage,browsePages-1);
            var pageRows=list.Skip(browsePage*BrowsePageSize).Take(BrowsePageSize).ToArray();
            if(!pageRows.Any(row=>row.Id==selectedRow))selectedRow=null;
            long ticket=++revision,tv=theme.Version,pv=points.Revision;var lang=LanguageDatabase.activeLanguage;var expectedResult=view.LastActionResult;string expectedSelection=selected,expectedOperation=selectedOperation,expectedRecovery=selectedRecovery;
            Func<bool> current=()=>!disposed&&view!=null&&!view.IsClosed&&!dirty&&ticket==revision&&epoch==Interlocked.Read(ref changed)&&tv==theme.Version&&pv==points.Revision&&ReferenceEquals(lang,LanguageDatabase.activeLanguage)&&ReferenceEquals(expectedResult,view.LastActionResult)&&selected==expectedSelection&&selectedOperation==expectedOperation&&selectedRecovery==expectedRecovery&&rv==(recovery?.Revision??0)&&ov==(operations?.Revision??0);
            var context=new TradePresentationActionContext(pickedRecovery==null?"trade-list":"trade-inventory-recovery",ticket,picked?.TradeId??pickedOperation?.TradeId??pickedRecovery?.TradeId,picked?.Accepted??false,picked?.OtherPartyAccepted??false,picked?.ItemsOnOffer.Length??0,picked?.OtherPartyItemsOnOffer.Length??0,pickedOperation?.Token,pickedOperation?.State.ToString(),pickedOperation?.CanRetry??false,picked!=null,pickedRecovery?.ReservationId,pickedRecovery?.OperationId);
            var owned=new RowActionSets();
            try
            {
                // Each row captures its own trade context; opening no longer depends on selection.
                for(int i=0;i<pageRows.Length;i++)
                {
                    var row=pageRows[i];
                    if(!rows.TryGetValue(row.Id,out var tradeId))continue;
                    var rowTrade=snapshot.First(t=>t.TradeId==tradeId);
                    var rowContext=new TradePresentationActionContext("trade-list",ticket,rowTrade.TradeId,rowTrade.Accepted,rowTrade.OtherPartyAccepted,rowTrade.ItemsOnOffer.Length,rowTrade.OtherPartyItemsOnOffer.Length);
                    var rowSet=owned.Add(point.Collect(rowContext,current));
                    var rowActions=rowSet.Actions.Where(a=>a.OwnerId==owner.ExtensionId&&(a.Id=="open"||a.Id=="cancel")).Select(a=>new ClientPresentationAction(row.Id+"."+a.OwnerId+"."+a.Id,a,
                        a.Id=="open"?ClientButtonKind.Primary:ClientButtonKind.Danger,
                        a.Id=="cancel"?new ClientPresentationConfirmation(L("activeTrade_cancelButton"),string.Format(L("listCancelConfirmation"),Safe(PartyName(rowTrade),256)),L("activeTrade_cancelButton"),L("listKeepTrade")):null));
                    pageRows[i]=row.WithControls(rowActions);
                }
                var selectedSet=owned.Add(point.Collect(context,current));
                var bindings=selectedSet.Actions.Select(a=>new ClientPresentationAction(a.OwnerId+"."+a.Id,a,
                    a.OwnerId==owner.ExtensionId&&a.Id=="cancel"?ClientButtonKind.Danger:a.OwnerId==owner.ExtensionId&&a.Id=="open"?ClientButtonKind.Primary:ClientButtonKind.Normal,
                    a.OwnerId==owner.ExtensionId&&a.Id=="retry-settlement"?new ClientPresentationConfirmation(L("listRetrySettlement"),L("listRetrySettlementConfirm"),L("listRetrySettlement"),L("listKeepTrade")):a.OwnerId==owner.ExtensionId&&a.Id=="cancel"?new ClientPresentationConfirmation(L("activeTrade_cancelButton"),string.Format(L("listCancelConfirmation"),Safe(PartyName(picked),256)),L("activeTrade_cancelButton"),L("listKeepTrade")):null)).ToArray();
                string details=picked==null?(pickedOperation==null?L("listSelectTrade"):Safe(pickedOperation.TradeId)+"\n"+L("listOffer_"+pickedOperation.State)+"\n"+OperationMessage(pickedOperation)):string.Format(L("listStackCounts"),picked.ItemsOnOffer.Length,picked.OtherPartyItemsOnOffer.Length)+"\n"+L(picked.Accepted?"confirmOurTradeCheckboxChecked":"confirmOurTradeCheckboxUnchecked")+"\n"+string.Format(L(picked.OtherPartyAccepted?"confirmTheirTradeCheckboxChecked":"confirmTheirTradeCheckboxUnchecked"),Safe(PartyName(picked),256))+"\n"+Safe(picked.TradeId);
                if(pickedRecovery!=null)details=L("inventoryRecoveryReservation")+": "+Safe(pickedRecovery.ReservationId)+"\n"+L("inventoryRecoveryOperation")+": "+Safe(pickedRecovery.OperationId)+"\n"+L("inventoryRecovery_"+pickedRecovery.State)+"\n"+L("inventoryRecoveryEvidence")+": "+Safe(pickedRecovery.Evidence);
                var sections=new List<ClientPresentationSection>{
                    new ClientPresentationSection("tools",TabTitle(),browsePages>1?string.Format(L("browsePage"),browsePage+1,browsePages):"",bindings.Where(BrowseAction),status:new ClientPresentationStatus(message),fields:new[]{new ClientPresentationTextField("search","",search,placeholder:L("listSearch"),maxLength:256)}).WithPlainLayout().WithInlineToolbar(),
                    new ClientPresentationSection("list",string.Format(L("listEntryCounts"),snapshot.Length,operationInfo.Count)+" · "+string.Format(L("inventoryRecoveryCount"),recovered.Count),"",list:new ClientPresentationList("trades",pageRows,L("noActiveTradesPlaceholder"),selectedRow).WithCompactRows()).WithPlainLayout(),
                    new ClientPresentationSection("selection",pickedRecovery!=null?L("inventoryRecoveryTitle"):picked==null?(pickedOperation==null?L("listDetails"):L("listOfferRecovery")):string.Format(L("activeTrade_tradeWithLabel"),Safe(PartyName(picked),256)),"",bindings.Where(b=>!BrowseAction(b))).WithWrappedActions().WithPlainLayout(),
                    new ClientPresentationSection("details","",details,status:new ClientPresentationStatus(pickedRecovery!=null?L("inventoryRecoveryNote"):pickedOperation!=null?OperationStatus(pickedOperation):picked!=null&&pending.Contains(picked.TradeId)?L("listCancelPending"):L("listAuthorityNote"),pickedRecovery!=null||pickedOperation!=null||picked!=null&&pending.Contains(picked.TradeId)?ClientPresentationTone.Warning:ClientPresentationTone.Neutral)).WithPlainLayout()
                };
                if(pickedRecovery!=null)
                {
                    var entries=pickedRecovery.Lines.Select((line,index)=>new ClientPresentationRow("entry."+index,Safe(line.EntryId),string.Format(L("inventoryRecoveryUnits"),line.Units)));
                    sections.Add(new ClientPresentationSection("reserved-lines",L("inventoryRecoveryLines"),"",list:new ClientPresentationList("reserved-lines",entries,L("inventoryRecoveryNoLines")).WithReadOnly()).WithPlainLayout());
                }
                if(picked!=null)
                {
                    Func<string,string> label=id=>string.IsNullOrEmpty(id)?null:DefDatabase<ThingDef>.GetNamedSilentFail(id)?.label;
                    sections.Add(new ClientPresentationSection("our-quote",L("ourOfferLabel"),L("quotePreviewNote"),
                        list:new ClientPresentationList("our-quote-items",TradeQuoteRows.Build(picked.ItemsOnOffer,label,L,TradeQuoteRows.IconFromDef),L("quoteEmpty")).WithReadOnly().WithCompactRows()).WithPlainLayout());
                    sections.Add(new ClientPresentationSection("their-quote",L("theirOfferLabel"),"",
                        list:new ClientPresentationList("their-quote-items",TradeQuoteRows.Build(picked.OtherPartyItemsOnOffer,label,L,TradeQuoteRows.IconFromDef),L("quoteEmpty")).WithReadOnly().WithCompactRows()).WithPlainLayout());
                }
                if(picked==null&&pickedOperation==null&&pickedRecovery==null)sections.RemoveRange(2,sections.Count-2);
                dirty=false;view.Publish(new ClientPresentationPage("trade-list","",ticket,sections).WithFixedHeaderSections(1),owned,current);
            }
            catch {dirty=true;owned.Dispose();throw;}
            shown=epoch;operationVersion=ov;recoveryVersion=rv;themeVersion=tv;pointVersion=pv;language=lang;result=expectedResult;
        }
        private sealed class RowActionSets:IClientActionSet
        {
            private readonly List<IClientActionSet> sets=new List<IClientActionSet>();
            public IReadOnlyList<IClientContributedAction> Actions=>sets.SelectMany(s=>s.Actions).ToArray();
            public IClientActionSet Add(IClientActionSet set){sets.Add(set);return set;}
            public void Dispose(){var owned=sets.ToArray();sets.Clear();var failures=new List<Exception>();foreach(var set in owned)try{set.Dispose();}catch(Exception error){failures.Add(error);}if(failures.Count>0)throw new AggregateException(failures);}
        }
        private static string TabTitle()=>"Phinix_tabs_trades".Translate();
        private void Disconnected(object sender,EventArgs args){lock(sync){trades.Clear();waiting.Clear();notice="";noticeKey="listDisconnected";changed++;}}
        private void Synced(object sender,TradesSyncedEventArgs args){if(args==null)return;lock(sync){trades.Clear();trades.AddRange(args.Trades);if(noticeKey=="listDisconnected"){noticeKey=null;notice="";}waiting.RemoveWhere(id=>!trades.Any(t=>t!=null&&string.Equals(t.TradeId,id,StringComparison.OrdinalIgnoreCase)));changed++;}}
        private void Created(object sender,TradeCreationEventArgs args){if(args?.Trade==null)return;lock(sync){trades.RemoveAll(t=>t.TradeId==args.Trade.TradeId);trades.Add(args.Trade);changed++;}}
        private void Finished(object sender,TradeCompletionEventArgs args){if(args==null)return;lock(sync){trades.RemoveAll(t=>string.Equals(t.TradeId,args.TradeId,StringComparison.OrdinalIgnoreCase));waiting.Remove(args.TradeId);notice="";noticeKey="listFinished";changed++;}}
        private void Updated(object sender,TradeUpdateEventArgs args)
        {if(args?.Trade==null)return;lock(sync){int index=trades.FindIndex(t=>t.TradeId==args.Trade.TradeId);if(index>=0)trades[index]=args.Trade;if(args.FailureReason!=TradeFailureReason.None){noticeKey=null;notice=Safe(args.FailureMessage);}changed++;}}
        private void Renamed(object sender,UserDisplayNameChangedEventArgs args)
        {if(args==null)return;lock(sync){for(int i=0;i<trades.Count;i++){var t=trades[i];if(string.IsNullOrEmpty(t.OtherPartyUuid)||t.OtherPartyUuid!=args.Uuid)continue;var user=t.OtherParty;trades[i]=new ClientTradeSnapshot(t.TradeId,new ImmutableUser(user.Uuid,args.NewDisplayName,user.LoggedIn,user.AcceptingTrades),t.ItemsOnOffer,t.OtherPartyItemsOnOffer,t.Accepted,t.OtherPartyAccepted);}changed++;}}
    }
}
