using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Recipe_ImplantXenogerm
    {
        [HarmonyPatch(typeof(Recipe_ImplantXenogerm), nameof(Recipe_ImplantXenogerm.AvailableOnNow))]
        public static class AvailableOnNow
        {
            public static void Postfix(Thing thing, ref bool __result)
            {
                Pawn pawn = thing as Pawn;
                if (__result || pawn == null || !pawn.Spawned)
                {
                    return;
                }

                if (NetworkItemSearchUtility.AnyMatchingThing(pawn.Map, item => item.def == ThingDefOf.Xenogerm))
                {
                    __result = true;
                }
            }
        }
    }
}
