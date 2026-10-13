using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient.Framework;
using PhinixClient.Trade;
using UnityEngine;
using Verse;
using Utils;
namespace Phinix.TradeExtension.Client
{
    public partial class TradeWindow
    {
        private readonly string presentationId="trade-offer."+Guid.NewGuid().ToString("N");
        private TradeOfferPresentationFactory presentation;
        private IClientPresentationSession view;
        private bool presentationDirty=true;
        private long presentationRevision,shownTheme=-1,shownPoints=-1;
        private object shownLanguage;
        private ClientActionExecutionResult shownResult;
        private TradeAvailableItem selectedItem;
        private readonly Dictionary<string,TradeAvailableItem> itemRows=new Dictionary<string,TradeAvailableItem>();
        private int itemPage,itemPages=1;
        private string quantityDraft="0",quantityError,editorNotice="";
        private bool cancelSubmitted;
        private static string L(string key)=>("Phinix_trade_"+key).Translate();
        private static string Safe(string text)
        {string value=TextHelper.StripRichText(text??"");return value.Length>4096?value.Substring(0,4096):value;}
        private bool EditorBusy=>OfferBusy||getPendingAccepted().HasValue||cancelSubmitted;
        private void OpenPresentation()
        {
            presentation=clientHostContext?.OfferPresentation;
            if(presentation==null)throw new InvalidOperationException("TradeOfferPresentationUnavailable");
            presentation.Add(presentationId,this);
            try {view=presentation.Renderer.Open(presentation.Owner,presentationId);UpdatePresentation();}
            catch {ClosePresentation();throw;}
        }
        private void ClosePresentation()
        {var old=view;view=null;old?.Dispose();presentation?.Remove(presentationId);presentation=null;}
        public override void DoWindowContents(Rect rect)=>view?.Draw(rect);
        private void SetQuantity(int value)
        {
            if(selectedItem==null||EditorBusy)return;
            selectedItem.Selected=Math.Max(0,Math.Min(selectedItem.Count,value));
            quantityDraft=selectedItem.Selected.ToString();quantityError=null;presentationDirty=true;
        }
        internal IEnumerable<ClientActionDefinition> PresentationActions()
        {
            var actions=new List<ClientActionDefinition>();
            string blocked=EditorBusy?L("editorWaiting"):quantityError;
            actions.Add(new ClientActionDefinition("submit",L("editorSubmitQuote"),()=>{UpdateTradeItems();presentationDirty=true;},blocked??(availableItems.Any(i=>i.Selected>0)?null:L("editorSelectQuantity"))));
            actions.Add(new ClientActionDefinition("accept",L(trade.Accepted?"editorWithdraw":"confirmTrade"),()=>{sendTradeStatusUpdate(!trade.Accepted);presentationDirty=true;},blocked));
            actions.Add(new ClientActionDefinition("reset",L("resetButton"),()=>{ResetTradeItems();presentationDirty=true;},blocked??(trade.ItemsOnOffer.Length==0?L("quoteEmpty"):null)));
            actions.Add(new ClientActionDefinition("cancel",L("cancelButton"),()=>{sendCancelTradeRequest();cancelSubmitted=true;editorNotice=L("listCancelPending");presentationDirty=true;},blocked));
            actions.Add(new ClientActionDefinition("refresh-items",L("editorRefresh"),refreshAvailableItems,EditorBusy?L("editorWaiting"):null));
            if(itemPage>0)actions.Add(new ClientActionDefinition("previous",L("browsePrevious"),()=>{itemPage--;presentationDirty=true;}));
            if(itemPage+1<itemPages)actions.Add(new ClientActionDefinition("next",L("browseNext"),()=>{itemPage++;presentationDirty=true;}));
            if(selectedItem!=null)
            {
                foreach(var step in new[]{-10,-1,1,10}){int delta=step;actions.Add(new ClientActionDefinition("step."+(delta<0?"minus":"plus")+Math.Abs(delta),delta>0?"+"+delta:delta.ToString(),()=>SetQuantity((int)Math.Max(0,Math.Min(selectedItem.Count,(long)selectedItem.Selected+delta))),EditorBusy?L("editorWaiting"):null));}
                actions.Add(new ClientActionDefinition("all",L("selectAll"),()=>SetQuantity(selectedItem.Count),EditorBusy?L("editorWaiting"):null));
                actions.Add(new ClientActionDefinition("half",L("selectHalf"),()=>SetQuantity(selectedItem.Count/2),EditorBusy?L("editorWaiting"):null));
                actions.Add(new ClientActionDefinition("none",L("selectNone"),()=>SetQuantity(0),EditorBusy?L("editorWaiting"):null));
                actions.Add(new ClientActionDefinition("hundred",L("select100"),()=>SetQuantity(100),EditorBusy?L("editorWaiting"):null));
            }
            return actions;
        }
        private void UpdatePresentation()
        {
            if(view==null||view.IsClosed||closed)return;
            var renderedItem=selectedItem;
            while(view.TryDequeueInput(out var input))
            {
                if(input.Revision!=presentationRevision)continue;
                if(input.Id=="search"){searchText=input.Value??"";itemPage=0;RebuildAvailableItemFilter();}
                else if(input.Id=="available"&&itemRows.TryGetValue(input.Value,out var item))
                {selectedItem=item;quantityDraft=item.Selected.ToString();quantityError=null;}
                else if(input.Id=="quantity"&&selectedItem!=null&&ReferenceEquals(renderedItem,selectedItem)&&!EditorBusy)
                {
                    quantityDraft=input.Value;
                    if(TradeQuantityDraft.TryParse(quantityDraft,selectedItem.Count,out int quantity))
                    {selectedItem.Selected=quantity;quantityError=null;}
                    else quantityError=string.Format(L("editorQuantityInvalid"),selectedItem.Count);
                }
                presentationDirty=true;
            }
            long tv=presentation.Theme.Version,pv=presentation.Points.Revision;
            var lang=LanguageDatabase.activeLanguage;
            if(!presentationDirty&&shownTheme==tv&&shownPoints==pv&&ReferenceEquals(shownLanguage,lang)&&ReferenceEquals(shownResult,view.LastActionResult))return;
            if(selectedItem!=null&&!availableItems.Contains(selectedItem)){selectedItem=null;quantityError=null;}
            itemPages=Math.Max(1,(filteredAvailableItems.Count+99)/100);itemPage=Math.Min(itemPage,itemPages-1);
            itemRows.Clear();string selectedRow=null;
            var rows=filteredAvailableItems.Skip(itemPage*100).Take(100).Select((item,index)=>
            {
                string id="item."+(itemPage*100+index);itemRows.Add(id,item);if(ReferenceEquals(item,selectedItem))selectedRow=id;
                return new ClientPresentationRow(id,Safe(item.Label),string.Format(L("editorUnits"),item.Selected,item.Count))
                    .WithBadges(new[]{new ClientPresentationBadge("source",L(item.IsInventory?"editorInventory":"editorMap"),item.ThingDef?.uiIcon).WithColor(item.IsInventory?"ui.accent":"ui.text.secondary")});
            }).ToArray();
            long ticket=++presentationRevision;var expectedTrade=trade;long ov=clientHostContext.OfferOperations.Revision;
            var expectedItem=selectedItem;
            Func<bool> current=()=>!closed&&!shouldClose&&!tradeUpdated&&!presentationDirty&&view!=null&&!view.IsClosed&&ticket==presentationRevision&&ReferenceEquals(expectedTrade,trade)&&ReferenceEquals(expectedItem,selectedItem)&&gameScope.IsValid()&&ov==clientHostContext.OfferOperations.Revision&&tv==presentation.Theme.Version&&pv==presentation.Points.Revision&&ReferenceEquals(lang,LanguageDatabase.activeLanguage);
            var context=new TradeOfferPresentationActionContext(presentationId,ticket,trade.TradeId,selectedRow,selectedItem?.Count??0,selectedItem?.Selected??0,EditorBusy,trade.Accepted,trade.OtherPartyAccepted);
            var owned=presentation.Point.Collect(context,current);
            try
            {
                string confirm=L("editorConfirmNote");
                var missing=TradeItemConverter.FindMissingDependencies(trade.OtherPartyItemsOnOffer);
                if(missing.Count>0)confirm+="\n"+"Phinix_trade_missingDependencyWarning".Translate(string.Join("\n",missing));
                var bindings=owned.Actions.Select(a=>new ClientPresentationAction(a.OwnerId+"."+a.Id,a,
                    a.OwnerId==presentation.Owner.ExtensionId&&(a.Id=="cancel"||a.Id=="reset")?ClientButtonKind.Danger:a.OwnerId==presentation.Owner.ExtensionId&&(a.Id=="submit"||a.Id=="accept")?ClientButtonKind.Primary:ClientButtonKind.Normal,
                    a.OwnerId!=presentation.Owner.ExtensionId?null:a.Id=="accept"&&!trade.Accepted?new ClientPresentationConfirmation(L("confirmTrade"),confirm,L("confirmTrade"),L("listKeepTrade")):
                    a.Id=="cancel"?new ClientPresentationConfirmation(L("cancelButton"),L("editorCancelConfirm"),L("cancelButton"),L("listKeepTrade")):
                    a.Id=="reset"?new ClientPresentationConfirmation(L("resetButton"),L("editorResetConfirm"),L("resetButton"),L("listKeepTrade")):null)).ToArray();
                Func<ClientPresentationAction,bool> itemAction=b=>b.Action.OwnerId==presentation.Owner.ExtensionId&&(b.Action.Id.StartsWith("step.")||new[]{"all","half","none","hundred"}.Contains(b.Action.Id));
                Func<ClientPresentationAction,bool> browseAction=b=>b.Action.OwnerId==presentation.Owner.ExtensionId&&new[]{"refresh-items","previous","next"}.Contains(b.Action.Id);
                Func<ClientPresentationAction,bool> submitAction=b=>b.Action.OwnerId==presentation.Owner.ExtensionId&&b.Action.Id=="submit";
                var sections=new List<ClientPresentationSection>{
                    new ClientPresentationSection("trade-state",string.Format(L("activeTrade_tradeWithLabel"),Safe(trade.OtherPartyDisplayName)),L("editorCloseNote"),
                        status:new ClientPresentationStatus(EditorBusy?L("editorWaiting"):editorNotice,EditorBusy?ClientPresentationTone.Pending:ClientPresentationTone.Neutral))
                        .WithBadges(new[]{new ClientPresentationBadge("ours",L("ourOfferLabel")+" · "+L(trade.Accepted?"editorConfirmed":"editorUnconfirmed")).WithColor(trade.Accepted?"ui.success":"ui.warning"),
                            new ClientPresentationBadge("theirs",Safe(trade.OtherPartyDisplayName)+" · "+L(trade.OtherPartyAccepted?"editorConfirmed":"editorUnconfirmed")).WithColor(trade.OtherPartyAccepted?"ui.success":"ui.warning")}).WithPlainLayout(),
                    new ClientPresentationSection("browse",L("editorAvailable"),string.Format(L("browsePage"),itemPage+1,itemPages),bindings.Where(browseAction),fields:new[]{new ClientPresentationTextField("search",L("searchLabel"),searchText,maxLength:256)}).WithPlainLayout().WithWrappedActions(),
                    new ClientPresentationSection("items","","",list:new ClientPresentationList("available",rows,L("quoteEmpty"),selectedRow)).WithPlainLayout(),
                    new ClientPresentationSection("draft",selectedItem==null?L("editorSelectQuantity"):Safe(selectedItem.Label),L("editorLocalDraft"),bindings.Where(b=>itemAction(b)||submitAction(b)),fields:selectedItem==null?null:new[]{ClientPresentationTextField.Integer("quantity",string.Format(L("editorUnits"),selectedItem.Selected,selectedItem.Count),quantityDraft,quantityError,EditorBusy?L("editorWaiting"):null)})
                        .WithPlainLayout().WithWrappedActions().WithActionMenus(selectedItem==null?null:new[]{new ClientPresentationActionMenu("quantity-tools",L("editorQuantityTools"),bindings.Where(itemAction).Select(b=>b.Id))}),
                    new ClientPresentationSection("our-quote",L("ourOfferLabel"),L("editorAcknowledgedQuote"),surfaceKey:"ui.surface",
                        list:new ClientPresentationList("our-quote",TradeQuoteRows.Build(trade.ItemsOnOffer,DefLabel,L,TradeQuoteRows.IconFromDef),L("quoteEmpty")).WithReadOnly())
                        .WithBadges(new[]{new ClientPresentationBadge("side",L("editorYouGive")).WithColor("ui.success")}),
                    new ClientPresentationSection("their-quote",Safe(trade.OtherPartyDisplayName)+" · "+L("theirOfferLabel"),L("editorAcknowledgedQuote"),surfaceKey:"ui.surface",
                        list:new ClientPresentationList("their-quote",TradeQuoteRows.Build(trade.OtherPartyItemsOnOffer,DefLabel,L,TradeQuoteRows.IconFromDef),L("quoteEmpty")).WithReadOnly())
                        .WithBadges(new[]{new ClientPresentationBadge("side",L("editorYouReceive")).WithColor("ui.info")}),
                    new ClientPresentationSection("commands",L("editorTradeActions"),L("editorConfirmNote"),bindings.Where(b=>!browseAction(b)&&!itemAction(b)&&!submitAction(b))).WithPlainLayout().WithWrappedActions()
                };
                presentationDirty=false;
                view.Publish(new ClientPresentationPage(presentationId,L("editorTitle"),ticket,sections).WithSplitView(new ClientPresentationSplitView(4,L("editorAvailable"),L("editorQuotes"),.45f).WithFixedHeaders(1,1,0).WithFixedFooters(1,1)),owned,current);
                shownTheme=tv;shownPoints=pv;shownLanguage=lang;shownResult=view.LastActionResult;
            }
            catch {presentationDirty=true;owned.Dispose();throw;}
        }
        private static string DefLabel(string id)=>string.IsNullOrEmpty(id)?null:DefDatabase<ThingDef>.GetNamedSilentFail(id)?.label;
    }
}
