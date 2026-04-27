using HarmonyLib;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_HaulAIUtility
    {
        [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
        public static class PawnCanAutomaticallyHaulFast
        {
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(Pawn p, Thing t, ref bool __result)
            {
                NetworksMapComponent mapComp = p.Map.GetComponent<NetworksMapComponent>();
                if (mapComp.TryGetItemNetwork(t, out _))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }
    }
}
