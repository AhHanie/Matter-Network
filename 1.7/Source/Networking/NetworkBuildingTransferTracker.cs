using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network
{
    // Tracks "these NetworkBuilding instances are moving together as a preserved transfer"
    // regardless of whether the source is vanilla gravship (NetworkBuilding.PreSwapMap) or an
    // external mod's own map-transfer pipeline (e.g. Save Our Ship 2's ShipInteriorMod2.MoveShip,
    // which never calls PreSwapMap). Both paths share the same registration set so DeSpawn/disk
    // drive/chute logic doesn't need to know which pipeline triggered the move.
    public static class NetworkBuildingTransferTracker
    {
        // Opaque handle returned by BeginExternalTransfer. Only NetworkBuildingTransferTracker
        // itself inspects the contents; callers just hold it until the external move finishes.
        public sealed class ExternalTransferContext
        {
            public readonly List<NetworkBuilding> Buildings;
            public readonly Map SourceMap;
            public readonly Map TargetMap;
            public readonly string SourceMod;

            internal ExternalTransferContext(List<NetworkBuilding> buildings, Map sourceMap, Map targetMap, string sourceMod)
            {
                Buildings = buildings;
                SourceMap = sourceMap;
                TargetMap = targetMap;
                SourceMod = sourceMod;
            }
        }

        private static readonly HashSet<NetworkBuilding> registeredBuildings = new HashSet<NetworkBuilding>();
        private static readonly Dictionary<DataNetwork, bool> fullMoveCache = new Dictionary<DataNetwork, bool>();

        public static void RegisterForTransfer(NetworkBuilding building)
        {
            if (building == null) return;
            registeredBuildings.Add(building);
            if (building.ParentNetwork != null)
                fullMoveCache.Remove(building.ParentNetwork);
        }

        // Registers every building in the set as being transported together by an external mod's
        // own map-transfer pipeline. The returned context must be passed to CompleteExternalTransfer
        // once that pipeline finishes (successfully or not) so affected networks get refreshed and
        // the registration set is cleared. Must be paired with CompleteExternalTransfer from a
        // Harmony finalizer so an exception mid-transfer can't leave stale state behind.
        public static ExternalTransferContext BeginExternalTransfer(IEnumerable<NetworkBuilding> buildings, Map sourceMap, Map targetMap, string sourceMod)
        {
            List<NetworkBuilding> list = new List<NetworkBuilding>();
            foreach (NetworkBuilding building in buildings)
            {
                if (building == null) continue;
                list.Add(building);
                RegisterForTransfer(building);
            }

            return new ExternalTransferContext(list, sourceMap, targetMap, sourceMod);
        }

        // Returns true if every spawned building in network is being transported as a preserved move.
        // Must be called after all registrations for the current transfer have run (i.e. from DeSpawn or later).
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

        // Used by disk drives/chutes to suppress content-drop side effects for any building
        // currently registered as being transported, whether or not its whole network moves together.
        public static bool ShouldPreserveDuringTransfer(NetworkBuilding building)
        {
            return building != null && registeredBuildings.Contains(building);
        }

        public static void CompleteExternalTransfer(ExternalTransferContext context, bool success)
        {
            if (context == null)
            {
                Clear();
                return;
            }

            HashSet<DataNetwork> affectedNetworks = new HashSet<DataNetwork>();
            foreach (NetworkBuilding building in context.Buildings)
            {
                if (building?.ParentNetwork != null)
                    affectedNetworks.Add(building.ParentNetwork);
            }

            foreach (DataNetwork network in affectedNetworks)
            {
                Map newMap = success ? FindSpawnedMap(network) : context.SourceMap;
                if (newMap != null)
                {
                    Logger.Message($"Refreshing network {network.NetworkId} after {context.SourceMod} transfer");
                    network.RefreshAfterMapChange(newMap);
                    // Defensive: if the source mod's own rollback path respawned buildings with
                    // respawningAfterLoad=true (skipping NetworkBuilding.SpawnSetup's normal
                    // mapComp.AddNetwork call), make sure the network is still registered here.
                    newMap.GetComponent<NetworksMapComponent>()?.AddNetwork(network);
                }
            }

            Clear();
        }

        private static Map FindSpawnedMap(DataNetwork network)
        {
            foreach (NetworkBuilding b in network.Buildings)
            {
                if (b != null && b.Spawned && b.Map != null)
                    return b.Map;
            }
            return null;
        }

        public static void Clear()
        {
            registeredBuildings.Clear();
            fullMoveCache.Clear();
        }
    }
}
