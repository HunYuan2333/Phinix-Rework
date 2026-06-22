using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    internal static class TradeablePawnUtility
    {
        public static List<Pawn> GetTradeablePawns(Map map)
        {
            List<Pawn> result = new List<Pawn>();
            if (map == null || map.mapPawns == null)
                return result;

            AddRange(result, map.mapPawns.FreeColonistsSpawned);
            AddRange(result, map.mapPawns.SpawnedColonyAnimals);
            AddRange(result, map.mapPawns.SpawnedColonyMechs);
            AddRange(result, map.mapPawns.PrisonersOfColonySpawned);
            return result;
        }

        public static bool CanTrade(Pawn pawn)
        {
            if (!IsValidTradeTarget(pawn))
                return false;
            if (pawn.IsSlaveOfColony)
                return false;
            if (pawn.IsPrisonerOfColony)
                return true;
            if (pawn.IsColonyMech)
                return pawn.Faction == Faction.OfPlayer;
            if (pawn.RaceProps.Humanlike)
                return pawn.Faction == Faction.OfPlayer && pawn.IsColonistPlayerControlled;
            if (pawn.RaceProps.Animal)
                return pawn.Faction == Faction.OfPlayer;
            return false;
        }

        public static bool CanRentPawn(Pawn pawn)
        {
            if (!IsValidTradeTarget(pawn))
                return false;
            return pawn.Faction == Faction.OfPlayer
                && pawn.RaceProps.Humanlike
                && pawn.IsColonistPlayerControlled
                && !pawn.IsPrisonerOfColony
                && !pawn.IsSlaveOfColony;
        }

        public static bool CanRentPawn(PawnSummary summary)
        {
            return summary != null && (summary.PawnKind ?? "Colonist") == "Colonist";
        }

        public static string GetLabel(Pawn pawn)
        {
            if (pawn == null)
                return "???";
            PawnSummary summary = PawnSummary.FromPawn(pawn);
            return summary.GetDisplayLabel();
        }

        private static bool IsValidTradeTarget(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned)
                return false;
            if (pawn.RaceProps == null)
                return false;
            return true;
        }

        private static void AddRange(List<Pawn> result, IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
                return;
            foreach (Pawn pawn in pawns)
            {
                if (CanTrade(pawn) && !result.Contains(pawn))
                    result.Add(pawn);
            }
        }
    }
}
