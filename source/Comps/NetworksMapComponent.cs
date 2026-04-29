using Verse;
using System.Collections.Generic;
using System.Linq;

namespace SK_Matter_Network
{
    public class NetworksMapComponent : MapComponent
    {
        private List<DataNetwork> networks;

        public List<DataNetwork> Networks => networks;
        public IEnumerable<DataNetwork> ExtractionEnabledNetworks => networks.Where(network => network.CanExtractItems);

        public NetworksMapComponent(Map map) : base(map)
        {
            networks = new List<DataNetwork>();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref networks, "networks", LookMode.Deep);

            if (networks == null) networks = new List<DataNetwork>();
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
                networks.Add(network);
        }

        public void RemoveNetwork(DataNetwork network)
        {
            networks.Remove(network);
        }

        public bool CellHasNetworkBuilding(IntVec3 cell)
        {
            foreach (DataNetwork network in networks)
            {
                if (network.CellHasNetworkBuilding(cell)) return true;
            }
            return false;
        }

        public DataNetwork GetNetworkAtCell(IntVec3 cell)
        {
            foreach (DataNetwork network in networks)
            {
                if (network.CellHasNetworkBuilding(cell)) return network;
            }
            return null;
        }

        public List<NetworkBuilding> GetAllNetworkBuildings()
        {
            List<NetworkBuilding> result = new List<NetworkBuilding>();
            foreach (DataNetwork network in networks)
                result.AddRange(network.Buildings);
            return result;
        }

        public bool TryGetItemNetwork(Thing item, out DataNetwork network)
        {
            network = null;
            foreach (DataNetwork n in networks)
            {
                if (n.ItemInNetwork(item))
                {
                    network = n;
                    return true;
                }
            }
            return false;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            int currentTick = Find.TickManager.TicksGame;
            for (int i = 0; i < networks.Count; i++)
            {
                networks[i].UpdatePowerIfDue(currentTick);
            }
        }
    }
}
