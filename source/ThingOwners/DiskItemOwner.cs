using Verse;

namespace SK_Matter_Network
{
    public class DiskItemOwner : ThingOwner<Thing>
    {
        private CompDiskCapacity disk;

        public DiskItemOwner()
        {
            dontTickContents = true;
        }

        public DiskItemOwner(CompDiskCapacity disk)
        {
            this.disk = disk;
            dontTickContents = true;
        }

        public void SetDisk(CompDiskCapacity disk)
        {
            this.disk = disk;
            dontTickContents = true;
        }

        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (item == null || item.Destroyed)
                return false;

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
                    return true;
                }
            }

            return base.TryAdd(item, canMergeWithExistingStacks: false);
        }
    }
}
