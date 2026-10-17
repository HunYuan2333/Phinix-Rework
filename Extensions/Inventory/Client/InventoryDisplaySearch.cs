using System;
using System.Collections.Generic;
using System.Globalization;

namespace Phinix.InventoryExtension.Client
{
    internal static class InventoryDisplaySearch
    {
        // Inputs are cached, tags-stripped display strings, never payloads or ownership keys.
        internal static HashSet<int> Find(IReadOnlyList<string> labels, string query)
        {
            var found = new HashSet<int>();
            query = (query ?? "").Trim();
            for (int i = 0; i < labels.Count; i++)
                if (query.Length == 0 || CultureInfo.InvariantCulture.CompareInfo.IndexOf(labels[i] ?? "", query, CompareOptions.IgnoreCase) >= 0)
                    found.Add(i);
            return found;
        }
    }
}
