using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public class ControllerItemOwner : ThingOwner<Thing>
    {
        private NetworkBuildingController ctrl;
        private bool addingExistingNetworkItem;
        private int lastTryAddAcceptedCount;

        internal int LastTryAddAcceptedCount => lastTryAddAcceptedCount;

        public ControllerItemOwner() { }

        public ControllerItemOwner(NetworkBuildingController owner) : base(owner, oneStackOnly: false)
        {
            SetController(owner);
            dontTickContents = true;
        }

        public void SetController(NetworkBuildingController owner)
        {
            ctrl = owner;
        }

        public override int GetCountCanAccept(Thing item, bool canMergeWithExistingStacks = true)
        {
            int baseCount = base.GetCountCanAccept(item, canMergeWithExistingStacks);
            if (addingExistingNetworkItem)
            {
                return baseCount;
            }

            return System.Math.Min(baseCount, ctrl.ParentNetwork.ControllerCanAcceptCount(item));
        }

        internal bool TryAddExistingNetworkItem(Thing item, bool canMergeWithExistingStacks = true)
        {
            addingExistingNetworkItem = true;
            try
            {
                return TryAdd(item, canMergeWithExistingStacks);
            }
            finally
            {
                addingExistingNetworkItem = false;
            }
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            lastTryAddAcceptedCount = 0;

            if (item.Destroyed)
                return false;

            if (!addingExistingNetworkItem)
            {
                DataNetwork network = ctrl.ParentNetwork;
                int acceptedCount = network.ControllerCanAcceptCount(item);
                if (acceptedCount <= 0)
                {
                    return false;
                }

                if (acceptedCount < item.stackCount)
                {
                    Thing rejected = item.SplitOff(item.stackCount - acceptedCount);
                    DropRejectedItem(rejected, network);
                }
            }

            int countBeingAdded = item.stackCount;
            DataNetwork parentNetwork = ctrl.ParentNetwork;
            if (canMergeWithExistingStacks)
            {
                for (int i = 0; i < InnerListForReading.Count; i++)
                {
                    Thing existing = InnerListForReading[i];
                    if (!CanMergeIntoNetworkStack(existing, item))
                        continue;

                    int absorbed = item.stackCount;
                    existing.TryAbsorbStack(item, respectStackLimit: false);
                    NotifyAddedAndMergedWith(existing, absorbed);
                    parentNetwork.MarkBytesDirty();
                    lastTryAddAcceptedCount = absorbed;
                    return true;
                }
            }

            bool result = base.TryAdd(item, canMergeWithExistingStacks: false);
            if (result && !item.Destroyed && item.stackCount > 0)
            {
                parentNetwork.storedItems.Add(item);
                parentNetwork.MarkBytesDirty();
                lastTryAddAcceptedCount = countBeingAdded;
            }
            return result;
        }

        private static bool CanMergeIntoNetworkStack(Thing existing, Thing item)
        {
            if (existing.def.stackLimit <= 1 || item.def.stackLimit <= 1)
                return false;

            return existing.CanStackWith(item);
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

            dropCell = ctrl.Position;
            map = ctrl.Map;
            return true;
        }
    }
}
