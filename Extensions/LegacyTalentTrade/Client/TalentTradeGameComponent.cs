using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Per-save persistence for market listings.
    /// On save: backup listing pawn data (no side effects).
    /// On load: delist old listings, return pawns via drop pod.
    /// </summary>
    public class TalentTradeGameComponent : GameComponent
    {
        // Persisted: listing IDs + pawn data from last save
        private List<string> pendingReturnIds = new List<string>();
        private List<string> pendingReturnData = new List<string>();

        // Persisted: unique identity for this save lineage
        private string saveToken;

        // Persisted: last mod version the player has seen the notification for
        private string lastSeenVersion;

        // Runtime only: track which listings belong to THIS save session
        private HashSet<string> activeListingIds = new HashSet<string>();

        private static TalentTradeGameComponent current;
        public static TalentTradeGameComponent Current { get { return current; } }
        public string SaveToken { get { return saveToken; } }

        public TalentTradeGameComponent(Game game) : base()
        {
            current = this;
        }

        public override void FinalizeInit()
        {
            // 插件禁用（StaticActivationPolicy 跳过）时，GameComponent 仍会被 RimWorld
            // 自动实例化（GenTypes.AllSubclassesNonAbstract）。此处必须守卫，禁用即不做事。
            if (!LegacyTalentTradeRuntime.IsActive) return;

            current = this;
            // Generate token for new games or saves that predate this feature
            if (string.IsNullOrEmpty(saveToken))
                saveToken = Guid.NewGuid().ToString("N");
            // Detect save-switch and clean up orphaned listings
            TalentTradeManager.OnSaveSessionStart(saveToken);
            // Return any pawns that were listed in a previous session
            ReturnPendingPawns();
            // Show version letter on first load or after mod update
            VersionNotifier.TryNotify(lastSeenVersion);
            lastSeenVersion = VersionNotifier.ModVersion;
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Just backup — no delist, no clearing
                BackupActiveListings();
            }

            Scribe_Collections.Look(ref pendingReturnIds, "pendingReturnIds", LookMode.Value);
            Scribe_Collections.Look(ref pendingReturnData, "pendingReturnData", LookMode.Value);
            Scribe_Values.Look(ref saveToken, "saveToken", null);
            Scribe_Values.Look(ref lastSeenVersion, "lastSeenVersion", null);

            if (pendingReturnIds == null) pendingReturnIds = new List<string>();
            if (pendingReturnData == null) pendingReturnData = new List<string>();
        }

        public void TrackListing(string listingId)
        {
            activeListingIds.Add(listingId);
        }

        public void UntrackListing(string listingId)
        {
            activeListingIds.Remove(listingId);
        }

        public bool OwnsListing(string listingId)
        {
            return activeListingIds.Contains(listingId);
        }

        public HashSet<string> GetActiveListingIdsSnapshot()
        {
            return new HashSet<string>(activeListingIds);
        }

        /// <summary>
        /// Save only: backup active listing data into the save file.
        /// Does NOT delist or clear anything — game continues normally after save.
        /// </summary>
        private void BackupActiveListings()
        {
            pendingReturnIds = new List<string>();
            pendingReturnData = new List<string>();

            foreach (string listingId in activeListingIds)
            {
                string pawnData = TalentTradeManager.GetLocalPawnData(listingId);
                if (!string.IsNullOrEmpty(pawnData))
                {
                    pendingReturnIds.Add(listingId);
                    pendingReturnData.Add(pawnData);
                }
            }

            if (pendingReturnIds.Count > 0)
            {
                LegacyTalentTradeRuntime.LogMessage("【三角洲贸易】Backed up " + pendingReturnIds.Count + " listings into save | 备份了 " + pendingReturnIds.Count + " 个挂牌到存档");
            }
        }

        /// <summary>
        /// Load only: delist old listings and return pawns via drop pod.
        /// </summary>
        private void ReturnPendingPawns()
        {
            if (pendingReturnIds.Count == 0) return;

            int count = pendingReturnIds.Count;
            LegacyTalentTradeRuntime.LogMessage("【三角洲贸易】Returning " + count + " held pawns from previous session | 归还上次会话中 " + count + " 个待售 pawn");

            string localUuid = TalentTradeManager.GetLocalUuid();

            for (int i = 0; i < pendingReturnIds.Count && i < pendingReturnData.Count; i++)
            {
                string listingId = pendingReturnIds[i];
                string data = pendingReturnData[i];

                // Broadcast delist so other players remove it
                if (!string.IsNullOrEmpty(localUuid))
                {
                    string msg = TalentTradeProtocol.BuildMarketDelist(listingId, localUuid);
                    TalentTradeManager.SendProtocol(msg);
                }

                // Remove from manager
                TalentTradeManager.RemoveListingLocally(listingId);

                // Spawn pawn via drop pod
                TalentTradeManager.EnqueueMainThread(() =>
                {
                    Pawn pawn = PawnDeserializer.DeserializeAndSpawn(data);
                    if (pawn != null)
                    {
                        Messages.Message(
                            "Phinix_legacyTalentTrade_pawnRestored".Translate(pawn.LabelShortCap),
                            new LookTargets(pawn),
                            MessageTypeDefOf.NeutralEvent,
                            false);
                    }
                });
            }

            pendingReturnIds.Clear();
            pendingReturnData.Clear();
        }
    }
}
