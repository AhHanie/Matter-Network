using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageTabActions
    {
        internal void DropItemByGroup(NetworkBuildingNetworkInterface selectedInterface, GroupedItemEntry entry)
        {
            if (selectedInterface?.ParentNetwork == null || !selectedInterface.ParentNetwork.IsOperational)
            {
                return;
            }

            NetworkBuildingController controller = selectedInterface.ParentNetwork.ActiveController;
            if (controller?.innerContainer == null)
            {
                return;
            }

            Thing itemToDrop = controller.innerContainer.InnerListForReading.FirstOrDefault(thing =>
            {
                if (thing == null || thing.Destroyed) return false;
                if (entry.IsMinified)
                    return thing is MinifiedThing mt && mt.InnerThing?.def == entry.DisplayDef;
                return thing.def == entry.DisplayDef;
            });

            if (itemToDrop == null)
            {
                Logger.Warning($"Could not find {entry.DisplayDef.defName} in network storage");
                return;
            }

            DropStoredThing(selectedInterface, itemToDrop);
        }

        internal void DropStoredThing(NetworkBuildingNetworkInterface selectedInterface, Thing thing)
        {
            if (selectedInterface?.ParentNetwork == null || !selectedInterface.ParentNetwork.IsOperational || thing == null || thing.Destroyed)
            {
                return;
            }

            Thing itemToDrop = thing;
            if (itemToDrop.stackCount > itemToDrop.def.stackLimit)
            {
                itemToDrop = itemToDrop.SplitOff(Mathf.Min(itemToDrop.def.stackLimit, itemToDrop.stackCount));
            }
            else
            {
                NetworkBuildingController controller = selectedInterface.ParentNetwork.ActiveController;
                if (controller?.innerContainer == null || !controller.innerContainer.Contains(thing))
                {
                    return;
                }

                if (!controller.innerContainer.Remove(thing))
                {
                    return;
                }
            }

            selectedInterface.ParentNetwork.MarkBytesDirty();
            GenPlace.TryPlaceThing(itemToDrop, selectedInterface.Position, selectedInterface.Map, ThingPlaceMode.Near);
        }
    }
}
