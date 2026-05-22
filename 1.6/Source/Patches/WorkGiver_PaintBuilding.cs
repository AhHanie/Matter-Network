using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_WorkGiver_PaintBuilding
    {
        [HarmonyPatch(typeof(WorkGiver_PaintBuilding), nameof(WorkGiver_PaintBuilding.HasJobOnThing))]
        public static class HasJobOnThing
        {
            public static void Postfix(WorkGiver_PaintBuilding __instance, Pawn pawn, Thing t, bool forced, ref bool __result)
            {
                if (!__result && __instance.JobOnThing(pawn, t, forced) != null)
                {
                    __result = true;
                }
            }
        }
    }
}
