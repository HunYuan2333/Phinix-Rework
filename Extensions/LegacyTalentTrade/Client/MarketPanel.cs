using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Market sub-tab panel: browse listings, buy pawns, list pawns for sale.
    /// </summary>
    public class MarketPanel
    {
        private const float ROW_HEIGHT = 80f;
        private const float BUTTON_WIDTH = 90f;
        private const float BUTTON_HEIGHT = 30f;
        private const float TOOLBAR_HEIGHT = 36f;
        private const float SPACING = 6f;

        private Vector2 listScrollPos;
        private bool showMyListings;

        // Sell flow state
        private bool sellMode;
        private Pawn selectedPawn;
        private string priceBuffer = "100";
        private int priceValue = 100;

        public void Draw(Rect rect)
        {
            // Toolbar
            Rect toolbarRect = new Rect(rect.x, rect.y, rect.width, TOOLBAR_HEIGHT);
            DrawToolbar(toolbarRect);

            // Content
            Rect contentRect = new Rect(rect.x, rect.y + TOOLBAR_HEIGHT + SPACING, rect.width, rect.height - TOOLBAR_HEIGHT - SPACING);

            if (sellMode)
            {
                DrawSellPanel(contentRect);
            }
            else
            {
                DrawListings(contentRect);
            }
        }

        private void DrawToolbar(Rect rect)
        {
            float x = rect.x;

            // List for Sale button
            Rect sellBtnRect = new Rect(x, rect.y, 120f, rect.height);
            if (Widgets.ButtonText(sellBtnRect, sellMode ? "Phinix_legacyTalentTrade_cancel".Translate() : "Phinix_legacyTalentTrade_marketSell".Translate()))
            {
                sellMode = !sellMode;
                if (!sellMode) ResetSellState();
            }
            x += 120f + SPACING + 20f;

            // My Listings toggle
            if (!sellMode)
            {
                Rect myBtnRect = new Rect(x, rect.y, 120f, rect.height);
                if (Widgets.ButtonText(myBtnRect, "Phinix_legacyTalentTrade_marketMyListings".Translate()))
                {
                    showMyListings = !showMyListings;
                }
                x += 120f + SPACING;
            }

            // Refresh
            if (!sellMode)
            {
                // Force cleanup button (left of refresh)
                Rect cleanupRect = new Rect(rect.xMax - 80f - 20f - 80f - SPACING, rect.y, 80f, rect.height);
                if (Widgets.ButtonText(cleanupRect, "Phinix_legacyTalentTrade_forceCleanup".Translate()))
                {
                    ForceCleanupAllMyListings();
                }

                Rect refreshRect = new Rect(rect.xMax - 80f - 20f, rect.y, 80f, rect.height);
                if (Widgets.ButtonText(refreshRect, "Phinix_legacyTalentTrade_refresh".Translate()))
                {
                    string uuid = TalentTradeManager.GetLocalUuid();
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        TalentTradeManager.SendProtocol(TalentTradeProtocol.BuildMarketSync(uuid));
                    }
                }
            }
        }

        // --- Listings ---

        // §8.3：Draw 路径不每帧取快照——按状态版本缓存，状态变更时才刷新
        private int cachedStateVersion = -1;
        private MarketListing[] cachedListings = Array.Empty<MarketListing>();

        private MarketListing[] GetCachedListings()
        {
            if (cachedStateVersion != TalentTradeManager.StateVersion)
            {
                cachedStateVersion = TalentTradeManager.StateVersion;
                cachedListings = TalentTradeManager.GetMarketListingsSnapshot();
            }
            return cachedListings;
        }

        private void DrawListings(Rect rect)
        {
            MarketListing[] listings = GetCachedListings();
            string localUuid = TalentTradeManager.GetLocalUuid();

            // Filter - create a copy to avoid modification during iteration
            List<MarketListing> filtered = new List<MarketListing>();
            for (int i = 0; i < listings.Length; i++)
            {
                if (listings[i] == null) continue;
                if (listings[i].State != MarketListingState.Active) continue;
                if (showMyListings)
                {
                    if (listings[i].SellerUuid == localUuid)
                        filtered.Add(listings[i]);
                }
                else
                {
                    filtered.Add(listings[i]);
                }
            }

            if (filtered.Count == 0)
            {
                Widgets.DrawMenuSection(rect);
                Widgets.NoneLabelCenteredVertically(rect, "Phinix_legacyTalentTrade_marketNoListings".Translate());
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

        private void DrawListingRow(Rect rect, MarketListing listing, string localUuid)
        {
            // Background
            Widgets.DrawMenuSection(rect);
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            Rect inner = rect.ContractedBy(6f);

            // Left: pawn info
            float infoWidth = inner.width - BUTTON_WIDTH - SPACING;
            Rect infoRect = new Rect(inner.x, inner.y, infoWidth, inner.height);

            // Name line
            string displayLabel = listing.Summary != null ? listing.Summary.GetDisplayLabel() : "???";
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(infoRect.x, infoRect.y, infoRect.width, 22f), displayLabel);

            // Race + Age line
            Text.Font = GameFont.Tiny;
            string raceAge = "";
            if (listing.Summary != null)
            {
                string raceName = listing.Summary.RaceDefName ?? "Human";
                bool hasRace = DefDatabase<ThingDef>.GetNamedSilentFail(raceName) != null;
                string raceStatus = hasRace ? "✓" : "✗";
                raceAge = $"{raceStatus} {raceName} | {listing.Summary.BiologicalAge} {"Phinix_legacyTalentTrade_ageUnit".Translate()}";
            }
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 22f, infoRect.width, 18f), raceAge);

            // Seller line
            string sellerText = "Phinix_legacyTalentTrade_marketSeller".Translate(listing.SellerName ?? "???");
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 40f, infoRect.width, 18f), sellerText);

            // Price line
            string priceText = "Phinix_legacyTalentTrade_marketPriceFormat".Translate(listing.PriceSilver.ToString());
            Widgets.Label(new Rect(infoRect.x, infoRect.y + 58f, infoRect.width, 18f), priceText);

            Text.Font = GameFont.Small;

            // Right: action button
            Rect btnRect = new Rect(inner.xMax - BUTTON_WIDTH, inner.y + (inner.height - BUTTON_HEIGHT) / 2f, BUTTON_WIDTH, BUTTON_HEIGHT);

            bool isMine = listing.SellerUuid == localUuid;
            if (isMine)
            {
                if (Widgets.ButtonText(btnRect, "Phinix_legacyTalentTrade_marketDelist".Translate()))
                {
                    DelistListing(listing);
                }
            }
            else
            {
                if (Widgets.ButtonText(btnRect, "Phinix_legacyTalentTrade_marketBuy".Translate()))
                {
                    ConfirmBuy(listing);
                }
            }

            // Skills tooltip
            if (listing.Summary != null && Mouse.IsOver(infoRect))
            {
                string tip = listing.Summary.SkillsSummary;
                if (!string.IsNullOrEmpty(listing.Summary.TraitsSummary))
                    tip += "\n" + listing.Summary.TraitsSummary;
                if (!string.IsNullOrEmpty(listing.Summary.HealthSummary))
                    tip += "\n" + listing.Summary.HealthSummary;
                TooltipHandler.TipRegion(infoRect, tip);
            }
        }

        // --- Sell flow ---

        private void DrawSellPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(12f);

            float y = inner.y;

            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 30f), "Phinix_legacyTalentTrade_marketSell".Translate());
            Text.Font = GameFont.Small;
            y += 36f;

            // Pawn selector
            Widgets.Label(new Rect(inner.x, y, 120f, 28f), "Phinix_legacyTalentTrade_selectTradeablePawn".Translate());
            Rect pawnBtnRect = new Rect(inner.x + 130f, y, 200f, 28f);
            string pawnLabel = selectedPawn != null ? TradeablePawnUtility.GetLabel(selectedPawn) : (string)"Phinix_legacyTalentTrade_select".Translate();
            if (Widgets.ButtonText(pawnBtnRect, pawnLabel))
            {
                ShowPawnPicker();
            }
            y += 36f;

            // Pawn summary preview
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

            // Price input
            Widgets.Label(new Rect(inner.x, y, 120f, 28f), "Phinix_legacyTalentTrade_marketPrice".Translate());
            Rect priceFieldRect = new Rect(inner.x + 130f, y, 120f, 28f);
            priceBuffer = Widgets.TextField(priceFieldRect, priceBuffer);
            int.TryParse(priceBuffer, out priceValue);
            if (priceValue < 0) priceValue = 0;
            Widgets.Label(new Rect(inner.x + 260f, y, 60f, 28f), "Phinix_legacyTalentTrade_silver".Translate());
            y += 36f;

            // Confirm button
            Rect confirmRect = new Rect(inner.x, y, 160f, 36f);
            bool canConfirm = selectedPawn != null && priceValue > 0;
            if (canConfirm && Widgets.ButtonText(confirmRect, "Phinix_legacyTalentTrade_confirm".Translate()))
            {
                DoListForSale();
            }
        }

        private void ShowPawnPicker()
        {
            List<Pawn> tradeablePawns = TradeablePawnUtility.GetTradeablePawns(Find.CurrentMap);

            if (tradeablePawns.Count == 0)
            {
                Messages.Message("Phinix_legacyTalentTrade_noTradeablePawnsAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
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

        private void DoListForSale()
        {
            if (selectedPawn == null || priceValue <= 0) return;

            // Prevent listing a pawn that is not on the map
            if (!selectedPawn.Spawned || selectedPawn.Dead)
            {
                Messages.Message("Phinix_legacyTalentTrade_noTradeablePawnsAvailable".Translate(), MessageTypeDefOf.RejectInput, false);
                selectedPawn = null;
                return;
            }

            // Block AriandelLibrary OC pawns
            if (IsAriandelOCPawn(selectedPawn))
            {
                Messages.Message("Phinix_legacyTalentTrade_ocPawnBlocked".Translate(), MessageTypeDefOf.RejectInput, false);
                selectedPawn = null;
                return;
            }

            // Local self-check: if this race def doesn't even exist here, don't allow listing.
            if (selectedPawn.def == null || DefDatabase<ThingDef>.GetNamedSilentFail(selectedPawn.def.defName) == null)
            {
                string pawnName = TradeablePawnUtility.GetLabel(selectedPawn);
                string raceName = selectedPawn.def != null ? selectedPawn.def.defName : "Unknown";
                string message = "Phinix_legacyTalentTrade_raceIncompatible".Translate(pawnName, raceName);
                Find.WindowStack.Add(new Dialog_MessageBox(message));
                return;
            }

            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            string listingId = Guid.NewGuid().ToString("N").Substring(0, 12);
            string localName = TalentTradeManager.GetLocalDisplayName();
            PawnSummary summary = PawnSummary.FromPawn(selectedPawn);

            // Serialize pawn data and hold it
            string b64Pawn = PawnSerializer.Serialize(selectedPawn);
            if (string.IsNullOrEmpty(b64Pawn))
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】Failed to serialize pawn for market listing.");
                return;
            }

            PawnSerializer.DespawnAndHold(selectedPawn);

            // Register locally
            TalentTradeManager.AddLocalMarketListing(listingId, localUuid, localName, summary, priceValue, selectedPawn, b64Pawn);

            // Broadcast listing
            string msg = TalentTradeProtocol.BuildMarketList(listingId, localUuid, summary.ToBase64(), priceValue, localName);
            TalentTradeManager.SendProtocol(msg);

            // Reset sell state
            ResetSellState();
            sellMode = false;
        }

        // --- Buy flow ---

        private void ConfirmBuy(MarketListing listing)
        {
            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】ConfirmBuy called for listing {listing.Id}");

            // Check race compatibility on buyer side
            if (listing.Summary != null && !string.IsNullOrEmpty(listing.Summary.RaceDefName))
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(listing.Summary.RaceDefName) == null)
                {
                    // Buyer doesn't have the race mod
                    string pawnName = listing.Summary.GetDisplayLabel();
                    string message = "Phinix_legacyTalentTrade_cannotBuyNoRace".Translate(pawnName, listing.Summary.RaceDefName);
                    Find.WindowStack.Add(new Dialog_MessageBox(message));
                    return;
                }
            }

            string pawnName2 = listing.Summary != null ? listing.Summary.GetDisplayLabel() : "???";
            string confirmText = "Phinix_legacyTalentTrade_marketBuyConfirm".Translate(pawnName2, listing.PriceSilver.ToString());

            Dialog_MessageBox dialog = Dialog_MessageBox.CreateConfirmation(confirmText, delegate
            {
                LegacyTalentTradeRuntime.LogMessage("【三角洲贸易】Buy confirmation accepted");
                DoBuy(listing);
            }, destructive: false);
            Find.WindowStack.Add(dialog);
        }

        private void DoBuy(MarketListing listing)
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】DoBuy: localUuid={localUuid}, listingId={listing.Id}");
            if (string.IsNullOrEmpty(localUuid))
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】DoBuy: localUuid is null or empty, aborting");
                return;
            }

            string localName = TalentTradeManager.GetLocalDisplayName();
            string msg = TalentTradeProtocol.BuildMarketBuy(listing.Id, localUuid, localName);
            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Sending buy request: {msg}");
            TalentTradeManager.SendProtocol(msg);
            TalentTradeManager.TrackPurchase(listing.Id);
        }

        // --- Delist ---

        private void DelistListing(MarketListing listing)
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            // Prevent cross-save delist — only allow if this save owns the listing
            if (TalentTradeGameComponent.Current != null && !TalentTradeGameComponent.Current.OwnsListing(listing.Id))
            {
                Messages.Message("Phinix_legacyTalentTrade_cannotDelistWrongSave".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            // Restore held pawn
            TalentTradeManager.RestoreDelistedPawn(listing.Id);

            string msg = TalentTradeProtocol.BuildMarketDelist(listing.Id, localUuid);
            TalentTradeManager.SendProtocol(msg);
        }

        private void ResetSellState()
        {
            selectedPawn = null;
            priceBuffer = "100";
            priceValue = 100;
        }

        // --- AriandelLibrary OC pawn detection ---

        private static bool? ocCheckAvailable;
        private static object ocKindToIdMap;
        private static MethodInfo ocContainsKeyMethod;

        /// <summary>
        /// Returns true if the pawn's kindDef is registered in AriandelLibrary's SpecialPawnRegistry
        /// (i.e. it's an OC character with a unique ID that will be deleted if traded).
        /// </summary>
        private static bool IsAriandelOCPawn(Pawn pawn)
        {
            if (pawn == null || pawn.kindDef == null) return false;

            if (!ocCheckAvailable.HasValue)
            {
                try
                {
                    Type registryType = GenTypes.GetTypeInAnyAssembly("AriandelLibrary.SpecialPawnRegistry");
                    if (registryType != null)
                    {
                        FieldInfo mapField = registryType.GetField("KindToIdMap", BindingFlags.Public | BindingFlags.Static);
                        if (mapField != null)
                        {
                            ocKindToIdMap = mapField.GetValue(null);
                            ocContainsKeyMethod = ocKindToIdMap.GetType().GetMethod("ContainsKey");
                            ocCheckAvailable = ocContainsKeyMethod != null;
                        }
                        else
                        {
                            ocCheckAvailable = false;
                        }
                    }
                    else
                    {
                        ocCheckAvailable = false;
                    }
                }
                catch (Exception)
                {
                    // 特性探测：反射失败视为不支持，走降级路径
                    ocCheckAvailable = false;
                }
            }

            if (ocCheckAvailable == true && ocKindToIdMap != null && ocContainsKeyMethod != null)
            {
                try
                {
                    return (bool)ocContainsKeyMethod.Invoke(ocKindToIdMap, new object[] { pawn.kindDef });
                }
                catch (Exception)
                {
                    // 特性探测：反射调用失败视为不支持
                    return false;
                }
            }

            return false;
        }

        private void ForceCleanupAllMyListings()
        {
            string localUuid = TalentTradeManager.GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            MarketListing[] all = TalentTradeManager.GetMarketListingsSnapshot();
            List<string> toRemove = new List<string>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].SellerUuid == localUuid && all[i].State == MarketListingState.Active)
                {
                    toRemove.Add(all[i].Id);
                }
            }

            if (toRemove.Count == 0)
            {
                Messages.Message("Phinix_legacyTalentTrade_noListingsToClean".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            foreach (string id in toRemove)
            {
                // Broadcast delist
                string msg = TalentTradeProtocol.BuildMarketDelist(id, localUuid);
                TalentTradeManager.SendProtocol(msg);

                // Remove locally (pawn data is gone — sent to the warp)
                TalentTradeManager.RemoveListingLocally(id);

                if (TalentTradeGameComponent.Current != null)
                {
                    TalentTradeGameComponent.Current.UntrackListing(id);
                }
            }

            Messages.Message("Phinix_legacyTalentTrade_cleanupDone".Translate(toRemove.Count.ToString()), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
