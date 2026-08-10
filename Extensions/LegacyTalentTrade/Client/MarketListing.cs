using System;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public enum MarketListingState
    {
        Active,
        Sold,
        Delisted
    }

    public class MarketListing
    {
        public string Id;
        public string SellerUuid;
        public string SellerName;
        public PawnSummary Summary;
        // Optional metadata from the seller. Empty for listings created by
        // older clients; receivers must then show an unknown-compatibility warning.
        public string DefManifestData;
        public int PriceSilver;
        public MarketListingState State = MarketListingState.Active;
        public DateTime CreatedAtUtc;
        public DateTime LastRefreshUtc;

        // Local only — held pawn (seller side)
        public Verse.Pawn HeldPawn;
    }
}
