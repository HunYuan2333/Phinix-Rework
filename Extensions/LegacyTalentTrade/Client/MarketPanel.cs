using System;
using PhinixClient;
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

        private readonly TalentToolbar toolbar;
        private readonly TalentForm form = new TalentForm("Phinix_legacyTalentTrade_selectTradeablePawn", "Phinix_legacyTalentTrade_marketPrice");
        private readonly List<MarketListing> visibleListings = new List<MarketListing>();
        private readonly List<TalentCard> cards = new List<TalentCard>();
        private float[] offsets = Array.Empty<float>();
        private int layoutVersion = -1;
        private bool layoutMine;
        private string layoutUuid;
        private object layoutLanguage;
        private float layoutWidth = -1f;

        public MarketPanel()
        {
            toolbar = new TalentToolbar(ActivateToolbar,
                "Phinix_legacyTalentTrade_marketSell", "Phinix_legacyTalentTrade_marketMyListings",
                "Phinix_legacyTalentTrade_forceCleanup", "Phinix_legacyTalentTrade_refresh");
        }

        private Vector2 listScrollPos;
        private bool showMyListings;

        // Sell flow state
        private bool sellMode;
        private Pawn selectedPawn;
        private string priceBuffer = "100";
        private int priceValue = 100;

        public void Draw(Rect rect)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            float height = toolbar.Draw(rect, sellMode ? 1 : 4, sellMode ? 1 : 0,
                sellMode ? "Phinix_legacyTalentTrade_cancel" : "Phinix_legacyTalentTrade_marketSell");
            Rect content = TalentTradeUi.Below(rect, height + SPACING);
            if (content.height <= 0f) return;
            if (sellMode) DrawSellPanel(content);
            else DrawListings(content);
        }

        private void ActivateToolbar(int action)
        {
            switch (action)
            {
                case 0:
                    sellMode = !sellMode;
                    if (!sellMode) ResetSellState();
                    break;
                case 1: showMyListings = !showMyListings; break;
                case 2: ForceCleanupAllMyListings(); break;
                case 3:
                    string uuid = TalentTradeManager.GetLocalUuid();
                    if (!string.IsNullOrEmpty(uuid)) TalentTradeManager.SendProtocol(TalentTradeProtocol.BuildMarketSync(uuid));
                    break;
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
            float width = Mathf.Max(1f, rect.width - 16f);
            if (layoutVersion != cachedStateVersion || layoutMine != showMyListings ||
                layoutUuid != localUuid || layoutWidth != width || !ReferenceEquals(layoutLanguage, LanguageDatabase.activeLanguage))
            {
                layoutVersion = cachedStateVersion;
                layoutMine = showMyListings;
                layoutUuid = localUuid;
                layoutWidth = width;
                layoutLanguage = LanguageDatabase.activeLanguage;
                visibleListings.Clear();
                cards.Clear();
                for (int i = 0; i < listings.Length; i++)
                {
                    MarketListing listing = listings[i];
                    if (listing == null || listing.State != MarketListingState.Active ||
                        (showMyListings && listing.SellerUuid != localUuid)) continue;
                    visibleListings.Add(listing);
                    string text = TalentCard.Description(listing.Summary) + "\n" +
                        "Phinix_legacyTalentTrade_marketSeller".Translate(listing.SellerName ?? "???") + "\n" +
                        "Phinix_legacyTalentTrade_marketPriceFormat".Translate(listing.PriceSilver.ToString());
                    cards.Add(new TalentCard(text, TalentCard.Details(listing.Summary),
                        listing.SellerUuid == localUuid ? "Phinix_legacyTalentTrade_marketDelist" : "Phinix_legacyTalentTrade_marketBuy", width));
                }
                offsets = new float[cards.Count + 1];
                for (int i = 0; i < cards.Count; i++) offsets[i + 1] = offsets[i] + cards[i].Height + SPACING;
            }
            if (cards.Count == 0)
            {
                Widgets.NoneLabelCenteredVertically(rect, "Phinix_legacyTalentTrade_marketNoListings".Translate());
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
                        if (visibleListings[i].SellerUuid == localUuid) DelistListing(visibleListings[i]);
                        else ConfirmBuy(visibleListings[i]);
                    }
            }
            finally { Widgets.EndScrollView(); }
        }



        // --- Sell flow ---

        private void DrawSellPanel(Rect rect)
        {
            form.Begin(rect, selectedPawn);
            try
            {
                if (form.PawnButton()) ShowPawnPicker();
                form.Number(1, ref priceBuffer, ref priceValue, 0);
                if (form.Confirm(selectedPawn != null && priceValue > 0)) DoListForSale();
            }
            finally { form.End(); }
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
            string defManifestData = DefManifestHelper.SerializeCompressed(selectedPawn);

            // Serialize pawn data and hold it
            string b64Pawn = PawnSerializer.Serialize(selectedPawn);
            if (string.IsNullOrEmpty(b64Pawn))
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】Failed to serialize pawn for market listing.");
                return;
            }

            PawnSerializer.DespawnAndHold(selectedPawn);

            // Register locally
            TalentTradeManager.AddLocalMarketListing(listingId, localUuid, localName, summary, priceValue, selectedPawn, b64Pawn, defManifestData);

            // Broadcast listing
            string msg = TalentTradeProtocol.BuildMarketList(listingId, localUuid, summary.ToBase64(), priceValue, localName, defManifestData);
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
            TransferCompatibilityUi.Confirm(
                confirmText,
                pawnName2,
                listing.DefManifestData,
                delegate
                {
                    LegacyTalentTradeRuntime.LogMessage("【三角洲贸易】Buy confirmation accepted");
                    DoBuy(listing);
                });
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
