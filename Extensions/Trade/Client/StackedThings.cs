using System.Collections.Generic;
using System.Linq;
using PhinixClient.Trade;
using UnityEngine;
using Utils;
using Verse;

namespace Phinix.TradeExtension.Client
{
    public class StackedThings
    {
        public List<Thing> Things;

        public int Count
        {
            get
            {
                int totalCount = 0;
                for (int i = 0; i < Things.Count; i++)
                {
                    totalCount += Things[i].stackCount;
                }

                return totalCount;
            }
        }

        public string Label => Things.First().LabelCapNoCount;
        public ThingDef ThingDef => Things.First()?.def;
        public ThingDef StuffDef => Things.First()?.Stuff;
        public ThingStyleDef StyleDef => Things.First()?.StyleDef;

        public int Selected = 0;

        public StackedThings(IEnumerable<Thing> things)
        {
            this.Things = things.ToList();
        }

        /// <summary>
        /// Returns whether the given thing can stack with all things in the stack.
        /// </summary>
        /// <param name="thing">Thing to check</param>
        /// <returns>Whether the given thing can stack with all things in the stack</returns>
        public bool CanStack(Thing thing)
        {
            return Things.All(thing.CanStackWith);
        }

        /// <summary>
        /// Gets the selected amount of things as trade item snapshots.
        /// Does some hacky-feeling stuff to get just the right amount of stacks set in stone as serialisable trade items.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<TradeItemSnapshot> GetSelectedThingsAsTradeItems()
        {
            List<Thing> thingsToConvert = new List<Thing>();
            Thing thingToModify = null;

            int remainingThings = Selected;
            foreach (Thing thing in Things)
            {
                // Check if we have collected all necessary things
                if (remainingThings == 0) break;

                // Check if this thing has more in its stack than we need to take
                if (thing.stackCount > remainingThings)
                {
                    // Set this as the thing to modify and stop
                    remainingThings = 0;
                    thingToModify = thing;
                }
                else
                {
                    // Subtract this thing's stack size from the remaining count and add it to the conversion list
                    remainingThings -= thing.stackCount;
                    thingsToConvert.Add(thing);
                }
            }

            // Convert the readily-convertible things
            List<TradeItemSnapshot> convertedThings = thingsToConvert.Select(TradeItemConverter.ConvertThingFromVerse).ToList();

            // Check if we need to modify a thing to get the right amount
            if (thingToModify != null)
            {
                // Get the target stack size and difference from the current stack size
                int targetAmount = Selected - thingsToConvert.Sum(thing => thing.stackCount);
                int actualAmount = thingToModify.stackCount;

                // Set the stack size to the target amount
                thingToModify.stackCount = targetAmount;

                // Convert and add the modified thing
                convertedThings.Add(TradeItemConverter.ConvertThingFromVerse(thingToModify));

                // Set the stack size to what it was before
                thingToModify.stackCount = actualAmount;
            }

            // Return the list of converted trade item snapshots
            return convertedThings;
        }

        /// <summary>
        /// Removes the selected things from the stack and returns them.
        /// </summary>
        /// <returns>Selected things</returns>
        public IEnumerable<Thing> PopSelected()
        {
            return PopSelectedWithOrigins().Select(poppedThing => poppedThing.Thing);
        }

        public IEnumerable<PoppedThing> PopSelectedWithOrigins()
        {
            List<PoppedThing> poppedThings = new List<PoppedThing>();
            List<Thing> thingsToRemove = new List<Thing>();

            int remainingThings = Selected;
            try
            {
                foreach (Thing thing in Things)
                {
                    if (remainingThings == 0)
                    {
                        break;
                    }

                    Map originMap = thing.Map;
                    IntVec3 originPosition = thing.Position;
                    Rot4 originRotation = thing.Rotation;
                    bool wasSpawned = thing.Spawned;

                    if (thing.stackCount > remainingThings)
                    {
                        Thing splitThing = thing.SplitOff(remainingThings);
                        poppedThings.Add(new PoppedThing(splitThing, originMap, originPosition, originRotation, wasSpawned));
                        remainingThings = 0;
                    }
                    else
                    {
                        poppedThings.Add(new PoppedThing(thing, originMap, originPosition, originRotation, wasSpawned));
                        thingsToRemove.Add(thing);
                        remainingThings -= thing.stackCount;
                    }
                }
            }
            catch
            {
                PoppedThing.RestoreAll(poppedThings, null, nameof(StackedThings));
                throw;
            }

            Selected = 0;

            foreach (Thing thing in thingsToRemove)
            {
                Things.Remove(thing);
            }

            return poppedThings;
        }

        /// <summary>
        /// Deletes the selected amount of things from the thing list.
        /// </summary>
        public void DeleteSelected()
        {
            // Set up a list to hold all things pending destruction
            List<Thing> thingsToDestroy = new List<Thing>();

            int remainingThings = Selected;
            foreach (Thing thing in Things)
            {
                // Check if we have deleted all the necessary things, exiting the loop if so
                if (remainingThings == 0) break;

                // Check if this thing has more in its stack than we need to take
                if (thing.stackCount > remainingThings)
                {
                    // Just take the amount we need from this stack
                    remainingThings = 0;
                    thing.stackCount -= remainingThings;
                }
                else
                {
                    // Subtract this thing's stack size from the remaining count and destroy it
                    remainingThings -= thing.stackCount;
                    thingsToDestroy.Add(thing);
                }
            }

            // Remove and destroy all things pending destruction
            foreach (Thing thing in thingsToDestroy)
            {
                // Remove this thing from the things list
                Things.Remove(thing);

                // Destroy it
                thing.Destroy();
            }
        }

        /// <summary>
        /// Groups the given collection of items by their def type and stackability.
        /// </summary>
        /// <param name="items">Items to group</param>
        /// <returns>Grouped items list</returns>
        public static List<StackedThings> GroupThings(
            IEnumerable<Thing> items,
            System.Action<string, LogLevel> log = null)
        {
            Dictionary<string, List<StackedThings>> groupedItems = new Dictionary<string, List<StackedThings>>();

            try
            {
                foreach (Thing item in items ?? Enumerable.Empty<Thing>())
                {
                    if (item == null || item.Destroyed || item.def == null ||
                        string.IsNullOrEmpty(item.def.defName) || item.stackCount <= 0)
                    {
                        continue;
                    }

                    string defName = item.def.defName;
                    if (groupedItems.TryGetValue(defName, out List<StackedThings> itemStacks))
                    {
                        bool stacked = false;
                        foreach (StackedThings itemStack in itemStacks)
                        {
                            try
                            {
                                if (itemStack.CanStack(item))
                                {
                                    itemStack.Things.Add(item);
                                    stacked = true;
                                    break;
                                }
                            }
                            catch (System.Exception exception)
                            {
                                log?.Invoke(
                                    $"[StackedThings] Failed to compare stackability for thing '{item.ThingID}'; using an isolated stack.{System.Environment.NewLine}{exception}",
                                    LogLevel.WARNING);
                            }
                        }

                        if (!stacked)
                        {
                            itemStacks.Add(new StackedThings(new[] { item }));
                        }
                    }
                    else
                    {
                        groupedItems.Add(defName, new List<StackedThings> { new StackedThings(new[] { item }) });
                    }
                }
            }
            catch (System.Exception exception)
            {
                log?.Invoke(
                    $"[StackedThings] Item enumeration failed; returning the groups collected so far.{System.Environment.NewLine}{exception}",
                    LogLevel.WARNING);
            }

            return groupedItems.SelectMany(pair => pair.Value).ToList();
        }
    }
}
