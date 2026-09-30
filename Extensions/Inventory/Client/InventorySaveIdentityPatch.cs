using System;
using System.IO;
using HarmonyLib;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    /// <summary>RimWorld does not retain the loaded save file name on GameInfo.</summary>
    internal static class InventorySaveIdentityPatch
    {
        public static string CurrentPath { get; private set; }

        public static void Clear() { CurrentPath = null; }

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
            private static void Prefix(string saveFileName) { Capture(saveFileName); }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader), "LoadGame", new[] { typeof(FileInfo) })]
        private static class LoadByFile
        {
            private static void Prefix(FileInfo saveFile)
            {
                if (saveFile != null) CurrentPath = saveFile.FullName;
            }
        }

        [HarmonyPatch(typeof(GameDataSaveLoader), "SaveGame", new[] { typeof(string) })]
        private static class SaveByName
        {
            private static void Prefix(string fileName)
            {
                CurrentPath = null;
                Capture(fileName);
            }
            private static void Postfix() { BuiltInInventoryClientExtension.RefreshAfterSave(); }
        }
    }
}
