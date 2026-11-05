using System;
using System.IO;
using System.Threading;
using HarmonyLib;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    /// <summary>RimWorld does not retain the loaded save file name on GameInfo.</summary>
    internal static class InventorySaveIdentityPatch
    {
        private static readonly object pathGate=new object();
        private static string currentPath;
        private static int generation,loadPending;
        internal static bool ReceiveScopeSuspended => Volatile.Read(ref loadPending)!=0;
        internal static void CompleteLoad() { Interlocked.Exchange(ref loadPending,0); }
        private static void BeginLoad() { Interlocked.Exchange(ref loadPending,1);Interlocked.Increment(ref generation); }
        internal static int Generation => Volatile.Read(ref generation);
        public static string CurrentPath
        {
            get => Volatile.Read(ref currentPath);
            private set
            {
                lock(pathGate)
                {
                    if(string.Equals(currentPath,value,StringComparison.Ordinal)) return;
                    Volatile.Write(ref currentPath,value);
                    Interlocked.Increment(ref generation);
                }
            }
        }

        public static void Clear() { CurrentPath = null;Interlocked.Exchange(ref loadPending,0); }

        private static void Capture(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName) return;
            string gameName = fileName.EndsWith(".rws", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(fileName) : fileName;
            string candidate = Path.GetFullPath(GenFilePaths.FilePathForSavedGame(gameName));
            if (CurrentPath != null && string.Equals(Path.GetFileName(CurrentPath), Path.GetFileName(candidate),
                StringComparison.OrdinalIgnoreCase) && File.Exists(CurrentPath)) return;
            CurrentPath = candidate;
        }

        [HarmonyPatch(typeof(GameDataSaveLoader), "LoadGame", new[] { typeof(string) })]
        private static class LoadByName
        {
            private static void Prefix(string saveFileName) { BeginLoad();Capture(saveFileName); }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader), "LoadGame", new[] { typeof(FileInfo) })]
        private static class LoadByFile
        {
            private static void Prefix(FileInfo saveFile)
            {
                BeginLoad();
                if (saveFile != null) CurrentPath = saveFile.FullName;
            }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader), "SaveGame", new[] { typeof(string) })]
        private static class SaveByName
        {
            private static void Prefix(string fileName)
            {
                // Preserve same-path autosave scope. Save As still changes path generation.
                Capture(fileName);
            }
            private static void Postfix() { BuiltInInventoryClientExtension.RefreshAfterSave(); }
        }
    }
}
