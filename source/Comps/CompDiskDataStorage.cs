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
        private ThingOwnerDisk<Thing> innerContainer;
        private int cachedUsedBytes = 0;

        public CompProperties_DiskDataStorage Props => (CompProperties_DiskDataStorage)props;

        public ThingOwner<Thing> InnerContainer => innerContainer;

        public int MaxBytes => Props.maxBytes;

        public int UsedBytes => cachedUsedBytes;

        public int AvailableBytes => MaxBytes - UsedBytes;

        public CompDiskDataStorage()
        {
            innerContainer = new ThingOwnerDisk<Thing>(this, oneStackOnly: false);
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

            Log.Error($"Failed to add items to disk {parent.LabelShort}");
            return 0;
        }

        public void RemoveItemFromStorage(Thing item, int count, bool forceRemove)
        {
            if (forceRemove)
            {
                innerContainer.Remove(item);
            }
            cachedUsedBytes -= count;
            Log.Message($"Reduced {item.LabelShort} stack by {count} in disk {parent.LabelShort}");
        }


        public List<Thing> GetAllStoredItems()
        {
            return new List<Thing>(innerContainer.InnerListForReading);
        }

        public void SetUsedBytes(int bytes)
        {
            cachedUsedBytes = bytes;
            Log.Message($"Disk {parent.LabelShort} bytes corrected to {bytes}");
        }
    }
}