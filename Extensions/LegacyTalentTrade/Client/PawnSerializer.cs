using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Serializes a Pawn to a compressed Base64 string using RimWorld's Scribe system.
    /// Must be called on the main thread (Scribe is not thread-safe).
    /// </summary>
    public static class PawnSerializer
    {
        /// <summary>
        /// Serialize a Pawn to XML string via ScribeSaver.DebugOutputFor.
        /// Returns null on failure.
        /// </summary>
        public static string PawnToXml(Pawn pawn)
        {
            if (pawn == null) return null;
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】PawnToXml called while Scribe is busy (mode=" + Scribe.mode + "). Aborting.");
                return null;
            }

            try
            {
                string xml = Scribe.saver.DebugOutputFor(pawn);
                if (string.IsNullOrEmpty(xml))
                {
                    LegacyTalentTradeRuntime.LogError("【三角洲贸易】PawnToXml: DebugOutputFor returned empty for " + pawn.LabelShort);
                    return null;
                }
                return xml;
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】PawnToXml failed: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Full pipeline: Pawn → XML → GZip → Base64.
        /// Returns null on failure.
        /// </summary>
        public static string Serialize(Pawn pawn)
        {
            string xml = PawnToXml(pawn);
            if (xml == null) return null;

            try
            {
                return TalentTradeTransport.Compress(xml);
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】PawnSerializer.Serialize compress failed: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Despawn a pawn from the map and hold it for trading.
        /// Returns true if the pawn was successfully despawned or was already despawned.
        /// </summary>
        public static bool DespawnAndHold(Pawn pawn)
        {
            if (pawn == null) return false;

            try
            {
                // Disconnect mech from mechanitor control group before despawn
                if (pawn.IsColonyMech || (pawn.RaceProps != null && pawn.RaceProps.IsMechanoid))
                {
                    try
                    {
                        MechanitorUtility.ForceDisconnectMechFromOverseer(pawn);
                    }
                    catch (Exception ex2)
                    {
                        LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】ForceDisconnectMechFromOverseer failed (non-fatal): " + ex2);
                    }
                }

                // If pawn is a mechanitor, disconnect all overseen mechs so they become uncontrolled
                if (pawn.mechanitor != null)
                {
                    try
                    {
                        List<Pawn> mechs = new List<Pawn>(pawn.mechanitor.OverseenPawns);
                        for (int i = 0; i < mechs.Count; i++)
                        {
                            MechanitorUtility.ForceDisconnectMechFromOverseer(mechs[i]);
                        }
                    }
                    catch (Exception ex3)
                    {
                        LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】Disconnect overseen mechs failed (non-fatal): " + ex3);
                    }
                }

                // Remove pawn from texture atlas BEFORE despawn to prevent GC KeyNotFoundException
                RemoveFromTextureAtlas(pawn);

                if (pawn.Spawned)
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }

                return true;
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】DespawnAndHold failed: " + ex);
                return false;
            }
        }

        private static FieldInfo pawnAtlasesField;
        private static FieldInfo frameAssignmentsField;
        private static FieldInfo freeFrameSetsField;

        private static void RemoveFromTextureAtlas(Pawn pawn)
        {
            try
            {
                if (pawnAtlasesField == null)
                    pawnAtlasesField = typeof(GlobalTextureAtlasManager).GetField("pawnTextureAtlases", BindingFlags.Static | BindingFlags.NonPublic);
                if (frameAssignmentsField == null)
                    frameAssignmentsField = typeof(PawnTextureAtlas).GetField("frameAssignments", BindingFlags.Instance | BindingFlags.NonPublic);
                if (freeFrameSetsField == null)
                    freeFrameSetsField = typeof(PawnTextureAtlas).GetField("freeFrameSets", BindingFlags.Instance | BindingFlags.NonPublic);

                if (pawnAtlasesField == null || frameAssignmentsField == null || freeFrameSetsField == null) return;

                var atlases = (List<PawnTextureAtlas>)pawnAtlasesField.GetValue(null);
                if (atlases == null) return;

                foreach (var atlas in atlases)
                {
                    var assignments = (Dictionary<Pawn, PawnTextureAtlasFrameSet>)frameAssignmentsField.GetValue(atlas);
                    if (assignments == null) continue;

                    PawnTextureAtlasFrameSet frameSet;
                    if (assignments.TryGetValue(pawn, out frameSet))
                    {
                        assignments.Remove(pawn);
                        var freeSets = (List<PawnTextureAtlasFrameSet>)freeFrameSetsField.GetValue(atlas);
                        if (freeSets != null)
                        {
                            freeSets.Add(frameSet);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogWarning("【三角洲贸易】RemoveFromTextureAtlas failed (non-fatal): " + ex);
            }
        }
    }
}
