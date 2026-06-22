using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Collects and compares Def manifests for pawn transfer compatibility checks.
    /// A manifest lists all DefNames referenced by a pawn that the receiver needs to have loaded.
    /// </summary>
    public static class DefManifestHelper
    {
        /// <summary>
        /// Collect all Def names referenced by a pawn that matter for transfer compatibility.
        /// </summary>
        public static DefManifest CollectFromPawn(Pawn pawn)
        {
            var manifest = new DefManifest();
            if (pawn == null) return manifest;

            // Race
            if (pawn.def != null)
                manifest.RaceDefs.Add(pawn.def.defName);

            // Apparel
            if (pawn.apparel != null)
            {
                foreach (var ap in pawn.apparel.WornApparel)
                {
                    if (ap.def != null)
                        manifest.ApparelDefs.Add(ap.def.defName);
                    if (ap.Stuff != null)
                        manifest.StuffDefs.Add(ap.Stuff.defName);
                }
            }

            // Equipment (weapons)
            if (pawn.equipment != null)
            {
                foreach (var eq in pawn.equipment.AllEquipmentListForReading)
                {
                    if (eq.def != null)
                        manifest.WeaponDefs.Add(eq.def.defName);
                    if (eq.Stuff != null)
                        manifest.StuffDefs.Add(eq.Stuff.defName);
                }
            }

            // Hediffs
            if (pawn.health != null && pawn.health.hediffSet != null)
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff.def != null)
                        manifest.HediffDefs.Add(hediff.def.defName);
                }
            }

            // Traits
            if (pawn.story != null && pawn.story.traits != null)
            {
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    if (trait.def != null)
                        manifest.TraitDefs.Add(trait.def.defName);
                }
            }

            // Backstories
            if (pawn.story != null)
            {
                if (pawn.story.Childhood != null)
                    manifest.BackstoryDefs.Add(pawn.story.Childhood.defName);
                if (pawn.story.Adulthood != null)
                    manifest.BackstoryDefs.Add(pawn.story.Adulthood.defName);
            }

            // Genes (if Biotech DLC)
            try
            {
                if (pawn.genes != null)
                {
                    foreach (var gene in pawn.genes.GenesListForReading)
                    {
                        if (gene.def != null)
                            manifest.GeneDefs.Add(gene.def.defName);
                    }
                }
            }
            catch (Exception)
            {
                // Biotech not loaded, skip
            }

            return manifest;
        }

        /// <summary>
        /// Check which defs from a manifest are missing on the local game.
        /// Returns a report of what's compatible and what's missing.
        /// </summary>
        public static TransferReport CheckCompatibility(DefManifest manifest)
        {
            var report = new TransferReport();
            if (manifest == null) return report;

            // Race
            foreach (string defName in manifest.RaceDefs)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) != null)
                    report.Compatible.Add("Phinix_legacyTalentTrade_transferRaceOk".Translate(defName));
                else
                    report.Missing.Add("Phinix_legacyTalentTrade_transferRaceFail".Translate(defName));
            }

            // Apparel
            foreach (string defName in manifest.ApparelDefs)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null)
                    report.Missing.Add("Phinix_legacyTalentTrade_transferEquipRemoved".Translate(defName));
            }

            // Weapons
            foreach (string defName in manifest.WeaponDefs)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null)
                    report.Missing.Add("Phinix_legacyTalentTrade_transferEquipRemoved".Translate(defName));
            }

            // Stuff
            foreach (string defName in manifest.StuffDefs)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null)
                    report.Missing.Add("Phinix_legacyTalentTrade_transferEquipRemoved".Translate(defName));
            }

            // Hediffs
            foreach (string defName in manifest.HediffDefs)
            {
                if (DefDatabase<HediffDef>.GetNamedSilentFail(defName) == null)
                    report.Missing.Add("Phinix_legacyTalentTrade_transferHediffRemoved".Translate(defName));
            }

            // Traits
            foreach (string defName in manifest.TraitDefs)
            {
                if (DefDatabase<TraitDef>.GetNamedSilentFail(defName) == null)
                    report.Missing.Add("Phinix_legacyTalentTrade_transferTraitRemoved".Translate(defName));
                else
                    report.Compatible.Add("Phinix_legacyTalentTrade_transferTraitOk".Translate(defName));
            }

            // Backstories
            foreach (string defName in manifest.BackstoryDefs)
            {
                if (DefDatabase<BackstoryDef>.GetNamedSilentFail(defName) == null)
                    report.Missing.Add("Phinix_legacyTalentTrade_transferHediffRemoved".Translate(defName)); // reuse hediff string
            }

            // Genes
            try
            {
                foreach (string defName in manifest.GeneDefs)
                {
                    if (DefDatabase<GeneDef>.GetNamedSilentFail(defName) == null)
                        report.Missing.Add("Phinix_legacyTalentTrade_transferHediffRemoved".Translate(defName));
                }
            }
            catch (Exception)
            {
                // Biotech not loaded
            }

            return report;
        }

        /// <summary>
        /// Serialize a DefManifest to a pipe-separated string for protocol transmission.
        /// Format: category:defName1,defName2|category:defName3,defName4|...
        /// </summary>
        public static string SerializeManifest(DefManifest manifest)
        {
            if (manifest == null) return string.Empty;

            var sb = new StringBuilder();
            AppendCategory(sb, "race", manifest.RaceDefs);
            AppendCategory(sb, "apparel", manifest.ApparelDefs);
            AppendCategory(sb, "weapon", manifest.WeaponDefs);
            AppendCategory(sb, "stuff", manifest.StuffDefs);
            AppendCategory(sb, "hediff", manifest.HediffDefs);
            AppendCategory(sb, "trait", manifest.TraitDefs);
            AppendCategory(sb, "backstory", manifest.BackstoryDefs);
            AppendCategory(sb, "gene", manifest.GeneDefs);

            // Remove trailing separator
            if (sb.Length > 0 && sb[sb.Length - 1] == ';')
                sb.Length--;

            return sb.ToString();
        }

        /// <summary>
        /// Deserialize a DefManifest from the protocol string format.
        /// </summary>
        public static DefManifest DeserializeManifest(string data)
        {
            var manifest = new DefManifest();
            if (string.IsNullOrEmpty(data)) return manifest;

            string[] categories = data.Split(';');
            foreach (string cat in categories)
            {
                if (string.IsNullOrEmpty(cat)) continue;
                int colonIdx = cat.IndexOf(':');
                if (colonIdx <= 0) continue;

                string catName = cat.Substring(0, colonIdx);
                string defsStr = cat.Substring(colonIdx + 1);
                string[] defs = defsStr.Split(',');

                HashSet<string> target;
                switch (catName)
                {
                    case "race": target = manifest.RaceDefs; break;
                    case "apparel": target = manifest.ApparelDefs; break;
                    case "weapon": target = manifest.WeaponDefs; break;
                    case "stuff": target = manifest.StuffDefs; break;
                    case "hediff": target = manifest.HediffDefs; break;
                    case "trait": target = manifest.TraitDefs; break;
                    case "backstory": target = manifest.BackstoryDefs; break;
                    case "gene": target = manifest.GeneDefs; break;
                    default: continue;
                }

                foreach (string d in defs)
                {
                    if (!string.IsNullOrEmpty(d))
                        target.Add(d);
                }
            }

            return manifest;
        }

        private static void AppendCategory(StringBuilder sb, string name, HashSet<string> defs)
        {
            if (defs.Count == 0) return;
            sb.Append(name);
            sb.Append(':');
            bool first = true;
            foreach (string d in defs)
            {
                if (!first) sb.Append(',');
                sb.Append(d);
                first = false;
            }
            sb.Append(';');
        }
    }

    /// <summary>
    /// Holds all DefNames referenced by a pawn, grouped by category.
    /// </summary>
    public class DefManifest
    {
        public HashSet<string> RaceDefs = new HashSet<string>();
        public HashSet<string> ApparelDefs = new HashSet<string>();
        public HashSet<string> WeaponDefs = new HashSet<string>();
        public HashSet<string> StuffDefs = new HashSet<string>();
        public HashSet<string> HediffDefs = new HashSet<string>();
        public HashSet<string> TraitDefs = new HashSet<string>();
        public HashSet<string> BackstoryDefs = new HashSet<string>();
        public HashSet<string> GeneDefs = new HashSet<string>();
    }

    /// <summary>
    /// Result of a compatibility check between a DefManifest and the local game's loaded defs.
    /// </summary>
    public class TransferReport
    {
        public List<string> Compatible = new List<string>();
        public List<string> Missing = new List<string>();

        public bool HasMissing { get { return Missing.Count > 0; } }

        public string ToSummary()
        {
            var sb = new StringBuilder();
            foreach (string line in Compatible)
            {
                sb.AppendLine(line);
            }
            foreach (string line in Missing)
            {
                sb.AppendLine(line);
            }
            return sb.ToString();
        }
    }
}
