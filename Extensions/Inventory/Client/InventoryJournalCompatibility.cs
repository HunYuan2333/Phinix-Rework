using System;

namespace Phinix.InventoryExtension.Client
{
    /// <summary>
    /// Compatibility-only maintenance for legacy full-snapshot journals.
    /// Called automatically for the attached save after load/save. No directory sweep,
    /// item deletion, receipt pruning, recovery approval, or migration marker is performed.
    /// </summary>
    internal static class InventoryJournalCompatibility
    {
        internal static bool TryMaintain(InventoryJournal journal, string saveFile, string saveId,
            InventoryState expected, Action<string, bool> report)
        {
            if (journal == null) return false;
            try
            {
                using (var proof = InventorySaveCheckpoint.Open(saveFile, saveId, expected, journal.Identity))
                {
                    if (!journal.TryCheckpoint(proof))
                    {
                        Report(report, "[Inventory][Compatibility] History retained: " +
                            (journal.CheckpointError ?? journal.FaultReason ?? "checkpoint unavailable"), true);
                        return false;
                    }
                    if (journal.LastCheckpointBytesReclaimed > 0)
                        Report(report, "[Inventory][Compatibility] Reclaimed " + journal.LastCheckpointBytesReclaimed +
                            " bytes of saved journal history; sequence=" + proof.State.Sequence + ".", false);
                    return true;
                }
            }
            catch (Exception error)
            {
                // Never expose full player paths, snapshot contents or payloads.
                Report(report, "[Inventory][Compatibility] History retained; saved proof unavailable: " + error.GetType().Name + ".", true);
                return false;
            }
        }
        private static void Report(Action<string, bool> report, string message, bool warning)
        {
            try { report?.Invoke(message, warning); }
            catch (Exception) { /* Diagnostics cannot change a completed maintenance outcome. */ }
        }
    }
}
