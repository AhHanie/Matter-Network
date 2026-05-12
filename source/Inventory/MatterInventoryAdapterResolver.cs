using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public static class MatterInventoryAdapterResolver
    {
        public static bool TryResolve(Map map, IntVec3 targetCell, Thing port, out IMatterInventoryAdapter adapter)
        {
            adapter = null;

            List<Thing> things = map.thingGrid.ThingsListAt(targetCell);
            IMatterInventoryAdapter adaptiveStorageAdapter = null;
            IMatterInventoryAdapter thingOwnerAdapter = null;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == port || thing.Destroyed)
                {
                    continue;
                }

                CompMatterInventoryAdapter explicitAdapter = thing.TryGetComp<CompMatterInventoryAdapter>();
                if (explicitAdapter != null && explicitAdapter.IsValid)
                {
                    adapter = explicitAdapter;
                    return true;
                }

                if (adaptiveStorageAdapter == null && MatterAdaptiveStorageInventoryAdapter.CanAdapt(thing))
                {
                    adaptiveStorageAdapter = new MatterAdaptiveStorageInventoryAdapter((Building_Storage)thing, targetCell);
                    continue;
                }

                if (thingOwnerAdapter == null)
                {
                    ThingOwner owner = thing.TryGetInnerInteractableThingOwner();
                    if (owner != null)
                    {
                        thingOwnerAdapter = new MatterThingOwnerInventoryAdapter(thing, owner);
                    }
                }
            }

            if (adaptiveStorageAdapter != null)
            {
                adapter = adaptiveStorageAdapter;
                return true;
            }

            if (thingOwnerAdapter != null)
            {
                adapter = thingOwnerAdapter;
                return true;
            }

            SlotGroup slotGroup = map.haulDestinationManager.SlotGroupAt(targetCell);
            if (slotGroup != null)
            {
                adapter = new MatterSlotGroupInventoryAdapter(slotGroup, targetCell, map);
                return true;
            }

            return false;
        }
    }
}
