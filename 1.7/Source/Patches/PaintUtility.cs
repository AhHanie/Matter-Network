using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SK_Matter_Network.Patches
{
    public static class Patch_PaintUtility
    {
        [HarmonyPatch(typeof(PaintUtility), nameof(PaintUtility.FindNearbyDyes))]
        public static class FindNearbyDyes
        {
            public static void Postfix(Pawn pawn, bool forced, ref System.Collections.Generic.List<Thing> __result)
            {
                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(pawn.Map))
                {
                    if (item.def != ThingDefOf.Dye || item.IsForbidden(pawn) || !pawn.CanReserve(item, 1, -1, null, forced) || __result.Contains(item))
                    {
                        continue;
                    }

                    __result.Add(item);
                }
            }
        }
    }
}
