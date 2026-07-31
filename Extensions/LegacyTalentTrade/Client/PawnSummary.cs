using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    public class PawnSummary
    {
        public string Name = string.Empty;
        public string Gender = string.Empty;
        public int BiologicalAge;
        public string RaceDefName = "Human";
        public string SkillsSummary = string.Empty;
        public string TraitsSummary = string.Empty;
        public string HealthSummary = "Healthy";
        public string PawnKind = "Colonist";

        /// <summary>
        /// Extract a PawnSummary from an actual Pawn object.
        /// </summary>
        public static PawnSummary FromPawn(Pawn pawn)
        {
            var summary = new PawnSummary();
            if (pawn == null) return summary;

            summary.Name = pawn.Name != null ? pawn.Name.ToStringFull : pawn.LabelShortCap;
            summary.Gender = pawn.gender.ToString();
            summary.BiologicalAge = pawn.ageTracker != null ? (int)pawn.ageTracker.AgeBiologicalYearsFloat : 0;
            summary.RaceDefName = pawn.def != null ? pawn.def.defName : "Human";
            summary.PawnKind = GetPawnKindLabel(pawn);

            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike && pawn.skills != null)
            {
                var skillParts = new List<string>();
                foreach (var skill in pawn.skills.skills)
                {
                    if (skill.TotallyDisabled) continue;
                    string passion = "";
                    if (skill.passion == Passion.Minor) passion = "*";
                    else if (skill.passion == Passion.Major) passion = "**";
                    skillParts.Add(skill.def.skillLabel + " " + skill.Level + passion);
                }
                summary.SkillsSummary = string.Join(", ", skillParts.ToArray());
            }
            else if (pawn.RaceProps != null && pawn.RaceProps.Animal)
            {
                summary.SkillsSummary = "Animal";
            }
            else if (pawn.IsColonyMech)
            {
                summary.SkillsSummary = "Mechanoid";
            }

            if (pawn.story != null && pawn.story.traits != null)
            {
                var traitParts = new List<string>();
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    traitParts.Add(trait.LabelCap);
                }
                summary.TraitsSummary = string.Join(", ", traitParts.ToArray());
            }
            else if (pawn.IsPrisonerOfColony)
            {
                summary.TraitsSummary = "Prisoner";
            }

            if (pawn.health != null && pawn.health.hediffSet != null)
            {
                var hediffs = pawn.health.hediffSet.hediffs;
                if (hediffs.Count == 0)
                {
                    summary.HealthSummary = "Healthy";
                }
                else
                {
                    var healthParts = new List<string>();
                    foreach (var hediff in hediffs)
                    {
                        if (hediff != null)
                            healthParts.Add(hediff.LabelCap);
                    }
                    summary.HealthSummary = string.Join(", ", healthParts.ToArray());
                }
            }

            return summary;
        }

        public string ToBase64()
        {
            string raw = string.Join("\n", new[]
            {
                Name,
                Gender,
                BiologicalAge.ToString(),
                RaceDefName,
                SkillsSummary,
                TraitsSummary,
                HealthSummary,
                PawnKind
            });
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        public static PawnSummary FromBase64(string b64)
        {
            PawnSummary summary = new PawnSummary();
            if (string.IsNullOrEmpty(b64)) return summary;

            try
            {
                string raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                string[] lines = raw.Split('\n');
                if (lines.Length >= 1) summary.Name = lines[0];
                if (lines.Length >= 2) summary.Gender = lines[1];
                if (lines.Length >= 3) int.TryParse(lines[2], out summary.BiologicalAge);
                if (lines.Length >= 4) summary.RaceDefName = lines[3];
                if (lines.Length >= 5) summary.SkillsSummary = lines[4];
                if (lines.Length >= 6) summary.TraitsSummary = lines[5];
                if (lines.Length >= 7) summary.HealthSummary = lines[6];
                if (lines.Length >= 8) summary.PawnKind = lines[7];
            }
            catch (Exception)
            {
                // 防御性：摘要解析失败时返回默认空摘要
            }

            return summary;
        }

        public string GetDisplayLabel()
        {
            string genderSymbol = string.Empty;
            if (Gender == "Male") genderSymbol = "♂";
            else if (Gender == "Female") genderSymbol = "♀";

            string agePart = string.Empty;
            if (BiologicalAge > 0)
            {
                agePart = " " + BiologicalAge.ToString() + "Phinix_legacyTalentTrade_ageUnit".Translate().ToString();
            }
            string translatedKind = TranslatePawnKind(PawnKind);
            string kindPart = string.IsNullOrEmpty(translatedKind) ? string.Empty : (" [" + translatedKind + "]");
            return string.Concat(Name, " ", genderSymbol, agePart, kindPart).Trim();
        }

        private static string TranslatePawnKind(string pawnKind)
        {
            switch (pawnKind ?? "Colonist")
            {
                case "Prisoner":
                    return "Phinix_legacyTalentTrade_pawnKindPrisoner".Translate();
                case "Mech":
                    return "Phinix_legacyTalentTrade_pawnKindMech".Translate();
                case "Animal":
                    return "Phinix_legacyTalentTrade_pawnKindAnimal".Translate();
                default:
                    return "Phinix_legacyTalentTrade_pawnKindColonist".Translate();
            }
        }

        private static string GetPawnKindLabel(Pawn pawn)
        {
            if (pawn == null) return "Colonist";
            if (pawn.IsPrisonerOfColony) return "Prisoner";
            if (pawn.IsColonyMech) return "Mech";
            if (pawn.RaceProps != null && pawn.RaceProps.Animal) return "Animal";
            return "Colonist";
        }
    }
}
