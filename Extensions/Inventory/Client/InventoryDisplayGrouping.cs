using System;
using System.Collections.Generic;
using System.Globalization;
using Phinix.InventoryExtension;

namespace Phinix.InventoryExtension.Client
{
    internal sealed class InventoryDisplayGroup
    {
        public string Key;
        public readonly List<int> EntryIndices = new List<int>();
        public long AvailableItems;
    }

    /// <summary>Pure presentation projection. Never modifies ledger entries, payloads or ownership.</summary>
    internal static class InventoryDisplayGrouping
    {
        public static List<InventoryDisplayGroup> Build(IReadOnlyList<InventoryEntry> entries,
            InventoryItemPresentation[] presentations, long[] available)
        {
            List<InventoryDisplayGroup> groups = new List<InventoryDisplayGroup>();
            Dictionary<string, InventoryDisplayGroup> byKey = new Dictionary<string, InventoryDisplayGroup>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                InventoryEntry entry = entries[i];
                InventoryItemPresentation presentation = presentations[i];
                long items = checked(available[i] * presentation.ItemsPerUnit);
                string key = string.IsNullOrEmpty(presentation.GroupKey) ? null :
                    Part(entry.CodecId) + Part(entry.CodecVersion.ToString(CultureInfo.InvariantCulture)) +
                    Part(presentation.GroupKey) + Part(entry.Source) + Part(entry.OriginKind) +
                    Part(entry.OriginUserId) +
                    // Without a stable user ID, do not combine unrelated or anonymous events.
                    Part(string.IsNullOrEmpty(entry.OriginUserId) ? entry.OriginId : null);
                InventoryDisplayGroup group = null;
                if (key != null) byKey.TryGetValue(key, out group);
                // Keep individually valid entries separate if their combined count cannot be represented.
                if (group == null || items > long.MaxValue - group.AvailableItems)
                {
                    group = new InventoryDisplayGroup { Key = key ?? "entry:" + entry.EntryId };
                    groups.Add(group);
                    if (key != null) byKey[key] = group;
                }
                group.EntryIndices.Add(i);
                group.AvailableItems += items;
            }
            return groups;
        }

        private static string Part(string value)
        {
            value = value ?? string.Empty;
            return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }
    }
}
