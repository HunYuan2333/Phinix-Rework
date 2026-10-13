using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    public sealed partial class BuiltInInventoryClientExtension
    {
        private InventoryExtractionBatch extractionBatch;
        private Game batchGame;
        private InventoryGameComponent batchSave;
        private Map batchMap;
        private long batchVersion;
        public InventoryBatchProgress BatchProgress => extractionBatch?.Progress;
        private Map ExtractionMap => settings != null && settings.Get("trade.dropCurrentMap", false)
            ? Find.CurrentMap : Find.AnyPlayerHomeMap ?? Find.CurrentMap;
        public bool StartBatchExtraction(IReadOnlyList<InventoryBatchItem> items, long expectedVersion, out string reason)
        {
            if (!Ready(out reason)) return false;
            if (BatchProgress?.Running == true || expectedVersion != Version || items == null || items.Count < 1 || items.Count > 10000)
            { reason = "Phinix_inventory_batchInvalid".Translate().ToString(); return false; }
            var available = ledger.AvailableSnapshot().ToDictionary(e => e.EntryId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var copy = new InventoryBatchItem[items.Count]; var limits = new int[items.Count]; long total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || string.IsNullOrEmpty(item.EntryId) || !seen.Add(item.EntryId) || item.Quantity < 1 ||
                    !available.TryGetValue(item.EntryId, out var entry) || item.Quantity > entry.Quantity ||
                    !codecs.TryGetValue(entry.CodecId, out var codec) || codec.Version != entry.CodecVersion)
                { reason = "Phinix_inventory_batchInvalid".Translate().ToString(); return false; }
                if (!TryGetMaximumStackCount(codec, entry, out limits[i], out reason)) return false;
                if (limits[i] < 1) { reason = "Invalid codec stack limit."; return false; }
                // Opaque units are whole recorded objects, never reinterpreted as merged item counts.
                if (entry.AggregationKey == null) limits[i] = 1;
                if (item.Quantity > long.MaxValue - total) { reason = "Batch quantity overflow."; return false; }
                total += item.Quantity; copy[i] = new InventoryBatchItem(item.EntryId, item.Quantity);
            }
            batchMap = ExtractionMap;
            if (!InventoryDropDelivery.TryResolveTradeDropTarget(batchMap, out _, out reason)) return false;
            batchGame = Current.Game; batchSave = attachedSave; batchVersion = Version;
            extractionBatch = new InventoryExtractionBatch(copy, limits); RaiseAvailabilityChanged(); reason = null; return true;
        }
        public void StopBatchExtraction()
        {
            if (BatchProgress?.Running != true) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId)
                throw new InvalidOperationException("Inventory operations must run on the game main thread.");
            extractionBatch.Stop("Phinix_inventory_batchStopped".Translate().ToString());
            ReleaseBatchContext(); RaiseAvailabilityChanged();
        }
        private void ReleaseBatchContext() { batchGame = null; batchSave = null; batchMap = null; }
        private void AdvanceBatchExtraction()
        {
            if (BatchProgress?.Running != true) return;
            if (!ReferenceEquals(batchGame, Current.Game) || !ReferenceEquals(batchSave, attachedSave) ||
                !ReferenceEquals(batchMap, ExtractionMap) || batchVersion != Version || !Ready(out _))
            { extractionBatch.Stop("Phinix_inventory_batchChanged".Translate().ToString()); }
            else
            {
                try { extractionBatch.Advance((string id, long units, out string why) => TryExtractSingleStack(id, units, null, -1, out why)); }
                catch (Exception error) { extractionBatch.Stop(error.Message); host?.Log?.Invoke("[Inventory] Batch extraction stopped: " + error, Utils.LogLevel.ERROR); }
                batchVersion = Version;
            }
            if (!BatchProgress.Running) ReleaseBatchContext();
            RaiseAvailabilityChanged();
        }
    }
}
