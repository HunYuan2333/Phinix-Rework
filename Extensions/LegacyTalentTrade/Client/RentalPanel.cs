using System;
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
            Rect toolbarRect = new Rect(rect.x, rect.y, rect.width, TOOLBAR_HEIGHT);
            DrawToolbar(toolbarRect);

            Rect contentRect = new Rect(rect.x, rect.y + TOOLBAR_HEIGHT + SPACING, rect.width, rect.height - TOOLBAR_HEIGHT - SPACING);

            switch (viewMode)
            {
                case ViewMode.ListForRent:
                    DrawListForRentPanel(contentRect);
                    break;
                default:
                    DrawListings(contentRect);
                    break;
            }
        }

        private void DrawToolbar(Rect rect)
        {
            float x = rect.x;

            // List for Rent button
            Rect listBtn = new Rect(x, rect.y, 120f, rect.height);
            string listLabel = viewMode == ViewMode.ListForRent ? "Phinix_legacyTalentTrade_cancel".Translate() : "Phinix_legacyTalentTrade_rentalList".Translate();
            if (Widgets.ButtonText(listBtn, listLabel))
            {
                if (viewMode == ViewMode.ListForRent)
                {
                    viewMode = ViewMode.Browse;
                    ResetListState();
                }
                else
                {
                    viewMode = ViewMode.ListForRent;
                }
            }
            x += 120f + SPACING;

            if (viewMode != ViewMode.ListForRent)
            {
                // My Listings
                Rect myListBtn = new Rect(x, rect.y, 100f, rect.height);
                if (Widgets.ButtonText(myListBtn, "Phinix_legacyTalentTrade_rentalMyListings".Translate()))
                {
                    viewMode = viewMode == ViewMode.MyListings ? ViewMode.Browse : ViewMode.MyListings;
                }
                x += 100f + SPACING;

                // My Rentals
                Rect myRentBtn = new Rect(x, rect.y, 100f, rect.height);
                if (Widgets.ButtonText(myRentBtn, "Phinix_legacyTalentTrade_rentalMyRentals".Translate()))
                {
                    viewMode = viewMode == ViewMode.MyRentals ? ViewMode.Browse : ViewMode.MyRentals;
                }

                // Refresh
                Rect refreshRect = new Rect(rect.xMax - 80f - 20f, rect.y, 80f, rect.height);
                if (Widgets.ButtonText(refreshRect, "Phinix_legacyTalentTrade_refresh".Translate()))
                {
                    // Request rental sync (reuse market sync pattern)
                    string uuid = TalentTradeManager.GetLocalUuid();
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        TalentTradeManager.SendProtocol(TalentTradeProtocol.BuildMarketSync(uuid));
                    }
                }
            }
        }

        private void DrawListings(Rect rect)
        {
            // §8.3：Draw 路径不每帧取快照——按状态版本缓存
            if (cachedStateVersion != TalentTradeManager.StateVersion)
            {
                cachedStateVersion = TalentTradeManager.StateVersion;
                cachedContracts = TalentTradeManager.GetRentalContractsSnapshot();
            }
            RentalContract[] contracts = cachedContracts;
            string localUuid = TalentTradeManager.GetLocalUuid();

            List<RentalContract> filtered = new List<RentalContract>();
            for (int i = 0; i < contracts.Length; i++)
            {
                if (contracts[i] == null) continue;
                if ((contracts[i].State == RentalContractState.Listed || contracts[i].State == RentalContractState.Active)
                    && !TradeablePawnUtility.CanRentPawn(contracts[i].Summary)) continue;

                switch (viewMode)
                {
                    case ViewMode.MyListings:
                        if (contracts[i].OwnerUuid == localUuid && contracts[i].State == RentalContractState.Listed)
                            filtered.Add(contracts[i]);
                        break;
                    case ViewMode.MyRentals:
                        if (contracts[i].RenterUuid == localUuid && contracts[i].State == RentalContractState.Active)
                            filtered.Add(contracts[i]);
                        break;
                    default:
                        if (contracts[i].State == RentalContractState.Listed)
                            filtered.Add(contracts[i]);
                        break;
                }
            }

            if (filtered.Count == 0)
            {
                Widgets.DrawMenuSection(rect);
                Widgets.NoneLabelCenteredVertically(rect, "Phinix_legacyTalentTrade_rentalNoListings".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, filtered.Count * (ROW_HEIGHT + SPACING));
            Widgets.BeginScrollView(rect, ref listScrollPos, viewRect);

            float y = 0f;
            for (int i = 0; i < filtered.Count; i++)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, ROW_HEIGHT);
                DrawListingRow(rowRect, filtered[i], localUuid);
                y += ROW_HEIGHT + SPACING;
            }

            Widgets.EndScrollView();
        }

        private void DrawListingRow(Rect rect, RentalContract contract, string localUuid)
        {
            Widgets.DrawMenuSection(rect);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            Rect inner = rect.ContractedBy(6f);
            float infoWidth = inner.width - BUTTON_WIDTH - SPACING;
            Rect infoRect = new Rect(inner.x, inner.y, infoWidth, inner.height);

            // Name
            string displayLabel = contract.Summary != null ? contract.Summary.GetDisplayLabel() : "???";
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 22f), displayLabel);

            Text.Font = GameFont.Tiny;

            // Race + compatibility
            if (contract.Summary != null)
            {
                string raceName = contract.Summary.RaceDefName ?? "Human";
                bool hasRace = DefDatabase<ThingDef>.GetNamedSilentFail(raceName) != null;
                string raceStatus = hasRace ? "✓" : "✗";
                Widgets.Label(new Rect(infoRect.x, infoRect.y + 22f, infoRect.width, 18f),
                    $"{raceStatus} {raceName} | {contract.Summary.BiologicalAge} {"Phinix_legacyTalentTrade_ageUnit".Translate()}");
            }

            // Owner
            string ownerText = "Phinix_legacyTalentTrade_rentalOwner".Translate(contract.OwnerName ?? "???");
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), ownerText);

            // Price info
            string priceText = "Phinix_legacyTalentTrade_rentalPriceFormat".Translate(contract.PricePerDay.ToString());
            string depositText = "Phinix_legacyTalentTrade_rentalDepositFormat".Translate(contract.Deposit.ToString());
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 58f, infoRect.width, 18f),
                priceText + " | " + depositText + " | " + "Phinix_legacyTalentTrade_rentalMaxDays".Translate() + ": " + contract.MaxDays);

            Text.Font = GameFont.Small;

            // Action button
            Rect btnRect = new Rect(inner.xMax - BUTTON_WIDTH, inner.y + (inner.height - BUTTON_HEIGHT) / 2f, BUTTON_WIDTH, BUTTON_HEIGHT);

            bool isMine = contract.OwnerUuid == localUuid;
            bool isMyRental = contract.RenterUuid == localUuid && contract.State == RentalContractState.Active;

            if (isMyRental)
            {
                // Show days remaining
                if (contract.ExpiryTick > 0 && Find.TickManager != null)
                {
                    int ticksLeft = contract.ExpiryTick - Find.TickManager.TicksGame;
                    int daysLeft = Mathf.Max(0, ticksLeft / GenDate.TicksPerDay);
                    Rect daysRect = new Rect(btnRect.x, btnRect.y - 20f, BUTTON_WIDTH, 18f);
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(daysRect, "Phinix_legacyTalentTrade_rentalDaysLeft".Translate(daysLeft.ToString()));
                    Text.Font = GameFont.Small;
                }

                if (Widgets.ButtonText(btnRect, "Phinix_legacyTalentTrade_rentalReturn".Translate()))
                {
                    DoReturn(contract);
                }
            }
            else if (isMine && contract.State == RentalContractState.Listed)
            {
                if (Widgets.ButtonText(btnRect, "Phinix_legacyTalentTrade_rentalDelist".Translate()))
                {
                    DoDelist(contract);
                }
            }
            else if (contract.State == RentalContractState.Listed)
            {
                if (Widgets.ButtonText(btnRect, "Phinix_legacyTalentTrade_rentalRent".Translate()))
                {
                    ConfirmRent(contract);
                }
            }

            // Tooltip
            if (contract.Summary != null && Mouse.IsOver(infoRect))
            {
                string tip = contract.Summary.SkillsSummary;
                if (!string.IsNullOrEmpty(contract.Summary.TraitsSummary))
                    tip += "\n" + contract.Summary.TraitsSummary;
                if (!string.IsNullOrEmpty(contract.Summary.HealthSummary))
                    tip += "\n" + contract.Summary.HealthSummary;
                TooltipHandler.TipRegion(infoRect, tip);
            }
        }

        // --- List for Rent flow ---

        private void DrawListForRentPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(12f);
            float y = inner.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 30f), "Phinix_legacyTalentTrade_rentalList".Translate());
            Text.Font = GameFont.Small;
            y += 36f;

            // Pawn selector
            Widgets.Label(new Rect(inner.x, y, 120f, 28f), "Phinix_legacyTalentTrade_selectRentalPawn".Translate());
            Rect pawnBtnRect = new Rect(inner.x + 130f, y, 200f, 28f);
            string pawnLabel = selectedPawn != null ? TradeablePawnUtility.GetLabel(selectedPawn) : (string)"Phinix_legacyTalentTrade_select".Translate();
            if (Widgets.ButtonText(pawnBtnRect, pawnLabel))
            {
                ShowPawnPicker();
            }
            y += 36f;

            // Pawn preview
            if (selectedPawn != null)
            {
                PawnSummary preview = PawnSummary.FromPawn(selectedPawn);
                Text.Font = GameFont.Tiny;
                string previewText = preview.GetDisplayLabel() + "\n" + preview.SkillsSummary + "\n" + preview.TraitsSummary;
                float previewHeight = Text.CalcHeight(previewText, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, previewHeight), previewText);
                y += previewHeight + SPACING;
                Text.Font = GameFont.Small;
            }

            // Price per day
            Widgets.Label(new Rect(inner.x, y, 120f, 28f), "Phinix_legacyTalentTrade_rentalPricePerDay".Translate());
            Rect priceField = new Rect(inner.x + 130f, y, 120f, 28f);
            pricePerDayBuffer = Widgets.TextField(priceField, pricePerDayBuffer);
            int.TryParse(pricePerDayBuffer, out pricePerDayValue);
            if (pricePerDayValue < 0) pricePerDayValue = 0;
            Widgets.Label(new Rect(inner.x + 260f, y, 60f, 28f), "Phinix_legacyTalentTrade_silver".Translate());
            y += 36f;

            // Max days
            Widgets.Label(new Rect(inner.x, y, 120f, 28f), "Phinix_legacyTalentTrade_rentalMaxDays".Translate());
            Rect daysField = new Rect(inner.x + 130f, y, 120f, 28f);
            maxDaysBuffer = Widgets.TextField(daysField, maxDaysBuffer);
            int.TryParse(maxDaysBuffer, out maxDaysValue);
            if (maxDaysValue < 1) maxDaysValue = 1;
            y += 36f;

            // Deposit
            Widgets.Label(new Rect(inner.x, y, 120f, 28f), "Phinix_legacyTalentTrade_rentalDeposit".Translate());
            Rect depositField = new Rect(inner.x + 130f, y, 120f, 28f);
            depositBuffer = Widgets.TextField(depositField, depositBuffer);
            int.TryParse(depositBuffer, out depositValue);
            if (depositValue < 0) depositValue = 0;
            Widgets.Label(new Rect(inner.x + 260f, y, 60f, 28f), "Phinix_legacyTalentTrade_silver".Translate());
            y += 36f;

            // Confirm
            Rect confirmRect = new Rect(inner.x, y, 160f, 36f);
            bool canConfirm = selectedPawn != null && pricePerDayValue > 0 && maxDaysValue > 0;
            if (canConfirm && Widgets.ButtonText(confirmRect, "Phinix_legacyTalentTrade_confirm".Translate()))
            {
                DoListForRent();
            }
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
