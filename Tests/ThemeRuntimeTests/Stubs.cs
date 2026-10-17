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
        public float yMax=>y+height; public float xMax=>x+width;
        public Rect ContractedBy(float amount)=>new Rect(x+amount,y+amount,width-amount*2,height-amount*2);
    }
    public enum TextAnchor { UpperLeft, MiddleLeft, MiddleCenter }
    public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b); public static float Clamp01(float v)=>Clamp(v,0,1); public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v)); }
    public static class GUI { public static Color color=Color.white,backgroundColor=Color.white,contentColor=Color.white;public static bool enabled=true; public static void BeginGroup(Rect r){} public static void EndGroup(){} public static void SetNextControlName(string name){} public static string GetNameOfFocusedControl()=>""; public static void DrawTexture(Rect r,Texture t){} public static void DrawTexture(Rect r,Texture2D t,ScaleMode m,bool alpha,float aspect,Color c,Vector4 border,Vector4 radius){} }
    public static class Input { public static bool GetMouseButton(int button)=>false; }
}
namespace Verse
{
    public sealed class TabRecord { }
    public static class TabDrawer { public static float TabHeight=>32; public static void DrawTabsOverflow(UnityEngine.Rect rect,System.Collections.Generic.List<TabRecord> tabs,float min,float max){} }
    public static class Mouse { public static bool IsOver(UnityEngine.Rect r)=>false; }
    public static class TooltipHandler { public static void TipRegion(UnityEngine.Rect r,string s){} }
    public enum GameFont { Small, Tiny }
    public static class Text { public static float CalcHeight(string text,float width)=>24; public static GameFont Font;public static UnityEngine.TextAnchor Anchor;public static bool WordWrap=true; public static UnityEngine.Vector2 CalcSize(string s)=>new UnityEngine.Vector2(s.Length*8,24); }
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
namespace PhinixClient { internal sealed class ThemeSource { internal string Id,Path,Source,Name,Author;internal byte[] Document;internal bool Migrate; } }

namespace PhinixClient { internal static class Client { public const string PackageId="hunyuan2333.phinixrework"; public const string CompatibilityVersion="0.9.7"; } }

namespace UnityEngine
{
    public enum ScaleMode { StretchToFill }
    public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;} public static Vector4 zero=>new Vector4(); }
}

namespace UnityEngine { public class Texture {} public sealed class Texture2D : Texture {public static Texture2D whiteTexture=new Texture2D();} public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}} }

// Type marker lets source-linked reflective adapters discover this fixture's contract assembly.
namespace PhinixClient.Framework { public interface IClientSessionContext {} }

// Current native-window and drawing seams. Theme tests never execute an engine backend.
namespace UnityEngine { public sealed class GUIStyle {} }
namespace Verse
{
    public interface IWindowDrawing
    {
        UnityEngine.GUIStyle EmptyStyle { get; }
        void DoWindowBackground(UnityEngine.Rect r); bool DoCloseButton(UnityEngine.Rect r,string text);
        bool DoClostButtonSmall(UnityEngine.Rect r); void BeginGroup(UnityEngine.Rect r); void EndGroup(); void DoGrayOut(UnityEngine.Rect r);
    }
    public sealed class DefaultWindowDrawing : IWindowDrawing
    {
        public UnityEngine.GUIStyle EmptyStyle=>new UnityEngine.GUIStyle();
        public void DoWindowBackground(UnityEngine.Rect r){} public bool DoCloseButton(UnityEngine.Rect r,string text)=>false;
        public bool DoClostButtonSmall(UnityEngine.Rect r)=>false; public void BeginGroup(UnityEngine.Rect r){} public void EndGroup(){} public void DoGrayOut(UnityEngine.Rect r){}
    }
}
namespace PhinixClient.Framework
{
    internal static class ClientGameDrawing
    {
        internal static void RoundedSurface(UnityEngine.Rect r,UnityEngine.Color c,float radius){}
        internal static void RoundedBorder(UnityEngine.Rect r,UnityEngine.Color c,float radius,float thickness){}
        internal static void Scrollbars(UnityEngine.Rect r,UnityEngine.Vector2 s,UnityEngine.Rect content,UnityEngine.Color track,UnityEngine.Color thumb,UnityEngine.Color hover){}
    }
}
namespace UnityEngine { public enum EventType { Repaint } public sealed class Event { public static Event current; public EventType type; } }
