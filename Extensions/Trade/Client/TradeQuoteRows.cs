using System;
using System.Collections.Generic;
using PhinixClient.Framework;
using PhinixClient.Trade;
using Utils;
using UnityEngine;
using Verse;

namespace Phinix.TradeExtension.Client
{
    // Snapshot description only. Never materialize a Thing or interpret opaque codec bytes.
    internal static class TradeQuoteRows
    {
        internal static Texture2D IconFromDef(string id)=>DefDatabase<ThingDef>.GetNamedSilentFail(string.IsNullOrEmpty(id)?"UnknownItem":id)?.uiIcon ?? DefDatabase<ThingDef>.GetNamedSilentFail("UnknownItem")?.uiIcon;
        private static string Text(string value)
        { value=TextHelper.StripRichText(value??"");return value.Length>1024?value.Substring(0,1024)+"…":value; }
        internal static List<ClientPresentationRow> Build(TradeItemSnapshot[] items, Func<string,string> label, Func<string,string> translate, Func<string,Texture2D> icon = null)
        {
            var rows=new List<ClientPresentationRow>();
            for(int index=0;index<(items?.Length??0);index++)
            {
                var item=items[index];var badges=new List<ClientPresentationBadge>();
                if(item==null)
                {
                    rows.Add(new ClientPresentationRow("item."+index,translate("quoteInvalid"))
                        .WithBadges(new[]{new ClientPresentationBadge("invalid",translate("quoteInvalid")).WithColor("ui.warning")}).WithIcon(icon?.Invoke(null)));
                    continue;
                }
                string name=label(item.DefName);
                if(string.IsNullOrEmpty(name))
                {name=string.IsNullOrEmpty(item.DefName)?translate("quoteUnknown"):item.DefName;badges.Add(new ClientPresentationBadge("unknown",translate("quoteUnknown")).WithColor("ui.warning"));}
                var details=new List<string>();
                if(!string.IsNullOrEmpty(item.StuffDefName))details.Add(translate("quoteMaterial")+": "+Text(label(item.StuffDefName)??item.StuffDefName));
                if(item.Quality!=TradeItemQuality.None)details.Add(translate("quoteQuality")+": "+Text(translate("quoteQuality_"+item.Quality)));
                details.Add(translate("quoteHitPoints")+": "+item.HitPoints);
                if(item.InnerItem!=null)details.Add(translate("quoteContains")+": "+Text(label(item.InnerItem.DefName)??item.InnerItem.DefName)+" × "+item.InnerItem.StackCount);
                if(!string.IsNullOrEmpty(item.StateCodecId))badges.Add(new ClientPresentationBadge("state",translate("quoteHasState")).WithColor("ui.info"));
                if(item.StackCount<=0)badges.Add(new ClientPresentationBadge("invalid",translate("quoteInvalidQuantity")).WithColor("ui.warning"));
                rows.Add(new ClientPresentationRow("item."+index,Text(name)+" × "+item.StackCount,string.Join("\n",details)).WithBadges(badges).WithIcon(icon?.Invoke(item.InnerItem?.DefName ?? item.DefName)));
            }
            return rows;
        }
    }
}
