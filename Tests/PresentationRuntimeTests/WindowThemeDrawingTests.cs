using PhinixClient.Framework;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static void CheckWindowThemeDrawing()
    {
        int captures=0;
        var theme=new Theme();
        var drawing=new ClientThemeWindowDrawing(()=>{captures++;return theme.Capture();});
        var rect=new Rect(0,0,620,500);
        var prior=GUI.color; GUI.color=new Color(.2f,.3f,.4f);
        int native=DefaultWindowDrawing.Backgrounds;
        GUI.SurfaceRects.Clear(); GUI.SurfaceColors.Clear();
        drawing.DoWindowBackground(rect);
        Check(captures==1 && DefaultWindowDrawing.Backgrounds==native,"Window background replaces the native rectangle through the per-window hook");
        Check(GUI.SurfaceRects.Count>0 && GUI.SurfaceRects[0].width==620 && GUI.SurfaceRects[0].height==500 && GUI.SurfaceRects[0].x==0,"Theme background covers full window before the native 18-unit content inset");
        Check(GUI.color.r==.2f && GUI.color.g==.3f && GUI.color.b==.4f,"Window background preserves caller GUI tint");
        theme.Change();drawing.DoWindowBackground(rect);
        Check(captures==2,"Open windows capture the active palette again after hot application");
        int closes=DefaultWindowDrawing.SmallClose;
        Check(drawing.DoClostButtonSmall(rect) && DefaultWindowDrawing.SmallClose==closes+1,"Native close hit testing remains delegated");
        int depth=GUI.GroupDepth;drawing.BeginGroup(new Rect(18,18,584,464));drawing.EndGroup();
        Check(GUI.GroupDepth==depth,"Native content group coordinates remain paired");
        new ClientThemeWindowDrawing(()=>null).DoWindowBackground(rect);
        Check(DefaultWindowDrawing.Backgrounds==native+1,"Absent optional theme preserves native window drawing");
        GUI.color=prior;
    }
}
