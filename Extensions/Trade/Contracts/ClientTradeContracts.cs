using System;
using System.Collections.Generic;
using UserManagement;
using Utils;
using Utils.Framework;
using Phinix.TradeExtension;
using Verse;
using Thing = Verse.Thing;
namespace Phinix.TradeExtension.Client
{
    public interface IFrameworkTradeUpdateResultApi
    {
        void BeginTradeUpdate(string tradeId, string token);

        bool IsTradeUpdatePending(string tradeId, string token);

        bool CompleteTradeUpdate(string tradeId, string token, PhinixClient.Trade.TradeFailureReason failureReason, string failureMessage);
    }

    public interface IFrameworkTradeClientApi
    {
        event EventHandler RepositoryChanged;
        event EventHandler<PhinixClient.Trade.TradeCreationEventArgs> OnTradeCreationSuccess;
        event EventHandler<PhinixClient.Trade.TradeCreationEventArgs> OnTradeCreationFailure;
        event EventHandler<PhinixClient.Trade.TradeUpdateEventArgs> OnTradeUpdateSuccess;
        event EventHandler<PhinixClient.Trade.TradeUpdateEventArgs> OnTradeUpdateFailure;
        event EventHandler<PhinixClient.Trade.TradesSyncedEventArgs> OnTradesSynced;
        event EventHandler<PhinixClient.Trade.TradeCompletionEventArgs> OnTradeCompleted;
        event EventHandler<PhinixClient.Trade.TradeCompletionEventArgs> OnTradeCancelled;

        Phinix.TradeExtension.FrameworkTradeStateSnapshot[] GetRepositoryTrades();
        string[] GetTradeIds();
        PhinixClient.Trade.ClientTradeSnapshot[] GetTrades();
        bool TryGetTrade(string tradeId, out PhinixClient.Trade.ClientTradeSnapshot trade);
        bool TryGetOtherPartyUuid(string tradeId, string localUuid, out string otherPartyUuid);
        bool TryGetOtherPartyAccepted(string tradeId, string localUuid, out bool otherPartyAccepted);
        bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted);
        bool TryGetItemsOnOffer(string tradeId, string partyUuid, out IEnumerable<PhinixClient.Trade.TradeItemSnapshot> items);
        FrameworkPacket CreateSnapshotRequestPacket(string sessionId, string senderUuid);
        FrameworkPacket CreateTradeRequest(string otherPartyUuid, ClientFrameworkContext context);
        FrameworkItemPayload[] EncodeTradeItems(IEnumerable<PhinixClient.Trade.TradeItemSnapshot> tradeItems);
        FrameworkPacket CreateOfferUpdateRequest(string tradeId, IEnumerable<PhinixClient.Trade.TradeItemSnapshot> tradeItems, ClientFrameworkContext context);
        FrameworkPacket CreateOfferUpdateRequest(string tradeId, IEnumerable<string> itemPacketRefs, ClientFrameworkContext context);
        FrameworkPacket CreateStatusUpdateRequest(string tradeId, bool? accepted, bool? cancelled, ClientFrameworkContext context);
        void TrackPendingTradeUpdate(string tradeId, string token);
        void HandleSnapshot(FrameworkPacket packet);
        void HandleCreateResponse(FrameworkPacket packet);
        void HandleOfferUpdateResponse(FrameworkPacket packet);
        void HandleStatusUpdateResponse(FrameworkPacket packet);
        void HandleCompletedEvent(FrameworkPacket packet);
        void HandleCancelledEvent(FrameworkPacket packet);
    }

    /// <summary>Translated authoritative state input. A snapshot does not acknowledge an operation.</summary>
    public interface ITradeStateInput
    {
        void UpsertTrade(FrameworkTradeStateSnapshot snapshot);
        void RemoveTrade(string tradeId);
        void CompleteTrade(string tradeId, bool success, string otherPartyUuid, IEnumerable<FrameworkItemPayload> items);
    }

    public interface ITradeUiHostContext
    {
        IClientTradeService TradeService { get; }

        bool AllItemsTradable { get; }

        event EventHandler OnDisconnect;

        event EventHandler<UserDisplayNameChangedEventArgs> OnUserDisplayNameChanged;

        LookTargets DropPods(IEnumerable<Thing> verseThings);

        void RunOnMainThread(Action action);

        void OpenTradeWindow(PhinixClient.Trade.ClientTradeSnapshot trade);

        void Log(LogEventArgs args);
    }

}
