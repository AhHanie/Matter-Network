using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_TakeDrugsForDrugPolicy
    {
        [HarmonyPatch(typeof(JobGiver_TakeDrugsForDrugPolicy), "FindDrugFor")]
        public static class FindDrugFor
        {
            public static void Postfix(Pawn pawn, ThingDef drugDef, ref Thing __result)
            {
                Thing networkDrug = NetworkItemSearchUtility.FindClosestReachableThing(
                    pawn,
                    item => IsValidNetworkDrug(pawn, drugDef, item),
                    out float networkDistanceSquared);

                if (networkDrug == null)
                {
                    return;
                }

                if (__result == null || networkDistanceSquared < NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result))
                {
                    __result = networkDrug;
                }
            }

            private static bool IsValidNetworkDrug(Pawn pawn, ThingDef drugDef, Thing drug)
            {
                if (drug.def != drugDef || !drug.def.IsDrug)
                {
                    return false;
                }

                if (!NetworkDrugUtility.IsReachableNetworkItem(pawn, drug, out _))
                {
                    return false;
                }

                return NetworkDrugUtility.PassesSpawnedWorldChecks(pawn, drug);
            }
        }
    }
}
