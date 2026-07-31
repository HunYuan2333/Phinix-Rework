using System;
using System.Collections.Generic;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public enum DirectTradeState
    {
        Pending,
        Accepted,
        Negotiating,
        Confirmed,
        Executing,
        Completed,
        Cancelled
    }

    public class TradeOffer
    {
        public List<PawnSummary> Pawns = new List<PawnSummary>();
        public int SilverAmount;
        public List<string> ItemSummaries = new List<string>();
        // Serialized pawn data (b64 compressed XML) — populated when pawns are added to the offer
        public List<string> PawnData = new List<string>();
    }

    public class DirectTrade
    {
        public string Id;
        public string InitiatorUuid;
        public string TargetUuid;
        public string InitiatorName;
        public string TargetName;
        public TradeOffer InitiatorOffer = new TradeOffer();
        public TradeOffer TargetOffer = new TradeOffer();
        public bool InitiatorConfirmed;
        public bool TargetConfirmed;
        public DirectTradeState State = DirectTradeState.Pending;
        public DateTime CreatedAtUtc;
        public DateTime ExpiresAtUtc;

        // Local only — held pawns/items that have been despawned
        public List<Pawn> HeldPawns = new List<Pawn>();
        public List<Thing> HeldItems = new List<Thing>();
        public bool LocalExecuteSent;
    }
}
