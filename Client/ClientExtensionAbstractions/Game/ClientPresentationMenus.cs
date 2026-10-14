using System;
using System.Collections.Generic;
using Verse;

namespace PhinixClient.Framework
{
    // Current game bridge: no discovery, business lookup or additional action registry.
    public static class ClientPresentationMenus
    {
        public static List<FloatMenuOption> ToGameOptions(IReadOnlyList<ClientPresentationMenuItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var options = new List<FloatMenuOption>(items.Count);
            foreach (var item in items) options.Add(new FloatMenuOption(item.Label, item.Request));
            return options;
        }
    }
}
