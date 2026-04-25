using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_DiskCapacity : CompProperties
    {
        public int maxBytes = 1024;

        public CompProperties_DiskCapacity()
        {
            compClass = typeof(CompDiskCapacity);
        }
    }

    public class CompDiskCapacity : ThingComp
    {
        private DiskItemOwner archivedContainer;

        public CompProperties_DiskCapacity Props => (CompProperties_DiskCapacity)props;

        public int MaxBytes => Props.maxBytes;
        public DiskItemOwner ArchivedContainer
        {
            get
            {
                EnsureArchivedContainer();
                return archivedContainer;
            }
        }

        public bool HasArchivedItems => ArchivedUsedBytes > 0;
        public bool IsArchived => HasArchivedItems;
        public bool CanContributeActiveCapacity => !HasArchivedItems;
        public int ArchivedUsedBytes
        {
            get
            {
                EnsureArchivedContainer();
                int used = 0;
                foreach (Thing thing in archivedContainer.InnerListForReading)
                {
                    if (thing != null && !thing.Destroyed)
                        used += thing.stackCount;
                }
                return used;
            }
        }

        public int ArchivedFreeBytes => System.Math.Max(0, MaxBytes - ArchivedUsedBytes);
        public int ArchivedStackCount
        {
            get
            {
                EnsureArchivedContainer();
                int count = 0;
                foreach (Thing thing in archivedContainer.InnerListForReading)
                {
                    if (thing != null && !thing.Destroyed)
                        count++;
                }
                return count;
            }
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            EnsureArchivedContainer();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref archivedContainer, "archivedContainer");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureArchivedContainer();
                RemoveInvalidArchivedItems();
            }
        }

        public override string CompInspectStringExtra()
        {
            if (HasArchivedItems)
            {
                return "MN_DiskInspectCapacity".Translate(MaxBytes) + "\n" +
                    "MN_DiskInspectArchived".Translate(ArchivedUsedBytes, ArchivedStackCount);
            }

            return "MN_DiskInspectCapacity".Translate(MaxBytes);
        }

        private void EnsureArchivedContainer()
        {
            if (archivedContainer == null)
                archivedContainer = new DiskItemOwner(this);
            else
                archivedContainer.SetDisk(this);
        }

        private void RemoveInvalidArchivedItems()
        {
            for (int i = archivedContainer.InnerListForReading.Count - 1; i >= 0; i--)
            {
                Thing thing = archivedContainer.InnerListForReading[i];
                if (thing == null || thing.Destroyed)
                    archivedContainer.Remove(thing);
            }
        }
    }
}
