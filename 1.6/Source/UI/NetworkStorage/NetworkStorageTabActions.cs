using System.Linq;
using UnityEngine;
using Verse;

namespace SK_Matter_Network
{
    internal sealed class NetworkStorageTabActions
    {
        internal void DropItemByDef(NetworkBuildingNetworkInterface selectedInterface, ThingDef thingDef)
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

            Thing itemToDrop = controller.innerContainer.InnerListForReading.FirstOrDefault(
                thing => thing != null && !thing.Destroyed && thing.def == thingDef);

            if (itemToDrop == null)
            {
                Log.Warning($"Could not find {thingDef.defName} in network storage");
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
