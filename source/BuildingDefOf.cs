using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    [DefOf]
    public class BuildingDefOf
    {
        public static ThingDef MN_DiskDrive;
        public static ThingDef MN_AdvancedNetworkPowerStorage;
        public static ThingDef MN_NetworkCable;
        public static ThingDef MN_NetworkController;
        public static ThingDef MN_NetworkInterface;
        public static ThingDef MN_NetworkPowerStorage;

        static BuildingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(BuildingDefOf));
        }
    }
}
