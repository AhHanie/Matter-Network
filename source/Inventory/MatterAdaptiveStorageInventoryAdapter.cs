using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class MatterAdaptiveStorageInventoryAdapter : IMatterInventoryAdapter
    {
        private static readonly Type ThingClassType = AccessTools.TypeByName("AdaptiveStorage.ThingClass");
        private static readonly Type ThingCollectionType = AccessTools.TypeByName("AdaptiveStorage.ThingCollection");
        private static readonly MethodInfo AcceptsMethod = ThingClassType?.GetMethod("Accepts", new[] { typeof(Thing) });
        private static readonly PropertyInfo StoredThingsProperty = ThingClassType?.GetProperty("StoredThings");
        private static readonly MethodInfo ContainsMethod = ThingCollectionType?.GetMethod("Contains", new[] { typeof(Thing) });
        private static readonly MethodInfo MapPositionOfMethod = ThingCollectionType?.GetMethod("MapPositionOf");
        private static readonly MethodInfo AddAtMapCellMethod = ThingCollectionType?.GetMethod("Add", new[] { typeof(Thing), typeof(IntVec3) });
        private static readonly MethodInfo NotifyItemStackChangedMethod = ThingClassType?.GetMethod("Notify_ItemStackChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private readonly Building_Storage storage;
        private readonly ThingOwner storedThings;
        private readonly IntVec3 targetCell;

        public Thing ParentThing => storage;
        public string Label => storage.LabelCap;
        public bool IsValid => storage.Spawned && !storage.Destroyed;

        public MatterAdaptiveStorageInventoryAdapter(Building_Storage storage, IntVec3 targetCell)
        {
            this.storage = storage;
            this.targetCell = targetCell;
            storedThings = (ThingOwner)StoredThingsProperty.GetValue(storage, null);
        }

        public static bool CanAdapt(Thing thing)
        {
            return ThingClassType != null
                && AcceptsMethod != null
                && StoredThingsProperty != null
                && ContainsMethod != null
                && MapPositionOfMethod != null
                && AddAtMapCellMethod != null
                && NotifyItemStackChangedMethod != null
                && thing is Building_Storage
                && ThingClassType.IsAssignableFrom(thing.GetType());
        }

        public IEnumerable<Thing> GetStoredThings()
        {
            for (int i = 0; i < storedThings.Count; i++)
            {
                yield return storedThings[i];
            }
        }

        public bool CanPull(Thing thing, int count)
        {
            return IsValid && thing != null && !thing.Destroyed && count > 0 && ContainsStoredThing(thing);
        }

        public int PullTo(Thing thing, int count, ThingOwner destination)
        {
            if (!CanPull(thing, count) || destination == null)
            {
                return 0;
            }

            int moveCount = Math.Min(count, thing.stackCount);
            IntVec3 mapPosition = MapPositionOf(thing);
            bool fullStack = moveCount >= thing.stackCount;
            Thing moving;

            if (fullStack)
            {
                if (thing.Spawned)
                {
                    thing.DeSpawn();
                }
                else
                {
                    storedThings.Remove(thing);
                }

                moving = thing;
            }
            else
            {
                moving = thing.SplitOff(moveCount);
                NotifyItemStackChanged(thing);
            }

            if (destination.TryAdd(moving, canMergeWithExistingStacks: true))
            {
                return moveCount;
            }

            RestorePulledThing(thing, moving, fullStack, mapPosition);
            return 0;
        }

        public bool Accepts(Thing thing)
        {
            bool adaptiveAccepts = IsValid && thing != null && !thing.Destroyed && AdaptiveStorageAccepts(thing);
            bool result = adaptiveAccepts && TryFindStoreCell(thing, out IntVec3 _);
            return result;
        }

        public int CountCanAccept(Thing thing)
        {
            if (!Accepts(thing))
            {
                return 0;
            }

            return Math.Min(thing.stackCount, thing.def.stackLimit);
        }

        public int PushFrom(Thing thing, int count)
        {
            if (!Accepts(thing) || count <= 0 || thing.holdingOwner == null)
            {
                return 0;
            }

            int moveCount = Math.Min(count, thing.stackCount);
            ThingOwner source = thing.holdingOwner;
            Thing moving = source.Take(thing, moveCount);
            if (TryFindStoreCell(moving, out IntVec3 storeCell) && GenPlace.TryPlaceThing(moving, storeCell, storage.Map, ThingPlaceMode.Direct))
            {
                return moveCount;
            }

            source.TryAdd(moving, canMergeWithExistingStacks: true);
            return 0;
        }

        public string GetStatus()
        {
            return "MN_MatterIOPortStatusAdaptiveStorage".Translate(Label, storedThings.Count);
        }

        private bool TryFindStoreCell(Thing thing, out IntVec3 cell)
        {
            if (StoreUtility.IsGoodStoreCell(targetCell, storage.Map, thing, null, Faction.OfPlayer))
            {
                cell = targetCell;
                return true;
            }

            List<IntVec3> cells = storage.slotGroup.CellsList;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 candidate = cells[i];
                if (candidate == targetCell)
                {
                    continue;
                }

                if (StoreUtility.IsGoodStoreCell(candidate, storage.Map, thing, null, Faction.OfPlayer))
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private IntVec3 MapPositionOf(Thing thing)
        {
            return (IntVec3)MapPositionOfMethod.Invoke(storedThings, new object[] { thing });
        }

        private bool ContainsStoredThing(Thing thing)
        {
            return (bool)ContainsMethod.Invoke(storedThings, new object[] { thing });
        }

        private bool AdaptiveStorageAccepts(Thing thing)
        {
            return (bool)AcceptsMethod.Invoke(storage, new object[] { thing });
        }

        private void RestorePulledThing(Thing original, Thing moving, bool fullStack, IntVec3 mapPosition)
        {
            if (fullStack)
            {
                if (storage.Spawned)
                {
                    GenSpawn.Spawn(moving, mapPosition, storage.Map);
                }
                else
                {
                    AddAtMapCellMethod.Invoke(storedThings, new object[] { moving, mapPosition });
                }

                return;
            }

            original.TryAbsorbStack(moving, respectStackLimit: false);
            NotifyItemStackChanged(original);
        }

        private void NotifyItemStackChanged(Thing thing)
        {
            NotifyItemStackChangedMethod.Invoke(storage, new object[] { thing });
        }
    }
}
