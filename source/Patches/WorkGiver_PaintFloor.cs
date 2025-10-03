using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_PaintFloor
    {
        [HarmonyPatch(typeof(WorkGiver_PaintFloor), nameof(WorkGiver_PaintFloor.HasJobOnCell))]
        public static class HasJobOnCell
        {
            public static void Postfix(WorkGiver_PaintFloor __instance, Pawn pawn, IntVec3 c, bool forced, ref bool __result)
            {
                if (!__result && __instance.JobOnCell(pawn, c, forced) != null)
                {
                    __result = true;
                }
            }
        }
    }
}
