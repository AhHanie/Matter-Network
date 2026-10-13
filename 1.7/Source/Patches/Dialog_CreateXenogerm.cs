using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_Dialog_CreateXenogerm
    {
        [HarmonyPatch(typeof(Dialog_CreateXenogerm), "ColonyHasEnoughArchites")]
        public static class ColonyHasEnoughArchites
        {
            public static void Postfix(Dialog_CreateXenogerm __instance, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                Building_GeneAssembler geneAssembler = __instance.geneAssembler;
                int arc = __instance.arc;
                if (arc <= 0)
                {
                    return;
                }

                int networkCount = NetworkItemSearchUtility.CountMatchingThingStacks(geneAssembler.MapHeld, item => item.def == ThingDefOf.ArchiteCapsule);
                if (networkCount >= arc)
                {
                    __result = true;
                }
            }
        }
    }
}
