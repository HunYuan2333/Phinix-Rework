using System;
using System.IO;
using Phinix.InventoryExtension;
using Phinix.InventoryExtension.Client;

internal static class CapacityRegression
{
    internal static InventoryState State(long sequence, byte[] payload = null)
    {
        var state = new InventoryState { Sequence = sequence };
        if (payload != null) state.Entries.Add(new InventoryEntry { EntryId = "synthetic", CodecId = "fixture", CodecVersion = 1,
            Quantity = 10, Payload = payload, Label = "fixture", Source = "test" });
        return state;
    }
    internal static void Run(string root)
    {
        string file = Path.Combine(root, "capacity.journal");
        byte[] payload = new byte[256 * 1024]; for (int i = 0; i < payload.Length; i++) payload[i] = 255;
        using (var journal = new InventoryJournal(file, State(0)))
        {
            long sequence = 1;
            while (sequence < 250 && journal.TryAppend(State(sequence, payload))) sequence++;
            if (sequence == 250 || !journal.Faulted || journal.FaultReason == null || !journal.FaultReason.Contains("Capacity"))
                throw new Exception("Capacity regression: append refusal must publish an explicit persistence fault, not silently return false.");
            Console.WriteLine("CAPACITY regression passed; accepted=" + (sequence - 1) + "; bytes=" + new FileInfo(file).Length);
        }
    }
}
