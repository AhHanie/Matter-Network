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
                    if (!CanMergeIntoNetworkStack(existing, item))
                        continue;

                    int absorbed = item.stackCount;
                    existing.TryAbsorbStack(item, respectStackLimit: false);
                    NotifyAddedAndMergedWith(existing, absorbed);
                    return true;
                }
            }

            return base.TryAdd(item, canMergeWithExistingStacks: false);
        }

        private static bool CanMergeIntoNetworkStack(Thing existing, Thing item)
        {
            if (existing?.def == null || item?.def == null)
                return false;

            if (existing.def.stackLimit <= 1 || item.def.stackLimit <= 1)
                return false;

            return existing.CanStackWith(item);
        }
    }
}
