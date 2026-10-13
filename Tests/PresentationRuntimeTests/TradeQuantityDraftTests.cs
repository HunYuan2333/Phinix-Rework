using Phinix.TradeExtension.Client;
internal static partial class Program
{
    private static void CheckTradeQuantityDraft()
    {
        Check(TradeQuantityDraft.TryParse("0",10,out var q)&&q==0,"Empty quantity is an explicit zero draft");
        Check(TradeQuantityDraft.TryParse("10",10,out q)&&q==10,"Entire available quantity accepted");
        Check(TradeQuantityDraft.TryParse(int.MaxValue.ToString(),int.MaxValue,out q)&&q==int.MaxValue,"Maximum count has no overflow");
        foreach(var text in new[]{"", "-1", "11", "2147483648", "1.5", " 1", "+1", "abc"})
            Check(!TradeQuantityDraft.TryParse(text,10,out q),"Invalid draft never becomes a clamped offer: "+text);
        Check(!TradeQuantityDraft.TryParse("0",-1,out q),"Invalid availability cannot authorize a quantity");
    }
}
