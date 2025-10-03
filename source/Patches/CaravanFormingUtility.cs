using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_CaravanFormingUtility
    {
        [HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllReachableColonyItems))]
        public static class AllReachableColonyItems
        {
            public static void Postfix(ref List<Thing> __result)
            {
                if (__result == null || __result.Count <= 1)
                {
                    return;
                }

                HashSet<Thing> seen = new HashSet<Thing>();
                for (int i = __result.Count - 1; i >= 0; i--)
                {
                    if (!seen.Add(__result[i]))
                    {
                        __result.RemoveAt(i);
                    }
                }
            }
        }
    }
}
