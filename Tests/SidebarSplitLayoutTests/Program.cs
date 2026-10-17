using System;
using PhinixClient;
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void Main()
    {
        float width;
        Check(SidebarSplitLayout.TryWidth(1200,270,120,0,out width)&&width==270,"Default provider width");
        var ratio=SidebarSplitLayout.RatioForWidth(1200,120,400);
        Check(SidebarSplitLayout.TryWidth(1200,270,120,ratio,out width)&&Math.Abs(width-400)<.01,"Drag position round trip");
        Check(SidebarSplitLayout.TryWidth(1600,270,120,ratio,out width)&&Math.Abs(width-1592*ratio)<.01,"Window resize preserves fraction");
        Check(SidebarSplitLayout.TryWidth(800,270,120,ratio,out width)&&width<=312,"Main minimum survives shrinking");
        Check(SidebarSplitLayout.TryWidth(1200,270,120,ratio,out width)&&Math.Abs(width-400)<.01,"Temporary clamp does not replace preference");
        foreach(float desired in new[]{-1000f,0f,10000f})
        {
            var limited=SidebarSplitLayout.RatioForWidth(1200,200,desired);
            Check(SidebarSplitLayout.TryWidth(1200,270,200,limited,out width)&&width>=200&&width<=712,"Drag cannot crush either pane");
        }
        Check(!SidebarSplitLayout.TryWidth(687,270,200,ratio,out width)&&width==0,"Too narrow uses drawer");
        Check(SidebarSplitLayout.TryWidth(688,270,200,ratio,out width)&&width==200,"Exact minimum split");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-1f,2f})
            Check(SidebarSplitLayout.TryWidth(1200,270,120,invalid,out width)&&width==270,"Invalid saved fraction falls back");
        Check(!SidebarSplitLayout.TryWidth(float.NaN,270,120,0,out width),"Nonfinite viewport rejected");
        Check(SidebarSplitLayout.TryWidth(1200,float.NaN,float.NaN,0,out width)&&width==270,"Invalid hints fall back");
        Console.WriteLine("Sidebar split geometry: "+checks+" checks passed (no native pointer/UI acceptance).");
    }
}
