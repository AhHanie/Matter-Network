using System.Collections.Generic;

namespace SK_Matter_Network
{
    public static class GravshipNetworkTransferTracker
    {
        private static readonly HashSet<NetworkBuilding> registeredBuildings = new HashSet<NetworkBuilding>();
        private static readonly Dictionary<DataNetwork, bool> fullMoveCache = new Dictionary<DataNetwork, bool>();

        public static void RegisterForTransfer(NetworkBuilding building)
        {
            if (building == null) return;
            registeredBuildings.Add(building);
            if (building.ParentNetwork != null)
                fullMoveCache.Remove(building.ParentNetwork);
        }

        // Returns true if every spawned building in network is being transported by gravship.
        // Must be called after all PreSwapMap calls have run (i.e. from DeSpawn or later).
        public static bool IsFullNetworkMove(DataNetwork network)
        {
            if (network == null || registeredBuildings.Count == 0) return false;
            if (fullMoveCache.TryGetValue(network, out bool cached)) return cached;

            bool isFull = true;
            foreach (NetworkBuilding b in network.Buildings)
            {
                if (b != null && b.Spawned && !registeredBuildings.Contains(b))
                {
                    isFull = false;
                    break;
                }
            }
            fullMoveCache[network] = isFull;
            return isFull;
        }

        public static void Clear()
        {
            registeredBuildings.Clear();
            fullMoveCache.Clear();
        }
    }
}
