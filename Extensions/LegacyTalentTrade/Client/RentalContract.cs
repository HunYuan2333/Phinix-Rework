using System;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public enum RentalContractState
    {
        Listed,
        Active,
        Returned,
        PawnLost
    }

    public class RentalContract
    {
        public string Id;
        public string OwnerUuid;
        public string RenterUuid;
        public string OwnerName;
        public string RenterName;
        public PawnSummary Summary;
        // Optional metadata from the owner; see MarketListing.DefManifestData.
        public string DefManifestData;
        public int PricePerDay;
        public int Deposit;
        public int MaxDays;
        public int RentedDays;
        public int StartTick;
        public int ExpiryTick;
        public RentalContractState State = RentalContractState.Listed;

        // Local tracking
        public string RentedPawnThingID;
        public string OriginalPawnData;
    }
}
