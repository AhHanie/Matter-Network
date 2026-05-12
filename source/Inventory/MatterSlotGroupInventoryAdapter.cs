using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class MatterSlotGroupInventoryAdapter : IMatterInventoryAdapter
    {
        private readonly SlotGroup slotGroup;
        private readonly IntVec3 targetCell;
        private readonly Map map;

        public Thing ParentThing => slotGroup.parent as Thing;
        public string Label => slotGroup.GetName();
        public bool IsValid => map != null && targetCell.InBounds(map) && slotGroup.parent?.Map == map;

        public MatterSlotGroupInventoryAdapter(SlotGroup slotGroup, IntVec3 targetCell, Map map)
        {
            this.slotGroup = slotGroup;
            this.targetCell = targetCell;
            this.map = map;
        }

        public IEnumerable<Thing> GetStoredThings()
        {
            List<IntVec3> cells = slotGroup.CellsList;
            for (int i = 0; i < cells.Count; i++)
            {
                foreach (Thing thing in StoredThingsAt(cells[i]))
                {
                    yield return thing;
                }
            }
        }

        public bool CanPull(Thing thing, int count)
        {
            return IsValid && thing != null && !thing.Destroyed && thing.Spawned && map.haulDestinationManager.SlotGroupAt(thing.Position) == slotGroup && count > 0;
        }

        public int PullTo(Thing thing, int count, ThingOwner destination)
        {
            if (!CanPull(thing, count) || destination == null)
            {
                return 0;
            }

            int moveCount = System.Math.Min(count, thing.stackCount);
            Thing moving = thing.SplitOff(moveCount);
            if (destination.TryAdd(moving, canMergeWithExistingStacks: true))
            {
                return moveCount;
            }

            RestoreToMapOrStack(thing, moving);
            return 0;
        }

        public bool Accepts(Thing thing)
        {
            if (!IsValid || thing == null || thing.Destroyed || !slotGroup.parent.Accepts(thing))
            {
                return false;
            }

            return TryFindStoreCell(thing, out IntVec3 _);
        }

        public int CountCanAccept(Thing thing)
        {
            if (!Accepts(thing))
            {
                return 0;
            }

            return System.Math.Min(thing.stackCount, thing.def.stackLimit);
        }

        public int PushFrom(Thing thing, int count)
        {
            if (!Accepts(thing) || count <= 0 || thing.holdingOwner == null)
            {
                return 0;
            }

            int moveCount = System.Math.Min(count, thing.stackCount);
            ThingOwner source = thing.holdingOwner;
            Thing moving = source.Take(thing, moveCount);
            if (TryFindStoreCell(moving, out IntVec3 storeCell) && GenPlace.TryPlaceThing(moving, storeCell, map, ThingPlaceMode.Direct))
            {
                return moveCount;
            }

            source.TryAdd(moving, canMergeWithExistingStacks: true);
            return 0;
        }

        public string GetStatus()
        {
            return "MN_MatterIOPortStatusSlotGroup".Translate(Label);
        }

        private IEnumerable<Thing> StoredThingsAt(IntVec3 cell)
        {
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing.def.EverStorable(willMinifyIfPossible: false))
                {
                    yield return thing;
                }
            }
        }

        private bool TryFindStoreCell(Thing thing, out IntVec3 cell)
        {
            if (StoreUtility.IsGoodStoreCell(targetCell, map, thing, null, Faction.OfPlayer))
            {
                cell = targetCell;
                return true;
            }

            List<IntVec3> cells = slotGroup.CellsList;
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 candidate = cells[i];
                if (candidate == targetCell)
                {
                    continue;
                }

                if (StoreUtility.IsGoodStoreCell(candidate, map, thing, null, Faction.OfPlayer))
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private void RestoreToMapOrStack(Thing original, Thing moving)
        {
            if (moving == null || moving.Destroyed)
            {
                return;
            }

            if (original != moving && original.Spawned && original.CanStackWith(moving))
            {
                original.TryAbsorbStack(moving, respectStackLimit: false);
                return;
            }

            GenPlace.TryPlaceThing(moving, targetCell, map, ThingPlaceMode.Near);
        }
    }
}
