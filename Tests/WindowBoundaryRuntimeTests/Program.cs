using System;
using System.Collections.Generic;
using PhinixClient.Framework;

namespace Verse
{
    public class Window { public bool IsOpen; }
    public static class UnityData { public static bool IsInMainThread = true; }
    public sealed class WindowStack
    {
        public readonly List<Window> Added = new List<Window>();
        public bool Fail;
        public void Add(Window window)
        {
            if (Fail) throw new InvalidOperationException("stack failure");
            Added.Add(window); window.IsOpen = true;
        }
    }
    public static class Find { public static readonly WindowStack WindowStack = new WindowStack(); }
}
namespace PhinixClient.Framework
{
    // Ordinary contract has no native parameters; its actual signature is also
    // checked by the real-assembly PresentationValues probe in both builds.
    public interface IClientSettingsWindowService { void OpenSettingsWindow(); }
    public sealed class SettingsWindow : Verse.Window { public static int Created; public SettingsWindow() { Created++; } }
    public sealed class ExtensionManagerWindow : Verse.Window { }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { checks++; return; } throw new Exception("Expected rejection"); }
    private static void Main()
    {
        var service = new ClientWindowService();
        var owned = new Verse.Window();
        service.Open(owned);
        Check(ReferenceEquals(Verse.Find.WindowStack.Added[0], owned), "Exact owner window reaches the stack.");
        Verse.UnityData.IsInMainThread = false;
        service.Open(null);
        Check(Verse.Find.WindowStack.Added.Count == 1, "Null remains a no-op.");
        Reject(() => service.Open(new Verse.Window()));
        Reject(service.OpenSettingsWindow);
        Reject(service.OpenExtensionManagerWindow);
        Check(Verse.Find.WindowStack.Added.Count == 1 && SettingsWindow.Created == 0, "Off-thread calls create no settings window or stack effects.");
        Verse.UnityData.IsInMainThread = true;
        service.OpenSettingsWindow();
        Check(SettingsWindow.Created == 1 && Verse.Find.WindowStack.Added[1] is SettingsWindow, "Settings uses the same stack and separate ordinary entry.");
        service.OpenExtensionManagerWindow();
        var manager = Verse.Find.WindowStack.Added[2];
        service.OpenExtensionManagerWindow();
        Check(Verse.Find.WindowStack.Added.Count == 3, "Open management window is not duplicated.");
        manager.IsOpen = false;
        service.OpenExtensionManagerWindow();
        Check(Verse.Find.WindowStack.Added.Count == 4 && !ReferenceEquals(manager, Verse.Find.WindowStack.Added[3]), "Closed management window may reopen.");
        Verse.Find.WindowStack.Fail = true;
        Reject(() => service.Open(owned));
        Check(Verse.Find.WindowStack.Added.Count == 4, "Stack failure is propagated without invented success.");
        Verse.Find.WindowStack.Fail = false;
        service.Open(owned);
        Check(Verse.Find.WindowStack.Added.Count == 5 && ReferenceEquals(owned, Verse.Find.WindowStack.Added[4]), "Host does not assume ownership or suppress owner reuse.");
        Console.WriteLine("Window boundary checks passed: " + checks);
    }
}
