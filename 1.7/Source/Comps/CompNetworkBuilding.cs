using System.Collections.Generic;
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

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (!(parent is NetworkBuilding networkBuilding))
            {
                yield break;
            }

            if (!ModSettings.EnableNetworkReconnectGizmo && !DebugSettings.ShowDevGizmos)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "Debug: reconnect adjacent networks",
                defaultDesc = "Merges this building's network with every distinct, cardinally adjacent Matter Network's network.",
                action = delegate
                {
                    bool merged = NetworkManager.TryReconnectAdjacentNetworks(networkBuilding);
                    if (merged)
                    {
                        Messages.Message(
                            "Adjacent networks merged.",
                            networkBuilding,
                            MessageTypeDefOf.PositiveEvent,
                            historical: false);
                    }
                    else
                    {
                        Messages.Message(
                            "No distinct adjacent network to merge.",
                            networkBuilding,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                }
            };
        }
    }
}
