using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SK_Matter_Network
{
    public static class NetworkManager
    {
        public static void HandleBuildingAdded(NetworkBuilding building, NetworksMapComponent mapComp)
        {
            List<NetworkBuilding> adjacentBuildings = GetAdjacentNetworkBuildings(building, mapComp);

            if (adjacentBuildings.Count == 0)
            {
                DataNetwork newNetwork = new DataNetwork(mapComp.map);
                newNetwork.AddBuilding(building);
                mapComp.AddNetwork(newNetwork);
                Logger.Message($"Created new network for building at {building.Position}");
            }
            else
            {
                HashSet<DataNetwork> adjacentNetworks = new HashSet<DataNetwork>();
                foreach (NetworkBuilding adj in adjacentBuildings)
                {
                    if (adj.ParentNetwork != null)
                        adjacentNetworks.Add(adj.ParentNetwork);
                }

                if (adjacentNetworks.Count == 0)
                {
                    DataNetwork newNetwork = new DataNetwork(mapComp.map);
                    newNetwork.AddBuilding(building);
                    mapComp.AddNetwork(newNetwork);
                }
                else if (adjacentNetworks.Count == 1)
                {
                    DataNetwork network = adjacentNetworks.First();
                    network.AddBuilding(building);
                    Logger.Message($"Added building at {building.Position} to existing network {network.NetworkId}");
                }
                else
                {
                    DataNetwork primaryNetwork = adjacentNetworks.First();
                    List<DataNetwork> networksToMerge = adjacentNetworks.Skip(1).ToList();

                    primaryNetwork.AddBuilding(building);

                    foreach (DataNetwork toMerge in networksToMerge)
                        MergeNetworks(primaryNetwork, toMerge, mapComp);

                    primaryNetwork.ValidateControllerConflicts();
                    Logger.Message($"Merged {networksToMerge.Count + 1} networks into {primaryNetwork.NetworkId}");
                }
            }
        }

        public static void HandleBuildingRemoved(NetworkBuilding building, NetworksMapComponent mapComp)
        {
            DataNetwork network = building.ParentNetwork;
            if (network == null)
            {
                Logger.Warning($"Tried to remove building at {building.Position} but it has no parent network");
                return;
            }

            network.RemoveBuilding(building);

            if (network.IsEmpty())
            {
                mapComp.RemoveNetwork(network);
                Logger.Message($"Removed empty network {network.NetworkId}");
                return;
            }

            List<List<NetworkBuilding>> connectedGroups = FindConnectedGroups(network.Buildings, mapComp.map);

            if (connectedGroups.Count > 1)
            {
                Logger.Message($"Network {network.NetworkId} split into {connectedGroups.Count} groups");

                List<NetworkBuilding> firstGroup = connectedGroups[0];
                List<NetworkBuilding> buildingsToMove = network.Buildings.Where(b => !firstGroup.Contains(b)).ToList();

                foreach (NetworkBuilding b in buildingsToMove)
                    network.RemoveBuilding(b);

                network.ValidateControllerConflicts();

                for (int i = 1; i < connectedGroups.Count; i++)
                {
                    DataNetwork newNetwork = new DataNetwork(mapComp.map);
                    foreach (NetworkBuilding b in connectedGroups[i])
                        newNetwork.AddBuilding(b);
                    newNetwork.ValidateControllerConflicts();
                    mapComp.AddNetwork(newNetwork);
                    Logger.Message($"Created new network {newNetwork.NetworkId} with {newNetwork.BuildingCount} buildings after split");
                }
            }
        }

        private static void MergeNetworks(DataNetwork primary, DataNetwork toMerge, NetworksMapComponent mapComp)
        {
            List<NetworkBuilding> toTransfer = toMerge.Buildings.ToList();
            foreach (NetworkBuilding b in toTransfer)
            {
                toMerge.RemoveBuilding(b);
                primary.AddBuilding(b);
            }
            mapComp.RemoveNetwork(toMerge);
        }

        private static List<List<NetworkBuilding>> FindConnectedGroups(List<NetworkBuilding> buildings, Map map)
        {
            HashSet<NetworkBuilding> unvisited = new HashSet<NetworkBuilding>(buildings);
            List<List<NetworkBuilding>> groups = new List<List<NetworkBuilding>>();

            while (unvisited.Count > 0)
            {
                NetworkBuilding start = unvisited.First();
                List<NetworkBuilding> group = FloodFillGroup(start, unvisited, map);
                groups.Add(group);
            }

            return groups;
        }

        private static List<NetworkBuilding> FloodFillGroup(NetworkBuilding start, HashSet<NetworkBuilding> unvisited, Map map)
        {
            List<NetworkBuilding> group = new List<NetworkBuilding>();
            Queue<NetworkBuilding> queue = new Queue<NetworkBuilding>();

            queue.Enqueue(start);
            unvisited.Remove(start);

            while (queue.Count > 0)
            {
                NetworkBuilding current = queue.Dequeue();
                group.Add(current);

                foreach (NetworkBuilding adj in GetAdjacentNetworkBuildingsFromSet(current, unvisited, map))
                {
                    if (unvisited.Contains(adj))
                    {
                        unvisited.Remove(adj);
                        queue.Enqueue(adj);
                    }
                }
            }

            return group;
        }

        private static List<NetworkBuilding> GetAdjacentNetworkBuildings(NetworkBuilding building, NetworksMapComponent mapComp)
        {
            List<NetworkBuilding> result = new List<NetworkBuilding>();
            Map map = mapComp.map;

            foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(building))
            {
                if (!adj.InBounds(map)) continue;
                foreach (Thing t in map.thingGrid.ThingsListAt(adj))
                {
                    if (t is NetworkBuilding nb && nb != building)
                        result.Add(nb);
                }
            }

            return result;
        }

        private static List<NetworkBuilding> GetAdjacentNetworkBuildingsFromSet(NetworkBuilding building, HashSet<NetworkBuilding> set, Map map)
        {
            List<NetworkBuilding> result = new List<NetworkBuilding>();

            foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(building))
            {
                if (!adj.InBounds(map)) continue;
                foreach (Thing t in map.thingGrid.ThingsListAt(adj))
                {
                    if (t is NetworkBuilding nb && set.Contains(nb))
                        result.Add(nb);
                }
            }

            return result;
        }
    }
}
