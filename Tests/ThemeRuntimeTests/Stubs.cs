using System;
namespace UnityEngine
{
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1f){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color white=>new Color(1,1,1); public static Color clear=>new Color(0,0,0,0);
    }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
        public float yMax=>y+height;
        public Rect ContractedBy(float amount)=>new Rect(x+amount,y+amount,width-amount*2,height-amount*2);
    }
    public enum TextAnchor { UpperLeft, MiddleCenter }
    public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b); public static float Clamp01(float v)=>Clamp(v,0,1); public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v)); }
    public static class GUI { public static Color color=Color.white,backgroundColor=Color.white,contentColor=Color.white;public static bool enabled=true; public static void SetNextControlName(string name){} public static string GetNameOfFocusedControl()=>""; }
    public static class Input { public static bool GetMouseButton(int button)=>false; }
}
namespace Verse
{
    public sealed class TabRecord { }
    public static class TabDrawer { public static float TabHeight=>32; public static void DrawTabsOverflow(UnityEngine.Rect rect,System.Collections.Generic.List<TabRecord> tabs,float min,float max){} }
    public static class Mouse { public static bool IsOver(UnityEngine.Rect r)=>false; }
    public static class TooltipHandler { public static void TipRegion(UnityEngine.Rect r,string s){} }
    public enum GameFont { Small, Tiny }
    public static class Text { public static GameFont Font;public static UnityEngine.TextAnchor Anchor;public static bool WordWrap=true; }
    public static class Widgets
    {
        public static int Clicks; public static bool Throw;
        public static void DrawBoxSolid(UnityEngine.Rect r,UnityEngine.Color c){}
        public static void Label(UnityEngine.Rect r,string s){}
        public static void LabelEllipses(UnityEngine.Rect r,string s){}
        public static void DrawBox(UnityEngine.Rect r,int thickness=1){}
        public static bool RadioButtonLabeled(UnityEngine.Rect r,string s,bool selected)=>false;
        public static bool ButtonInvisible(UnityEngine.Rect r)=>false;
        public static string TextField(UnityEngine.Rect r,string s)=>s;
        public static string TextField(UnityEngine.Rect r,string s,int maxLength,System.Text.RegularExpressions.Regex inputValidator=null)=>s;
        public static float HorizontalSlider(UnityEngine.Rect r,float value,float min,float max)=>value;
        public static void CheckboxLabeled(UnityEngine.Rect r,string s,ref bool value,bool disabled=false){}
        public static bool ButtonText(UnityEngine.Rect r,string s,bool drawBackground=true,bool doMouseoverSound=true,bool active=true){if(Throw)throw new InvalidOperationException();Clicks++;return UnityEngine.GUI.enabled && active;}
    }
}
namespace PhinixClient { internal sealed class ThemeSource { internal string Id,Path,Source;internal bool Migrate; } }
