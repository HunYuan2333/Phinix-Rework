using RimWorld;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Version constant. The actual notification is triggered from TalentTradeGameComponent.FinalizeInit().
    /// </summary>
    internal static class VersionNotifier
    {
        public const string ModVersion = "v2.0";

        public static void TryNotify(string lastSeenVersion)
        {
            if (lastSeenVersion == ModVersion)
                return;

            Find.LetterStack.ReceiveLetter(
                "Phinix_legacyTalentTrade_versionTitle".Translate(ModVersion),
                "Phinix_legacyTalentTrade_versionText".Translate(ModVersion),
                LetterDefOf.NeutralEvent,
                (LookTargets)null);
        }
    }
}
