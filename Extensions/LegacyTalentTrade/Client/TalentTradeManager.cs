using System;
using System.Collections.Generic;
using PhinixClient;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public static class TalentTradeManager
    {
        private static readonly Queue<Action> MainThreadQueue = new Queue<Action>();
        private static readonly object ProcessedLock = new object();
        private static readonly HashSet<string> ProcessedProtocolKeys = new HashSet<string>();

        private static bool initialized;
        private static int lastUpdateFrame = -1;
        // 状态版本：任何市场/租借/直交状态变更时递增，供 UI 按版本缓存快照（§8.3）
        private static int stateVersion;
        private static IClientUserEventStream subscribedUserEvents;
        private static EventHandler disconnectedHandler;
        // 设计哲学 §3.6：主线程队列有界，超出丢最旧并记 Warning（与框架 pendingActions 上限一致）
        private const int MaxMainThreadQueue = 500;

        public static int StateVersion => stateVersion;

        private static void MarkStateChanged()
        {
            stateVersion++;
        }

        // --- Market state ---
        private static readonly object MarketLock = new object();
        private static readonly Dictionary<string, MarketListing> MarketListings = new Dictionary<string, MarketListing>();
        // Local-only: serialized pawn data for listings we own (seller side)
        private static readonly Dictionary<string, string> LocalPawnData = new Dictionary<string, string>();

        // --- Rental state ---
        private static readonly object RentalLock = new object();
        private static readonly Dictionary<string, RentalContract> RentalContracts = new Dictionary<string, RentalContract>();

        // --- Direct trade state ---
        private static readonly object TradeLock = new object();
        private static readonly Dictionary<string, DirectTrade> ActiveTrades = new Dictionary<string, DirectTrade>();

        // --- Blob assembly ---
        private static readonly object BlobLock = new object();
        private static readonly Dictionary<string, string[]> PendingBlobs = new Dictionary<string, string[]>();

        // --- Def manifest exchange ---
        private static readonly object DefExchangeLock = new object();
        private static readonly Dictionary<string, TransferReport> PendingDefReports = new Dictionary<string, TransferReport>();
        private static readonly Dictionary<string, Action<TransferReport>> DefReportCallbacks = new Dictionary<string, Action<TransferReport>>();

        // --- Offline listing cache ---
        private static readonly Dictionary<string, MarketListing> OfflineListingCache = new Dictionary<string, MarketListing>();
        private static readonly Dictionary<string, string> OfflinePawnDataCache = new Dictionary<string, string>();

        // --- Purchase timeout tracking ---
        private static readonly Dictionary<string, int> PendingPurchases = new Dictionary<string, int>();
        private const int PURCHASE_TIMEOUT_TICKS = 1800; // 30 seconds at 60 FPS

        // --- Listing expiry & heartbeat ---
        private const double LISTING_EXPIRY_MINUTES = 5.0;
        private const double HEARTBEAT_INTERVAL_MINUTES = 3.5;
        private static DateTime lastHeartbeatUtc = DateTime.MinValue;
        private static DateTime lastExpiryCheckUtc = DateTime.MinValue;

        // --- Save session token ---
        private static string activeSaveToken = null;

        public static void Initialize(IClientUserEventStream userEvents)
        {
            if (initialized) return;

            initialized = true;
            if (userEvents != null && disconnectedHandler == null)
            {
                disconnectedHandler = (s, e) =>
                {
                    CacheMyListingsOnLogout();
                    Clear();
                };
                userEvents.Disconnected += disconnectedHandler;
                subscribedUserEvents = userEvents;
            }

            // Restore cached listings on login
            RestoreMyListingsOnLogin();
        }

        /// <summary>
        /// 插件 Shutdown 时清理：退订由 Initialize 内联处理；此处清空中继与状态。
        /// </summary>
        public static void Shutdown()
        {
            initialized = false;
            // §8.1：事件订阅配对取消
            if (subscribedUserEvents != null && disconnectedHandler != null)
            {
                subscribedUserEvents.Disconnected -= disconnectedHandler;
            }
            subscribedUserEvents = null;
            disconnectedHandler = null;
            TalentTradeTransport.Clear();
            Clear();
        }

        public static void Update()
        {
            int frame = Time.frameCount;
            if (frame == lastUpdateFrame) return;
            lastUpdateFrame = frame;

            if (!initialized) return;

            try
            {
                TalentTradeTransport.Update();
                PollRelayBuffer();

                // Only run game-dependent operations when a map is loaded
                if (Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null)
                {
                    RunMainThreadQueue();
                    CheckPurchaseTimeouts();
                    ExpireOldListings();
                    HeartbeatMyListings();
                }
            }
            catch (Exception ex)
            {
                // 设计哲学 §3.5：单帧异常不中断后续帧，也不得外泄到框架主线程队列
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】TalentTradeManager.Update error: " + ex);
            }
        }

        public static void EnqueueMainThread(Action action)
        {
            if (action == null) return;
            lock (MainThreadQueue)
            {
                if (MainThreadQueue.Count >= MaxMainThreadQueue)
                {
                    MainThreadQueue.Dequeue();
                    LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】MainThreadQueue overflow, dropped oldest action.");
                }
                MainThreadQueue.Enqueue(action);
            }
        }

        public static string GetLocalUuid()
        {
            if (!LegacyTalentTradeRuntime.IsActive) return null;
            try
            {
                string uuid = LegacyTalentTradeRuntime.LocalUuid;
                return string.IsNullOrEmpty(uuid) ? null : uuid;
            }
            catch (Exception)
            {
                // 防御性：运行时服务访问异常时降级返回 null
                return null;
            }
        }

        public static string GetLocalDisplayName()
        {
            string uuid = GetLocalUuid();
            if (string.IsNullOrEmpty(uuid) || !LegacyTalentTradeRuntime.IsActive) return "Unknown";
            try
            {
                string name;
                if (LegacyTalentTradeRuntime.TryGetDisplayName(uuid, out name))
                {
                    return name ?? "Unknown";
                }
                return "Unknown";
            }
            catch (Exception)
            {
                // 防御性：显示名查询异常时降级返回 Unknown
                return "Unknown";
            }
        }

        public static void SendProtocol(string protocolMessage)
        {
            string uuid = GetLocalUuid();
            if (string.IsNullOrEmpty(uuid)) return;
            TalentTradeTransport.EnqueueProtocol(protocolMessage, uuid);
        }

        // --- Market accessors ---

        public static MarketListing[] GetMarketListingsSnapshot()
        {
            lock (MarketLock)
            {
                MarketListing[] result = new MarketListing[MarketListings.Count];
                int i = 0;
                foreach (var kvp in MarketListings)
                {
                    result[i++] = kvp.Value;
                }
                return result;
            }
        }

        // --- Market API ---

        /// <summary>
        /// Register a local market listing (seller side). Stores the held pawn and serialized data.
        /// </summary>
        public static void AddLocalMarketListing(string listingId, string sellerUuid, string sellerName, PawnSummary summary, int priceSilver, Pawn heldPawn, string b64PawnData)
        {
            MarkStateChanged();
            MarketListing listing = new MarketListing
            {
                Id = listingId,
                SellerUuid = sellerUuid,
                SellerName = sellerName,
                Summary = summary,
                PriceSilver = priceSilver,
                State = MarketListingState.Active,
                CreatedAtUtc = DateTime.UtcNow,
                LastRefreshUtc = DateTime.UtcNow,
                HeldPawn = heldPawn
            };

            lock (MarketLock)
            {
                MarketListings[listingId] = listing;
                LocalPawnData[listingId] = b64PawnData;
            }

            // Track this listing as belonging to the current save
            if (TalentTradeGameComponent.Current != null)
            {
                TalentTradeGameComponent.Current.TrackListing(listingId);
            }
        }

        /// <summary>
        /// Restore a delisted pawn back to the map (seller cancels listing).
        /// </summary>
        public static void RestoreDelistedPawn(string listingId)
        {
            MarkStateChanged();
            string b64PawnData;
            lock (MarketLock)
            {
                if (!LocalPawnData.TryGetValue(listingId, out b64PawnData))
                {
                    LegacyTalentTradeRuntime.LogWarning($"【三角洲贸易】RestoreDelistedPawn: No pawn data for listing {listingId}");
                    return;
                }
                MarketListings.Remove(listingId);
                LocalPawnData.Remove(listingId);
            }

            // Untrack from current save
            if (TalentTradeGameComponent.Current != null)
            {
                TalentTradeGameComponent.Current.UntrackListing(listingId);
            }

            EnqueueMainThread(() =>
            {
                Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64PawnData);
                if (pawn != null)
                {
                    Messages.Message("Phinix_legacyTalentTrade_pawnRestored".Translate(pawn.LabelShortCap), new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
                }
            });
        }

        /// <summary>
        /// Get the serialized pawn data for a local listing (seller side).
        /// </summary>
        public static string GetLocalPawnData(string listingId)
        {
            lock (MarketLock)
            {
                string data;
                if (LocalPawnData.TryGetValue(listingId, out data))
                    return data;
                return null;
            }
        }

        /// <summary>
        /// Remove a listing and its pawn data from local state (used by GameComponent on save exit).
        /// Does NOT broadcast delist — caller handles that.
        /// </summary>
        public static void RemoveListingLocally(string listingId)
        {
            MarkStateChanged();
            lock (MarketLock)
            {
                MarketListings.Remove(listingId);
                LocalPawnData.Remove(listingId);
            }
        }

        // --- Rental accessors ---

        public static RentalContract[] GetRentalContractsSnapshot()
        {
            lock (RentalLock)
            {
                RentalContract[] result = new RentalContract[RentalContracts.Count];
                int i = 0;
                foreach (var kvp in RentalContracts)
                {
                    result[i++] = kvp.Value;
                }
                return result;
            }
        }

        /// <summary>
        /// Register a local rental listing (owner side). Stores the held pawn and serialized data.
        /// </summary>
        public static void AddLocalRentalListing(string rentalId, string ownerUuid, string ownerName, PawnSummary summary,
            int pricePerDay, int maxDays, int deposit, Pawn heldPawn, string b64PawnData)
        {
            MarkStateChanged();
            RentalContract contract = new RentalContract
            {
                Id = rentalId,
                OwnerUuid = ownerUuid,
                OwnerName = ownerName,
                Summary = summary,
                PricePerDay = pricePerDay,
                MaxDays = maxDays,
                Deposit = deposit,
                State = RentalContractState.Listed,
                OriginalPawnData = b64PawnData
            };

            lock (RentalLock)
            {
                RentalContracts[rentalId] = contract;
            }
        }

        /// <summary>
        /// Restore a delisted rental pawn back to the map (owner cancels listing).
        /// </summary>
        public static void RestoreDelistedRentalPawn(string rentalId)
        {
            MarkStateChanged();
            string b64PawnData;
            lock (RentalLock)
            {
                RentalContract contract;
                if (!RentalContracts.TryGetValue(rentalId, out contract))
                {
                    LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】RestoreDelistedRentalPawn: No contract for " + rentalId);
                    return;
                }
                b64PawnData = contract.OriginalPawnData;
                RentalContracts.Remove(rentalId);
            }

            if (string.IsNullOrEmpty(b64PawnData))
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】RestoreDelistedRentalPawn: No pawn data for " + rentalId);
                return;
            }

            EnqueueMainThread(() =>
            {
                Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64PawnData);
                if (pawn != null)
                {
                    Messages.Message("Phinix_legacyTalentTrade_pawnRestored".Translate(pawn.LabelShortCap), new LookTargets(pawn), MessageTypeDefOf.NeutralEvent, false);
                }
            });
        }

        /// <summary>
        /// Return a rented pawn (renter side). Serializes and sends back to owner.
        /// </summary>
        public static void ReturnRentedPawn(string rentalId)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            RentalContract contract;
            lock (RentalLock)
            {
                if (!RentalContracts.TryGetValue(rentalId, out contract)) return;
                if (contract.RenterUuid != localUuid) return;
                if (contract.State != RentalContractState.Active) return;
            }

            // Send return message (owner will use their cached data, not ours)
            string msg = TalentTradeProtocol.BuildRentalReturn(rentalId, localUuid, "");
            SendProtocol(msg);

            lock (RentalLock)
            {
                RentalContracts.Remove(rentalId);
            }
        }

        // --- Direct trade accessors ---

        public static DirectTrade[] GetActiveTradesSnapshot()
        {
            lock (TradeLock)
            {
                DirectTrade[] result = new DirectTrade[ActiveTrades.Count];
                int i = 0;
                foreach (var kvp in ActiveTrades)
                {
                    result[i++] = kvp.Value;
                }
                return result;
            }
        }

        // --- Def exchange API ---

        /// <summary>
        /// Initiate a def compatibility check with a target player before sending a pawn.
        /// Collects the pawn's referenced defs, sends them to the target, and invokes the callback
        /// when the target responds with their missing defs report.
        /// </summary>
        public static string RequestDefCheck(Pawn pawn, string targetUuid, Action<TransferReport> onResult)
        {
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid) || string.IsNullOrEmpty(targetUuid)) return null;

            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            DefManifest manifest = DefManifestHelper.CollectFromPawn(pawn);
            string defListStr = DefManifestHelper.SerializeManifest(manifest);
            string b64DefList = TalentTradeTransport.Compress(defListStr);

            if (onResult != null)
            {
                lock (DefExchangeLock)
                {
                    DefReportCallbacks[sessionId] = onResult;
                }
            }

            string msg = TalentTradeProtocol.BuildDefManifest(sessionId, localUuid, targetUuid, b64DefList);
            SendProtocol(msg);
            return sessionId;
        }

        public static string RequestRaceCheck(Pawn pawn, string targetUuid, Action<TransferReport> onResult)
        {
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid) || string.IsNullOrEmpty(targetUuid) || pawn == null || pawn.def == null) return null;

            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);
            DefManifest manifest = new DefManifest();
            manifest.RaceDefs.Add(pawn.def.defName);
            string defListStr = DefManifestHelper.SerializeManifest(manifest);
            string b64DefList = TalentTradeTransport.Compress(defListStr);

            if (onResult != null)
            {
                lock (DefExchangeLock)
                {
                    DefReportCallbacks[sessionId] = onResult;
                }
            }

            string msg = TalentTradeProtocol.BuildDefManifest(sessionId, localUuid, targetUuid, b64DefList);
            SendProtocol(msg);
            return sessionId;
        }

        /// <summary>
        /// Get a previously received def report by session ID. Returns null if not yet received.
        /// </summary>
        public static TransferReport GetDefReport(string sessionId)
        {
            lock (DefExchangeLock)
            {
                TransferReport report;
                if (PendingDefReports.TryGetValue(sessionId, out report))
                    return report;
                return null;
            }
        }

        // --- Internal ---

        private static void Clear()
        {
            MarkStateChanged();
            lock (MarketLock) { MarketListings.Clear(); LocalPawnData.Clear(); }
            lock (RentalLock) { RentalContracts.Clear(); }
            lock (TradeLock) { ActiveTrades.Clear(); }
            lock (BlobLock) { PendingBlobs.Clear(); }
            lock (ProcessedLock) { ProcessedProtocolKeys.Clear(); }
            lock (DefExchangeLock) { PendingDefReports.Clear(); DefReportCallbacks.Clear(); }
            lock (MainThreadQueue) { MainThreadQueue.Clear(); }
            TalentTradeTransport.Clear();
        }

        private static void PollRelayBuffer()
        {
            // 原版每帧(60fps)处理 20 条 ≈ 1200条/秒；插件改为 Timer 200ms 驱动后，
            // 若保持 20 条/次则吞吐降至 100条/秒，大流量下入站队列会积压。
            // 提升到 200 条/次 ≈ 1000条/秒，且 ≥ 中继每次拉取上限(200条/700ms ≈ 285条/秒)，
            // 保证消费速度不落后于生产速度，行为与原版一致。
            int maxPerFrame = 200;
            int count = 0;
            string message;
            while (count < maxPerFrame && TalentTradeTransport.TryDequeueIncoming(out message))
            {
                count++;
                ProcessProtocolMessage(message);
            }
        }

        private static void ProcessProtocolMessage(string message)
        {
            TalentTradeMessageType msgType;
            string[] parts;
            if (!TalentTradeProtocol.TryParse(message, out msgType, out parts)) return;

            // Dedup（在 handler 之前登记：即使本条处理失败也不重试，与原行为一致）
            string dedupKey = BuildDedupKey(msgType, parts);
            if (!string.IsNullOrEmpty(dedupKey))
            {
                lock (ProcessedLock)
                {
                    if (!ProcessedProtocolKeys.Add(dedupKey)) return;
                }
            }

            try
            {
                switch (msgType)
                {
                    case TalentTradeMessageType.MarketList:
                        HandleMarketList(parts);
                        break;
                    case TalentTradeMessageType.MarketDelist:
                        HandleMarketDelist(parts);
                        break;
                    case TalentTradeMessageType.MarketBuy:
                        HandleMarketBuy(parts);
                        break;
                    case TalentTradeMessageType.MarketSell:
                        HandleMarketSell(parts);
                        break;
                    case TalentTradeMessageType.MarketPaid:
                        HandleMarketPaid(parts);
                        break;
                    case TalentTradeMessageType.MarketSync:
                        HandleMarketSync(parts);
                        break;
                    case TalentTradeMessageType.TradeRequest:
                        HandleTradeRequest(parts);
                        break;
                    case TalentTradeMessageType.TradeAccept:
                        HandleTradeAccept(parts);
                        break;
                    case TalentTradeMessageType.TradeReject:
                        HandleTradeReject(parts);
                        break;
                    case TalentTradeMessageType.TradeOffer:
                        HandleTradeOffer(parts);
                        break;
                    case TalentTradeMessageType.TradeLock:
                        HandleTradeLock(parts);
                        break;
                    case TalentTradeMessageType.TradeExecute:
                        HandleTradeExecute(parts);
                        break;
                    case TalentTradeMessageType.TradeCancel:
                        HandleTradeCancel(parts);
                        break;
                    case TalentTradeMessageType.RentalList:
                        HandleRentalList(parts);
                        break;
                    case TalentTradeMessageType.RentalDelist:
                        HandleRentalDelist(parts);
                        break;
                    case TalentTradeMessageType.RentalRent:
                        HandleRentalRent(parts);
                        break;
                    case TalentTradeMessageType.RentalConfirm:
                        HandleRentalConfirm(parts);
                        break;
                    case TalentTradeMessageType.RentalReturn:
                        HandleRentalReturn(parts);
                        break;
                    case TalentTradeMessageType.RentalExpiry:
                        HandleRentalExpiry(parts);
                        break;
                    case TalentTradeMessageType.RentalDead:
                        HandleRentalDead(parts);
                        break;
                    case TalentTradeMessageType.RentalRevive:
                        HandleRentalRevive(parts);
                        break;
                    case TalentTradeMessageType.DefManifest:
                        HandleDefManifest(parts);
                        break;
                    case TalentTradeMessageType.DefAck:
                        HandleDefAck(parts);
                        break;
                    case TalentTradeMessageType.BlobPart:
                        HandleBlobPart(parts);
                        break;
                }
            }
            catch (Exception ex)
            {
                // 设计哲学 §3.5：单条消息处理失败跳过，不中断同一批其余消息
                LegacyTalentTradeRuntime.LogWarning($"【三角洲贸易】Failed to process {msgType} message: {ex}");
                return;
            }

            // 入站消息都会变更状态（新增/删除/更新），统一递增状态版本（§8.3）
            MarkStateChanged();
        }

        private static string BuildDedupKey(TalentTradeMessageType msgType, string[] parts)
        {
            // Don't dedup mlist (heartbeat needs to update LastRefreshUtc)
            if (msgType == TalentTradeMessageType.MarketList)
                return null;

            // Use type + first ID field for dedup
            if (parts.Length > 3)
            {
                return msgType.ToString() + "|" + parts[3];
            }
            return null;
        }

        private static void RunMainThreadQueue()
        {
            Action[] actions;
            lock (MainThreadQueue)
            {
                if (MainThreadQueue.Count == 0) return;
                actions = MainThreadQueue.ToArray();
                MainThreadQueue.Clear();
            }

            for (int i = 0; i < actions.Length; i++)
            {
                try
                {
                    actions[i]();
                }
                catch (Exception ex)
                {
                    LegacyTalentTradeRuntime.LogError("【三角洲贸易】MainThreadQueue action error: " + ex);
                }
            }
        }

        // --- Stub handlers (to be implemented in later phases) ---

        private static void HandleMarketList(string[] parts)
        {
            // PHXTT|v1|mlist|listingId|sellerUuid|b64PawnSummary|priceSilver|b64SellerName
            if (parts.Length < 8) return;
            string listingId = parts[3];
            string sellerUuid = parts[4];

            if (LegacyTalentTradeRuntime.Settings?.EnableDebugLog == true)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】HandleMarketList: listingId={listingId}, sellerUuid={sellerUuid}, localUuid={GetLocalUuid()}");
            }

            // Skip own listings — we are the authority for our own data.
            if (sellerUuid == GetLocalUuid())
            {
                if (LegacyTalentTradeRuntime.Settings?.EnableDebugLog == true)
                {
                    LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Skipping own listing {listingId}");
                }
                return;
            }

            string b64Summary = parts[5];
            int priceSilver;
            if (!int.TryParse(parts[6], out priceSilver)) return;
            string sellerName = TalentTradeProtocol.DecodeField(parts[7]);

            PawnSummary summary = PawnSummary.FromBase64(b64Summary);

            lock (MarketLock)
            {
                MarketListing existing;
                if (MarketListings.TryGetValue(listingId, out existing))
                {
                    // Update LastRefreshUtc for heartbeat
                    existing.LastRefreshUtc = DateTime.UtcNow;
                    if (LegacyTalentTradeRuntime.Settings?.EnableDebugLog == true)
                    {
                        LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Updated heartbeat for listing {listingId}");
                    }
                }
                else
                {
                    // New listing
                    MarketListing listing = new MarketListing
                    {
                        Id = listingId,
                        SellerUuid = sellerUuid,
                        SellerName = sellerName,
                        Summary = summary,
                        PriceSilver = priceSilver,
                        State = MarketListingState.Active,
                        CreatedAtUtc = DateTime.UtcNow,
                        LastRefreshUtc = DateTime.UtcNow
                    };
                    MarketListings[listingId] = listing;
                    if (LegacyTalentTradeRuntime.Settings?.EnableDebugLog == true)
                    {
                        LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Added new listing {listingId} from {sellerName}, price={priceSilver}");
                    }
                }
            }
        }

        private static void HandleMarketDelist(string[] parts)
        {
            if (parts.Length < 5) return;
            string listingId = parts[3];
            lock (MarketLock)
            {
                MarketListings.Remove(listingId);
            }
        }

        private static void HandleMarketBuy(string[] parts)
        {
            // PHXTT|v1|mbuy|listingId|buyerUuid|b64BuyerName
            // Received by the SELLER — someone wants to buy our listing
            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】HandleMarketBuy called, parts.Length={parts.Length}");
            if (parts.Length < 6) return;
            string listingId = parts[3];
            string buyerUuid = parts[4];
            string buyerName = TalentTradeProtocol.DecodeField(parts[5]);
            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】HandleMarketBuy: listingId={listingId}, buyerUuid={buyerUuid}");

            string localUuid = GetLocalUuid();
            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】HandleMarketBuy: localUuid={localUuid}");
            if (string.IsNullOrEmpty(localUuid)) return;

            // Check if we own this listing
            MarketListing listing;
            lock (MarketLock)
            {
                if (!MarketListings.TryGetValue(listingId, out listing))
                {
                    LegacyTalentTradeRuntime.LogWarning($"【三角洲贸易】HandleMarketBuy: Listing {listingId} not found");
                    return;
                }
                if (listing.SellerUuid != localUuid)
                {
                    LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】HandleMarketBuy: Not our listing (seller={listing.SellerUuid})");
                    return;
                }
                if (listing.State != MarketListingState.Active)
                {
                    LegacyTalentTradeRuntime.LogWarning($"【三角洲贸易】HandleMarketBuy: Listing state is {listing.State}, not Active");
                    return;
                }
            }

            // Notify seller via letter
            string pawnName = listing.Summary != null ? listing.Summary.GetDisplayLabel() : "???";
            EnqueueMainThread(() =>
            {
                Find.LetterStack.ReceiveLetter(
                    "Phinix_legacyTalentTrade_buyLetterTitle".Translate(),
                    "Phinix_legacyTalentTrade_buyLetterText".Translate(buyerName, pawnName, listing.PriceSilver.ToString()),
                    LetterDefOf.PositiveEvent);
            });

            LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Completing sale directly");
            CompleteSale(listingId, buyerUuid);
        }

        private static void CompleteSale(string listingId, string buyerUuid)
        {
            string localUuid = GetLocalUuid();
            string b64PawnData;
            MarketListing listing;

            lock (MarketLock)
            {
                if (!MarketListings.TryGetValue(listingId, out listing)) return;
                if (listing.SellerUuid != localUuid) return;
                if (listing.State != MarketListingState.Active) return;

                listing.State = MarketListingState.Sold;

                if (!LocalPawnData.TryGetValue(listingId, out b64PawnData))
                {
                    b64PawnData = null;
                }
                LocalPawnData.Remove(listingId);
            }

            if (string.IsNullOrEmpty(b64PawnData))
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】CompleteSale: No pawn data for listing " + listingId);
                return;
            }

            string msg = TalentTradeProtocol.BuildMarketSell(listingId, localUuid, buyerUuid, b64PawnData);
            SendProtocol(msg);

            // Untrack from current save
            if (TalentTradeGameComponent.Current != null)
            {
                TalentTradeGameComponent.Current.UntrackListing(listingId);
            }

            lock (MarketLock)
            {
                MarketListings.Remove(listingId);
            }
        }

        private static void HandleMarketSell(string[] parts)
        {
            // PHXTT|v1|msell|listingId|sellerUuid|buyerUuid|b64CompressedPawnData
            // Received by the BUYER — seller is sending us the pawn
            if (parts.Length < 7) return;
            string listingId = parts[3];
            string buyerUuid = parts[5];
            string b64PawnData = parts[6];

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;
            if (buyerUuid != localUuid) return;

            // Remove from pending purchases
            PendingPurchases.Remove(listingId);

            // Remove listing from local view
            lock (MarketLock)
            {
                MarketListings.Remove(listingId);
            }

            // Deserialize and spawn pawn on main thread
            EnqueueMainThread(() =>
            {
                Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64PawnData);
                if (pawn != null)
                {
                    Messages.Message(
                        "Phinix_legacyTalentTrade_pawnReceivedMessage".Translate(pawn.LabelShortCap),
                        new LookTargets(pawn),
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
                else
                {
                    LegacyTalentTradeRuntime.LogError("【三角洲贸易】HandleMarketSell: Failed to deserialize pawn for listing " + listingId);
                }
            });
        }

        private static void HandleMarketPaid(string[] parts)
        {
            // PHXTT|v1|mpaid|listingId|buyerUuid|b64CompressedSilverItems
            // Future: handle silver transfer confirmation. For now, market is trust-based.
            if (parts.Length < 6) return;
        }

        private static void HandleMarketSync(string[] parts)
        {
            // PHXTT|v1|msync|requestorUuid
            // Re-broadcast all our active listings so the requestor can see them
            if (parts.Length < 4) return;

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;
            string localName = GetLocalDisplayName();

            MarketListing[] snapshot;
            lock (MarketLock)
            {
                snapshot = new MarketListing[MarketListings.Count];
                int idx = 0;
                foreach (var kvp in MarketListings)
                {
                    snapshot[idx++] = kvp.Value;
                }
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                MarketListing l = snapshot[i];
                if (l.SellerUuid != localUuid) continue;
                if (l.State != MarketListingState.Active) continue;
                if (l.Summary == null) continue;

                string msg = TalentTradeProtocol.BuildMarketList(l.Id, localUuid, l.Summary.ToBase64(), l.PriceSilver, localName);
                SendProtocol(msg);
            }
        }

        private static void HandleTradeRequest(string[] parts)
        {
            // PHXTT|v1|treq|tradeId|initiatorUuid|targetUuid|b64InitiatorName
            if (parts.Length < 7) return;
            string tradeId = parts[3];
            string initiatorUuid = parts[4];
            string targetUuid = parts[5];
            string initiatorName = TalentTradeProtocol.DecodeField(parts[6]);

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;
            if (targetUuid != localUuid) return;

            DirectTrade trade = new DirectTrade
            {
                Id = tradeId,
                InitiatorUuid = initiatorUuid,
                TargetUuid = targetUuid,
                InitiatorName = initiatorName,
                TargetName = GetLocalDisplayName(),
                State = DirectTradeState.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            };

            lock (TradeLock)
            {
                ActiveTrades[tradeId] = trade;
            }

            EnqueueMainThread(() =>
            {
                Messages.Message(
                    "Phinix_legacyTalentTrade_tradeRequestReceived".Translate(initiatorName),
                    MessageTypeDefOf.NeutralEvent,
                    false);
            });
        }

        private static void HandleTradeAccept(string[] parts)
        {
            // PHXTT|v1|tacc|tradeId|responderUuid
            if (parts.Length < 5) return;
            string tradeId = parts[3];
            string responderUuid = parts[4];

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                if (trade.TargetUuid != responderUuid) return;
                trade.State = DirectTradeState.Negotiating;
            }
        }

        private static void HandleTradeReject(string[] parts)
        {
            // PHXTT|v1|trej|tradeId|responderUuid
            if (parts.Length < 5) return;
            string tradeId = parts[3];

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                trade.State = DirectTradeState.Cancelled;
                ActiveTrades.Remove(tradeId);
            }

            EnqueueMainThread(() =>
            {
                Messages.Message("Phinix_legacyTalentTrade_tradeCancelled".Translate(), MessageTypeDefOf.NeutralEvent, false);
            });
        }

        private static void HandleTradeOffer(string[] parts)
        {
            // PHXTT|v1|toff|tradeId|senderUuid|b64OfferJson
            if (parts.Length < 6) return;
            string tradeId = parts[3];
            string senderUuid = parts[4];
            string b64Offer = parts[5];

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                if (trade.State != DirectTradeState.Negotiating && trade.State != DirectTradeState.Accepted)
                {
                    trade.State = DirectTradeState.Negotiating;
                }

                TradeOffer offer = TradeOfferSerializer.FromBase64(b64Offer);
                if (offer == null) return;

                if (senderUuid == trade.InitiatorUuid)
                {
                    trade.InitiatorOffer = offer;
                    trade.InitiatorConfirmed = false;
                }
                else if (senderUuid == trade.TargetUuid)
                {
                    trade.TargetOffer = offer;
                    trade.TargetConfirmed = false;
                }
            }
        }

        private static void HandleTradeLock(string[] parts)
        {
            // PHXTT|v1|tlock|tradeId|senderUuid
            if (parts.Length < 5) return;
            string tradeId = parts[3];
            string senderUuid = parts[4];

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;

                if (senderUuid == trade.InitiatorUuid)
                    trade.InitiatorConfirmed = true;
                else if (senderUuid == trade.TargetUuid)
                    trade.TargetConfirmed = true;

                // Both confirmed → execute
                if (trade.InitiatorConfirmed && trade.TargetConfirmed)
                {
                    trade.State = DirectTradeState.Executing;
                    ExecuteDirectTrade(trade);
                }
            }
        }

        private static void HandleTradeExecute(string[] parts)
        {
            // PHXTT|v1|texe|tradeId|senderUuid|b64CompressedPawnData|b64CompressedItems
            if (parts.Length < 6) return;
            string tradeId = parts[3];
            string senderUuid = parts[4];
            string b64PawnData = parts[5];
            string b64Items = parts.Length > 6 ? parts[6] : "";

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            DirectTrade trade;
            lock (TradeLock)
            {
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                // Only accept if we're the other party
                if (senderUuid == localUuid) return;
            }

            if (!trade.LocalExecuteSent)
            {
                ExecuteDirectTrade(trade);
            }

            // Spawn received pawns
            if (!string.IsNullOrEmpty(b64PawnData))
            {
                EnqueueMainThread(() =>
                {
                    // b64PawnData may contain multiple pawns separated by ';'
                    string[] pawnDatas = b64PawnData.Split(';');
                    for (int i = 0; i < pawnDatas.Length; i++)
                    {
                        if (string.IsNullOrEmpty(pawnDatas[i])) continue;
                        Pawn pawn = PawnDeserializer.DeserializeAndSpawn(pawnDatas[i]);
                        if (pawn != null)
                        {
                            Messages.Message(
                                "Phinix_legacyTalentTrade_pawnReceivedMessage".Translate(pawn.LabelShortCap),
                                new LookTargets(pawn),
                                MessageTypeDefOf.PositiveEvent,
                                false);
                        }
                    }
                });
            }

            lock (TradeLock)
            {
                DirectTrade currentTrade;
                if (ActiveTrades.TryGetValue(tradeId, out currentTrade) && currentTrade == trade)
                {
                    trade.State = DirectTradeState.Completed;
                    ActiveTrades.Remove(tradeId);
                }
            }
        }

        private static void HandleTradeCancel(string[] parts)
        {
            // PHXTT|v1|tcan|tradeId|senderUuid
            if (parts.Length < 5) return;
            string tradeId = parts[3];

            DirectTrade trade;
            lock (TradeLock)
            {
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                trade.State = DirectTradeState.Cancelled;
            }

            // Restore held pawns
            EnqueueMainThread(() =>
            {
                RestoreHeldPawns(trade);
            });

            lock (TradeLock)
            {
                ActiveTrades.Remove(tradeId);
            }

            EnqueueMainThread(() =>
            {
                Messages.Message("Phinix_legacyTalentTrade_tradeCancelled".Translate(), MessageTypeDefOf.NeutralEvent, false);
            });
        }

        private static void HandleRentalList(string[] parts)
        {
            // PHXTT|v1|rlist|rentalId|ownerUuid|b64PawnSummary|pricePerDay|maxDays|deposit|b64OwnerName
            if (parts.Length < 10) return;
            string rentalId = parts[3];
            string ownerUuid = parts[4];

            // Skip own echo — local contract already has OriginalPawnData
            if (ownerUuid == GetLocalUuid())
            {
                lock (RentalLock)
                {
                    if (RentalContracts.ContainsKey(rentalId)) return;
                }
            }

            string b64Summary = parts[5];
            int pricePerDay;
            if (!int.TryParse(parts[6], out pricePerDay)) return;
            int maxDays;
            if (!int.TryParse(parts[7], out maxDays)) return;
            int deposit;
            if (!int.TryParse(parts[8], out deposit)) return;
            string ownerName = TalentTradeProtocol.DecodeField(parts[9]);

            PawnSummary summary = PawnSummary.FromBase64(b64Summary);
            if (!TradeablePawnUtility.CanRentPawn(summary))
            {
                return;
            }

            RentalContract contract = new RentalContract
            {
                Id = rentalId,
                OwnerUuid = ownerUuid,
                OwnerName = ownerName,
                Summary = summary,
                PricePerDay = pricePerDay,
                MaxDays = maxDays,
                Deposit = deposit,
                State = RentalContractState.Listed
            };

            lock (RentalLock)
            {
                RentalContracts[rentalId] = contract;
            }
        }

        private static void HandleRentalDelist(string[] parts)
        {
            if (parts.Length < 5) return;
            string rentalId = parts[3];
            lock (RentalLock)
            {
                RentalContracts.Remove(rentalId);
            }
        }

        private static void HandleRentalRent(string[] parts)
        {
            // PHXTT|v1|rrent|rentalId|renterUuid|days|b64RenterName
            // Received by OWNER — someone wants to rent our pawn
            if (parts.Length < 7) return;
            string rentalId = parts[3];
            string renterUuid = parts[4];
            int days;
            if (!int.TryParse(parts[5], out days)) return;
            string renterName = TalentTradeProtocol.DecodeField(parts[6]);

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            RentalContract contract;
            string cachedXml;
            lock (RentalLock)
            {
                if (!RentalContracts.TryGetValue(rentalId, out contract)) return;
                if (contract.OwnerUuid != localUuid) return;
                if (contract.State != RentalContractState.Listed) return;

                // Cache original pawn XML before sending
                cachedXml = contract.OriginalPawnData;
            }

            if (string.IsNullOrEmpty(cachedXml))
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】HandleRentalRent: No cached pawn data for rental " + rentalId);
                return;
            }

            // Update contract state
            lock (RentalLock)
            {
                contract.State = RentalContractState.Active;
                contract.RenterUuid = renterUuid;
                contract.RenterName = renterName;
                contract.RentedDays = days;
                contract.StartTick = Find.TickManager.TicksGame;
                contract.ExpiryTick = contract.StartTick + days * 60000;
            }

            // Send cached pawn data to renter
            string msg = TalentTradeProtocol.BuildRentalConfirm(rentalId, localUuid, renterUuid, cachedXml);
            SendProtocol(msg);
        }

        private static void HandleRentalConfirm(string[] parts)
        {
            // PHXTT|v1|rconf|rentalId|ownerUuid|renterUuid|b64CompressedPawnData
            // Received by RENTER — owner is sending us the pawn
            if (parts.Length < 7) return;
            string rentalId = parts[3];
            string renterUuid = parts[5];
            string b64PawnData = parts[6];

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;
            if (renterUuid != localUuid) return;

            // Spawn pawn
            EnqueueMainThread(() =>
            {
                Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64PawnData);
                if (pawn != null)
                {
                    Messages.Message(
                        "Phinix_legacyTalentTrade_pawnReceivedMessage".Translate(pawn.LabelShortCap),
                        new LookTargets(pawn),
                        MessageTypeDefOf.PositiveEvent,
                        false);
                }
            });
        }

        private static void HandleRentalReturn(string[] parts)
        {
            // PHXTT|v1|rret|rentalId|renterUuid|b64CompressedPawnData
            // Received by OWNER — renter is returning the pawn (IGNORE their data, use cache)
            if (parts.Length < 6) return;
            string rentalId = parts[3];
            string renterUuid = parts[4];

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            RentalContract contract;
            string cachedXml;
            lock (RentalLock)
            {
                if (!RentalContracts.TryGetValue(rentalId, out contract)) return;
                if (contract.OwnerUuid != localUuid) return;
                if (contract.RenterUuid != renterUuid) return;

                cachedXml = contract.OriginalPawnData;
                contract.State = RentalContractState.Returned;
                RentalContracts.Remove(rentalId);
            }

            // Restore from cache (ignore renter's data)
            if (!string.IsNullOrEmpty(cachedXml))
            {
                EnqueueMainThread(() =>
                {
                    Pawn pawn = PawnDeserializer.DeserializeAndSpawn(cachedXml);
                    if (pawn != null)
                    {
                        Messages.Message(
                            "Phinix_legacyTalentTrade_pawnReceivedMessage".Translate(pawn.LabelShortCap),
                            new LookTargets(pawn),
                            MessageTypeDefOf.PositiveEvent,
                            false);
                    }
                });
            }
        }

        private static void HandleRentalExpiry(string[] parts)
        {
            // PHXTT|v1|rexp|rentalId
            // Broadcast: rental expired, auto-return
            if (parts.Length < 4) return;
            string rentalId = parts[3];

            lock (RentalLock)
            {
                RentalContracts.Remove(rentalId);
            }
        }

        private static void HandleRentalDead(string[] parts)
        {
            // PHXTT|v1|rdead|rentalId|renterUuid
            // Received by OWNER — renter reports pawn died, we revive from cache
            if (parts.Length < 5) return;
            string rentalId = parts[3];
            string renterUuid = parts[4];

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            RentalContract contract;
            string cachedXml;
            lock (RentalLock)
            {
                if (!RentalContracts.TryGetValue(rentalId, out contract)) return;
                if (contract.OwnerUuid != localUuid) return;
                if (contract.RenterUuid != renterUuid) return;

                cachedXml = contract.OriginalPawnData;
                contract.State = RentalContractState.PawnLost;
            }

            // Revive from cache using ResurrectionUtility
            if (!string.IsNullOrEmpty(cachedXml))
            {
                EnqueueMainThread(() =>
                {
                    Pawn pawn = PawnDeserializer.Deserialize(cachedXml);
                    if (pawn != null)
                    {
                        // Resurrect the pawn
                        ResurrectionUtility.TryResurrect(pawn, new ResurrectionParams());
                        PawnDeserializer.SpawnViaDropPod(pawn);

                        // Send revived pawn back to renter
                        string revivedXml = PawnSerializer.Serialize(pawn);
                        if (!string.IsNullOrEmpty(revivedXml))
                        {
                            string msg = TalentTradeProtocol.BuildRentalRevive(rentalId, localUuid, revivedXml);
                            SendProtocol(msg);
                        }
                    }
                });
            }
        }

        private static void HandleRentalRevive(string[] parts)
        {
            // PHXTT|v1|rrev|rentalId|ownerUuid|b64CompressedPawnData
            // Received by RENTER — owner revived and returned the pawn
            if (parts.Length < 6) return;
            string rentalId = parts[3];
            string b64PawnData = parts[5];

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            // Spawn revived pawn
            EnqueueMainThread(() =>
            {
                Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64PawnData);
                if (pawn != null)
                {
                    Messages.Message(
                        "Phinix_legacyTalentTrade_rentalPawnDied".Translate(pawn.LabelShortCap),
                        new LookTargets(pawn),
                        MessageTypeDefOf.NeutralEvent,
                        false);
                }
            });

            lock (RentalLock)
            {
                RentalContracts.Remove(rentalId);
            }
        }

        private static void HandleDefManifest(string[] parts)
        {
            // PHXTT|v1|defs|sessionId|senderUuid|targetUuid|b64CompressedDefList
            if (parts.Length < 7) return;
            string sessionId = parts[3];
            string senderUuid = parts[4];
            string targetUuid = parts[5];
            string b64DefList = parts[6];

            // Only process if we are the target
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid) || targetUuid != localUuid) return;

            // Decompress and deserialize the manifest
            string defListStr;
            try
            {
                defListStr = TalentTradeTransport.Decompress(b64DefList);
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】DefList decompress failed, treating as uncompressed: " + ex);
                defListStr = b64DefList; // fallback: maybe it wasn't compressed
            }

            DefManifest manifest = DefManifestHelper.DeserializeManifest(defListStr);
            TransferReport report = DefManifestHelper.CheckCompatibility(manifest);

            // Collect our missing defs and send back
            string missingStr = string.Empty;
            if (report.HasMissing)
            {
                missingStr = string.Join("\n", report.Missing.ToArray());
            }

            string b64Missing = TalentTradeTransport.Compress(missingStr);
            string ackMsg = TalentTradeProtocol.BuildDefAck(sessionId, localUuid, b64Missing);
            SendProtocol(ackMsg);
        }

        private static void HandleDefAck(string[] parts)
        {
            // PHXTT|v1|dack|sessionId|senderUuid|b64MissingDefs
            if (parts.Length < 6) return;
            string sessionId = parts[3];
            string b64Missing = parts[5];

            string missingStr;
            try
            {
                missingStr = TalentTradeTransport.Decompress(b64Missing);
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】Missing defs decompress failed, treating as uncompressed: " + ex);
                missingStr = b64Missing;
            }

            // Build a report from the response
            var report = new TransferReport();
            if (!string.IsNullOrEmpty(missingStr))
            {
                string[] lines = missingStr.Split('\n');
                foreach (string line in lines)
                {
                    if (!string.IsNullOrEmpty(line))
                        report.Missing.Add(line);
                }
            }

            // Store the report and invoke callback if registered
            Action<TransferReport> callback = null;
            lock (DefExchangeLock)
            {
                PendingDefReports[sessionId] = report;
                if (DefReportCallbacks.TryGetValue(sessionId, out callback))
                {
                    DefReportCallbacks.Remove(sessionId);
                }
            }

            if (callback != null)
            {
                EnqueueMainThread(() => callback(report));
            }
        }

        private static void HandleBlobPart(string[] parts)
        {
            // PHXTT|v1|blob|blobId|partIndex|totalParts|b64PartData
            if (parts.Length < 7) return;
            string blobId = parts[3];
            int partIndex;
            if (!int.TryParse(parts[4], out partIndex)) return;
            int totalParts;
            if (!int.TryParse(parts[5], out totalParts)) return;
            if (totalParts <= 0 || partIndex < 0 || partIndex >= totalParts) return;

            string partData = parts[6];

            lock (BlobLock)
            {
                string[] blobParts;
                if (!PendingBlobs.TryGetValue(blobId, out blobParts))
                {
                    blobParts = new string[totalParts];
                    PendingBlobs[blobId] = blobParts;
                }

                blobParts[partIndex] = partData;

                // Check if complete
                bool complete = true;
                for (int i = 0; i < blobParts.Length; i++)
                {
                    if (blobParts[i] == null) { complete = false; break; }
                }

                if (complete)
                {
                    PendingBlobs.Remove(blobId);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < blobParts.Length; i++)
                    {
                        sb.Append(blobParts[i]);
                    }
                    // Reassembled blob — process as a protocol message
                    string assembled = sb.ToString();
                    ProcessProtocolMessage(assembled);
                }
            }
        }

        // --- Listing expiry & heartbeat ---

        private static void ExpireOldListings()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastExpiryCheckUtc).TotalSeconds < 30) return;
            lastExpiryCheckUtc = now;

            string localUuid = GetLocalUuid();
            List<MarketListing> expired = new List<MarketListing>();

            lock (MarketLock)
            {
                foreach (var kvp in MarketListings)
                {
                    MarketListing l = kvp.Value;
                    if (l.State != MarketListingState.Active) continue;
                    if ((now - l.LastRefreshUtc).TotalMinutes > LISTING_EXPIRY_MINUTES)
                    {
                        expired.Add(l);
                    }
                }
                foreach (MarketListing l in expired)
                {
                    MarketListings.Remove(l.Id);
                    LocalPawnData.Remove(l.Id);
                }
            }

            if (expired.Count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Expired {expired.Count} stale listings");
                foreach (MarketListing l in expired)
                {
                    if (l.SellerUuid == localUuid)
                    {
                        ReturnExpiredPawn(l);
                    }
                }
            }
        }

        private static void ReturnExpiredPawn(MarketListing listing)
        {
            if (listing.HeldPawn != null && !listing.HeldPawn.Destroyed)
            {
                EnqueueMainThread(() =>
                {
                    DropPodUtility.DropThingsNear(
                        DropCellFinder.TradeDropSpot(Find.CurrentMap),
                        Find.CurrentMap,
                        new List<Thing> { listing.HeldPawn },
                        110,
                        false,
                        false,
                        true);
                    Messages.Message(
                        "Phinix_legacyTalentTrade_listingExpired".Translate(listing.HeldPawn.LabelShortCap),
                        new LookTargets(listing.HeldPawn),
                        MessageTypeDefOf.NeutralEvent,
                        false);
                });
            }
        }

        private static void HeartbeatMyListings()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - lastHeartbeatUtc).TotalMinutes < HEARTBEAT_INTERVAL_MINUTES) return;
            lastHeartbeatUtc = now;

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;
            string localName = GetLocalDisplayName();

            MarketListing[] snapshot;
            lock (MarketLock)
            {
                snapshot = new MarketListing[MarketListings.Count];
                int idx = 0;
                foreach (var kvp in MarketListings)
                {
                    snapshot[idx++] = kvp.Value;
                }
            }

            int count = 0;
            for (int i = 0; i < snapshot.Length; i++)
            {
                MarketListing l = snapshot[i];
                if (l == null || l.SellerUuid != localUuid || l.State != MarketListingState.Active) continue;
                if (l.Summary == null) continue;

                string msg = TalentTradeProtocol.BuildMarketList(l.Id, localUuid, l.Summary.ToBase64(), l.PriceSilver, localName);
                SendProtocol(msg);
                count++;
            }

            if (count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Heartbeat: re-broadcast {count} listings");
            }
        }

        // --- Offline handling ---

        public static void TrackPurchase(string listingId)
        {
            MarkStateChanged();
            PendingPurchases[listingId] = Find.TickManager.TicksGame;
        }

        private static void CheckPurchaseTimeouts()
        {
            if (PendingPurchases.Count == 0) return;

            int currentTick = Find.TickManager.TicksGame;
            List<string> timedOut = new List<string>();

            foreach (var kvp in PendingPurchases)
            {
                if (currentTick - kvp.Value > PURCHASE_TIMEOUT_TICKS)
                {
                    timedOut.Add(kvp.Key);
                }
            }

            foreach (string listingId in timedOut)
            {
                PendingPurchases.Remove(listingId);
                lock (MarketLock)
                {
                    MarketListings.Remove(listingId);
                }
                LegacyTalentTradeRuntime.LogWarning($"【三角洲贸易】Purchase timeout for listing {listingId}, seller offline");
                Messages.Message("Phinix_legacyTalentTrade_sellerOffline".Translate(), MessageTypeDefOf.RejectInput, false);
            }
        }

        // --- Direct Trade helpers ---

        private static void ExecuteDirectTrade(DirectTrade trade)
        {
            if (trade == null || trade.LocalExecuteSent) return;

            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            trade.LocalExecuteSent = true;
            trade.State = DirectTradeState.Executing;

            bool isInitiator = trade.InitiatorUuid == localUuid;
            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;

            if (myOffer == null || myOffer.PawnData == null || myOffer.PawnData.Count == 0)
            {
                // No pawns to send, just send empty execute
                string msg = TalentTradeProtocol.BuildTradeExecute(trade.Id, localUuid, "", "");
                SendProtocol(msg);
                return;
            }

            // Serialize all held pawns
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < myOffer.PawnData.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(myOffer.PawnData[i]);
            }

            string pawnMsg = TalentTradeProtocol.BuildTradeExecute(trade.Id, localUuid, sb.ToString(), "");
            SendProtocol(pawnMsg);
        }

        private static void RestoreHeldPawns(DirectTrade trade)
        {
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            bool isInitiator = trade.InitiatorUuid == localUuid;
            TradeOffer myOffer = isInitiator ? trade.InitiatorOffer : trade.TargetOffer;

            if (myOffer == null || myOffer.PawnData == null) return;

            for (int i = 0; i < myOffer.PawnData.Count; i++)
            {
                string b64 = myOffer.PawnData[i];
                if (string.IsNullOrEmpty(b64)) continue;
                Pawn pawn = PawnDeserializer.DeserializeAndSpawn(b64);
                if (pawn != null)
                {
                    Messages.Message(
                        "Phinix_legacyTalentTrade_pawnRestored".Translate(pawn.LabelShortCap),
                        new LookTargets(pawn),
                        MessageTypeDefOf.NeutralEvent,
                        false);
                }
            }
        }

        // --- Direct Trade local API (for UI) ---

        public static DirectTrade GetTrade(string tradeId)
        {
            lock (TradeLock)
            {
                DirectTrade trade;
                if (ActiveTrades.TryGetValue(tradeId, out trade)) return trade;
                return null;
            }
        }

        public static string InitiateDirectTrade(string targetUuid)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return null;

            string tradeId = Guid.NewGuid().ToString("N").Substring(0, 12);
            string localName = GetLocalDisplayName();

            DirectTrade trade = new DirectTrade
            {
                Id = tradeId,
                InitiatorUuid = localUuid,
                TargetUuid = targetUuid,
                InitiatorName = localName,
                TargetName = "",
                State = DirectTradeState.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            };

            lock (TradeLock)
            {
                ActiveTrades[tradeId] = trade;
            }

            string msg = TalentTradeProtocol.BuildTradeRequest(tradeId, localUuid, targetUuid, localName);
            SendProtocol(msg);

            Messages.Message("Phinix_legacyTalentTrade_tradeRequestSent".Translate(targetUuid), MessageTypeDefOf.NeutralEvent, false);
            return tradeId;
        }

        public static void AcceptTrade(string tradeId)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                if (trade.TargetUuid != localUuid) return;
                trade.State = DirectTradeState.Negotiating;
            }

            string msg = TalentTradeProtocol.BuildTradeAccept(tradeId, localUuid);
            SendProtocol(msg);
        }

        public static void RejectTrade(string tradeId)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                trade.State = DirectTradeState.Cancelled;
                ActiveTrades.Remove(tradeId);
            }

            string msg = TalentTradeProtocol.BuildTradeReject(tradeId, localUuid);
            SendProtocol(msg);
        }

        public static void CancelTrade(string tradeId)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            DirectTrade trade;
            lock (TradeLock)
            {
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;
                trade.State = DirectTradeState.Cancelled;
            }

            EnqueueMainThread(() => RestoreHeldPawns(trade));

            lock (TradeLock)
            {
                ActiveTrades.Remove(tradeId);
            }

            string msg = TalentTradeProtocol.BuildTradeCancel(tradeId, localUuid);
            SendProtocol(msg);
        }

        public static void SendTradeOffer(string tradeId, TradeOffer offer)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;

                if (localUuid == trade.InitiatorUuid)
                {
                    trade.InitiatorOffer = offer;
                    trade.InitiatorConfirmed = false;
                }
                else
                {
                    trade.TargetOffer = offer;
                    trade.TargetConfirmed = false;
                }
            }

            string b64Offer = TradeOfferSerializer.ToBase64(offer);
            string msg = TalentTradeProtocol.BuildTradeOffer(tradeId, localUuid, b64Offer);
            SendProtocol(msg);
        }

        public static void LockTrade(string tradeId)
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            lock (TradeLock)
            {
                DirectTrade trade;
                if (!ActiveTrades.TryGetValue(tradeId, out trade)) return;

                if (localUuid == trade.InitiatorUuid)
                    trade.InitiatorConfirmed = true;
                else
                    trade.TargetConfirmed = true;

                if (trade.InitiatorConfirmed && trade.TargetConfirmed)
                {
                    trade.State = DirectTradeState.Executing;
                    ExecuteDirectTrade(trade);
                    return;
                }
            }

            string msg = TalentTradeProtocol.BuildTradeLock(tradeId, localUuid);
            SendProtocol(msg);
        }

        public static string[] GetOnlineUserUuids()
        {
            try
            {
                if (!LegacyTalentTradeRuntime.IsActive) return new string[0];
                return LegacyTalentTradeRuntime.GetOnlineUserUuids();
            }
            catch (Exception)
            {
                // 防御性：用户列表查询异常时降级返回空数组
                return new string[0];
            }
        }

        public static void RestoreMyListingsOnLogin()
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            lock (MarketLock)
            {
                foreach (var kvp in OfflineListingCache)
                {
                    MarketListings[kvp.Key] = kvp.Value;
                }
                foreach (var kvp in OfflinePawnDataCache)
                {
                    LocalPawnData[kvp.Key] = kvp.Value;
                }
            }

            if (OfflineListingCache.Count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Restored {OfflineListingCache.Count} cached listings");
                string localName = GetLocalDisplayName();
                foreach (var kvp in OfflineListingCache)
                {
                    MarketListing l = kvp.Value;
                    if (l.Summary != null)
                    {
                        string msg = TalentTradeProtocol.BuildMarketList(l.Id, localUuid, l.Summary.ToBase64(), l.PriceSilver, localName);
                        SendProtocol(msg);
                    }
                }
            }
        }

        public static void CacheMyListingsOnLogout()
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid)) return;

            OfflineListingCache.Clear();
            OfflinePawnDataCache.Clear();

            lock (MarketLock)
            {
                foreach (var kvp in MarketListings)
                {
                    if (kvp.Value.SellerUuid == localUuid)
                    {
                        OfflineListingCache[kvp.Key] = kvp.Value;
                    }
                }
                foreach (var kvp in LocalPawnData)
                {
                    OfflinePawnDataCache[kvp.Key] = kvp.Value;
                }
            }

            if (OfflineListingCache.Count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Cached {OfflineListingCache.Count} listings for next login");
            }
        }

        // --- Save session lifecycle ---

        /// <summary>
        /// Called from GenScene.GoToMainMenu Prefix — player is leaving the save.
        /// Broadcasts delist for all own listings and clears static state.
        /// Does NOT spawn pawns — recovery is handled by ReturnPendingPawns on next load.
        /// </summary>
        public static void OnExitSave()
        {
            MarkStateChanged();
            string localUuid = GetLocalUuid();
            if (string.IsNullOrEmpty(localUuid))
            {
                activeSaveToken = null;
                return;
            }

            DelistAllOwnListings(localUuid, "OnExitSave");

            OfflineListingCache.Clear();
            OfflinePawnDataCache.Clear();
            PendingPurchases.Clear();
            activeSaveToken = null;
        }

        /// <summary>
        /// Called from TalentTradeGameComponent.FinalizeInit when a save is loaded.
        /// Always cleans up orphaned listings — own listings in static state that the
        /// loaded save doesn't know about (not in activeListingIds).
        /// </summary>
        public static void OnSaveSessionStart(string newToken)
        {
            MarkStateChanged();
            if (string.IsNullOrEmpty(newToken)) return;

            string localUuid = GetLocalUuid();
            if (!string.IsNullOrEmpty(localUuid))
            {
                if (activeSaveToken != null && activeSaveToken != newToken)
                {
                    // Save switch — delist everything we own, it all belongs to the old save
                    LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】Save switch detected (old={activeSaveToken}, new={newToken}), cleaning up orphaned listings");
                    DelistAllOwnListings(localUuid, "OnSaveSessionStart-switch");
                    OfflineListingCache.Clear();
                    OfflinePawnDataCache.Clear();
                }
                else
                {
                    // Same save (or first load) — delist only orphans not tracked by this save version
                    var gc = TalentTradeGameComponent.Current;
                    if (gc != null)
                    {
                        HashSet<string> tracked = gc.GetActiveListingIdsSnapshot();
                        DelistOrphanedListings(localUuid, tracked);
                    }
                }
            }

            activeSaveToken = newToken;
        }

        /// <summary>
        /// Broadcasts delist for all own active listings and removes them from static state.
        /// </summary>
        private static void DelistAllOwnListings(string localUuid, string caller)
        {
            List<string> toRemove = new List<string>();

            lock (MarketLock)
            {
                foreach (var kvp in MarketListings)
                {
                    if (kvp.Value.SellerUuid == localUuid && kvp.Value.State == MarketListingState.Active)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                foreach (string id in toRemove)
                {
                    string msg = TalentTradeProtocol.BuildMarketDelist(id, localUuid);
                    SendProtocol(msg);
                    MarketListings.Remove(id);
                    LocalPawnData.Remove(id);
                }
            }

            if (toRemove.Count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】{caller}: Delisted {toRemove.Count} own listings");
            }
        }

        /// <summary>
        /// Delists own listings that are NOT tracked by the current save version.
        /// Handles the case: player lists a pawn, then loads an older save of the same lineage.
        /// </summary>
        private static void DelistOrphanedListings(string localUuid, HashSet<string> trackedIds)
        {
            List<string> orphans = new List<string>();

            lock (MarketLock)
            {
                foreach (var kvp in MarketListings)
                {
                    if (kvp.Value.SellerUuid == localUuid && kvp.Value.State == MarketListingState.Active)
                    {
                        if (!trackedIds.Contains(kvp.Key))
                        {
                            orphans.Add(kvp.Key);
                        }
                    }
                }

                foreach (string id in orphans)
                {
                    string msg = TalentTradeProtocol.BuildMarketDelist(id, localUuid);
                    SendProtocol(msg);
                    MarketListings.Remove(id);
                    LocalPawnData.Remove(id);
                }
            }

            if (orphans.Count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage($"【三角洲贸易】OnSaveSessionStart: Delisted {orphans.Count} orphaned listings not tracked by loaded save");
            }
        }
    }
}
