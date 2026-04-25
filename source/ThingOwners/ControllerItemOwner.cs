using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public class ControllerItemOwner : ThingOwner<Thing>
    {
        private NetworkBuildingController ctrl;

        public ControllerItemOwner() { }

        public ControllerItemOwner(NetworkBuildingController owner) : base(owner, oneStackOnly: false)
        {
            ctrl = owner;
            dontTickContents = true;
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (item == null || item.Destroyed)
                return false;

            DataNetwork network = ctrl?.ParentNetwork;
            if (network == null || network.WouldControllerAddExceedCapacity(item))
            {
                DropRejectedItem(item, network);
                return false;
            }

            if (canMergeWithExistingStacks)
            {
                for (int i = 0; i < InnerListForReading.Count; i++)
                {
                    Thing existing = InnerListForReading[i];
                    if (!existing.CanStackWith(item))
                        continue;

                    int absorbed = item.stackCount;
                    existing.TryAbsorbStack(item, respectStackLimit: false);
                    NotifyAddedAndMergedWith(existing, absorbed);
                    network?.MarkBytesDirty();
                    return true;
                }
            }

            bool result = base.TryAdd(item, canMergeWithExistingStacks: false);
            if (result && !item.Destroyed && item.stackCount > 0)
            {
                network?.storedItems.Add(item);
                network?.MarkBytesDirty();
            }
            return result;
        }

        private void DropRejectedItem(Thing item, DataNetwork network)
        {
            if (TryGetDropTarget(item, network, out IntVec3 dropCell, out Map map))
            {
                if (item.holdingOwner != null)
                {
                    item.holdingOwner.TryDrop(item, dropCell, map, ThingPlaceMode.Near, out Thing _);
                    return;
                }

                GenPlace.TryPlaceThing(item, dropCell, map, ThingPlaceMode.Near);
            }
        }

        private bool TryGetDropTarget(Thing item, DataNetwork network, out IntVec3 dropCell, out Map map)
        {
            NetworkBuildingNetworkInterface closestInterface = null;
            float closestDistanceSquared = float.MaxValue;
            IntVec3 itemPosition = item.PositionHeld;

            if (network?.NetworkInterfaces != null)
            {
                foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
                {
                    float distanceSquared = (itemPosition - networkInterface.Position).LengthHorizontalSquared;
                    if (distanceSquared < closestDistanceSquared)
                    {
                        closestInterface = networkInterface;
                        closestDistanceSquared = distanceSquared;
                    }
                }

                if (closestInterface != null)
                {
                    dropCell = closestInterface.Position;
                    map = closestInterface.Map;
                    return true;
                }
            }

            dropCell = ctrl.Position;
            map = ctrl.Map;
            return true;
        }
    }
}
