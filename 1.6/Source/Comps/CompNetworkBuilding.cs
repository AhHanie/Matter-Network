using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_NetworkBuilding : CompProperties
    {
        public int powerUsage = 0;

        public CompProperties_NetworkBuilding()
        {
            compClass = typeof(CompNetworkBuilding);
        }
    }

    public class CompNetworkBuilding : ThingComp
    {
        public CompProperties_NetworkBuilding Props => (CompProperties_NetworkBuilding)props;

        public int PowerUsageWatts => System.Math.Max(0, Props.powerUsage);

        public override string CompInspectStringExtra()
        {
            if (PowerUsageWatts <= 0)
            {
                return null;
            }

            return "MN_NetworkBuildingPowerUsage".Translate(PowerUsageWatts);
        }
    }
}
