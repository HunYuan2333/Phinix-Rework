// Only peripheral contracts/game entry are doubled. Error service, notification controller,
// dispatcher, public task/error contracts and RootPatch are the production source files.
using System;
using PhinixClient.Framework;

namespace Utils { public enum LogLevel { DEBUG, INFO, WARNING, ERROR, FATAL } }
namespace Utils.Framework
{
    public sealed class PhinixExtensionAttribute : Attribute
    {
        public string ExtensionId { get; }
        public PhinixExtensionAttribute(string id) { ExtensionId = id; }
    }
    public sealed class HostLogEntry
    {
        public HostLogEntry(string owner, Utils.LogLevel level, string message, Exception error = null, string correlationId = null)
        { ExtensionId = owner; Level = level; Message = message; Exception = error; CorrelationId = correlationId; }
        public string CorrelationId { get; }
        public string ExtensionId { get; }
        public Utils.LogLevel Level { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public string ToDisplayMessage() => Message + "\n" + Exception;
    }
    public sealed class ExtensionHostContext
    {
        public Action<HostLogEntry> StructuredLog;
        public Action<string,Utils.LogLevel> Log;
    }
    public interface IPhinixExtensionModule { string ExtensionId { get; } }
    public interface IExtensionModuleLifecycleObserver
    {
        void OnActivating(IPhinixExtensionModule module);
        void OnStopped(IPhinixExtensionModule module);
    }
}
namespace PhinixClient.Framework
{
    public interface IClientUserEventStream { }
    public interface IClientShellEventStream { }
    public sealed class UserBlockStateChangedEventArgs : EventArgs { }
    public interface IClientMainThreadDispatcher { void Enqueue(Action action); }
    public interface IClientDispatcherDiagnostics { int PendingCount { get; } long DroppedCount { get; } }
}
namespace UserManagement { public sealed class UserDisplayNameChangedEventArgs : EventArgs { } }
namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public sealed class HarmonyPostfix : Attribute { }
}
namespace Verse
{
    public static class Log { public static void Error(string value) { } }
    public class Root { public void Update() { } }
    public class Window
    {
        public bool doCloseX, closeOnAccept, closeOnCancel, closeOnClickedOutside, absorbInputAroundWindow, draggable, resizeable;
        public bool IsOpen = true;
        public virtual UnityEngine.Vector2 InitialSize => new UnityEngine.Vector2(100, 100);
        public virtual void DoWindowContents(UnityEngine.Rect rect) { }
        public void Close() { IsOpen = false; }
    }
    public enum GameFont { Small, Medium }
    public static class Text
    {
        public static GameFont Font;
        public static UnityEngine.TextAnchor Anchor;
        public static bool WordWrap;
        public static float CalcHeight(string text, float width)
            => Math.Max(1, (float)Math.Ceiling(text.Length / Math.Max(1, width / 10))) * 24;
    }
    public static class Translation
    {
        public static string Translate(this string key) => key;
    }
    public static class Widgets
    {
        public sealed class Button { public UnityEngine.Rect Rect; public string Label; }
        public static readonly System.Collections.Generic.List<Button> Buttons = new System.Collections.Generic.List<Button>();
        public static string Click;
        public static bool ThrowLabel;
        public static int ScrollDepth;
        public static UnityEngine.Rect LastViewport, LastContent;
        public static void Label(UnityEngine.Rect rect, string text) { if (ThrowLabel) throw new Exception("broken native label"); }
        public static bool ButtonText(UnityEngine.Rect rect, string text)
        {
            Buttons.Add(new Button { Rect = rect, Label = text });
            if (Click == text) { Click = null; return true; } return false;
        }
        public static void BeginScrollView(UnityEngine.Rect viewport, ref UnityEngine.Vector2 position, UnityEngine.Rect content)
        { LastViewport = viewport; LastContent = content; ScrollDepth++; }
        public static void EndScrollView() { ScrollDepth--; }
    }
}
namespace UnityEngine
{
    public struct Color { public static Color white => new Color(); }
    public static class GUI { public static bool enabled; public static Color color, backgroundColor, contentColor; }
    public enum TextAnchor { UpperLeft }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Rect
    {
        public float x, y, width, height;
        public float xMax => x + width;
        public float yMax => y + height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public static class Mathf { public static float Min(float a, float b) => Math.Min(a, b); public static float Max(float a, float b) => Math.Max(a, b); }
    public static class GUIUtility { public static string systemCopyBuffer; }
}
namespace PhinixClient.Framework
{
    public struct ClientGuiScope : IDisposable { public ClientGuiScope(bool capture) { } public void Dispose() { } }
    public static class UiScreenSafeArea { public static UnityEngine.Rect Current => new UnityEngine.Rect(0, 0, 800, 600); }
}
namespace PhinixClient
{
    public sealed class Client
    {
        public static Client Instance;
        internal ClientErrorService Errors;
        internal Action Work;
        internal bool ThrowWhileReporting;
        public void Update() { Work(); }
        internal void ReportUpdateFailure(Exception error)
        {
            if (ThrowWhileReporting) throw new Exception("broken reporter");
            Errors.ReportHost(error, "HostUpdateFailed", "HostUpdate", true, false);
        }
    }
}
