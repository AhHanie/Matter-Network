using Verse;
using System.Collections.Generic;

namespace SK_Matter_Network
{
    public class NetworksMapComponent : MapComponent
    {
        private List<DataNetwork> networks;
        private NetworksMapCache cache;

        public List<DataNetwork> Networks => networks;
        public NetworksMapCache Cache => cache;
        public HashSet<IntVec3> NetworkInterfacePositions => cache.NetworkInterfacePositions;

        public NetworksMapComponent(Map map) : base(map)
        {
            networks = new List<DataNetwork>();
            cache = new NetworksMapCache();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref networks, "networks", LookMode.Deep);
            Scribe_Deep.Look(ref cache, "cache");

            if (networks == null)
            {
                networks = new List<DataNetwork>();
            }

            if (cache == null)
            {
                cache = new NetworksMapCache();
            }
        }

        public void AddBuilding(NetworkBuilding building)
        {
            cache.RegisterNetworkInterfaceBuilding(building);
            NetworkManager.HandleBuildingAdded(building, this);
        }

        public void RemoveBuilding(NetworkBuilding building)
        {
            cache.DeregisterNetworkInterfaceBuilding(building);
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
                if (netwrk.ItemInNetwork(item))
                {
                    network = netwrk;
                    return true;
                }
            }
            return false;
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 300 != 0)
            {
                return;
            }

            RebuildIsInValidBestStorageCache();
        }

        public void RebuildIsInValidBestStorageCache()
        {
            NetworksStaticCache.RemoveThingsForMap(map);

            foreach (DataNetwork network in networks)
            {
                foreach (Thing thing in network.StoredItems)
                {
                    NetworksStaticCache.SetIsInValidBestStorage(thing, Patches.Patch_StoreUtility.CalculateIsInValidBestStorage(thing, network));
                }
            }

            foreach (NetworkBuilding building in GetAllNetworkBuildings())
            {
                if (!(building is NetworkBuildingDiskDrive diskDrive))
                {
                    continue;
                }

                foreach (Thing disk in diskDrive.HeldItems)
                {
                    if (diskDrive.Locked)
                    {
                        NetworksStaticCache.SetIsInValidBestStorage(disk, true);
                    }
                    else
                    {
                        NetworksStaticCache.RemoveThing(disk);
                    }
                }
            }
        }

    }
}
