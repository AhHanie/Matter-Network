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
        public CompProperties_DiskCapacity Props => (CompProperties_DiskCapacity)props;

        public int MaxBytes => Props.maxBytes;

        public override string CompInspectStringExtra()
        {
            return "MN_DiskInspectCapacity".Translate(MaxBytes);
        }
    }
}
