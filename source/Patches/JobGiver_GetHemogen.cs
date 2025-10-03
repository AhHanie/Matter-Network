using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_JobGiver_GetHemogen
    {
        [HarmonyPatch(typeof(JobGiver_GetHemogen), "GetHemogenPack")]
        public static class GetHemogenPack
        {
            public static void Postfix(Pawn pawn, ref Thing __result)
            {
                Thing networkPack = NetworkItemSearchUtility.FindClosestReachableThing(pawn, item => item.def == ThingDefOf.HemogenPack && pawn.CanReserve(item) && !item.IsForbidden(pawn), out float networkDistanceSquared);
                if (networkPack == null)
                {
                    return;
                }

                if (__result == null || networkDistanceSquared < NetworkItemSearchUtility.GetThingDistanceSquared(pawn, __result))
                {
                    __result = networkPack;
                }
            }
        }
    }
}
