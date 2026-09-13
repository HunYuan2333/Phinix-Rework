using System;
using PhinixClient;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Rental sub-tab panel: browse rental listings, rent pawns, list pawns for rent.
    /// </summary>
    public class RentalPanel
    {
        private const float ROW_HEIGHT = 90f;
        private const float BUTTON_WIDTH = 90f;
        private const float BUTTON_HEIGHT = 30f;
        private const float TOOLBAR_HEIGHT = 36f;
        private const float SPACING = 6f;

        private readonly TalentToolbar toolbar;
        private readonly TalentForm form = new TalentForm("Phinix_legacyTalentTrade_selectRentalPawn",
            "Phinix_legacyTalentTrade_rentalPricePerDay", "Phinix_legacyTalentTrade_rentalMaxDays", "Phinix_legacyTalentTrade_rentalDeposit");
        private readonly List<RentalContract> visibleContracts = new List<RentalContract>();
        private readonly List<TalentCard> cards = new List<TalentCard>();
        private float[] offsets = Array.Empty<float>();
        private int layoutVersion = -1;
        private ViewMode layoutMode;
        private string layoutUuid;
        private object layoutLanguage;
        private float layoutWidth = -1f;
        private int nextDaysChangeTick = int.MaxValue;

        public RentalPanel()
        {
            toolbar = new TalentToolbar(ActivateToolbar, "Phinix_legacyTalentTrade_rentalList",
                "Phinix_legacyTalentTrade_rentalMyListings", "Phinix_legacyTalentTrade_rentalMyRentals", "Phinix_legacyTalentTrade_refresh");
        }

        private Vector2 listScrollPos;
        // §8.3：Draw 路径不每帧取快照——按状态版本缓存
        private int cachedStateVersion = -1;
        private RentalContract[] cachedContracts = System.Array.Empty<RentalContract>();

        private enum ViewMode { Browse, MyListings, MyRentals, ListForRent }
        private ViewMode viewMode = ViewMode.Browse;

        // List-for-rent flow state
        private Pawn selectedPawn;
        private string pricePerDayBuffer = "50";
        private int pricePerDayValue = 50;
        private string maxDaysBuffer = "15";
        private int maxDaysValue = 15;
        private string depositBuffer = "500";
        private int depositValue = 500;

        public void Draw(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            bool creating = viewMode == ViewMode.ListForRent;
            float height = toolbar.Draw(rect, creating ? 1 : 4, creating ? 1 : 0,
                creating ? "Phinix_legacyTalentTrade_cancel" : "Phinix_legacyTalentTrade_rentalList");
            Rect content = TalentTradeUi.Below(rect, height + SPACING);
            if (content.height <= 0f) return;
            if (viewMode == ViewMode.ListForRent) DrawListForRentPanel(content);
            else DrawListings(content);
        }

        private void ActivateToolbar(int action)
        {
            switch (action)
            {
                case 0:
                    if (viewMode == ViewMode.ListForRent) { viewMode = ViewMode.Browse; ResetListState(); }
                    else viewMode = ViewMode.ListForRent;
                    break;
                case 1: viewMode = viewMode == ViewMode.MyListings ? ViewMode.Browse : ViewMode.MyListings; break;
                case 2: viewMode = viewMode == ViewMode.MyRentals ? ViewMode.Browse : ViewMode.MyRentals; break;
                case 3:
                    string uuid = TalentTradeManager.GetLocalUuid();
                    if (!string.IsNullOrEmpty(uuid)) TalentTradeManager.SendProtocol(TalentTradeProtocol.BuildMarketSync(uuid));
                    break;
            }
        }

        private void DrawListings(Rect rect)
        {
            if (cachedStateVersion != TalentTradeManager.StateVersion)
            {
                cachedStateVersion = TalentTradeManager.StateVersion;
                cachedContracts = TalentTradeManager.GetRentalContractsSnapshot();
            }
            string localUuid = TalentTradeManager.GetLocalUuid();
            float width = Mathf.Max(1f, rect.width - 16f);
            int ticks = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            bool daysChanged = ticks >= nextDaysChangeTick;
            if (layoutVersion != cachedStateVersion || layoutMode != viewMode || layoutUuid != localUuid ||
                layoutWidth != width || daysChanged || !ReferenceEquals(layoutLanguage, LanguageDatabase.activeLanguage))
            {
                layoutVersion = cachedStateVersion;
                layoutMode = viewMode;
                layoutUuid = localUuid;
                layoutWidth = width;
                layoutLanguage = LanguageDatabase.activeLanguage;
                nextDaysChangeTick = int.MaxValue;
                visibleContracts.Clear();
                cards.Clear();
                for (int i = 0; i < cachedContracts.Length; i++)
                {
                    RentalContract contract = cachedContracts[i];
                    if (contract == null) continue;
                    if ((contract.State == RentalContractState.Listed || contract.State == RentalContractState.Active) &&
                        !TradeablePawnUtility.CanRentPawn(contract.Summary)) continue;
                    bool include = viewMode == ViewMode.MyListings
                        ? contract.OwnerUuid == localUuid && contract.State == RentalContractState.Listed
                        : viewMode == ViewMode.MyRentals
                            ? contract.RenterUuid == localUuid && contract.State == RentalContractState.Active
                            : contract.State == RentalContractState.Listed;
                    if (!include) continue;
                    bool rented = contract.RenterUuid == localUuid && contract.State == RentalContractState.Active;
                    string text = TalentCard.Description(contract.Summary) + "\n" +
                        "Phinix_legacyTalentTrade_rentalOwner".Translate(contract.OwnerName ?? "???") + "\n" +
                        "Phinix_legacyTalentTrade_rentalPriceFormat".Translate(contract.PricePerDay.ToString()) + " | " +
                        "Phinix_legacyTalentTrade_rentalDepositFormat".Translate(contract.Deposit.ToString()) + "\n" +
                        "Phinix_legacyTalentTrade_rentalMaxDays".Translate() + ": " + contract.MaxDays;
                    if (rented && contract.ExpiryTick > 0)
                    {
                        int days = Mathf.Max(0, (contract.ExpiryTick - ticks) / GenDate.TicksPerDay);
                        if (days > 0)
                            nextDaysChangeTick = Math.Min(nextDaysChangeTick, contract.ExpiryTick - days * GenDate.TicksPerDay + 1);
                        text += "\n" + "Phinix_legacyTalentTrade_rentalDaysLeft".Translate(
                            days.ToString());
                    }
                    visibleContracts.Add(contract);
                    cards.Add(new TalentCard(text, TalentCard.Details(contract.Summary),
                        rented ? "Phinix_legacyTalentTrade_rentalReturn" :
                        contract.OwnerUuid == localUuid ? "Phinix_legacyTalentTrade_rentalDelist" : "Phinix_legacyTalentTrade_rentalRent", width));
                }
                offsets = new float[cards.Count + 1];
                for (int i = 0; i < cards.Count; i++) offsets[i + 1] = offsets[i] + cards[i].Height + SPACING;
            }
            if (cards.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, "Phinix_legacyTalentTrade_rentalNoListings".Translate());
                return;
            }
            listScrollPos.y = Mathf.Clamp(listScrollPos.y, 0f, Mathf.Max(0f, offsets[cards.Count] - rect.height));
            Widgets.BeginScrollView(rect, ref listScrollPos, new Rect(0f, 0f, width, offsets[cards.Count]));
            try
            {
                var range = VirtualListLayout.GetDynamicRange(offsets, cards.Count, listScrollPos.y, rect.height, 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                    if (cards[i].Draw(new Rect(0f, offsets[i], width, cards[i].Height)))
                    {
                        var contract = visibleContracts[i];
                        if (contract.RenterUuid == localUuid && contract.State == RentalContractState.Active) DoReturn(contract);
                        else if (contract.OwnerUuid == localUuid) DoDelist(contract);
                        else ConfirmRent(contract);
                    }
            }
            finally { Widgets.EndScrollView(); }
        }



        // --- List for Rent flow ---

        private void DrawListForRentPanel(Rect rect)
        {
            form.Begin(rect, selectedPawn);
            try
            {
                if (form.PawnButton()) ShowPawnPicker();
                form.Number(1, ref pricePerDayBuffer, ref pricePerDayValue, 0);
                form.Number(2, ref maxDaysBuffer, ref maxDaysValue, 1);
                form.Number(3, ref depositBuffer, ref depositValue, 0);
                if (form.Confirm(selectedPawn != null && pricePerDayValue > 0 && maxDaysValue > 0)) DoListForRent();
            }
            finally { form.End(); }
        }

        private void ShowPawnPicker()
        {
            List<Pawn> tradeablePawns = TradeablePawnUtility.GetTradeablePawns(Find.CurrentMap);
            tradeablePawns.RemoveAll(p => !TradeablePawnUtility.CanRentPawn(p));

            if (tradeablePawns.Count == 0)
            {
                Messages.Message("Phinix_legacyTalentTrade_rentalNoColonistsAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (Pawn p in tradeablePawns)
            {
                Pawn captured = p;
                options.Add(new FloatMenuOption(TradeablePawnUtility.GetLabel(captured), delegate
                {
                    selectedPawn = captured;
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DoListForRent()
        {
            if (selectedPawn == null || pricePerDayValue <= 0 || maxDaysValue <= 0) return;

            if (!selectedPawn.Spawned || selectedPawn.Dead)
            {
                Messages.Message("Phinix_legacyTalentTrade_rentalNoColonistsAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                selectedPawn = null;
                return;
            }

            if (!TradeablePawnUtility.CanRentPawn(selectedPawn))
            {
                Messages.Message("Phinix_legacyTalentTrade_rentalColonistOnly".Translate(), MessageTypeDefOf.RejectInput, false);
                selectedPawn = null;
                return;
            }

            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            string rentalId = Guid.NewGuid().ToString("N").Substring(0, 12);
            string localName = TalentTradeManager.GetLocalDisplayName();
            PawnSummary summary = PawnSummary.FromPawn(selectedPawn);
            string defManifestData = DefManifestHelper.SerializeCompressed(selectedPawn);

            // Serialize pawn
            string b64Pawn = PawnSerializer.Serialize(selectedPawn);
            if (string.IsNullOrEmpty(b64Pawn))
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】Failed to serialize pawn for rental listing.");
                return;
            }

            PawnSerializer.DespawnAndHold(selectedPawn);

            // Register locally
            TalentTradeManager.AddLocalRentalListing(rentalId, localUuid, localName, summary,
                pricePerDayValue, maxDaysValue, depositValue, selectedPawn, b64Pawn, defManifestData);

            // Broadcast
            string msg = TalentTradeProtocol.BuildRentalList(rentalId, localUuid, summary.ToBase64(),
                pricePerDayValue, maxDaysValue, depositValue, localName, defManifestData);
            TalentTradeManager.SendProtocol(msg);

            ResetListState();
            viewMode = ViewMode.Browse;
        }

        // --- Rent flow ---

        private void ConfirmRent(RentalContract contract)
        {
            // Race check
            if (contract.Summary != null && !string.IsNullOrEmpty(contract.Summary.RaceDefName))
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(contract.Summary.RaceDefName) == null)
                {
                    string pawnName = contract.Summary.GetDisplayLabel();
                    string message = "Phinix_legacyTalentTrade_cannotBuyNoRace".Translate(pawnName, contract.Summary.RaceDefName);
                    Find.WindowStack.Add(new Dialog_MessageBox(message));
                    return;
                }
            }

            string pawnName2 = contract.Summary != null ? contract.Summary.GetDisplayLabel() : "???";
            string confirmText = "Phinix_legacyTalentTrade_rentalRentConfirm".Translate(
                pawnName2, contract.PricePerDay.ToString(), contract.MaxDays.ToString(), contract.Deposit.ToString());

            TransferCompatibilityUi.Confirm(
                confirmText,
                pawnName2,
                contract.DefManifestData,
                delegate { DoRent(contract); });
        }

        private void DoRent(RentalContract contract)
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            string localName = TalentTradeManager.GetLocalDisplayName();
            string msg = TalentTradeProtocol.BuildRentalRent(contract.Id, localUuid, contract.MaxDays, localName);
            TalentTradeManager.SendProtocol(msg);
        }

        // --- Return ---

        private void DoReturn(RentalContract contract)
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            TalentTradeManager.ReturnRentedPawn(contract.Id);
        }

        // --- Delist ---

        private void DoDelist(RentalContract contract)
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            TalentTradeManager.RestoreDelistedRentalPawn(contract.Id);

            string msg = TalentTradeProtocol.BuildRentalDelist(contract.Id, localUuid);
            TalentTradeManager.SendProtocol(msg);
        }

        private void ResetListState()
        {
            selectedPawn = null;
            pricePerDayBuffer = "50";
            pricePerDayValue = 50;
            maxDaysBuffer = "15";
            maxDaysValue = 15;
            depositBuffer = "500";
            depositValue = 500;
        }
    }
}
