using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Phinix.InventoryExtension;
using PhinixClient;
using PhinixClient.GUI;
using PhinixClient.Framework;
using PhinixClient.Trade;
using RimWorld;
using UnityEngine;
using Utils;
using Verse;
using static PhinixClient.GUI.GUIUtils;

namespace Phinix.TradeExtension.Client
{
    public partial class TradeWindow : Window
    {
        private const float MINIMUM_WINDOW_WIDTH = 640f;
        private const float MINIMUM_WINDOW_HEIGHT = 480f;
        private const float PREFERRED_WINDOW_WIDTH = 1000f;
        private const float PREFERRED_WINDOW_HEIGHT = 750f;
        public override Vector2 InitialSize
        {
            get
            {
                Rect safeRect = UiScreenSafeArea.Current;
                return new Vector2(
                    Mathf.Min(PREFERRED_WINDOW_WIDTH, safeRect.width),
                    Mathf.Min(PREFERRED_WINDOW_HEIGHT, safeRect.height));
            }
        }

        private readonly ITradeUiHostContext hostContext;
        private readonly IClientTradeService tradeService;

        private ClientTradeSnapshot trade;
        private ClientTradeSnapshot updatedTrade;
        private bool tradeUpdated = false;
        private object updatedTradeLock = new object();

        private List<TradeAvailableItem> availableItems = new List<TradeAvailableItem>();
        private List<TradeAvailableItem> filteredAvailableItems = new List<TradeAvailableItem>();
        private string searchText = string.Empty;

        private long seenOfferRevision=-1;
        private readonly ClientTradeUiHostContext clientHostContext;
        private readonly TradeOfferGameScope gameScope;
        private EventHandler inventoryChangedHandler;
        private bool closed;

        private volatile bool shouldClose;
        private readonly object pendingAcceptedLock = new object();
        private bool? pendingAccepted;
        public TradeWindow(ClientTradeSnapshot trade, ITradeUiHostContext hostContext)
            : base(new ClientThemeWindowDrawing(() => (hostContext as ClientTradeUiHostContext)?.OfferPresentation?.Theme.Capture()))
        {
            this.trade = trade;
            this.hostContext = hostContext;
            this.clientHostContext = hostContext as ClientTradeUiHostContext;
            this.gameScope = clientHostContext?.CaptureOfferGameScope();
            this.tradeService = hostContext.TradeService;

            this.doCloseX = true;
            this.closeOnAccept = false;
            this.closeOnCancel = false;
            this.closeOnClickedOutside = false;
            this.forcePause = true;
            this.draggable = true;
            this.resizeable = true;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            ClampToSafeArea();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            ClampToSafeArea();
            if(shouldClose||gameScope!=null&&!gameScope.IsValid()){Close();return;}
            if(tradeUpdated)
            {
                ClientTradeSnapshot latest;
                lock(updatedTradeLock){latest=updatedTrade;tradeUpdated=false;}
                if(latest!=null)
                {
                    trade=latest;
                    presentationDirty=true;
                }
            }
            if(!closed&&clientHostContext?.OfferOperations!=null&&seenOfferRevision!=clientHostContext.OfferOperations.Revision)
            {seenOfferRevision=clientHostContext.OfferOperations.Revision;refreshAvailableItems();}
            try {UpdatePresentation();}catch(Exception error){logMessage("Trade offer presentation failed: "+error,LogLevel.ERROR);Close();}
        }

        private void ClampToSafeArea()
        {
            windowRect = UiScreenSafeArea.ClampWindow(
                windowRect,
                new Vector2(MINIMUM_WINDOW_WIDTH, MINIMUM_WINDOW_HEIGHT));
        }

        public override void PreOpen()
        {
            base.PreOpen();

            try
            {
                tradeService.OnTradeCompleted += OnTradeFinished;
                tradeService.OnTradeCancelled += OnTradeFinished;
                tradeService.OnTradeUpdateSuccess += OnTradeUpdated;
                tradeService.OnTradeUpdateFailure += OnTradeUpdated;
                if (clientHostContext?.InventoryReadApi != null)
                {
                    inventoryChangedHandler = (_, __) => hostContext.RunOnMainThread(()=>{if(!closed&&(gameScope==null||gameScope.IsValid()))refreshAvailableItems();});
                    clientHostContext.InventoryReadApi.InventoryChanged += inventoryChangedHandler;
                }
                refreshAvailableItems();
                OpenPresentation();
            }
            catch {Close(false);throw;}

        }

        public override void Close(bool doCloseSound = true)
        {
            base.Close(doCloseSound);
            closed = true;
            ClosePresentation();

            tradeService.OnTradeCompleted -= OnTradeFinished;
            tradeService.OnTradeCancelled -= OnTradeFinished;
            if (clientHostContext?.InventoryReadApi != null && inventoryChangedHandler != null)
                clientHostContext.InventoryReadApi.InventoryChanged -= inventoryChangedHandler;
            inventoryChangedHandler = null;
            DisposeAvailableItems();
            UnsubscribeTradeUpdates();
        }

        private bool OfferBusy=>gameScope!=null&&!gameScope.IsValid()||clientHostContext?.OfferOperations?.IsBusy(trade.TradeId)==true;

        private void UpdateTradeItems()
        {
            string token=null;TradeOfferEscrow escrow=null;
            try
            {
                if(clientHostContext?.OfferOperations==null)throw new InvalidOperationException("TradeOfferOwnerUnavailable");
                var operations=clientHostContext.OfferOperations;
                escrow=new TradeOfferEscrow(clientHostContext,gameScope);
                if(!escrow.ValidateScope())throw new InvalidOperationException(escrow.LastError);
                token=operations.Begin(trade.TradeId,false,escrow.Commit,escrow.Restore,escrow.Unknown,escrow.Abort,()=>escrow.LastError);escrow.Token=token;
                var inventoryOffer=new List<TradeItemSnapshot>();
                var selectedInventory=availableItems.Where(i=>i.IsInventory&&i.Selected>0).ToArray();
                if(selectedInventory.Length>0)
                {
                    var api=clientHostContext.InventoryReservationApi;
                    if(api==null)throw new InvalidOperationException("Inventory reservations are unavailable.");
                    var reservation=api.TryReserve(new InventoryReservationRequest {OperationId="trade-offer:"+trade.TradeId+":"+token,OwnerExtensionId="builtin.trade",Purpose="trade.offer",
                        Lines=selectedInventory.Select(i=>new InventoryReservationLine {EntryId=i.InventoryEntry.EntryId,Quantity=i.Selected}).ToArray()});
                    if(!reservation.Succeeded||reservation.Reservation==null)throw new InvalidOperationException(reservation.Reason??"Inventory items could not be reserved.");
                    escrow.ReservationId=reservation.Reservation.ReservationId;
                    var materialized=api.MaterializeReservation(escrow.ReservationId);
                    try
                    {
                        if(!materialized.Succeeded)throw new InvalidOperationException(materialized.Reason??"Reserved inventory items could not be materialized.");
                        foreach(var thing in materialized.Things)inventoryOffer.Add(TradeItemConverter.ConvertThingFromVerse(thing));
                    }
                    finally {DiscardTransientThings(materialized?.Things);}
                }
                foreach(var stack in availableItems)foreach(var popped in stack.PopSelectedPhysical())escrow.Physical.Add(popped);
                foreach(var popped in escrow.Physical)popped.DeSpawn();
                var actualOffer=trade.ItemsOnOffer.Concat(escrow.Physical.Select(t=>TradeItemConverter.ConvertThingFromVerse(t.Thing))).Concat(inventoryOffer).ToArray();
                string id=trade.TradeId;
                operations.Submit(token,correlation=>tradeService.UpdateTradeItems(id,actualOffer,correlation));
                editorNotice=L("editorOfferSubmitted");
            }
            catch(Exception error)
            {
                if(token!=null)clientHostContext.OfferOperations.AbortBeforeSend(token);
                editorNotice=L("editorPreparationFailed");
                hostContext.Log(new LogEventArgs("Trade offer preparation failed: "+error,LogLevel.ERROR));
            }
            if(!closed)refreshAvailableItems();
        }

        private void ResetTradeItems()
        {
            try
            {
                if(clientHostContext?.OfferOperations==null)throw new InvalidOperationException("TradeOfferOwnerUnavailable");
                var owner=clientHostContext.OfferOperations;
                var context=clientHostContext;
                var escrow=new TradeOfferEscrow(context,gameScope);
                if(!escrow.ValidateScope())throw new InvalidOperationException(escrow.LastError);
                var payloads=TradeOfferEscrow.FreezeReturn(context,trade.ItemsOnOffer);
                string id=trade.TradeId;
                string token=owner.Begin(id,true,()=>escrow.ReturnFrozen(payloads),()=>TradeOfferSettlement.Done,null,errorSource:()=>escrow.LastError);
                owner.Submit(token,correlation=>tradeService.UpdateTradeItems(id,Array.Empty<TradeItemSnapshot>(),correlation));
                foreach(var stack in availableItems)stack.Selected=0;
                rowDrafts.Clear();rowErrors.Clear();quantityDraft="0";quantityError=null;editorNotice=L("editorResetSubmitted");
            }
            catch(Exception error){editorNotice=L("editorPreparationFailed");hostContext.Log(new LogEventArgs("Trade reset request failed: "+error,LogLevel.ERROR));}
        }

        private void RebuildAvailableItemFilter()
        {
            filteredAvailableItems.Clear();
            for (int i = 0; i < availableItems.Count; i++)
            {
                TradeAvailableItem stack = availableItems[i];
                if (stack.Count > 0 && stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    filteredAvailableItems.Add(stack);
            }
        }

        private void OnTradeFinished(object sender, TradeCompletionEventArgs args)
        {
            if(args==null||!CompletionMatches(trade.TradeId,args.TradeId))return;
            hostContext.RunOnMainThread(() => {if(!closed)shouldClose=true;});
        }

        private void OnTradeUpdated(object sender, TradeUpdateEventArgs args)
        {
            hostContext.RunOnMainThread(() => applyTradeUpdated(args));
        }

        internal static bool CompletionMatches(string currentId,string completedId)
        {return !string.IsNullOrWhiteSpace(currentId)&&!string.IsNullOrWhiteSpace(completedId)&&string.Equals(currentId,completedId,StringComparison.OrdinalIgnoreCase);}

        private void sendTradeStatusUpdate(bool accepted)
        {
            if(OfferBusy)return;
            hostContext.Log(new LogEventArgs($"[TradeWindow] Accept toggled: tradeId={trade.TradeId}, accepted={accepted}", LogLevel.DEBUG));
            setPendingAccepted(accepted);
            try
            {
                tradeService.UpdateTradeStatus(trade.TradeId, accepted: accepted);
            }
            catch (Exception exception)
            {
                clearPendingAccepted();
                hostContext.Log(new LogEventArgs($"Failed to send trade status update for trade '{trade.TradeId}': {exception.Message}", LogLevel.ERROR));
            }
        }

        private void sendCancelTradeRequest()
        {
            if(OfferBusy)return;
            hostContext.Log(new LogEventArgs($"[TradeWindow] Cancel clicked: tradeId={trade.TradeId}", LogLevel.DEBUG));
            try
            {
                tradeService.CancelTrade(trade.TradeId);
            }
            catch (Exception exception)
            {
                hostContext.Log(new LogEventArgs($"Failed to send trade cancel request for trade '{trade.TradeId}': {exception.Message}", LogLevel.ERROR));
            }
        }

        private void refreshAvailableItems()
        {
            if(closed||gameScope!=null&&!gameScope.IsValid())return;
            presentationDirty=true; selectedItem=null; itemPage=0; quantityError=null;
            DisposeAvailableItems();
            List<Map> homeMaps = Find.Maps.Where(map => map != null && map.IsPlayerHome).ToList();
            List<Thing> rawThings = StoredThingCollector
                .Collect(homeMaps, hostContext.AllItemsTradable, logMessage)
                .Where(thing => thing.def.category == ThingCategory.Item && !thing.def.IsCorpse)
                .ToList();
            availableItems = StackedThings.GroupThings(rawThings, logMessage)
                .Select(TradeAvailableItem.FromPhysical).ToList();
            if (clientHostContext?.InventoryReservationApi != null)
            {
                foreach (InventoryEntry entry in clientHostContext.InventoryReservationApi.GetAvailableSnapshot())
                {
                    try
                    {
                        InventoryMaterializationResult preview =
                            clientHostContext.InventoryReservationApi.CreatePreview(entry.EntryId);
                        if (!preview.Succeeded || preview.Things == null || preview.Things.Count != 1)
                        {
                            DiscardTransientThings(preview?.Things);
                            continue;
                        }
                        availableItems.Add(TradeAvailableItem.FromInventory(entry, preview.Things[0]));
                    }
                    catch (Exception exception)
                    {
                        hostContext.Log(new LogEventArgs(
                            $"[TradeWindow] Inventory entry '{entry.EntryId}' cannot be offered: {exception}",
                            LogLevel.WARNING));
                    }
                }
            }
            filteredAvailableItems = availableItems
                .Where(stack => stack.Count > 0 && stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) > -1)
                .ToList();

            hostContext.Log(new LogEventArgs($"[TradeWindow] refreshAvailableItems: homeMaps={homeMaps.Count()}, allItemsTradable={hostContext.AllItemsTradable}, rawThings={rawThings.Count}, groupedStacks={availableItems.Count}, filteredStacks={filteredAvailableItems.Count}, searchText='{searchText}'", LogLevel.DEBUG));
        }

        private void DisposeAvailableItems()
        {
            ClearQuantityDrafts();
            foreach (TradeAvailableItem item in availableItems) item?.Dispose();
            availableItems.Clear();
            filteredAvailableItems.Clear();
        }

        private static void DiscardTransientThings(IEnumerable<Thing> things)
        {
            foreach (Thing thing in things ?? Enumerable.Empty<Thing>())
            {
                if (thing != null && !thing.Destroyed && !thing.Spawned && thing.ParentHolder == null)
                    thing.Destroy(DestroyMode.Vanish);
            }
        }

        private void applyTradeUpdated(TradeUpdateEventArgs args)
        {
            if(closed||args==null)return;
            hostContext.Log(new LogEventArgs($"[TradeWindow] OnTradeUpdated: tradeId={args.Trade?.TradeId}, failure={args.FailureReason}, token={args.Token ?? "null"}", LogLevel.DEBUG));
            bool matchingTrade = args.Trade != null &&
                string.Equals(args.Trade.TradeId, trade.TradeId, StringComparison.OrdinalIgnoreCase);
            if (matchingTrade)
            {
                bool? pendingAcceptedValue = getPendingAccepted();
                if (args.FailureReason != TradeFailureReason.None ||
                    (pendingAcceptedValue.HasValue && args.Trade.Accepted == pendingAcceptedValue.Value))
                {
                    clearPendingAccepted();
                }

                lock (updatedTradeLock)
                {
                    updatedTrade = args.Trade;
                    tradeUpdated = true;
                }
            }

        }

        private void UnsubscribeTradeUpdates()
        {
            tradeService.OnTradeUpdateSuccess -= OnTradeUpdated;
            tradeService.OnTradeUpdateFailure -= OnTradeUpdated;
        }

        private void logMessage(string message, LogLevel level)
        {
            hostContext.Log(new LogEventArgs(message, level));
        }

        private bool? getPendingAccepted()
        {
            lock (pendingAcceptedLock)
            {
                return pendingAccepted;
            }
        }

        private void setPendingAccepted(bool? value)
        {
            lock (pendingAcceptedLock)
            {
                pendingAccepted = value;
            }
        }

        private void clearPendingAccepted()
        {
            lock (pendingAcceptedLock)
            {
                pendingAccepted = null;
            }
        }

    }
}
