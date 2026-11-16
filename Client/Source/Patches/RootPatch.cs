using System;
using HarmonyLib;
using Verse;

namespace PhinixClient.Patches
{
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal class RootPatch
    {
        [HarmonyPostfix]
        private static void Update()
        {
            try { Client.Instance?.Update(); }
            catch (Exception error)
            {
                try { Client.Instance?.ReportUpdateFailure(error); }
                catch { /* Reporting must never replace the exception being handed to the game. */ }
                throw;
            }
        }
    }
}
