using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ItemAvailability
    {
        [HarmonyPatch(typeof(ItemAvailability), nameof(ItemAvailability.ThingsAvailableAnywhere))]
        public static class ThingsAvailableAnywhere
        {
            public static void Postfix(ItemAvailability __instance, ThingDef need, int amount, Pawn pawn, ref bool __result)
            {
                if (__result)
                {
                    return;
                }
                Map map = __instance.map;

                int availableCount = CountAvailableOnMap(map, need, pawn);
                if (availableCount >= amount)
                {
                    __result = true;
                    return;
                }

                foreach (Thing item in NetworkItemSearchUtility.AllNetworkItems(map))
                {
                    if (item.def != need || item.IsForbidden(pawn))
                    {
                        continue;
                    }

                    availableCount += item.stackCount;
                    if (availableCount >= amount)
                    {
                        __result = true;
                        return;
                    }
                }
            }

            private static int CountAvailableOnMap(Map map, ThingDef need, Pawn pawn)
            {
                int count = 0;
                List<Thing> things = map.listerThings.ThingsOfDef(need);
                for (int i = 0; i < things.Count; i++)
                {
                    if (!things[i].IsForbidden(pawn))
                    {
                        count += things[i].stackCount;
                    }
                }

                return count;
            }
        }
    }
}
