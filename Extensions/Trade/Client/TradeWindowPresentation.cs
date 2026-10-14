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
        private long nextItemRow;
        private readonly Dictionary<TradeAvailableItem,string> stableItemRows=new Dictionary<TradeAvailableItem,string>();
        private readonly Dictionary<TradeAvailableItem,string> rowDrafts=new Dictionary<TradeAvailableItem,string>();
        private readonly Dictionary<TradeAvailableItem,string> rowErrors=new Dictionary<TradeAvailableItem,string>();
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
        public override void DoWindowContents(Rect rect)=>view?.Draw(new ClientPresentationBounds(rect.x, rect.y, rect.width, rect.height));
        private void SetQuantity(int value)
        {
            if(selectedItem==null||EditorBusy)return;
            selectedItem.Selected=Math.Max(0,Math.Min(selectedItem.Count,value));
            quantityDraft=selectedItem.Selected.ToString();quantityError=null;rowDrafts.Remove(selectedItem);rowErrors.Remove(selectedItem);presentationDirty=true;
        }
        internal IEnumerable<ClientActionDefinition> PresentationActions()
        {
            var actions=new List<ClientActionDefinition>();
            string busy=EditorBusy?L("editorWaiting"):null;
            string blocked=busy??rowErrors.Values.FirstOrDefault()??quantityError;
            actions.Add(new ClientActionDefinition("submit",L("editorSubmitQuote"),()=>{UpdateTradeItems();presentationDirty=true;},blocked??(availableItems.Any(i=>i.Selected>0)?null:L("editorSelectQuantity"))));
            actions.Add(new ClientActionDefinition("accept",L(trade.Accepted?"editorWithdraw":"confirmTrade"),()=>{sendTradeStatusUpdate(!trade.Accepted);presentationDirty=true;},blocked));
            actions.Add(new ClientActionDefinition("reset",L("resetButton"),()=>{ResetTradeItems();presentationDirty=true;},busy??(trade.ItemsOnOffer.Length==0?L("quoteEmpty"):null)));
            actions.Add(new ClientActionDefinition("cancel",L("cancelButton"),()=>{sendCancelTradeRequest();cancelSubmitted=true;editorNotice=L("listCancelPending");presentationDirty=true;},busy));
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
            while(view.TryDequeueInput(out var input))
            {
                if(input.Revision!=presentationRevision)continue;
                if(input.Id=="search"){searchText=input.Value??"";itemPage=0;RebuildAvailableItemFilter();}
                else if(input.Id=="available"&&itemRows.TryGetValue(input.Value,out var item))
                {selectedItem=item;quantityDraft=item.Selected.ToString();quantityError=null;}
                else if(input.Id.StartsWith("quantity.",StringComparison.Ordinal)&&itemRows.TryGetValue(input.Id.Substring("quantity.".Length),out var editedItem)&&!EditorBusy)
                {
                    // Row id belongs to the published revision; no selection switch is needed.
                    rowDrafts[editedItem]=input.Value;
                    if(TradeQuantityDraft.TryParse(input.Value,editedItem.Count,out int quantity))
                    {editedItem.Selected=quantity;rowErrors.Remove(editedItem);}
                    else rowErrors[editedItem]=string.Format(L("editorQuantityInvalid"),editedItem.Count);
                    if(ReferenceEquals(editedItem,selectedItem))
                    {quantityDraft=input.Value;quantityError=rowErrors.TryGetValue(editedItem,out var error)?error:null;}
                }
                presentationDirty=true;
            }
            long tv=presentation.Theme.Version,pv=presentation.Points.Revision;
            var lang=LanguageDatabase.activeLanguage;
            if(!presentationDirty&&shownTheme==tv&&shownPoints==pv&&ReferenceEquals(shownLanguage,lang)&&ReferenceEquals(shownResult,view.LastActionResult))return;
            if(selectedItem!=null&&!availableItems.Contains(selectedItem)){selectedItem=null;quantityError=null;}
            foreach(var stale in rowDrafts.Keys.Where(item=>!availableItems.Contains(item)).ToArray()){rowDrafts.Remove(stale);rowErrors.Remove(stale);}
            itemPages=Math.Max(1,(filteredAvailableItems.Count+99)/100);itemPage=Math.Min(itemPage,itemPages-1);
            itemRows.Clear();string selectedRow=null;
            var rows=filteredAvailableItems.Skip(itemPage*100).Take(100).Select((item,index)=>
            {
                if(!stableItemRows.TryGetValue(item,out var id)){id="item."+(++nextItemRow);stableItemRows.Add(item,id);}
                itemRows.Add(id,item);if(ReferenceEquals(item,selectedItem))selectedRow=id;
                return new ClientPresentationRow(id,Safe(item.Label),string.Format(L("editorUnits"),item.Selected,item.Count)+" · "+L(item.IsInventory?"editorInventory":"editorMap"))
                    .WithControls(null,ClientPresentationTextField.Integer("quantity."+id,"",rowDrafts.TryGetValue(item,out var draft)?draft:item.Selected.ToString(),rowErrors.TryGetValue(item,out var error)?error:null,EditorBusy?L("editorWaiting"):null))
                    .WithImage(ClientTextureImage.FromTexture(item.ThingDef?.uiIcon));
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
                var state=new ClientPresentationSection("trade-state",string.Format(L("activeTrade_tradeWithLabel"),Safe(trade.OtherPartyDisplayName)),"",bindings.Where(b=>!browseAction(b)&&!itemAction(b)&&!submitAction(b)),
                    status:new ClientPresentationStatus(EditorBusy?L("editorWaiting"):editorNotice,EditorBusy?ClientPresentationTone.Pending:ClientPresentationTone.Neutral))
                    .WithPlainLayout().WithInlineToolbar();
                var ours=new ClientPresentationSection("our-quote",L("ourOfferLabel")+" · "+L(trade.Accepted?"editorConfirmed":"editorUnconfirmed"),"",
                    list:new ClientPresentationList("our-quote",TradeQuoteRows.Build(trade.ItemsOnOffer,DefLabel,L,TradeQuoteRows.IconFromDef),L("quoteEmpty")).WithReadOnly().WithCompactRows()).WithPlainLayout();
                var theirs=new ClientPresentationSection("their-quote",L("theirOfferLabel")+" · "+L(trade.OtherPartyAccepted?"editorConfirmed":"editorUnconfirmed"),"",
                    list:new ClientPresentationList("their-quote",TradeQuoteRows.Build(trade.OtherPartyItemsOnOffer,DefLabel,L,TradeQuoteRows.IconFromDef),L("quoteEmpty")).WithReadOnly().WithCompactRows()).WithPlainLayout();
                var browse=new ClientPresentationSection("browse","",L("editorLocalDraft")+" · "+L("editorCloseNote")+(itemPages>1?" · "+string.Format(L("browsePage"),itemPage+1,itemPages):""),bindings.Where(b=>browseAction(b)||submitAction(b)||itemAction(b)).OrderBy(b=>b.Action.Id=="refresh-items"?0:submitAction(b)?1:2),
                    fields:new[]{new ClientPresentationTextField("search","",searchText,placeholder:L("searchLabel"),maxLength:256)})
                    .WithPlainLayout().WithInlineToolbar().WithActionMenus(selectedItem==null?null:new[]{new ClientPresentationActionMenu("quantity-tools",Safe(L("editorQuantityTools")+" · "+selectedItem.Label),bindings.Where(itemAction).Select(b=>b.Id))});
                var items=new ClientPresentationSection("items","","",list:new ClientPresentationList("available",rows,L("quoteEmpty"),selectedRow).WithCompactRows()).WithPlainLayout();
                var sections=new[]{state,ours,theirs,browse,items};
                presentationDirty=false;
                view.Publish(new ClientPresentationPage(presentationId,"",ticket,sections).WithSplitView(new ClientPresentationSplitView(2,L("ourOfferLabel"),L("theirOfferLabel"),.5f).WithFixedHeaders(1,0,0).WithSharedFooter(2,trade.ItemsOnOffer.Length==0&&trade.OtherPartyItemsOnOffer.Length==0?.65f:.6f,1)),owned,current);
                shownTheme=tv;shownPoints=pv;shownLanguage=lang;shownResult=view.LastActionResult;
            }
            catch {presentationDirty=true;owned.Dispose();throw;}
        }
        private void ClearQuantityDrafts(){rowDrafts.Clear();rowErrors.Clear();stableItemRows.Clear();quantityDraft="0";quantityError=null;}
        private static string DefLabel(string id)=>string.IsNullOrEmpty(id)?null:DefDatabase<ThingDef>.GetNamedSilentFail(id)?.label;
    }
}
