using System;
using HarmonyLib;
using Utils.Framework;
using Verse;

namespace Phinix.InventoryExtension.Client
{
    /// <summary>Snapshot belongs to the RimWorld save; journal only recovers its later local transactions.</summary>
    public sealed class InventoryGameComponent : GameComponent
    {
        private string saveId;
        private string snapshotJson;
        internal bool Initialized { get; private set; }

        public static InventoryGameComponent Current { get; private set; }
        public string SaveId => saveId;

        public InventoryGameComponent(Game game) : base()
        {
            Current = this;
        }

        // Membership follows the game's component list, including Scribe-restored instances.
        internal static bool BelongsTo(InventoryGameComponent component, Game game)
        {
            return component != null && game != null && game.components != null && game.components.Contains(component);
        }

        internal static InventoryGameComponent EnsureFor(Game game)
        {
            if (game == null || game.components == null) return null;

            foreach (GameComponent component in game.components)
            {
                InventoryGameComponent inventory = component as InventoryGameComponent;
                if (inventory == null) continue;
                Current = inventory;
                return inventory;
            }

            InventoryGameComponent created = new InventoryGameComponent(game);
            game.components.Add(created);
            return created;
        }

        public override void FinalizeInit()
        {
            if (string.IsNullOrEmpty(saveId)) saveId = Guid.NewGuid().ToString("N");
            Current = this;
            Initialized = true;
            BuiltInInventoryClientExtension.AttachSave(this);
        }

        public override void StartedNewGame()
        {
            InventorySaveIdentityPatch.Clear();
            BuiltInInventoryClientExtension.AttachSave(this);
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && BuiltInInventoryClientExtension.IsAttached(this))
                snapshotJson = BuiltInInventoryClientExtension.ExportSnapshot();
            Scribe_Values.Look(ref saveId, "phinixInventorySaveId", null);
            Scribe_Values.Look(ref snapshotJson, "phinixInventorySnapshot", null);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager != null && Find.TickManager.TicksGame % 250 == 0)
                BuiltInInventoryClientExtension.TickSchedules();
        }

        internal InventoryState ReadSnapshot()
        {
            if (string.IsNullOrEmpty(snapshotJson)) return new InventoryState();
            return FrameworkSerialization.DeserializePayload<InventoryState>(snapshotJson);
        }
    }

    /// <summary>
    /// Common/Extensions assemblies are loaded after RimWorld may have populated
    /// its GameComponent type cache. Ensure the dynamically loaded inventory
    /// component participates in save/load even when automatic discovery missed it.
    /// </summary>
    [HarmonyPatch(typeof(Game), "FillComponents")]
    internal static class InventoryGameComponentAttachmentPatch
    {
        private static void Postfix(Game __instance)
        {
            InventoryGameComponent.EnsureFor(__instance);
        }
    }

    /// <summary>
    /// Scribe resolves Class names through GenTypes, whose active-mod assembly
    /// list excludes assemblies dynamically loaded from Common/Extensions.
    /// FillComponents attachment alone cannot restore an existing XML node.
    /// Resolve only this plugin's exact persisted type; leave all other lookups alone.
    /// </summary>
    [HarmonyPatch(typeof(GenTypes), nameof(GenTypes.GetTypeInAnyAssembly), new[] { typeof(string), typeof(string) })]
    internal static class InventoryGameComponentTypeResolutionPatch
    {
        internal static bool Prefix(string typeName, ref Type __result)
        {
            if (!string.Equals(typeName, typeof(InventoryGameComponent).FullName, StringComparison.Ordinal)) return true;
            __result = typeof(InventoryGameComponent);
            return false;
        }
    }

    // Owned by Inventory's ordinary activation/shutdown Harmony lease.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    internal static class InventoryGameLifetimePatch
    {
        private static void Postfix() { BuiltInInventoryClientExtension.ObserveGameLifetime(); }
    }
}
