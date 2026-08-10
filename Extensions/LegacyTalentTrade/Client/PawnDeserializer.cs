using System;
using System.Collections.Generic;
using System.Xml;
using RimWorld;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Deserializes a Pawn from compressed Base64 data and spawns it on the map.
    /// Must be called on the main thread (Scribe is not thread-safe).
    /// </summary>
    public static class PawnDeserializer
    {
        /// <summary>
        /// Full pipeline: Base64 → GZip decompress → XML → Pawn object.
        /// Returns null on failure. Does NOT spawn the pawn.
        /// </summary>
        public static Pawn Deserialize(string b64Compressed)
        {
            if (string.IsNullOrEmpty(b64Compressed)) return null;

            string xml;
            try
            {
                xml = TalentTradeTransport.Decompress(b64Compressed);
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】PawnDeserializer.Deserialize decompress failed: " + ex);
                return null;
            }

            Pawn pawn = XmlToPawn(xml);
            if (pawn != null)
            {
                // Critical check: reject pawn if def is null (missing race mod)
                if (pawn.def == null)
                {
                    LegacyTalentTradeRuntime.LogError("【三角洲贸易】Pawn deserialization failed: pawn.def is null (missing race mod). Rejecting pawn.");
                    return null;
                }
                // 错误隔离（设计哲学 §3.5）：后处理失败只记录警告，不中断投递。
                // 反序列化的 pawn 可能带损坏的 tracker（跨存档/跨 mod 环境），
                // 单个修正步骤失败不应导致领取动作整体失败。
                try
                {
                    PostProcessPawn(pawn);
                }
                catch (Exception ex)
                {
                    LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】PostProcessPawn failed (pawn still delivered): " + ex);
                }
                LegacyTalentTradeRuntime.LogMessage("【三角洲贸易】Pawn 反序列化成功。注意：上方任何 hediff/need 警告是 Scribe 时序导致的预期行为，不影响功能。| Pawn deserialized successfully. Any hediff/need warnings above are expected due to Scribe timing and do not affect functionality.");
            }
            return pawn;
        }

        /// <summary>
        /// Parse XML string into a Pawn object using Scribe loader.
        /// </summary>
        public static Pawn XmlToPawn(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】XmlToPawn called while Scribe is busy (mode=" + Scribe.mode + "). Aborting.");
                return null;
            }

            Pawn pawn = null;
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                XmlNode pawnNode = doc.DocumentElement;
                if (pawnNode.Name == "saveable")
                {
                    // DebugOutputFor wraps in <saveable>, use it directly
                }
                else if (doc.DocumentElement["saveable"] != null)
                {
                    pawnNode = doc.DocumentElement["saveable"];
                }

                // These trackers contain references to objects owned by the
                // sender's save (Ideo, apparel policy, drug policy, and royal
                // title state). They cannot be resolved in the receiver's
                // save and cause Scribe to emit noisy cross-reference errors
                // or invoke tracker post-load code with null state. The
                // receiver's Scribe load creates fresh trackers; PostProcessPawn
                // then applies the local faction/ideology semantics.
                RemoveCrossSaveTrackers(pawnNode);

                // Set up Scribe for loading
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe.loader.curXmlParent = pawnNode;
                Scribe.loader.curParent = null;
                Scribe.loader.curPathRelToParent = null;

                pawn = ScribeExtractor.SaveableFromNode<Pawn>(pawnNode, new object[0]);

                // Critical check BEFORE cross-ref resolution
                if (pawn != null && pawn.def == null)
                {
                    LegacyTalentTradeRuntime.LogError("【三角洲贸易】Pawn.def is null after SaveableFromNode, aborting");
                    pawn = null;
                }

                if (pawn != null)
                {
                    // Resolve cross-references
                    Scribe.loader.crossRefs.ResolveAllCrossReferences();

                    // Clean up null hediffs BEFORE post-load init
                    if (pawn.health != null && pawn.health.hediffSet != null)
                    {
                        pawn.health.hediffSet.hediffs.RemoveAll(h => h == null || h.def == null);
                    }

                    // Run post-load init
                    Scribe.loader.initer.DoAllPostLoadInits();
                }
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】XmlToPawn failed: " + ex);
                pawn = null;
            }
            finally
            {
                // Always reset Scribe state
                Scribe.mode = LoadSaveMode.Inactive;
                Scribe.loader.FinalizeLoading();
            }

            return pawn;
        }

        private static void RemoveCrossSaveTrackers(XmlNode pawnNode)
        {
            if (pawnNode == null) return;

            string[] trackerNames = { "ideo", "outfits", "drugs", "royalty" };
            for (int i = 0; i < trackerNames.Length; i++)
            {
                XmlNode tracker = pawnNode.SelectSingleNode("./" + trackerNames[i]);
                if (tracker != null && tracker.ParentNode != null)
                {
                    tracker.ParentNode.RemoveChild(tracker);
                }
            }
        }

        /// <summary>
        /// Post-process a deserialized pawn to fix cross-save incompatibilities.
        /// </summary>
        private static void PostProcessPawn(Pawn pawn)
        {
            if (pawn == null) return;

            bool isAnimal = pawn.RaceProps != null && pawn.RaceProps.Animal;
            bool isMech = pawn.RaceProps != null && pawn.RaceProps.IsMechanoid;
            bool isColonyMech = pawn.IsColonyMech || isMech;
            bool isPrisoner = pawn.guest != null && pawn.guest.IsPrisoner;

            // Scribe creates these trackers when their XML nodes are absent,
            // but keep the invariant explicit for malformed/old payloads.
            if (pawn.outfits == null)
                pawn.outfits = new Pawn_OutfitTracker(pawn);
            if (pawn.drugs == null)
                pawn.drugs = new Pawn_DrugPolicyTracker(pawn);
            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike &&
                ModsConfig.IdeologyActive && pawn.ideo == null)
                pawn.ideo = new Pawn_IdeoTracker(pawn);

            // Regenerate Thing IDs to avoid conflicts
            pawn.SetForbidden(false, false);
            pawn.thingIDNumber = -1;
            pawn.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();

            // Regenerate IDs for apparel
            if (pawn.apparel != null && pawn.apparel.WornApparel != null)
            {
                foreach (var ap in pawn.apparel.WornApparel)
                {
                    if (ap != null)
                    {
                        ap.thingIDNumber = -1;
                        ap.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();
                    }
                }
            }

            // Regenerate IDs for equipment
            if (pawn.equipment != null && pawn.equipment.AllEquipmentListForReading != null)
            {
                foreach (var eq in pawn.equipment.AllEquipmentListForReading)
                {
                    if (eq != null)
                    {
                        eq.thingIDNumber = -1;
                        eq.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();
                    }
                }
            }

            // Regenerate IDs for inventory
            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
            {
                foreach (var item in pawn.inventory.innerContainer)
                {
                    if (item != null)
                    {
                        item.thingIDNumber = -1;
                        item.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();
                    }
                }
            }

            // Clean up null hediffs
            if (pawn.health != null && pawn.health.hediffSet != null)
            {
                pawn.health.hediffSet.hediffs.RemoveAll(h => h == null || h.def == null);

                // Regenerate hediff loadIDs
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff != null)
                    {
                        hediff.loadID = Find.UniqueIDsManager.GetNextHediffID();
                    }
                }
            }

            // Regenerate Gene loadIDs
            if (pawn.genes != null)
            {
                if (pawn.genes.Endogenes != null)
                {
                    foreach (var gene in pawn.genes.Endogenes)
                    {
                        if (gene != null)
                        {
                            gene.loadID = Find.UniqueIDsManager.GetNextGeneID();
                        }
                    }
                }
                if (pawn.genes.Xenogenes != null)
                {
                    foreach (var gene in pawn.genes.Xenogenes)
                    {
                        if (gene != null)
                        {
                            gene.loadID = Find.UniqueIDsManager.GetNextGeneID();
                        }
                    }
                }
            }

            if (Faction.OfPlayer != null && pawn.Faction != Faction.OfPlayer)
            {
                try
                {
                    pawn.SetFaction(Faction.OfPlayer);
                }
                catch (Exception ex)
                {
                    // 反序列化的 guest tracker 可能损坏（SetGuestStatus 内部 NRE）。
                    // 重置 tracker 后重试；仍失败则跳过，由外层错误隔离兜底。
                    LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】SetFaction failed, resetting guest tracker and retrying: " + ex);
                    try
                    {
                        pawn.guest = new RimWorld.Pawn_GuestTracker(pawn);
                        pawn.SetFaction(Faction.OfPlayer);
                    }
                    catch (Exception ex2)
                    {
                        LegacyTalentTradeRuntime.LogError("【三角洲贸易】SetFaction failed twice: " + ex2);
                    }
                }
            }

            if (pawn.guest != null)
            {
                if (isPrisoner)
                    pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            }

            // Fix Ideo only for humanlikes that actually use ideology.
            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike && ModsConfig.IdeologyActive && pawn.ideo != null)
            {
                Ideo receiverIdeo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
                if (receiverIdeo != null)
                {
                    pawn.ideo.SetIdeo(receiverIdeo);
                }
            }

            // Royal titles can keep a null-linked faction/title chain after transfer.
            // Replace with a fresh empty tracker instead of nulling — ThoughtWorker_BedroomRequirementsNotMet
            // calls p.royalty.GetUnmetBedroomRequirements() without a null guard, so setting royalty=null
            // causes a NullReferenceException every tick. A new empty tracker has no titles and returns
            // no unmet requirements, which is exactly what we want.
            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike && ModsConfig.RoyaltyActive)
            {
                pawn.royalty = new RimWorld.Pawn_RoyaltyTracker(pawn);
            }

            // Clear invalid Thing references
            if (pawn.mindState != null)
            {
                pawn.mindState.lastAttackedTarget = LocalTargetInfo.Invalid;
                pawn.mindState.enemyTarget = null;
                pawn.mindState.meleeThreat = null;
            }

            // Reset jobs to avoid stale references
            if (pawn.jobs != null)
            {
                pawn.jobs.ClearQueuedJobs();
                if (pawn.jobs.curJob != null)
                {
                    pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced, false);
                }
            }

            // Reset stances
            if (pawn.stances != null)
            {
                pawn.stances.SetStance(new Stance_Mobile());
            }

            // Reset verb tracker to rebuild from current equipment
            if (pawn.verbTracker != null)
            {
                pawn.verbTracker = new VerbTracker(pawn);
            }
            if (pawn.meleeVerbs != null)
            {
                pawn.meleeVerbs.Notify_PawnDespawned();
            }

            if (isAnimal)
            {
                pawn.ownership = null;
                pawn.training = pawn.training ?? new Pawn_TrainingTracker(pawn);
            }

            if (isColonyMech)
            {
                try
                {
                    MechanitorUtility.ForceDisconnectMechFromOverseer(pawn);
                }
                catch (Exception)
                {
                    // 单个后处理步骤失败不中断（§3.5，外层已有隔离）
                }
                pawn.relations?.ClearAllRelations();
            }

            if (isPrisoner && pawn.guest != null)
            {
                pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            }
        }

        /// <summary>
        /// Spawn a deserialized pawn on the map via drop pod at the trade drop spot.
        /// </summary>
        public static bool SpawnViaDropPod(Pawn pawn, Map map = null)
        {
            if (pawn == null) return false;

            try
            {
                if (map == null)
                {
                    map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                }
                if (map == null)
                {
                    LegacyTalentTradeRuntime.LogError("【三角洲贸易】SpawnViaDropPod: No map available.");
                    return false;
                }

                IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
                bool isPrisoner = pawn.guest != null && pawn.guest.IsPrisoner;

                if (pawn.Faction == null || pawn.Faction != Faction.OfPlayer)
                {
                    pawn.SetFaction(Faction.OfPlayer);
                }

                if (isPrisoner && pawn.guest != null)
                {
                    pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                }

                TradeUtility.SpawnDropPod(dropSpot, map, pawn);

                if (pawn.RaceProps != null && pawn.RaceProps.IsMechanoid)
                {
                    try
                    {
                        MechanitorUtility.ForceDisconnectMechFromOverseer(pawn);
                    }
                    catch (Exception)
                    {
                        // 机械体强制断开失败不中断投递
                    }
                }

                if (isPrisoner && pawn.guest != null)
                {
                    pawn.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                }

                // Force refresh pawn graphics cache
                pawn.Drawer.renderer.SetAllGraphicsDirty();
                PortraitsCache.SetDirty(pawn);

                return true;
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】SpawnViaDropPod failed: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Full pipeline: deserialize + spawn via drop pod.
        /// </summary>
        public static Pawn DeserializeAndSpawn(string b64Compressed, Map map = null)
        {
            Pawn pawn = Deserialize(b64Compressed);
            if (pawn == null) return null;

            if (SpawnViaDropPod(pawn, map))
            {
                return pawn;
            }

            return null;
        }
    }
}
