using JetBrains.Annotations;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_DiskDataStorage : CompProperties
    {
        public int maxBytes = 2000;

        public CompProperties_DiskDataStorage()
        {
            compClass = typeof(CompDiskDataStorage);
        }
    }

    public class CompDiskDataStorage : ThingComp, IThingHolder
    {
        private ThingOwner<Thing> innerContainer;
        private int cachedUsedBytes = 0;

        public CompProperties_DiskDataStorage Props => (CompProperties_DiskDataStorage)props;

        public ThingOwner<Thing> InnerContainer => innerContainer;

        public int MaxBytes => Props.maxBytes;

        public int UsedBytes => cachedUsedBytes;

        public int AvailableBytes => MaxBytes - UsedBytes;

        public CompDiskDataStorage()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            innerContainer.dontTickContents = true;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public bool CanAcceptItem(Thing item)
        {
            if (UsedBytes >= MaxBytes) return false;
            return true;
        }

        public bool TryAddItem(Thing item)
        {
            if (!CanAcceptItem(item))
            {
                return false;
            }

            int spaceNeeded = item.stackCount;
            if (spaceNeeded > AvailableBytes)
            {
                return false;
            }

            return innerContainer.TryAdd(item, canMergeWithExistingStacks: true);
        }

        public bool TryRemoveItem(Thing item)
        {
            return innerContainer.Remove(item);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref cachedUsedBytes, "cachedUsedBytes", 0);
        }

        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append($"Storage: {UsedBytes}/{MaxBytes} bytes");

            return sb.ToString();
        }

        public int CanAcceptCount(Thing item)
        {
            return AvailableBytes;
        }

        public int TryAddItemToComp(Thing item)
        {
            int originalStackCount = item.stackCount;
            if (innerContainer.TryAdd(item, canMergeWithExistingStacks: true))
            {
                cachedUsedBytes += originalStackCount;
                Log.Message($"Disk {parent.LabelShort} accepted {originalStackCount} {item.def.defName}");
                return originalStackCount;
            }
            else
            {
                Log.Error($"Failed to add items to disk {parent.LabelShort}");
            }
            return 0;
        }

        public bool RemoveItemFromStorage(Thing item, int count)
        {
            // Check if item is still in container (it might have been removed by SplitOff already)
            bool itemStillInContainer = innerContainer.Contains(item);

            if (!itemStillInContainer && count < item.stackCount)
            {
                // Partial removal but item not in container - shouldn't happen
                Log.Error($"Item {item.LabelShort} not in disk {parent.LabelShort} container for partial removal");
                return false;
            }

            if (itemStillInContainer)
            {
                // Item is still here, need to remove it
                int originalStackCount = item.stackCount;

                if (count >= originalStackCount)
                {
                    // Remove entire item
                    if (innerContainer.Remove(item))
                    {
                        cachedUsedBytes -= originalStackCount;
                        Log.Message($"Removed entire stack of {item.LabelShort} from disk {parent.LabelShort}");
                        return true;
                    }
                    else
                    {
                        Log.Error($"Failed to remove {item.LabelShort} from disk {parent.LabelShort}");
                        return false;
                    }
                }
                else
                {
                    // Partial removal - just update the bytes
                    cachedUsedBytes -= count;
                    Log.Message($"Reduced {item.LabelShort} stack by {count} in disk {parent.LabelShort}");
                    return true;
                }
            }
            else
            {
                // Item was already removed (by SplitOff when entire stack taken)
                // Just update the bytes
                cachedUsedBytes -= count;
                Log.Message($"Item {item.LabelShort} already removed from disk {parent.LabelShort} (by SplitOff), updating bytes");
                return true;
            }
        }


        public List<Thing> GetAllStoredItems()
        {
            return new List<Thing>(innerContainer.InnerListForReading);
        }
    }
}