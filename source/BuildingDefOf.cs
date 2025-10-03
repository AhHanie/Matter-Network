using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    [DefOf]
    public class BuildingDefOf
    {
        public static ThingDef MN_DiskDrive;
        public static ThingDef MN_NetworkCable;
        public static ThingDef MN_NetworkController;
        public static ThingDef MN_NetworkInterface;

        static BuildingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BuildingDefOf));
        }
    }
}
