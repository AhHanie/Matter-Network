using Verse;
using Verse.AI;
using RimWorld;

namespace SK_Matter_Network.Patches
{
    internal static class NetworkDrugUtility
    {
        public static bool IsReachableNetworkItem(Pawn pawn, Thing item, out DataNetwork network)
        {
            network = null;
            if (pawn?.Map == null || item == null || item.Destroyed || item.stackCount <= 0)
            {
                return false;
            }

            NetworksMapComponent mapComp = pawn.Map.GetComponent<NetworksMapComponent>();
            if (!mapComp.TryGetItemNetwork(item, out network) || !network.CanExtractItems)
            {
                return false;
            }

            return NetworkItemSearchUtility.GetClosestReachableInterfaceDistanceSquared(pawn, item) != float.MaxValue;
        }

        public static bool PassesSpawnedWorldChecks(Pawn pawn, Thing item, bool checkPolitical = false)
        {
            if (!item.Spawned)
            {
                return true;
            }

            if (!pawn.CanReserve(item) || item.IsForbidden(pawn) || !item.IsSociallyProper(pawn))
            {
                return false;
            }

            return !checkPolitical || item.IsPoliticallyProper(pawn);
        }
    }
}
