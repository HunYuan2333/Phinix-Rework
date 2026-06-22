using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Fix vanilla PawnTextureAtlas.GC() KeyNotFoundException.
    /// The original code uses frameAssignments[pawn] indexer which throws
    /// when pawn hash changes after despawn. We replace with TryGetValue.
    /// </summary>
    [HarmonyPatch(typeof(PawnTextureAtlas), nameof(PawnTextureAtlas.GC))]
    public static class PawnTextureAtlasGCPatch
    {
        private static readonly List<Pawn> tmpPawnsToFree = new List<Pawn>();

        public static bool Prefix(PawnTextureAtlas __instance)
        {
            var frameAssignments = Traverse.Create(__instance).Field<Dictionary<Pawn, PawnTextureAtlasFrameSet>>("frameAssignments").Value;
            var freeFrameSets = Traverse.Create(__instance).Field<List<PawnTextureAtlasFrameSet>>("freeFrameSets").Value;

            if (frameAssignments == null || freeFrameSets == null) return true;

            try
            {
                foreach (Pawn pawn in frameAssignments.Keys)
                {
                    if (pawn == null || !pawn.SpawnedOrAnyParentSpawned)
                    {
                        tmpPawnsToFree.Add(pawn);
                    }
                }

                foreach (Pawn pawn in tmpPawnsToFree)
                {
                    PawnTextureAtlasFrameSet frameSet;
                    if (pawn != null && frameAssignments.TryGetValue(pawn, out frameSet))
                    {
                        freeFrameSets.Add(frameSet);
                    }
                    frameAssignments.Remove(pawn);
                }
            }
            finally
            {
                tmpPawnsToFree.Clear();
            }

            return false; // skip original
        }
    }
}
