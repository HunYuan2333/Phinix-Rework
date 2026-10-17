using System;using System.Linq;using System.Collections.Generic;
using Phinix.InventoryExtension.Client;using PhinixClient.Framework;
namespace UnityEngine { public class Texture {} public sealed class Texture2D:Texture {} }
namespace Verse
{
    public sealed class ThingDef { public UnityEngine.Texture uiIcon; }
    public static class DefDatabase<T> where T:class
    {
        public static readonly Dictionary<string,T> Values=new Dictionary<string,T>();public static int Reads;
        public static T GetNamedSilentFail(string name){Reads++;if(name=="broken")throw new Exception("bad optional icon");return Values.TryGetValue(name,out var v)?v:null;}
    }
    public static class BaseContent { public static UnityEngine.Texture BadTex=new UnityEngine.Texture2D(); }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Main()
    {
        var labels=new[]{"草药\nHerbal medicine\n红包：用户甲","钢铁\nSTEEL\n交易：用户乙","草药\n草药原名\n交易：用户甲"};
        Check(InventoryDisplaySearch.Find(labels,"草药").SetEquals(new[]{0,2}),"Chinese substring finds all records, not just the group's first record");
        Check(InventoryDisplaySearch.Find(labels," steel ").SetEquals(new[]{1}),"Invariant case and surrounding spaces");
        Check(InventoryDisplaySearch.Find(labels,"用户甲").SetEquals(new[]{0,2}),"Cached source text is searchable");
        Check(InventoryDisplaySearch.Find(labels,"missing").Count==0,"No match stays empty");
        Check(InventoryDisplaySearch.Find(labels,"").Count==3&&InventoryDisplaySearch.Find(labels,"  ").Count==3,"Clearing search restores the full population");
        Check(InventoryDisplaySearch.Find(new string[]{null,""},"x").Count==0,"Missing optional labels are safe");
        Check(labels[0].StartsWith("草药")&&labels.Length==3,"Search does not change source records or order");
        var image=new UnityEngine.Texture2D();var unknown=new UnityEngine.Texture2D();
        Verse.DefDatabase<Verse.ThingDef>.Values["Steel"]=new Verse.ThingDef{uiIcon=image};
        Verse.DefDatabase<Verse.ThingDef>.Values["UnknownItem"]=new Verse.ThingDef{uiIcon=unknown};
        var icons=new InventoryIconCache();var first=icons.Get("Steel");int reads=Verse.DefDatabase<Verse.ThingDef>.Reads;
        Check(ReferenceEquals(first,icons.Get("Steel"))&&Verse.DefDatabase<Verse.ThingDef>.Reads==reads,"One cached asset per Def, not per record or frame");
        using(var lease=(IClientTextureImageLease)first.Acquire())
        {Check(lease.TryGetTexture(out var texture)&&ReferenceEquals(texture,image),"Codec Def resolves its borrowed UI texture");}
        foreach(var name in new[]{"missing",null,"broken",new string('x',513)})
        {using(var lease=(IClientTextureImageLease)icons.Get(name).Acquire())Check(lease.TryGetTexture(out var texture)&&ReferenceEquals(texture,unknown),"Absent/invalid/failing metadata has a question-mark fallback");}
        var held=first.Acquire();icons.Clear();Check(!held.IsValid&&first.Acquire()==null,"Closing/switching games revokes old image leases");held.Dispose();
        Verse.DefDatabase<Verse.ThingDef>.Values.Remove("UnknownItem");
        using(var lease=(IClientTextureImageLease)icons.Get(null).Acquire())Check(lease.TryGetTexture(out var texture)&&ReferenceEquals(texture,Verse.BaseContent.BadTex),"No UnknownItem Def still leaves a visible fallback");
        icons.Clear();Console.WriteLine("Inventory display: "+checks+" checks passed (no native GUI/materialization).");
    }
}
