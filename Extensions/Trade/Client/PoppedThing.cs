using System;
using System.Collections.Generic;
using Utils;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public sealed class PoppedThing
    {
        public Thing Thing { get; }

        public Map OriginMap { get; }

        public IntVec3 OriginPosition { get; }

        public Rot4 OriginRotation { get; }

        public bool WasSpawned { get; }

        internal PoppedThing(Thing thing, Map originMap, IntVec3 originPosition, Rot4 originRotation, bool wasSpawned)
        {
            Thing = thing;
            OriginMap = originMap;
            OriginPosition = originPosition;
            OriginRotation = originRotation;
            WasSpawned = wasSpawned;
        }

        public void DeSpawn()
        {
            if (Thing != null && !Thing.Destroyed && Thing.Spawned)
            {
                Thing.DeSpawn();
            }
        }

        public bool TryRestore()
        {
            if (Thing == null || Thing.Destroyed)
            {
                return false;
            }

            if (!WasSpawned || Thing.Spawned)
            {
                return true;
            }

            if (OriginMap == null || !OriginPosition.IsValid || !OriginPosition.InBounds(OriginMap))
            {
                return false;
            }

            return GenPlace.TryPlaceThing(
                Thing,
                OriginPosition,
                OriginMap,
                ThingPlaceMode.Near,
                rot: OriginRotation);
        }

        public static List<Thing> RestoreAll(
            IEnumerable<PoppedThing> poppedThings,
            Action<string, LogLevel> log,
            string context)
        {
            List<Thing> unrestoredThings = new List<Thing>();
            if (poppedThings == null)
            {
                return unrestoredThings;
            }

            foreach (PoppedThing poppedThing in poppedThings)
            {
                try
                {
                    if (poppedThing == null || !poppedThing.TryRestore())
                    {
                        if (poppedThing?.Thing != null && !poppedThing.Thing.Destroyed)
                        {
                            unrestoredThings.Add(poppedThing.Thing);
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (poppedThing?.Thing != null && !poppedThing.Thing.Destroyed)
                    {
                        unrestoredThings.Add(poppedThing.Thing);
                    }

                    log?.Invoke(
                        $"[{context}] Failed to restore a transferred thing.{Environment.NewLine}{exception}",
                        LogLevel.ERROR);
                }
            }

            return unrestoredThings;
        }
    }
}
