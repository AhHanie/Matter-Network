using RimWorld;
using Verse;

namespace SK_Matter_Network
{
    public class CompProperties_NetworkPowerStorage : CompProperties
    {
        public float capacity = 0f;
        public bool showInspectString = true;

        public CompProperties_NetworkPowerStorage()
        {
            compClass = typeof(CompNetworkPowerStorage);
        }
    }

    public class CompNetworkPowerStorage : ThingComp
    {
        public CompProperties_NetworkPowerStorage Props => (CompProperties_NetworkPowerStorage)props;

        public float CapacityWd => System.Math.Max(0f, Props.capacity);

        public override string CompInspectStringExtra()
        {
            if (!Props.showInspectString)
            {
                return null;
            }

            if (parent is NetworkBuilding building && building.ParentNetwork != null)
            {
                DataNetwork network = building.ParentNetwork;
                return "MN_NetworkPowerStorageInspectNetworkReserve".Translate(
                    network.StoredReserveEnergyWd.ToString("F0"),
                    network.MaxReserveEnergyWd.ToString("F0"),
                    CapacityWd.ToString("F0"));
            }

            return "MN_NetworkPowerStorageInspectCapacity".Translate(CapacityWd.ToString("F0"));
        }
    }
}
