using Verse;

namespace SK_Matter_Network
{
    public class ThingOwnerDisk<T> : ThingOwner<T> where T : Thing
    {
        public ThingOwnerDisk()
        {
        }

        public ThingOwnerDisk(IThingHolder owner)
            : base(owner)
        {
        }

        public ThingOwnerDisk(IThingHolder owner, LookMode contentsLookMode = LookMode.Deep, bool removeContentsIfDestroyed = true)
            : base(owner)
        {
        }

        public ThingOwnerDisk(IThingHolder owner, bool oneStackOnly, LookMode contentsLookMode = LookMode.Deep, bool removeContentsIfDestroyed = true)
        : base(owner, oneStackOnly, contentsLookMode, removeContentsIfDestroyed)
        {
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (item == null)
            {
                Log.Warning("Tried to add null item to ThingOwner.");
                return false;
            }
            if (!(item is T item2))
            {
                return false;
            }
            if (Contains(item))
            {
                Log.Warning("Tried to add " + item.ToStringSafe() + " to ThingOwner but this item is already here.");
                return false;
            }
            if (item.holdingOwner != null)
            {
                Log.Warning("Tried to add " + item.ToStringSafe() + " to ThingOwner but this thing is already in another container. owner=" + owner.ToStringSafe() + ", current container owner=" + item.holdingOwner.Owner.ToStringSafe() + ". Use TryAddOrTransfer, TryTransferToContainer, or remove the item before adding it.");
                return false;
            }
            if (!CanAcceptAnyOf(item, canMergeWithExistingStacks))
            {
                return false;
            }
            if (canMergeWithExistingStacks)
            {
                for (int i = 0; i < InnerListForReading.Count; i++)
                {
                    T val = InnerListForReading[i];
                    if (!val.CanStackWith(item))
                    {
                        continue;
                    }
                    int stackCount = item.stackCount;
                    val.TryAbsorbStack(item, respectStackLimit: false);
                    NotifyAddedAndMergedWith(val, stackCount);
                    if (item.Destroyed || item.stackCount == 0)
                    {
                        return true;
                    }
                }
            }
            if (Count >= maxStacks)
            {
                return false;
            }
            item.holdingOwner = this;
            InnerListForReading.Add(item2);
            NotifyAdded(item2);
            return true;
        }
    }
}
