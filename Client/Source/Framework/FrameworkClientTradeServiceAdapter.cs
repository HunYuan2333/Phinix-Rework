using System;
using System.Collections.Generic;
using System.Linq;
using Authentication;
using Trading;
using UserManagement;
using Utils;
using Utils.Framework;

namespace PhinixClient.Framework
{
    public sealed class FrameworkClientTradeServiceAdapter : IClientTradeService
    {
        private readonly PhinixFrameworkTradeClientService tradeService;
        private readonly PhinixFrameworkClient frameworkClient;
        private readonly ClientAuthenticator authenticator;
        private readonly ClientUserManager userManager;
        private readonly Func<ClientFrameworkContext> createContext;

        public FrameworkClientTradeServiceAdapter(PhinixFrameworkTradeClientService tradeService, PhinixFrameworkClient frameworkClient, ClientAuthenticator authenticator, ClientUserManager userManager, Func<ClientFrameworkContext> createContext, Action<LogEventArgs> log)
        {
            this.tradeService = tradeService;
            this.frameworkClient = frameworkClient;
            this.authenticator = authenticator;
            this.userManager = userManager;
            this.createContext = createContext;
        }

        public event EventHandler<LogEventArgs> OnLogEntry
        {
            add { }
            remove { }
        }

        public event EventHandler<CreateTradeEventArgs> OnTradeCreationSuccess
        {
            add => tradeService.OnTradeCreationSuccess += value;
            remove => tradeService.OnTradeCreationSuccess -= value;
        }

        public event EventHandler<CreateTradeEventArgs> OnTradeCreationFailure
        {
            add => tradeService.OnTradeCreationFailure += value;
            remove => tradeService.OnTradeCreationFailure -= value;
        }

        public event EventHandler<CompleteTradeEventArgs> OnTradeCompleted
        {
            add => tradeService.OnTradeCompleted += value;
            remove => tradeService.OnTradeCompleted -= value;
        }

        public event EventHandler<CompleteTradeEventArgs> OnTradeCancelled
        {
            add => tradeService.OnTradeCancelled += value;
            remove => tradeService.OnTradeCancelled -= value;
        }

        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateSuccess
        {
            add => tradeService.OnTradeUpdateSuccess += value;
            remove => tradeService.OnTradeUpdateSuccess -= value;
        }

        public event EventHandler<TradeUpdateEventArgs> OnTradeUpdateFailure
        {
            add => tradeService.OnTradeUpdateFailure += value;
            remove => tradeService.OnTradeUpdateFailure -= value;
        }

        public event EventHandler<TradesSyncedEventArgs> OnTradesSynced
        {
            add => tradeService.OnTradesSynced += value;
            remove => tradeService.OnTradesSynced -= value;
        }

        public void CreateTrade(string uuid)
        {
            frameworkClient.SendFrameworkPacket(tradeService.CreateTradeRequest(uuid, createContext()));
        }

        public void CancelTrade(string tradeId)
        {
            frameworkClient.SendFrameworkPacket(tradeService.CreateStatusUpdateRequest(tradeId, null, true, createContext()));
        }

        public string[] GetTradeIds() => tradeService.GetTradeIds();

        public ImmutableTrade[] GetTrades() => tradeService.GetImmutableTrades(userManager);

        public ImmutableTrade[] GetTradesExceptWith(IEnumerable<string> otherPartyUuids)
        {
            HashSet<string> ignored = new HashSet<string>(otherPartyUuids ?? Enumerable.Empty<string>());
            return GetTrades().Where(trade => !ignored.Contains(trade.OtherPartyUuid)).ToArray();
        }

        public bool TryGetTrade(string tradeId, out ImmutableTrade trade) => tradeService.TryGetImmutableTrade(tradeId, userManager, out trade);

        public bool TryGetOtherPartyUuid(string tradeId, out string otherPartyUuid) => tradeService.TryGetOtherPartyUuid(tradeId, userManager.Uuid, out otherPartyUuid);

        public bool TryGetOtherPartyAccepted(string tradeId, out bool otherPartyAccepted) => tradeService.TryGetOtherPartyAccepted(tradeId, userManager.Uuid, out otherPartyAccepted);

        public bool TryGetPartyAccepted(string tradeId, string partyUuid, out bool accepted) => tradeService.TryGetPartyAccepted(tradeId, partyUuid, out accepted);

        public bool TryGetItemsOnOffer(string tradeId, string uuid, out IEnumerable<ProtoThing> items) => tradeService.TryGetItemsOnOffer(tradeId, uuid, out items);

        public void UpdateTradeItems(string tradeId, IEnumerable<ProtoThing> items, string token = "")
        {
            FrameworkPacket packet = tradeService.CreateOfferUpdateRequest(tradeId, items, createContext());
            if (!string.IsNullOrEmpty(token))
            {
                packet.SetCorrelationId(token);
            }

            frameworkClient.SendFrameworkPacket(packet);
        }

        public void UpdateTradeStatus(string tradeId, bool? accepted = null, bool? cancelled = null)
        {
            frameworkClient.SendFrameworkPacket(tradeService.CreateStatusUpdateRequest(tradeId, accepted, cancelled, createContext()));
        }

        public void RequestInitialSync()
        {
            tradeService.RequestSnapshot(frameworkClient, authenticator.Authenticated, userManager.LoggedIn, authenticator.SessionId, userManager.Uuid);
        }
    }
}
