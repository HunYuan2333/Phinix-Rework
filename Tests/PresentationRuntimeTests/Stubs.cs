using System;
namespace UnityEngine
{
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1f){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color gray=>new Color(.5f,.5f,.5f); public static Color white=>new Color(1,1,1); public static Color clear=>new Color(0,0,0,0);
    }
    public struct Rect
    {
        public float x,y,width,height;
        public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
        public float yMin=>y; public float xMin=>x; public float yMax=>y+height; public float xMax=>x+width;
        public Rect ContractedBy(float amount)=>new Rect(x+amount,y+amount,width-amount*2,height-amount*2);
    }
    public enum TextAnchor { UpperLeft, MiddleLeft, MiddleCenter }
    public static class Mathf { public static float Min(float a,float b)=>Math.Min(a,b); public static float Max(float a,float b)=>Math.Max(a,b); public static float Clamp01(float v)=>Clamp(v,0,1); public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v)); }
    public static class GUI { public static Color color=Color.white,backgroundColor=Color.white,contentColor=Color.white;public static bool enabled=true; public static void SetNextControlName(string name){} public static string FocusedControl=""; public static string GetNameOfFocusedControl()=>FocusedControl; private static readonly System.Collections.Generic.Stack<Vector2> Groups = new System.Collections.Generic.Stack<Vector2>();
        public static int GroupDepth => Groups.Count; public static float GroupX, GroupY;
        public static void BeginGroup(Rect r){Groups.Push(new Vector2(GroupX,GroupY));GroupX+=r.x;GroupY+=r.y;}
        public static void EndGroup(){var previous=Groups.Pop();GroupX=previous.x;GroupY=previous.y;} public static void FocusControl(string name){FocusedControl=name;} public static readonly System.Collections.Generic.List<Color> SurfaceColors=new System.Collections.Generic.List<Color>(); public static int SurfaceCalls; public static bool ThrowIcon; public static int IconCalls; public static Rect LastIconRect; public static void DrawTexture(Rect rect,Texture texture){IconCalls++;LastIconRect=rect;if(ThrowIcon)throw new InvalidOperationException("GPU");} public static void DrawTexture(Rect r,Texture2D t,ScaleMode m,bool alpha,float aspect,Color c,Vector4 border,Vector4 radius){SurfaceCalls++;SurfaceColors.Add(c);} }
    public static class Input { public static bool GetMouseButton(int button)=>false; public static string compositionString=""; }
}
namespace Verse
{
    public sealed class TabRecord { }
    public static class TabDrawer { public static float TabHeight=>32; public static void DrawTabsOverflow(UnityEngine.Rect rect,System.Collections.Generic.List<TabRecord> tabs,float min,float max){} }
    public static class Mouse { public static bool IsOver(UnityEngine.Rect r)=>false; }
    public static class TooltipHandler { public static void TipRegion(UnityEngine.Rect r,string s){} }
    public enum GameFont { Small, Tiny }
    public static class Text { public static GameFont Font;public static UnityEngine.TextAnchor Anchor;public static bool WordWrap=true; public static bool MeasureWrapping; public static float CalcHeight(string s,float width)=>MeasureWrapping && WordWrap ? 24 * Math.Max(1,(int)Math.Ceiling(s.Length*8f/Math.Max(1,width))) : 24; public static UnityEngine.Vector2 CalcSize(string s)=>new UnityEngine.Vector2(s.Length*8,24); }
    public static class Widgets
    {
        public static int Clicks; public static bool Throw; public static string ClickLabel, FieldValue; public static bool ConsumeFieldEvent;
        public static readonly System.Collections.Generic.List<float> ScrollStarts = new System.Collections.Generic.List<float>();
        public static readonly System.Collections.Generic.List<float> ScrollWorldY = new System.Collections.Generic.List<float>();
        public static float? NextScrollY;
        public static void BeginScrollView(UnityEngine.Rect r, ref UnityEngine.Vector2 s, UnityEngine.Rect v){ScrollStarts.Add(s.y);ScrollWorldY.Add(r.y + UnityEngine.GUI.GroupY);if(NextScrollY.HasValue){s.y=NextScrollY.Value;NextScrollY=null;}} public static void EndScrollView(){}
        public static readonly System.Collections.Generic.List<(UnityEngine.Rect Rect, UnityEngine.Color Color, int GroupDepth, bool Enabled)> FilledBoxes = new System.Collections.Generic.List<(UnityEngine.Rect, UnityEngine.Color, int, bool)>();
        public static void DrawBoxSolid(UnityEngine.Rect r,UnityEngine.Color c){FilledBoxes.Add((r,c,UnityEngine.GUI.GroupDepth,UnityEngine.GUI.enabled));}
        public static bool ThrowFittedIcon; public static int FittedIcons; public static void DrawTextureFitted(UnityEngine.Rect r,UnityEngine.Texture t,float scale){FittedIcons++;if(ThrowFittedIcon)throw new InvalidOperationException("GPU");}
        public static readonly System.Collections.Generic.List<(string Text, GameFont Font, UnityEngine.Rect Rect)> Labels = new System.Collections.Generic.List<(string, GameFont, UnityEngine.Rect)>(); public static int EllipsisCalls; public static UnityEngine.Rect LastFieldRect; public static string LastLabel; public static void Label(UnityEngine.Rect r,string s){LastLabel=s;Labels.Add((s,Text.Font,r));}
        public static void LabelEllipses(UnityEngine.Rect r,string s){EllipsisCalls++;}
        public static void DrawBox(UnityEngine.Rect r,int thickness=1){}
        public static bool RadioButtonLabeled(UnityEngine.Rect r,string s,bool selected)=>false;
        public static UnityEngine.Rect LastInvisibleRect; public static int InvisibleClicks; public static bool ButtonInvisible(UnityEngine.Rect r){LastInvisibleRect=r;if(InvisibleClicks<=0)return false;InvisibleClicks--;return UnityEngine.GUI.enabled;}
        public static string TextField(UnityEngine.Rect r,string s)=>s;
        public static System.Text.RegularExpressions.Regex LastValidator; public static string TextField(UnityEngine.Rect r,string s,int maxLength,System.Text.RegularExpressions.Regex inputValidator=null){LastFieldRect=r;LastValidator=inputValidator;var value=FieldValue;FieldValue=null;if(ConsumeFieldEvent){ConsumeFieldEvent=false;UnityEngine.Event.current.Use();}return value != null && (inputValidator == null || inputValidator.IsMatch(value)) ? value : s;}
        public static float? SliderValue; public static UnityEngine.Rect LastSliderRect;
        public static float HorizontalSlider(UnityEngine.Rect r,float value,float min,float max){LastSliderRect=r;float next=SliderValue??value;SliderValue=null;return next;}
        public static void CheckboxLabeled(UnityEngine.Rect r,string s,ref bool value,bool disabled=false){}
        public static bool ButtonText(UnityEngine.Rect r,string s,bool drawBackground=true,bool doMouseoverSound=true,bool active=true){if(Throw)throw new InvalidOperationException();Clicks++;if(ClickLabel!=(s=="" ? LastLabel : s))return false;ClickLabel=null;return UnityEngine.GUI.enabled && active;}
    }
}


namespace UnityEngine
{
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public enum EventType { Layout, Repaint, MouseDown, KeyDown, Used }
    public sealed class Event { public static Event current=new Event(); public EventType type; public KeyCode keyCode; public void Use(){type=EventType.Used;} }
}
namespace Verse
{
    public class Window { public bool doCloseX,closeOnAccept,absorbInputAroundWindow,draggable,resizeable,forcePause; public bool IsOpen; public UnityEngine.Rect windowRect;
        public virtual UnityEngine.Vector2 InitialSize=>new UnityEngine.Vector2(); public virtual void PreOpen(){IsOpen=true;} public virtual void WindowUpdate(){} public virtual void PostClose(){} public virtual void DoWindowContents(UnityEngine.Rect r){} public void Close(){IsOpen=false;PostClose();} }
    public static class Find { public static Stack WindowStack=new Stack(); }
    public sealed class Stack { public Window Last, currentlyDrawnWindow; public FloatMenu FloatMenu; public bool GetsInput(Window w)=>true; public void Add(Window w){Last=w;} }
    public static class TranslateStub { public static int Calls; public static string Translate(this string s){Calls++;return s;} public static string Translate(this string s,object argument){Calls++;return s;} }
}
namespace Utils.Framework { public interface IPhinixExtensionModule { string ExtensionId { get; } } }
namespace PhinixClient
{
    public static class UiScreenSafeArea { public static UnityEngine.Rect Normalize(UnityEngine.Rect r)=>new UnityEngine.Rect(r.x,r.y,Math.Max(0,r.width),Math.Max(0,r.height)); public static UnityEngine.Rect Current=>new UnityEngine.Rect(0,0,1000,800); public static UnityEngine.Rect ClampWindow(UnityEngine.Rect r,UnityEngine.Vector2 min)=>r; }
}
namespace PhinixClient.Framework
{
    public interface IClientLifetimeService { IClientLifetimeScope ForModule(Utils.Framework.IPhinixExtensionModule module); }
    public interface IClientLifetimeScope : System.IDisposable { string OwnerId { get; } bool IsClosed { get; } void Own(System.IDisposable resource); }
    public interface IClientContributedAction { string OwnerId=>"test"; string Id=>Label; string Label { get; } string DisabledReason { get; } ClientActionExecutionResult TryExecute(); }
    public interface IClientActionSet : System.IDisposable { System.Collections.Generic.IReadOnlyList<IClientContributedAction> Actions { get; } }
    public sealed class ClientActionExecutionResult { public bool Completed { get; set; } public string Code { get; set; } }
}

namespace UnityEngine { public class Texture {} public sealed class Texture2D : Texture { public static Texture2D whiteTexture=new Texture2D(); } public enum KeyCode { Return,KeypadEnter } public enum FocusType { Passive } public static class GUIUtility { public static int GetControlID(int hint,FocusType focus)=>hint; } }
namespace Verse { public class FloatMenu : Window { public readonly System.Collections.Generic.List<FloatMenuOption> Options; public FloatMenu(System.Collections.Generic.List<FloatMenuOption> options){Options=options;} } public class FloatMenuOption { public readonly System.Action Action; public readonly string Label; public FloatMenuOption(string label,System.Action action){Label=label;Action=action;} } }

namespace UnityEngine
{
    public enum ScaleMode { StretchToFill }
    public struct Vector4 { public float x,y,z,w; public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;} public static Vector4 zero=>new Vector4(); }
}

namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }

namespace Verse { public static class Prefs { public static bool DevMode=true; } public static class LanguageDatabase { public static object activeLanguage=new object(); } }
namespace PhinixClient.Framework
{
    public interface IClientExtensionPointService { long Revision {get;} IClientExtensionPointScope ForModule(Utils.Framework.IPhinixExtensionModule module)=>throw new System.NotSupportedException(); }
    public interface IClientExtensionPointScope : System.IDisposable
    {IClientActionPoint<T> DefineActions<T>(string id) where T:class;void ContributeActions<T>(string id,System.Func<T,System.Collections.Generic.IEnumerable<ClientActionDefinition>> factory,float order=0) where T:class;}
    public interface IClientActionPoint<T> where T:class { IClientActionSet Collect(T context,Func<bool> current=null); }
    public sealed class ClientActionDefinition { public readonly string Id,Label,DisabledReason;public readonly Action Execute;
        public ClientActionDefinition(string id,string label,Action execute,string disabledReason=null){Id=id;Label=label;Execute=execute;DisabledReason=disabledReason;} }
    public static class ClientEnvironmentPaths { public static string NormalizeAbsolute(string path)=>System.IO.Path.GetFullPath(path); }
}

namespace Verse {public static class Translator {public static bool CanTranslate(string key)=>false;}}
