using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SK_Matter_Network.Patches
{
    public static class Patch_ResourceCounter
    {
        [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.UpdateResourceCounts))]
        public static class UpdateResourceCounts
        {
            public static void Postfix(ResourceCounter __instance)
            {
                Map map = __instance.map;
                NetworksMapComponent mapComp = map.GetComponent<NetworksMapComponent>();
                Dictionary<ThingDef, int> countedAmounts = __instance.AllCountedAmounts;

                foreach (DataNetwork network in mapComp.Networks)
                {
                    foreach (Thing storedThing in network.StoredItems)
                    {
                        Thing innerThing = storedThing.GetInnerIfMinified();
                        if (!innerThing.def.CountAsResource || !ShouldCount(innerThing))
                        {
                            continue;
                        }

                        if (!countedAmounts.ContainsKey(innerThing.def))
                        {
                            countedAmounts.Add(innerThing.def, 0);
                        }

                        countedAmounts[innerThing.def] += innerThing.stackCount;
                    }
                }
            }

            private static bool ShouldCount(Thing thing)
            {
                if (thing.IsNotFresh())
                {
                    return false;
                }

                return true;
            }
        }
    }
}
