using Verse;
using System.Collections.Generic;
using System.Linq;

namespace SK_Matter_Network
{
    public class NetworksMapComponent : MapComponent
    {
        private List<DataNetwork> networks;

        public List<DataNetwork> Networks => networks;

        public NetworksMapComponent(Map map) : base(map)
        {
            networks = new List<DataNetwork>();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref networks, "networks", LookMode.Deep);
        }

        public void AddBuilding(NetworkBuilding building)
        {
            NetworkManager.HandleBuildingAdded(building, this);
        }

        public void RemoveBuilding(NetworkBuilding building)
        {
            NetworkManager.HandleBuildingRemoved(building, this);
        }

        public void AddNetwork(DataNetwork network)
        {
            if (!networks.Contains(network))
            {
                networks.Add(network);
            }
        }

        public void RemoveNetwork(DataNetwork network)
        {
            networks.Remove(network);
        }

        public bool CellHasNetworkBuilding(IntVec3 cell)
        {
            foreach (DataNetwork network in networks)
            {
                if (network.CellHasNetworkBuilding(cell))
                {
                    return true;
                }
            }
            return false;
        }

        public DataNetwork GetNetworkAtCell(IntVec3 cell)
        {
            foreach (DataNetwork network in networks)
            {
                if (network.CellHasNetworkBuilding(cell))
                {
                    return network;
                }
            }
            return null;
        }

        public List<NetworkBuilding> GetAllNetworkBuildings()
        {
            List<NetworkBuilding> allBuildings = new List<NetworkBuilding>();
            foreach (DataNetwork network in networks)
            {
                allBuildings.AddRange(network.Buildings);
            }
            return allBuildings;
        }

        public bool TryGetItemNetwork(Thing item, out DataNetwork network)
        {
            network = null;
            foreach (DataNetwork netwrk in networks)
            {
                if (netwrk.ItemInNetwork(item)) {
                    network = netwrk;
                    return true;
                }
            }
            return false;
        }
    }
}