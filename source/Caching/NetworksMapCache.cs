using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    public class NetworksMapCache : IExposable
    {
        private HashSet<IntVec3> networkInterfacePositions;

        public HashSet<IntVec3> NetworkInterfacePositions => networkInterfacePositions;

        public NetworksMapCache()
        {
            networkInterfacePositions = new HashSet<IntVec3>();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref networkInterfacePositions, "networkInterfacePositions", LookMode.Value);

            if (networkInterfacePositions == null)
            {
                networkInterfacePositions = new HashSet<IntVec3>();
            }
        }

        public void RegisterNetworkInterfaceBuilding(NetworkBuilding building)
        {
            if (!(building is NetworkBuildingNetworkInterface networkInterface))
            {
                return;
            }

            networkInterfacePositions.Add(networkInterface.Position);
        }

        public void DeregisterNetworkInterfaceBuilding(NetworkBuilding building)
        {
            if (!(building is NetworkBuildingNetworkInterface networkInterface))
            {
                return;
            }

            networkInterfacePositions.Remove(networkInterface.Position);
        }

        public void RebuildFromNetworks(List<DataNetwork> networks)
        {
            networkInterfacePositions.Clear();

            foreach (DataNetwork network in networks)
            {
                foreach (NetworkBuildingNetworkInterface networkInterface in network.NetworkInterfaces)
                {
                    networkInterfacePositions.Add(networkInterface.Position);
                }
            }
        }
    }
}
