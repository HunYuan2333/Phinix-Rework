using System;
using PhinixClient;
namespace UnityEngine
{
    public sealed class Texture2D { }
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public enum CursorMode { Auto }
    public static class Cursor
    {
        public static int Sets; public static Texture2D Texture; public static Vector2 Hotspot;
        public static void SetCursor(Texture2D texture,Vector2 hotspot,CursorMode mode){Sets++;Texture=texture;Hotspot=hotspot;}
    }
}
namespace Verse
{
    public static class ContentFinder<T> where T:class
    {
        public static T Value; public static int Loads;
        public static T Get(string path,bool fail){if(path!="UI/Phinix/ResizeHorizontal")throw new Exception("Unexpected cursor asset");Loads++;return Value;}
    }
    public static class Log { public static int Warnings; public static void Warning(string s){Warnings++;} }
    public static class Prefs { public static bool CustomCursorEnabled=true; }
    public static class CustomCursor
    {
        public static int Activations,Deactivations;
        public static void Activate(){Activations++;}
        public static void Deactivate(){Deactivations++;}
    }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Main()
    {
        var texture=new UnityEngine.Texture2D(); Verse.ContentFinder<UnityEngine.Texture2D>.Value=texture;
        var cursor=new SidebarResizeCursor();cursor.Release();
        Check(Verse.CustomCursor.Activations==0&&Verse.CustomCursor.Deactivations==0,"Unowned cursor is untouched");
        cursor.Update(true);cursor.Update(true);
        Check(UnityEngine.Cursor.Sets==1&&UnityEngine.Cursor.Texture==texture,"Hover acquires once, not per GUI event");
        Check(UnityEngine.Cursor.Hotspot.x==16&&UnityEngine.Cursor.Hotspot.y==16,"Hotspot matches the 32px symmetric asset");
        Check(Verse.ContentFinder<UnityEngine.Texture2D>.Loads==1,"Content texture borrowed once");
        cursor.Update(false);cursor.Release();
        Check(Verse.CustomCursor.Activations==1,"Leaving/close restores native game cursor exactly once");
        cursor.Update(true);Verse.Prefs.CustomCursorEnabled=false;cursor.Release();
        Check(Verse.CustomCursor.Deactivations==1,"Restore respects the current OS cursor preference");
        cursor.Update(true);cursor.Update(false);
        Check(UnityEngine.Cursor.Sets==3&&Verse.CustomCursor.Deactivations==2&&Verse.ContentFinder<UnityEngine.Texture2D>.Loads==1,"Reentry reuses asset and focus loss releases ownership");
        Verse.ContentFinder<UnityEngine.Texture2D>.Value=null;var missing=new SidebarResizeCursor();missing.Update(true);missing.Update(true);missing.Release();
        Check(UnityEngine.Cursor.Sets==3&&Verse.CustomCursor.Deactivations==2,"Missing asset leaves other cursor untouched");
        Check(Verse.Log.Warnings==1&&Verse.ContentFinder<UnityEngine.Texture2D>.Loads==2,"Missing asset is loaded/logged once");
        Console.WriteLine("Sidebar cursor ownership: "+checks+" checks passed (stub calls, not native cursor acceptance).");
    }
}
