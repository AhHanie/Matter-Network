using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ListerHaulables
    {
        [HarmonyPatch(typeof(ListerHaulables), "ListerHaulablesTick")]
        public static class ListerHaulablesTick
        {
            public static void Postfix(Map ___map, HashSet<Thing> ___haulables)
            {
                NetworksMapComponent mapComp = ___map.GetComponent<NetworksMapComponent>();
                foreach (DataNetwork network in mapComp.Networks)
                {
                    foreach (Thing item in network.StoredItems)
                    {
                        Check(item, ___haulables);
                    }
                }
            }
        }

        private static void Check(Thing t, HashSet<Thing> haulables)
        {
            if (ShouldBeHaulable(t))
            {
                haulables.Add(t);
            }
            else
            {
                haulables.Remove(t);
            }
        }

        private static bool ShouldBeHaulable(Thing t)
        {
            if (t.IsInValidBestStorage())
            {
                return false;
            }
            return true;
        }
    }
}
