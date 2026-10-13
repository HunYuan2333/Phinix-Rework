using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhinixClient.Framework;
using PhinixClient.Trade;

internal static partial class Program
{
    private static void CheckQuotePreview(Type module)
    {
        var method=module.Assembly.GetType("Phinix.TradeExtension.Client.TradeQuoteRows",true).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Static);
        var item=new TradeItemSnapshot("rifle",2,87,TradeItemQuality.Good,"steel",new TradeItemSnapshot("inside",1,5),"opaque.codec",new byte[]{1,2,3});
        Func<string,string> label=id=>id=="rifle"?"Rifle":id=="steel"?"Steel":null;
        Func<string,string> translate=key=>"T:"+key;
        var iconIds=new List<string>();Func<string,UnityEngine.Texture2D> icon=id=>{iconIds.Add(id);return null;};
        var rows=(List<ClientPresentationRow>)method.Invoke(null,new object[]{new[]{item,new TradeItemSnapshot("missing",-2,3),null},label,translate,icon});
        Assert(iconIds.SequenceEqual(new[]{"inside","missing",null}),"Actual snapshot projection resolves inner and missing icons without creating Things.");
        Assert(rows.Count==3&&rows[0].Label=="Rifle × 2","Actual quote snapshot rows preserve quantities without materializing Verse things.");
        Assert(rows[0].Detail.Contains("Steel")&&rows[0].Detail.Contains("87")&&rows[0].Detail.Contains("quoteQuality_Good")&&rows[0].Detail.Contains("inside × 1"),"Actual material, hit points, quality and inner preview facts are projected.");
        Assert(rows[0].Badges.Any(b=>b.Id=="state")&&item.StatePayload.SequenceEqual(new byte[]{1,2,3}),"Opaque state is indicated without decoding or modifying payload.");
        Assert(rows[1].Label=="missing × -2"&&rows[1].Badges.Any(b=>b.Id=="unknown")&&rows[1].Badges.Any(b=>b.Id=="invalid"),"Missing definition and invalid quantity remain visible with warnings.");
        Assert(rows[2].Badges.Any(b=>b.Id=="invalid"),"Null record is retained as invalid rather than silently filtered.");
        Assert(((List<ClientPresentationRow>)method.Invoke(null,new object[]{Array.Empty<TradeItemSnapshot>(),label,translate,icon})).Count==0,"Empty receiving side remains empty; no fictional gift items.");
    }
}
